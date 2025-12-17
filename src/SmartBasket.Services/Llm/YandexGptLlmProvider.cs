using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для YandexGPT (Yandex Cloud) с поддержкой стриминга и tools
/// https://cloud.yandex.ru/docs/yandexgpt/api-ref/v1/TextGeneration/completion
/// </summary>
public class YandexGptLlmProvider : ILlmProvider
{
    private const string YandexGptApiUrl = "https://llm.api.cloud.yandex.net/foundationModels/v1/completion";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YandexGptLlmProvider> _logger;
    private readonly AiProviderConfig _config;

    public string Name => $"YandexGPT/{_config.Model}";

    public bool SupportsConversationReset => false;

    // YandexGPT пока не поддерживает native tool calling публично
    // Используем fallback через prompt injection и парсинг текстового ответа
    public bool SupportsTools => false;

    public void ResetConversation()
    {
        // YandexGPT не хранит состояние — ничего не делаем
    }

    public YandexGptLlmProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<YandexGptLlmProvider> logger,
        AiProviderConfig config)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _config = config;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                return (false, "YandexGPT API key is not configured");
            }

            if (string.IsNullOrWhiteSpace(_config.FolderId))
            {
                return (false, "YandexGPT Folder ID is not configured");
            }

            // Делаем тестовый запрос
            var result = await GenerateAsync(
                "Привет! Ответь одним словом: Работает",
                cancellationToken: cancellationToken);

            if (result.IsSuccess)
            {
                return (true, $"YandexGPT connected, model: {_config.Model}");
            }

            return (false, $"YandexGPT test failed: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            return (false, $"YandexGPT connection failed: {ex.Message}");
        }
    }

    public async Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LlmGenerationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT API key is not configured";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_config.FolderId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT Folder ID is not configured";
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var maxTokens = _config.MaxTokens ?? 2000;
            var temperature = _config.Temperature;

            string modelUri;
            if (_config.Model.StartsWith("general:"))
            {
                modelUri = $"gpt://{_config.FolderId}/{_config.Model}";
            }
            else
            {
                modelUri = $"gpt://{_config.FolderId}/{_config.Model}/latest";
            }

            var request = new YandexGptRequest
            {
                ModelUri = modelUri,
                CompletionOptions = new YandexCompletionOptions
                {
                    Stream = true,
                    Temperature = temperature,
                    MaxTokens = maxTokens.ToString()
                },
                Messages = new[]
                {
                    new YandexMessage
                    {
                        Role = "user",
                        Text = prompt
                    }
                }
            };

            progress?.Report($"  [YandexGPT] Sending STREAMING request to {YandexGptApiUrl}...");
            progress?.Report($"  [YandexGPT] Model: {modelUri}");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, YandexGptApiUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json")
            };

            if (_config.ApiKey.StartsWith("t1.") || _config.ApiKey.StartsWith("y"))
            {
                httpRequest.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
            }
            else
            {
                httpRequest.Headers.Add("Authorization", $"Api-Key {_config.ApiKey}");
            }

            httpRequest.Headers.Add("x-folder-id", _config.FolderId);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexGPT timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexGPT returned {response.StatusCode}: {errorContent}";
                _logger.LogError("YandexGPT error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return result;
            }

            progress?.Report($"  [YandexGPT] === STREAMING RESPONSE ===");
            var fullResponse = new StringBuilder();
            var lineBuffer = new StringBuilder();

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<YandexStreamChunk>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk?.Result?.Alternatives?.Length > 0)
                    {
                        var alternative = chunk.Result.Alternatives[0];
                        var text = alternative.Message?.Text ?? string.Empty;

                        var newText = text;
                        if (text.Length > fullResponse.Length)
                        {
                            newText = text.Substring(fullResponse.Length);
                        }
                        else
                        {
                            newText = text.Length == fullResponse.Length ? "" : text;
                        }

                        if (!string.IsNullOrEmpty(newText))
                        {
                            foreach (var ch in newText)
                            {
                                if (ch == '\n')
                                {
                                    progress?.Report($"  {lineBuffer}");
                                    lineBuffer.Clear();
                                }
                                else
                                {
                                    lineBuffer.Append(ch);
                                }
                            }
                        }

                        if (text.Length > fullResponse.Length)
                        {
                            fullResponse.Clear();
                            fullResponse.Append(text);
                        }

                        if (alternative.Status == "ALTERNATIVE_STATUS_FINAL" ||
                            alternative.Status == "ALTERNATIVE_STATUS_COMPLETE")
                        {
                            if (lineBuffer.Length > 0)
                            {
                                progress?.Report($"  {lineBuffer}");
                                lineBuffer.Clear();
                            }
                            break;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            stopwatch.Stop();
            progress?.Report($"  [YandexGPT] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            if (fullResponse.Length > 0)
            {
                result.IsSuccess = true;
                result.Response = fullResponse.ToString();
                progress?.Report($"  [YandexGPT] Total response: {fullResponse.Length} chars");
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT returned empty response";
            }
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
            _logger.LogError(ex, "YandexGPT generation error");
        }

        return result;
    }

    public async Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LlmGenerationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT API key is not configured";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_config.FolderId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT Folder ID is not configured";
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var maxTokens = _config.MaxTokens ?? 2000;
            var temperature = _config.Temperature;

            string modelUri;
            if (_config.Model.StartsWith("general:"))
            {
                modelUri = $"gpt://{_config.FolderId}/{_config.Model}";
            }
            else
            {
                modelUri = $"gpt://{_config.FolderId}/{_config.Model}/latest";
            }

            // Конвертируем сообщения в формат YandexGPT
            var yandexMessages = ConvertMessages(messages);

            // Конвертируем tools в формат YandexGPT (если поддерживаются)
            var yandexTools = tools != null && SupportsTools ? ConvertTools(tools) : null;

            var request = new YandexGptRequestWithTools
            {
                ModelUri = modelUri,
                CompletionOptions = new YandexCompletionOptions
                {
                    Stream = true,
                    Temperature = temperature,
                    MaxTokens = maxTokens.ToString()
                },
                Messages = yandexMessages,
                Tools = yandexTools
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            _logger.LogInformation("[YandexGPT Chat] ========================================");
            _logger.LogInformation("[YandexGPT Chat] >>> ЗАПРОС К YandexGPT");
            _logger.LogInformation("[YandexGPT Chat] Model: {Model}", modelUri);
            _logger.LogInformation("[YandexGPT Chat] Messages count: {Count}", yandexMessages.Length);
            _logger.LogInformation("[YandexGPT Chat] Tools count: {Count}", yandexTools?.Length ?? 0);
            _logger.LogInformation("[YandexGPT Chat] Temperature: {Temp}, MaxTokens: {MaxTokens}",
                temperature, maxTokens);

            // Полное логирование запроса
            var fullRequestJson = JsonSerializer.Serialize(request, jsonOptions);
            _logger.LogDebug("[YandexGPT Chat] ===== FULL REQUEST JSON START =====");
            _logger.LogDebug("[YandexGPT Chat] Request ({Length} chars):\n{Json}", fullRequestJson.Length, fullRequestJson);
            _logger.LogDebug("[YandexGPT Chat] ===== FULL REQUEST JSON END =====");

            // Полное логирование каждого сообщения
            _logger.LogDebug("[YandexGPT Chat] ===== MESSAGES DETAIL START =====");
            for (var i = 0; i < yandexMessages.Length; i++)
            {
                var msg = yandexMessages[i];
                _logger.LogDebug("[YandexGPT Chat] [{Index}] Role={Role}, Text ({Length} chars)",
                    i, msg.Role, msg.Text.Length);
                _logger.LogDebug("[YandexGPT Chat] [{Index}] Text:\n{Text}", i, msg.Text);
            }
            _logger.LogDebug("[YandexGPT Chat] ===== MESSAGES DETAIL END =====");

            // Полное логирование tools
            if (yandexTools != null && yandexTools.Length > 0)
            {
                _logger.LogDebug("[YandexGPT Chat] ===== TOOLS DETAIL START =====");
                foreach (var tool in yandexTools)
                {
                    _logger.LogDebug("[YandexGPT Chat] Tool: {Name}", tool.Function?.Name);
                    _logger.LogDebug("[YandexGPT Chat]   Description: {Description}", tool.Function?.Description);
                    if (tool.Function?.Parameters != null)
                    {
                        var paramsJson = JsonSerializer.Serialize(tool.Function.Parameters, jsonOptions);
                        _logger.LogDebug("[YandexGPT Chat]   Parameters:\n{Params}", paramsJson);
                    }
                }
                _logger.LogDebug("[YandexGPT Chat] ===== TOOLS DETAIL END =====");
            }

            progress?.Report($"[YandexGPT Chat] >>> ЗАПРОС");
            progress?.Report($"[YandexGPT Chat] Model: {modelUri}, Messages: {yandexMessages.Length}, Tools: {yandexTools?.Length ?? 0}");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, YandexGptApiUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request, jsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };

            if (_config.ApiKey.StartsWith("t1.") || _config.ApiKey.StartsWith("y"))
            {
                httpRequest.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
            }
            else
            {
                httpRequest.Headers.Add("Authorization", $"Api-Key {_config.ApiKey}");
            }

            httpRequest.Headers.Add("x-folder-id", _config.FolderId);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexGPT timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexGPT returned {response.StatusCode}: {errorContent}";
                _logger.LogError("[YandexGPT Chat] Ошибка HTTP: {StatusCode}, Body: {Body}", response.StatusCode, errorContent);
                return result;
            }

            var fullResponse = new StringBuilder();
            var lineBuffer = new StringBuilder();
            var collectedToolCalls = new List<LlmToolCall>();

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<YandexStreamChunkWithTools>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk?.Result?.Alternatives?.Length > 0)
                    {
                        var alternative = chunk.Result.Alternatives[0];
                        var text = alternative.Message?.Text ?? string.Empty;

                        var newText = text;
                        if (text.Length > fullResponse.Length)
                        {
                            newText = text.Substring(fullResponse.Length);
                        }
                        else
                        {
                            newText = text.Length == fullResponse.Length ? "" : text;
                        }

                        if (!string.IsNullOrEmpty(newText))
                        {
                            foreach (var ch in newText)
                            {
                                if (ch == '\n')
                                {
                                    progress?.Report($"  {lineBuffer}");
                                    lineBuffer.Clear();
                                }
                                else
                                {
                                    lineBuffer.Append(ch);
                                }
                            }
                        }

                        if (text.Length > fullResponse.Length)
                        {
                            fullResponse.Clear();
                            fullResponse.Append(text);
                        }

                        // Обработка tool calls
                        if (alternative.Message?.ToolCalls != null)
                        {
                            foreach (var toolCall in alternative.Message.ToolCalls)
                            {
                                if (toolCall?.FunctionCall != null)
                                {
                                    var argsJson = toolCall.FunctionCall.Arguments != null
                                        ? JsonSerializer.Serialize(toolCall.FunctionCall.Arguments)
                                        : "{}";

                                    collectedToolCalls.Add(new LlmToolCall
                                    {
                                        Id = Guid.NewGuid().ToString("N")[..8],
                                        Name = toolCall.FunctionCall.Name ?? "",
                                        Arguments = argsJson
                                    });

                                    _logger.LogInformation("[YandexGPT Chat] Tool call: {Name}({Args})",
                                        toolCall.FunctionCall.Name, argsJson);
                                    progress?.Report($"  [Tool Call] {toolCall.FunctionCall.Name}");
                                }
                            }
                        }

                        if (alternative.Status == "ALTERNATIVE_STATUS_FINAL" ||
                            alternative.Status == "ALTERNATIVE_STATUS_COMPLETE")
                        {
                            if (lineBuffer.Length > 0)
                            {
                                progress?.Report($"  {lineBuffer}");
                                lineBuffer.Clear();
                            }
                            break;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            stopwatch.Stop();

            if (fullResponse.Length > 0 || collectedToolCalls.Count > 0)
            {
                result.IsSuccess = true;
                result.Response = fullResponse.ToString();

                if (collectedToolCalls.Count > 0)
                {
                    result.ToolCalls = collectedToolCalls;
                    _logger.LogInformation("[YandexGPT Chat] <<< TOOL CALLS: {Count}", collectedToolCalls.Count);
                }
                else
                {
                    _logger.LogInformation("[YandexGPT Chat] <<< ОТВЕТ ПОЛУЧЕН ({Time}s)", stopwatch.Elapsed.TotalSeconds);
                }

                _logger.LogInformation("[YandexGPT Chat] Response length: {Length} chars", result.Response.Length);
                _logger.LogDebug("[YandexGPT Chat] ===== FINAL RESPONSE START =====");
                _logger.LogDebug("[YandexGPT Chat] Response:\n{Response}", result.Response);
                _logger.LogDebug("[YandexGPT Chat] ===== FINAL RESPONSE END =====");
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT returned empty response";
            }

            _logger.LogInformation("[YandexGPT Chat] ========================================");
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
            _logger.LogError(ex, "[YandexGPT Chat] Исключение: {Message}", ex.Message);
        }

        return result;
    }

    private YandexMessage[] ConvertMessages(IEnumerable<LlmChatMessage> messages)
    {
        var result = new List<YandexMessage>();

        foreach (var m in messages)
        {
            // YandexGPT поддерживает только system, user, assistant
            // Конвертируем tool результаты в user сообщения
            if (m.Role == "tool")
            {
                // Формируем сообщение с результатом инструмента
                var toolResult = $"[Результат инструмента {m.ToolCallId}]:\n{m.Content}";
                result.Add(new YandexMessage
                {
                    Role = "user",
                    Text = toolResult
                });
            }
            else if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                // Assistant сообщение с tool calls — показываем какой инструмент был вызван
                var toolCallsInfo = string.Join("\n", m.ToolCalls.Select(tc =>
                    $"[Вызов инструмента {tc.Name}]: {tc.Arguments}"));

                var text = string.IsNullOrEmpty(m.Content)
                    ? toolCallsInfo
                    : $"{m.Content}\n{toolCallsInfo}";

                result.Add(new YandexMessage
                {
                    Role = "assistant",
                    Text = text
                });
            }
            else
            {
                // Обычное сообщение
                result.Add(new YandexMessage
                {
                    Role = m.Role,
                    Text = m.Content ?? ""
                });
            }
        }

        return result.ToArray();
    }

    private YandexTool[] ConvertTools(IEnumerable<ToolDefinition> tools)
    {
        return tools.Select(t => new YandexTool
        {
            Function = new YandexToolFunction
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParametersSchema
            }
        }).ToArray();
    }

    // DTOs for YandexGPT API
    private class YandexGptRequest
    {
        [JsonPropertyName("modelUri")]
        public string ModelUri { get; set; } = string.Empty;

        [JsonPropertyName("completionOptions")]
        public YandexCompletionOptions CompletionOptions { get; set; } = new();

        [JsonPropertyName("messages")]
        public YandexMessage[] Messages { get; set; } = Array.Empty<YandexMessage>();
    }

    private class YandexGptRequestWithTools
    {
        [JsonPropertyName("modelUri")]
        public string ModelUri { get; set; } = string.Empty;

        [JsonPropertyName("completionOptions")]
        public YandexCompletionOptions CompletionOptions { get; set; } = new();

        [JsonPropertyName("messages")]
        public YandexMessage[] Messages { get; set; } = Array.Empty<YandexMessage>();

        [JsonPropertyName("tools")]
        public YandexTool[]? Tools { get; set; }
    }

    private class YandexCompletionOptions
    {
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("maxTokens")]
        public string MaxTokens { get; set; } = "2000";
    }

    private class YandexMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    // Tool DTOs
    private class YandexTool
    {
        [JsonPropertyName("function")]
        public YandexToolFunction? Function { get; set; }
    }

    private class YandexToolFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; }
    }

    // Streaming response chunk
    private class YandexStreamChunk
    {
        [JsonPropertyName("result")]
        public YandexGptResult? Result { get; set; }
    }

    private class YandexStreamChunkWithTools
    {
        [JsonPropertyName("result")]
        public YandexGptResultWithTools? Result { get; set; }
    }

    private class YandexGptResult
    {
        [JsonPropertyName("alternatives")]
        public YandexAlternative[]? Alternatives { get; set; }

        [JsonPropertyName("usage")]
        public YandexUsage? Usage { get; set; }

        [JsonPropertyName("modelVersion")]
        public string? ModelVersion { get; set; }
    }

    private class YandexGptResultWithTools
    {
        [JsonPropertyName("alternatives")]
        public YandexAlternativeWithTools[]? Alternatives { get; set; }

        [JsonPropertyName("usage")]
        public YandexUsage? Usage { get; set; }

        [JsonPropertyName("modelVersion")]
        public string? ModelVersion { get; set; }
    }

    private class YandexAlternative
    {
        [JsonPropertyName("message")]
        public YandexMessage? Message { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private class YandexAlternativeWithTools
    {
        [JsonPropertyName("message")]
        public YandexMessageWithToolCalls? Message { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private class YandexMessageWithToolCalls
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "assistant";

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("toolCalls")]
        public YandexToolCall[]? ToolCalls { get; set; }
    }

    private class YandexToolCall
    {
        [JsonPropertyName("functionCall")]
        public YandexFunctionCall? FunctionCall { get; set; }
    }

    private class YandexFunctionCall
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public object? Arguments { get; set; }
    }

    private class YandexUsage
    {
        [JsonPropertyName("inputTextTokens")]
        public string? InputTextTokens { get; set; }

        [JsonPropertyName("completionTokens")]
        public string? CompletionTokens { get; set; }

        [JsonPropertyName("totalTokens")]
        public string? TotalTokens { get; set; }
    }
}
