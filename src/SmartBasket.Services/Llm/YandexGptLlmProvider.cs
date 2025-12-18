using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для YandexGPT (Yandex Cloud) с поддержкой стриминга и tools
/// Использует OpenAI-совместимый API: https://yandex.cloud/ru/docs/ai-studio/concepts/openai-compatibility
/// </summary>
public class YandexGptLlmProvider : ILlmProvider, IReasoningProvider
{
    // OpenAI-совместимый endpoint для Completions API
    private const string YandexGptApiUrl = "https://llm.api.cloud.yandex.net/v1/chat/completions";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YandexGptLlmProvider> _logger;
    private readonly AiProviderConfig _config;

    // Runtime override для reasoning параметров (устанавливаются из UI)
    private Core.Configuration.ReasoningMode? _runtimeReasoningMode;
    private Core.Configuration.ReasoningEffort? _runtimeReasoningEffort;

    public string Name => $"YandexGPT/{_config.Model}";

    public bool SupportsConversationReset => false;

    // YandexGPT поддерживает tool calling через OpenAI-совместимый API
    public bool SupportsTools => true;

    // YandexGPT поддерживает режим рассуждений через параметр reasoning_effort
    public bool SupportsReasoning => true;

    public void ResetConversation()
    {
        // YandexGPT не хранит состояние — ничего не делаем
    }

    /// <summary>
    /// Устанавливает параметры режима рассуждений для текущей сессии.
    /// Переопределяет значения из конфигурации провайдера.
    /// </summary>
    /// <param name="mode">Режим рассуждений (null = использовать из конфигурации)</param>
    /// <param name="effort">Уровень рассуждений (null = использовать из конфигурации)</param>
    public void SetReasoningParameters(
        Core.Configuration.ReasoningMode? mode,
        Core.Configuration.ReasoningEffort? effort)
    {
        _runtimeReasoningMode = mode;
        _runtimeReasoningEffort = effort;
        _logger.LogInformation("[YandexGPT] Reasoning parameters set: Mode={Mode}, Effort={Effort}",
            mode?.ToString() ?? "(default)", effort?.ToString() ?? "(default)");
    }

    /// <summary>
    /// Сбрасывает runtime override для reasoning параметров
    /// </summary>
    public void ResetReasoningParameters()
    {
        _runtimeReasoningMode = null;
        _runtimeReasoningEffort = null;
        _logger.LogInformation("[YandexGPT] Reasoning parameters reset to config defaults");
    }

    /// <summary>
    /// Текущий режим рассуждений (runtime override или из конфигурации)
    /// </summary>
    public Core.Configuration.ReasoningMode CurrentReasoningMode =>
        _runtimeReasoningMode ?? _config.ReasoningMode;

    /// <summary>
    /// Текущий уровень рассуждений (runtime override или из конфигурации)
    /// </summary>
    public Core.Configuration.ReasoningEffort CurrentReasoningEffort =>
        _runtimeReasoningEffort ?? _config.ReasoningEffort;

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

            // OpenAI-совместимый формат модели
            var modelUri = BuildModelUri();

            // OpenAI-совместимый запрос
            var request = new OpenAiChatRequest
            {
                Model = modelUri,
                Messages = new[]
                {
                    new OpenAiMessage { Role = "user", Content = prompt }
                },
                Temperature = temperature,
                MaxTokens = maxTokens,
                Stream = true
            };

            progress?.Report($"  [YandexGPT] Sending STREAMING request to {YandexGptApiUrl}...");
            progress?.Report($"  [YandexGPT] Model: {modelUri}");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = CreateHttpRequest(request);

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

            // Парсим OpenAI SSE streaming ответ
            var (responseText, _) = await ParseOpenAiStreamingResponse(response, progress, linkedCts.Token);

