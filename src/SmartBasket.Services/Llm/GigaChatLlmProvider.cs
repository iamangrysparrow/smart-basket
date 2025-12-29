using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для GigaChat (Sber) с поддержкой стриминга и function calling
/// </summary>
public class GigaChatLlmProvider : ILlmProvider
{
    private const string OAuthUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
    private const string ApiBaseUrl = "https://gigachat.devices.sberbank.ru/api/v1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GigaChatLlmProvider> _logger;
    private readonly AiProviderConfig _config;
    private readonly ITokenUsageService _tokenUsageService;

    // Кэш токена
    private string? _accessToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly object _tokenLock = new();

    public string Name => $"GigaChat/{_config.Model}";

    public bool SupportsConversationReset => false;

    public bool SupportsTools => true;

    public void ResetConversation()
    {
        // GigaChat не хранит состояние — ничего не делаем
    }

    public GigaChatLlmProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<GigaChatLlmProvider> logger,
        AiProviderConfig config,
        ITokenUsageService tokenUsageService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _config = config;
        _tokenUsageService = tokenUsageService;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                return (false, "GigaChat API key (Authorization key) is not configured");
            }

            // Пробуем получить токен
            var token = await EnsureAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                return (false, "Failed to obtain GigaChat access token");
            }

            // Делаем тестовый запрос к /models
            var client = CreateHttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await client.GetAsync($"{ApiBaseUrl}/models", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, $"GigaChat connected, model: {_config.Model}");
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, $"GigaChat returned {response.StatusCode}: {errorContent}");
        }
        catch (Exception ex)
        {
            return (false, $"GigaChat connection failed: {ex.Message}");
        }
    }

    public async Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Для простой генерации используем ChatAsync с одним user сообщением
        var messages = new List<LlmChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        return await ChatAsync(messages, null, sessionContext, progress, cancellationToken);
    }

    public async Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LlmGenerationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "GigaChat API key (Authorization key) is not configured";
                return result;
            }

            var token = await EnsureAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Failed to obtain GigaChat access token";
                return result;
            }

            var client = CreateHttpClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var maxTokens = _config.MaxTokens ?? 2000;
            var temperature = _config.Temperature;
            var model = string.IsNullOrEmpty(_config.Model) ? "GigaChat" : _config.Model;

            // Конвертируем сообщения в формат GigaChat
            var gigaChatMessages = ConvertMessages(messages);

            // Конвертируем tools в формат GigaChat (functions)
            var gigaChatFunctions = tools != null ? ConvertTools(tools).ToArray() : null;

            var request = new GigaChatRequest
            {
                Model = model,
                Messages = gigaChatMessages,
                Functions = gigaChatFunctions?.Length > 0 ? gigaChatFunctions : null,
                FunctionCall = gigaChatFunctions?.Length > 0 ? "auto" : null,
                Temperature = temperature,
                MaxTokens = maxTokens,
                Stream = true
            };

            var requestJson = JsonSerializer.Serialize(request, LlmJsonOptions.ForLogging);
            var requestUrl = $"{ApiBaseUrl}/chat/completions";

            _logger.LogInformation("[GigaChat] ========================================");
            _logger.LogInformation("[GigaChat] >>> ЗАПРОС К GigaChat");
            _logger.LogInformation("[GigaChat] Model: {Model}", model);
            _logger.LogInformation("[GigaChat] Messages count: {Count}", gigaChatMessages.Length);
            _logger.LogInformation("[GigaChat] Functions count: {Count}", gigaChatFunctions?.Length ?? 0);
            _logger.LogInformation("[GigaChat] Temperature: {Temp}, MaxTokens: {MaxTokens}", temperature, maxTokens);

            _logger.LogDebug("[GigaChat] ===== FULL REQUEST JSON START =====");
            _logger.LogDebug("[GigaChat] Request ({Length} chars):\n{Json}", requestJson.Length, requestJson);
            _logger.LogDebug("[GigaChat] ===== FULL REQUEST JSON END =====");

            progress?.Report($"[GigaChat] >>> ЗАПРОС");
            progress?.Report($"[GigaChat] Model: {model}, Messages: {gigaChatMessages.Length}, Functions: {gigaChatFunctions?.Length ?? 0}");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("Authorization", $"Bearer {token}");

            // Добавляем X-Session-ID для кэширования токенов
            if (!string.IsNullOrEmpty(sessionContext?.SessionId))
            {
                httpRequest.Headers.Add("X-Session-ID", sessionContext.SessionId);
                _logger.LogDebug("[GigaChat] X-Session-ID: {SessionId}", sessionContext.SessionId);
            }

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"GigaChat timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                result.IsSuccess = false;
                result.ErrorMessage = $"GigaChat returned {response.StatusCode}: {errorContent}";
                _logger.LogError("[GigaChat] Ошибка HTTP: {StatusCode}, Body: {Body}", response.StatusCode, errorContent);
                return result;
            }

            // Парсим SSE streaming ответ
            var (responseText, toolCalls, usage) = await ParseStreamingResponse(response, progress, linkedCts.Token);

            stopwatch.Stop();

            if (!string.IsNullOrEmpty(responseText) || toolCalls.Count > 0)
            {
                result.IsSuccess = true;
                result.Response = responseText;
                result.Usage = usage;

                if (toolCalls.Count > 0)
                {
                    result.ToolCalls = toolCalls;
                    _logger.LogInformation("[GigaChat] <<< FUNCTION CALLS: {Count}", toolCalls.Count);
                }
                else
                {
                    _logger.LogInformation("[GigaChat] <<< ОТВЕТ ПОЛУЧЕН ({Time}s)", stopwatch.Elapsed.TotalSeconds);
                }

                _logger.LogInformation("[GigaChat] Response length: {Length} chars", result.Response?.Length ?? 0);
                _logger.LogDebug("[GigaChat] ===== FINAL RESPONSE START =====");
                _logger.LogDebug("[GigaChat] Response:\n{Response}", result.Response);
                _logger.LogDebug("[GigaChat] ===== FINAL RESPONSE END =====");

                // Логируем использование токенов в БД
                if (usage != null)
                {
                    try
                    {
                        await _tokenUsageService.LogUsageAsync(
                            provider: "GigaChat",
                            model: model,
                            aiFunction: AiFunctionNames.Chat,
                            usage: usage,
                            sessionId: sessionContext?.SessionId,
                            cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[GigaChat] Failed to log token usage: {Message}", ex.Message);
                    }
                }
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = "GigaChat returned empty response";
            }

            _logger.LogInformation("[GigaChat] ========================================");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Operation cancelled by user";
            throw;
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Generation timed out";
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"Error: {ex.Message}";
            _logger.LogError(ex, "[GigaChat] Исключение: {Message}", ex.Message);
        }

        return result;
    }

    // ==================== Вспомогательные методы ====================

    /// <summary>
    /// Создаёт HttpClient с отключённой проверкой TLS сертификата
    /// (для GigaChat требуется сертификат Минцифры России)
    /// </summary>
    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            {
                // Для production рекомендуется установить корневой сертификат Минцифры
                // Здесь отключаем проверку для упрощения тестирования
                if (sslPolicyErrors == SslPolicyErrors.None)
                    return true;

                _logger.LogDebug("[GigaChat] TLS certificate validation bypassed: {Errors}", sslPolicyErrors);
                return true;
            }
        };

        return new HttpClient(handler);
    }

    /// <summary>
    /// Получает или обновляет access token
    /// </summary>
    private async Task<string?> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        lock (_tokenLock)
        {
            // Токен ещё действителен (с запасом 2 минуты)
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow.AddMinutes(2) < _tokenExpiresAt)
            {
                return _accessToken;
            }
        }

        // Нужно обновить токен
        var newToken = await FetchAccessTokenAsync(cancellationToken);

        lock (_tokenLock)
        {
            return _accessToken;
        }
    }

    /// <summary>
    /// Запрашивает новый access token
    /// </summary>
    private async Task<string?> FetchAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateHttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var scope = _config.GigaChatScope switch
            {
                GigaChatScope.PERS => "GIGACHAT_API_PERS",
                GigaChatScope.B2B => "GIGACHAT_API_B2B",
                GigaChatScope.CORP => "GIGACHAT_API_CORP",
                _ => "GIGACHAT_API_PERS"
            };

            var requestContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("scope", scope)
            });

            var request = new HttpRequestMessage(HttpMethod.Post, OAuthUrl)
            {
                Content = requestContent
            };

            // Authorization: Basic <base64 encoded credentials>
            request.Headers.Add("Authorization", $"Basic {_config.ApiKey}");
            request.Headers.Add("RqUID", Guid.NewGuid().ToString());

            _logger.LogInformation("[GigaChat] Requesting access token, scope: {Scope}", scope);

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("[GigaChat] Failed to get access token: {StatusCode}, {Error}",
                    response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<GigaChatTokenResponse>(responseJson, LlmJsonOptions.ForParsing);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogError("[GigaChat] Invalid token response: {Response}", responseJson);
                return null;
            }

            lock (_tokenLock)
            {
                _accessToken = tokenResponse.AccessToken;
                // expires_at в миллисекундах unix timestamp
                _tokenExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(tokenResponse.ExpiresAt).UtcDateTime;
            }

            _logger.LogInformation("[GigaChat] Access token obtained, expires at: {ExpiresAt}", _tokenExpiresAt);

            return _accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GigaChat] Exception while fetching access token: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Конвертирует сообщения в формат GigaChat
    /// </summary>
    private GigaChatMessage[] ConvertMessages(IEnumerable<LlmChatMessage> messages)
    {
        var result = new List<GigaChatMessage>();

        foreach (var m in messages)
        {
            if (m.Role == "tool")
            {
                // GigaChat использует role="function" для результатов функций
                result.Add(new GigaChatMessage
                {
                    Role = "function",
                    Content = m.Content
                });
            }
            else if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                // Assistant с function call
                var tc = m.ToolCalls[0];
                result.Add(new GigaChatMessage
                {
                    Role = "assistant",
                    Content = m.Content ?? "",
                    FunctionCall = new GigaChatFunctionCall
                    {
                        Name = tc.Name,
                        Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Arguments)
                    }
                });
            }
            else
            {
                // Обычное сообщение
                result.Add(new GigaChatMessage
                {
                    Role = m.Role,
                    Content = m.Content
                });
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Конвертирует tools в формат GigaChat functions
    /// </summary>
    private IEnumerable<GigaChatFunction> ConvertTools(IEnumerable<ToolDefinition> tools)
    {
        return tools.Select(t => new GigaChatFunction
        {
            Name = t.Name,
            Description = t.Description,
            Parameters = t.ParametersSchema
        });
    }

    /// <summary>
    /// Парсит SSE streaming ответ
    /// </summary>
    private async Task<(string ResponseText, List<LlmToolCall> ToolCalls, LlmTokenUsage? Usage)> ParseStreamingResponse(
        HttpResponseMessage response,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var fullResponse = new StringBuilder();
        var toolCalls = new List<LlmToolCall>();
        LlmTokenUsage? usage = null;

        progress?.Report($"  [GigaChat] === STREAMING RESPONSE ===");

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            // SSE формат: "data: {...}" или "data: [DONE]"
            if (!line.StartsWith("data: ")) continue;

            var data = line.Substring(6); // Убираем "data: "

            if (data == "[DONE]")
            {
                break;
            }

            try
            {
                var chunk = JsonSerializer.Deserialize<GigaChatStreamChunk>(data, LlmJsonOptions.ForParsing);
                if (chunk == null) continue;

                // Парсим usage если пришёл
                if (chunk.Usage != null)
                {
                    usage = new LlmTokenUsage(
                        PromptTokens: chunk.Usage.PromptTokens,
                        CompletionTokens: chunk.Usage.CompletionTokens,
                        PrecachedPromptTokens: chunk.Usage.PrecachedPromptTokens,
                        ReasoningTokens: null,
                        TotalTokens: chunk.Usage.TotalTokens
                    );

                    _logger.LogInformation("[GigaChat] Usage: prompt={Prompt}, completion={Completion}, precached={Precached}, total={Total}",
                        usage.PromptTokens, usage.CompletionTokens, usage.PrecachedPromptTokens, usage.TotalTokens);
                }

                if (chunk.Choices == null || chunk.Choices.Length == 0) continue;

                var choice = chunk.Choices[0];
                var delta = choice.Delta;

                // Обработка текстового контента
                if (!string.IsNullOrEmpty(delta?.Content))
                {
                    fullResponse.Append(delta.Content);
                    progress?.Report($"  {delta.Content}");
                }

                // Обработка function call
                if (delta?.FunctionCall != null && !string.IsNullOrEmpty(delta.FunctionCall.Name))
                {
                    var argsJson = delta.FunctionCall.Arguments != null
                        ? JsonSerializer.Serialize(delta.FunctionCall.Arguments)
                        : "{}";

                    toolCalls.Add(new LlmToolCall
                    {
                        Id = $"call_{Guid.NewGuid().ToString("N")[..8]}",
                        Name = delta.FunctionCall.Name,
                        Arguments = argsJson
                    });

                    _logger.LogInformation("[GigaChat] Function call: {Name}({Args})",
                        delta.FunctionCall.Name, argsJson);
                    progress?.Report($"  [Function Call] {delta.FunctionCall.Name}");
                }

                // Проверяем finish_reason
                if (choice.FinishReason == "function_call" || choice.FinishReason == "stop")
                {
                    // Для function_call нужно получить полные аргументы из message
                    if (choice.FinishReason == "function_call" && choice.Message?.FunctionCall != null)
                    {
                        var fc = choice.Message.FunctionCall;
                        var argsJson = fc.Arguments != null
                            ? JsonSerializer.Serialize(fc.Arguments)
                            : "{}";

                        // Обновляем или добавляем tool call
                        if (toolCalls.Count == 0 || toolCalls[^1].Name != fc.Name)
                        {
                            toolCalls.Add(new LlmToolCall
                            {
                                Id = $"call_{Guid.NewGuid().ToString("N")[..8]}",
                                Name = fc.Name ?? "",
                                Arguments = argsJson
                            });
                        }
                        else
                        {
                            // Обновляем аргументы последнего вызова
                            toolCalls[^1].Arguments = argsJson;
                        }
                    }
                    break;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug("[GigaChat] JSON parse error: {Error}, Data: {Data}", ex.Message, data);
            }
        }

        return (fullResponse.ToString(), toolCalls, usage);
    }

    // ==================== DTOs ====================

    private class GigaChatTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public long ExpiresAt { get; set; }
    }

    private class GigaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "GigaChat";

        [JsonPropertyName("messages")]
        public GigaChatMessage[] Messages { get; set; } = Array.Empty<GigaChatMessage>();

        [JsonPropertyName("functions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GigaChatFunction[]? Functions { get; set; }

        [JsonPropertyName("function_call")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FunctionCall { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
    }

    private class GigaChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("function_call")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GigaChatFunctionCall? FunctionCall { get; set; }
    }

    private class GigaChatFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; }
    }

    private class GigaChatFunctionCall
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public object? Arguments { get; set; }
    }

    // Streaming response DTOs
    private class GigaChatStreamChunk
    {
        [JsonPropertyName("choices")]
        public GigaChatStreamChoice[]? Choices { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public GigaChatUsage? Usage { get; set; }
    }

    private class GigaChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("precached_prompt_tokens")]
        public int? PrecachedPromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    private class GigaChatStreamChoice
    {
        [JsonPropertyName("delta")]
        public GigaChatDelta? Delta { get; set; }

        [JsonPropertyName("message")]
        public GigaChatMessage? Message { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class GigaChatDelta
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("function_call")]
        public GigaChatFunctionCall? FunctionCall { get; set; }
    }
}