            stopwatch.Stop();
            progress?.Report($"  [YandexGPT] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            if (!string.IsNullOrEmpty(responseText))
            {
                result.IsSuccess = true;
                result.Response = responseText;
                progress?.Report($"  [YandexGPT] Total response: {responseText.Length} chars");
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

            // OpenAI-совместимый формат модели
            var modelUri = BuildModelUri();

            // Конвертируем сообщения в OpenAI формат
            var openAiMessages = ConvertMessagesToOpenAi(messages);

            // Конвертируем tools в OpenAI формат
            var openAiTools = tools != null ? ConvertToolsToOpenAi(tools).ToArray() : null;

            // Определяем параметры режима рассуждений если включен
            string? reasoningEffort = null;
            ReasoningOptionsDto? reasoningOptions = null;

            if (CurrentReasoningMode == Core.Configuration.ReasoningMode.EnabledHidden)
            {
                // reasoning_effort для моделей gpt-oss-*
                reasoningEffort = CurrentReasoningEffort switch
                {
                    Core.Configuration.ReasoningEffort.Low => "low",
                    Core.Configuration.ReasoningEffort.Medium => "medium",
                    Core.Configuration.ReasoningEffort.High => "high",
                    _ => "low"
                };

                // reasoning_options для YandexGPT Pro (нативный Yandex формат)
                reasoningOptions = new ReasoningOptionsDto { Mode = "ENABLED_HIDDEN" };
            }

            var request = new OpenAiChatRequest
            {
                Model = modelUri,
                Messages = openAiMessages,
                Temperature = temperature,
                MaxTokens = maxTokens,
                Stream = true,
                Tools = openAiTools?.Length > 0 ? openAiTools : null,
                ToolChoice = openAiTools?.Length > 0 ? "auto" : null,
                ReasoningEffort = reasoningEffort,
                ReasoningOptions = reasoningOptions
            };

            _logger.LogInformation("[YandexGPT Chat] ========================================");
            _logger.LogInformation("[YandexGPT Chat] >>> ЗАПРОС К YandexGPT (OpenAI-совместимый)");
            _logger.LogInformation("[YandexGPT Chat] Model: {Model}", modelUri);
            _logger.LogInformation("[YandexGPT Chat] Messages count: {Count}", openAiMessages.Length);
            _logger.LogInformation("[YandexGPT Chat] Tools count: {Count}", openAiTools?.Length ?? 0);
            _logger.LogInformation("[YandexGPT Chat] Temperature: {Temp}, MaxTokens: {MaxTokens}",
                temperature, maxTokens);
            if (!string.IsNullOrEmpty(reasoningEffort) || reasoningOptions != null)
            {
                _logger.LogInformation("[YandexGPT Chat] Reasoning: effort={Effort}, options.mode={Mode}",
                    reasoningEffort ?? "(null)", reasoningOptions?.Mode ?? "(null)");
            }

            // Полное логирование запроса
            var fullRequestJson = JsonSerializer.Serialize(request, LlmJsonOptions.ForLogging);
            _logger.LogDebug("[YandexGPT Chat] ===== FULL REQUEST JSON START =====");
            _logger.LogDebug("[YandexGPT Chat] Request ({Length} chars):\n{Json}", fullRequestJson.Length, fullRequestJson);
            _logger.LogDebug("[YandexGPT Chat] ===== FULL REQUEST JSON END =====");

            // Полное логирование каждого сообщения
            _logger.LogDebug("[YandexGPT Chat] ===== MESSAGES DETAIL START =====");
            for (var i = 0; i < openAiMessages.Length; i++)
            {
                var msg = openAiMessages[i];
                _logger.LogDebug("[YandexGPT Chat] [{Index}] Role={Role}, Content ({Length} chars), ToolCallId={ToolCallId}",
                    i, msg.Role, msg.Content?.Length ?? 0, msg.ToolCallId ?? "(null)");
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    _logger.LogDebug("[YandexGPT Chat] [{Index}] Content:\n{Content}", i, msg.Content);
                }
                if (msg.ToolCalls?.Length > 0)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        _logger.LogDebug("[YandexGPT Chat] [{Index}] ToolCall: {Name} (id={Id}), Args: {Args}",
                            i, tc.Function.Name, tc.Id, tc.Function.Arguments);
                    }
                }
            }
            _logger.LogDebug("[YandexGPT Chat] ===== MESSAGES DETAIL END =====");

            // Полное логирование tools
            if (openAiTools != null && openAiTools.Length > 0)
            {
                _logger.LogDebug("[YandexGPT Chat] ===== TOOLS DETAIL START =====");
                foreach (var tool in openAiTools)
                {
                    _logger.LogDebug("[YandexGPT Chat] Tool: {Name}", tool.Function.Name);
                    _logger.LogDebug("[YandexGPT Chat]   Description: {Description}", tool.Function.Description);
                    if (tool.Function.Parameters != null)
                    {
                        var paramsJson = JsonSerializer.Serialize(tool.Function.Parameters, LlmJsonOptions.ForLogging);
                        _logger.LogDebug("[YandexGPT Chat]   Parameters:\n{Params}", paramsJson);
                    }
                }
                _logger.LogDebug("[YandexGPT Chat] ===== TOOLS DETAIL END =====");
            }

            progress?.Report($"[YandexGPT Chat] >>> ЗАПРОС");
            progress?.Report($"[YandexGPT Chat] Model: {modelUri}, Messages: {openAiMessages.Length}, Tools: {openAiTools?.Length ?? 0}");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = CreateHttpRequest(request);

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

            // Парсим OpenAI streaming ответ
            var (responseText, toolCalls) = await ParseOpenAiStreamingResponse(response, progress, linkedCts.Token);

            stopwatch.Stop();

            if (!string.IsNullOrEmpty(responseText) || toolCalls.Count > 0)
            {
                result.IsSuccess = true;
                result.Response = responseText;

                if (toolCalls.Count > 0)
                {
                    result.ToolCalls = toolCalls;
                    _logger.LogInformation("[YandexGPT Chat] <<< TOOL CALLS: {Count}", toolCalls.Count);
                }
                else
                {
                    _logger.LogInformation("[YandexGPT Chat] <<< ОТВЕТ ПОЛУЧЕН ({Time}s)", stopwatch.Elapsed.TotalSeconds);
                }

                _logger.LogInformation("[YandexGPT Chat] Response length: {Length} chars", result.Response?.Length ?? 0);
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

    // ==================== Вспомогательные методы ====================

    /// <summary>
    /// Формирует URI модели в формате gpt://folder_id/model/latest
    /// </summary>
    private string BuildModelUri()
    {
        if (_config.Model.StartsWith("general:"))
        {
            return $"gpt://{_config.FolderId}/{_config.Model}";
        }
        return $"gpt://{_config.FolderId}/{_config.Model}/latest";
    }

    /// <summary>
    /// Создаёт HTTP запрос с правильными заголовками
    /// </summary>
    private HttpRequestMessage CreateHttpRequest(OpenAiChatRequest request)
    {
        var json = JsonSerializer.Serialize(request, LlmJsonOptions.ForLogging);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, YandexGptApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // Авторизация - Bearer для IAM токенов, Api-Key для API ключей
        if (_config.ApiKey?.StartsWith("t1.") == true || _config.ApiKey?.StartsWith("y") == true)
        {
            httpRequest.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
        }
        else
        {
            httpRequest.Headers.Add("Authorization", $"Api-Key {_config.ApiKey}");
        }

        // Folder ID передаётся как project в OpenAI-совместимом API
        // Но Yandex также принимает x-folder-id для обратной совместимости
        httpRequest.Headers.Add("x-folder-id", _config.FolderId);

        return httpRequest;
    }

    /// <summary>
    /// Конвертирует сообщения из LlmChatMessage в OpenAI формат
    /// </summary>
    private OpenAiMessage[] ConvertMessagesToOpenAi(IEnumerable<LlmChatMessage> messages)
    {
        var result = new List<OpenAiMessage>();

        foreach (var m in messages)
        {
            if (m.Role == "tool")
            {
                // Tool result в OpenAI формате
                result.Add(new OpenAiMessage
                {
                    Role = "tool",
                    Content = m.Content,
                    ToolCallId = m.ToolCallId
                });
            }
            else if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                // Assistant с tool calls
                result.Add(new OpenAiMessage
                {
                    Role = "assistant",
                    Content = m.Content,
                    ToolCalls = m.ToolCalls.Select(tc => new OpenAiToolCall
                    {
                        Id = tc.Id,
                        Type = "function",
                        Function = new OpenAiFunctionCall
                        {
                            Name = tc.Name,
                            Arguments = tc.Arguments
                        }
                    }).ToArray()
                });
            }
            else
            {
                // Обычное сообщение (user, assistant, system)
                result.Add(new OpenAiMessage
                {
                    Role = m.Role,
                    Content = m.Content
                });
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Конвертирует tools в OpenAI формат
    /// </summary>
    private IEnumerable<OpenAiTool> ConvertToolsToOpenAi(IEnumerable<ToolDefinition> tools)
    {
        return tools.Select(t => new OpenAiTool
        {
            Type = "function",
            Function = new OpenAiFunction
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParametersSchema
            }
        });
    }

    /// <summary>
    /// Парсит OpenAI SSE streaming ответ
    /// Формат: data: {...}\n\ndata: {...}\n\ndata: [DONE]
    /// </summary>
    private async Task<(string ResponseText, List<LlmToolCall> ToolCalls)> ParseOpenAiStreamingResponse(
        HttpResponseMessage response,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var fullResponse = new StringBuilder();
        var toolCalls = new List<LlmToolCall>();
        var toolCallsInProgress = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();

        progress?.Report($"  [YandexGPT] === STREAMING RESPONSE ===");

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            // OpenAI SSE формат: "data: {...}" или "data: [DONE]"
            if (!line.StartsWith("data: ")) continue;

            var data = line.Substring(6); // Убираем "data: "

            if (data == "[DONE]")
            {
                break;
            }

            try
            {
                var chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data, LlmJsonOptions.ForParsing);
                if (chunk?.Choices == null || chunk.Choices.Length == 0) continue;

                var choice = chunk.Choices[0];
                var delta = choice.Delta;

                // Обработка текстового контента
                if (!string.IsNullOrEmpty(delta?.Content))
                {
                    fullResponse.Append(delta.Content);
                    progress?.Report($"  {delta.Content}");
                }

                // Обработка tool calls (streaming формат - приходят по частям)
                if (delta?.ToolCalls != null)
                {
                    foreach (var tc in delta.ToolCalls)
                    {
                        if (!toolCallsInProgress.TryGetValue(tc.Index, out var inProgress))
                        {
                            // Новый tool call
                            inProgress = (tc.Id ?? "", tc.Function?.Name ?? "", new StringBuilder());
                            toolCallsInProgress[tc.Index] = inProgress;

                            _logger.LogInformation("[YandexGPT Chat] Tool call started: {Name} (id={Id})",
                                tc.Function?.Name, tc.Id);
                            progress?.Report($"  [Tool Call] {tc.Function?.Name}");
                        }

                        // Накапливаем аргументы (приходят по частям)
                        if (!string.IsNullOrEmpty(tc.Function?.Arguments))
                        {
                            inProgress.Args.Append(tc.Function.Arguments);
                            toolCallsInProgress[tc.Index] = inProgress;
                        }
                    }
                }

                // Проверяем finish_reason
                if (choice.FinishReason == "tool_calls" || choice.FinishReason == "stop")
                {
                    break;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug("[YandexGPT Chat] JSON parse error: {Error}, Data: {Data}", ex.Message, data);
            }
        }

        // Собираем tool calls из накопленных данных
        foreach (var (index, (id, name, args)) in toolCallsInProgress)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var argsStr = args.ToString();
                if (string.IsNullOrEmpty(argsStr)) argsStr = "{}";

                toolCalls.Add(new LlmToolCall
                {
                    Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N")[..8] : id,
                    Name = name,
                    Arguments = argsStr
                });

                _logger.LogInformation("[YandexGPT Chat] Tool call completed: {Name}({Args})", name, argsStr);
            }
        }

        return (fullResponse.ToString(), toolCalls);
    }

    // ==================== OpenAI-совместимые DTOs ====================

    // Запрос в формате OpenAI chat.completions
    private class OpenAiChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public OpenAiMessage[] Messages { get; set; } = Array.Empty<OpenAiMessage>();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAiTool[]? Tools { get; set; }

        [JsonPropertyName("tool_choice")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolChoice { get; set; }

        /// <summary>
        /// Уровень рассуждений: "low", "medium", "high"
        /// Для моделей gpt-oss-* (OpenAI-совместимый параметр)
        /// </summary>
        [JsonPropertyName("reasoning_effort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningEffort { get; set; }

        /// <summary>
        /// Режим рассуждений для YandexGPT Pro
        /// Нативный Yandex Cloud параметр
        /// </summary>
        [JsonPropertyName("reasoning_options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReasoningOptionsDto? ReasoningOptions { get; set; }
    }

    /// <summary>
    /// DTO для reasoning_options (нативный Yandex формат)
    /// </summary>
    private class ReasoningOptionsDto
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "DISABLED";
    }

    // Сообщение OpenAI формата
    private class OpenAiMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAiToolCall[]? ToolCalls { get; set; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }
    }

    // Tool в формате OpenAI
    private class OpenAiTool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public OpenAiFunction Function { get; set; } = new();
    }

    private class OpenAiFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; }
    }

    // Tool call в ответе модели (OpenAI формат)
    private class OpenAiToolCall
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public OpenAiFunctionCall Function { get; set; } = new();
    }

    private class OpenAiFunctionCall
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; } = "{}";
    }

    // ==================== OpenAI Streaming Response DTOs ====================

    // SSE chunk: data: {...}
    private class OpenAiStreamChunk
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("object")]
        public string? Object { get; set; }

        [JsonPropertyName("created")]
        public long? Created { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public OpenAiStreamChoice[]? Choices { get; set; }
    }

    private class OpenAiStreamChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("delta")]
        public OpenAiDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class OpenAiDelta
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public OpenAiDeltaToolCall[]? ToolCalls { get; set; }
    }

    private class OpenAiDeltaToolCall
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("function")]
        public OpenAiDeltaFunction? Function { get; set; }
    }

    private class OpenAiDeltaFunction
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
    }
}
