using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для Yandex AI Agent (через REST Assistant API)
/// https://rest-assistant.api.cloud.yandex.net/v1/responses
/// Примечание: Режим рассуждений для агентов настраивается в AI Studio, а не через API
/// </summary>
public class YandexAgentLlmProvider : ILlmProvider, IReasoningProvider
{
    // REST Assistant API endpoint для Yandex AI Studio агентов
    private const string YandexAgentApiUrl = "https://rest-assistant.api.cloud.yandex.net/v1/responses";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YandexAgentLlmProvider> _logger;
    private readonly AiProviderConfig _config;
    private readonly ITokenUsageService _tokenUsageService;

    // ID последнего ответа для поддержки истории диалога
    private string? _lastResponseId;

    // Runtime override для reasoning параметров (устанавливаются из UI)
    private Core.Configuration.ReasoningMode? _runtimeReasoningMode;
    private Core.Configuration.ReasoningEffort? _runtimeReasoningEffort;

    public string Name => $"YandexAgent/{_config.AgentId}";

    public bool SupportsConversationReset => true;

    public bool SupportsTools => true; // YandexAgent поддерживает function calling через REST API

    // YandexAgent (Responses API) поддерживает режим рассуждений через параметр reasoning_effort
    public bool SupportsReasoning => true;

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

    /// <summary>
    /// Устанавливает параметры режима рассуждений для текущей сессии
    /// </summary>
    public void SetReasoningParameters(Core.Configuration.ReasoningMode? mode, Core.Configuration.ReasoningEffort? effort)
    {
        _runtimeReasoningMode = mode;
        _runtimeReasoningEffort = effort;
        _logger.LogInformation("[YandexAgent] Reasoning parameters set: Mode={Mode}, Effort={Effort}",
            mode?.ToString() ?? "(default)", effort?.ToString() ?? "(default)");
    }

    /// <summary>
    /// Сбрасывает runtime override для reasoning параметров
    /// </summary>
    public void ResetReasoningParameters()
    {
        _runtimeReasoningMode = null;
        _runtimeReasoningEffort = null;
        _logger.LogInformation("[YandexAgent] Reasoning parameters reset to config defaults");
    }

    public void ResetConversation()
    {
        _lastResponseId = null;
        _logger.LogInformation("[YandexAgent] Conversation reset, previous_response_id cleared");
    }

    public YandexAgentLlmProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<YandexAgentLlmProvider> logger,
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
                return (false, "YandexAgent API key is not configured");
            }

            if (string.IsNullOrWhiteSpace(_config.FolderId))
            {
                return (false, "YandexAgent Folder ID is not configured");
            }

            if (string.IsNullOrWhiteSpace(_config.AgentId))
            {
                return (false, "YandexAgent Agent ID is not configured");
            }

            // Делаем тестовый запрос
            var result = await GenerateAsync(
                "Привет! Ответь одним словом: Работает",
                cancellationToken: cancellationToken);

            if (result.IsSuccess)
            {
                return (true, $"YandexAgent connected, agent: {_config.AgentId}");
            }

            return (false, $"YandexAgent test failed: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            return (false, $"YandexAgent connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Генерация с переменными промпта.
    /// Агент в Yandex AI Studio должен содержать плейсхолдеры {{ИМЯ_ПЕРЕМЕННОЙ}}.
    /// </summary>
    /// <param name="variables">Словарь переменных для подстановки в промпт агента</param>
    /// <param name="input">Входное сообщение (например, "Выполни инструкцию")</param>
    /// <param name="progress">Прогресс-репортер</param>
    /// <param name="cancellationToken">Токен отмены</param>
    public async Task<LlmGenerationResult> GenerateWithVariablesAsync(
        Dictionary<string, string> variables,
        string input = "Выполни инструкцию",
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await GenerateAsyncInternal(
            input,
            variables,
            progress,
            cancellationToken);
    }

    public async Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // YandexAgent использует PreviousResponseId из sessionContext если есть
        if (sessionContext?.PreviousResponseId != null && _lastResponseId == null)
        {
            _lastResponseId = sessionContext.PreviousResponseId;
            _logger.LogDebug("[YandexAgent] Using PreviousResponseId from sessionContext: {Id}", sessionContext.PreviousResponseId);
        }

        return await GenerateAsyncInternal(
            prompt,
            variables: null,
            progress,
            cancellationToken);
    }

    private async Task<LlmGenerationResult> GenerateAsyncInternal(
        string input,
        Dictionary<string, string>? variables,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = new LlmGenerationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent API key is not configured";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_config.FolderId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent Folder ID is not configured";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_config.AgentId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent Agent ID is not configured";
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 120);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // REST Assistant API формат запроса с поддержкой streaming
            var promptInfo = new PromptInfo { Id = _config.AgentId };
            if (variables != null && variables.Count > 0)
            {
                promptInfo.Variables = variables;
            }

            var request = new RestAssistantRequest
            {
                Prompt = promptInfo,
                Input = input,
                Stream = true  // Включаем streaming
            };

            // Compact JSON для экономии токенов на входе
            var requestJson = JsonSerializer.Serialize(request, LlmJsonOptions.Compact);

            // Для отладки — красивый JSON (используем ForLogging с кириллицей!)
            var prettyJson = JsonSerializer.Serialize(request, LlmJsonOptions.ForLogging);

            // Логирование в ILogger (полный JSON)
            _logger.LogDebug("[YandexAgent] === REQUEST JSON ===\n{RequestJson}", prettyJson);

            // Логирование в progress (краткое)
            if (variables != null && variables.Count > 0)
            {
                progress?.Report($"[YandexAgent] === VARIABLES ({variables.Count}) ===");
                foreach (var kv in variables)
                {
                    // Показываем полное значение для отладки
                    progress?.Report($"  {kv.Key}: {kv.Value}");
                }
                progress?.Report($"[YandexAgent] Input: {input}");
            }
            else
            {
                progress?.Report($"[YandexAgent] === INPUT START ===");
                progress?.Report(input);
                progress?.Report($"[YandexAgent] === INPUT END ===");
            }

            // Логируем полный JSON в progress для отладки
            progress?.Report($"[YandexAgent] === FULL REQUEST JSON ===");
            progress?.Report(prettyJson);
            progress?.Report($"[YandexAgent] === END REQUEST JSON ===");

            progress?.Report($"[YandexAgent] Sending STREAMING request to {YandexAgentApiUrl} (agent: {_config.AgentId}, timeout: {timeoutSeconds}s)...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, YandexAgentApiUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            // Авторизация - Bearer token
            httpRequest.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
            httpRequest.Headers.Add("x-folder-id", _config.FolderId);

            HttpResponseMessage response;
            try
            {
                // ResponseHeadersRead для streaming - не ждём полного ответа
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                stopwatch.Stop();
                progress?.Report($"[YandexAgent] === TIMEOUT ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexAgent timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexAgent returned {response.StatusCode}: {errorContent}";
                _logger.LogError("YandexAgent error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                progress?.Report($"[YandexAgent] ERROR: {response.StatusCode}");
                return result;
            }

            // Читаем SSE streaming ответ
            progress?.Report($"[YandexAgent] === STREAMING RESPONSE ===");
            var fullResponse = new StringBuilder();
            var lineBuffer = new StringBuilder();

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            string? currentData = null;

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);
                if (line == null) continue;

                // SSE формат: "data:{json}" затем "event:event_type"
                if (line.StartsWith("data:"))
                {
                    currentData = line.Substring(5); // Убираем "data:"
                }
                else if (line.StartsWith("event:") && currentData != null)
                {
                    var eventType = line.Substring(6); // Убираем "event:"

                    try
                    {
                        var eventObj = JsonSerializer.Deserialize<StreamEvent>(currentData, LlmJsonOptions.ForParsing);

                        if (eventType == "response.output_text.delta" && eventObj?.Delta != null)
                        {
                            // Получили дельту текста
                            fullResponse.Append(eventObj.Delta);

                            // Буферизация для построчного вывода в progress
                            foreach (var ch in eventObj.Delta)
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
                        else if (eventType == "response.completed")
                        {
                            // Финальное событие - можно получить полный текст из response.output_text
                            if (eventObj?.Response?.OutputText != null && fullResponse.Length == 0)
                            {
                                // Если дельты не пришли, берём из финального ответа
                                fullResponse.Append(eventObj.Response.OutputText);
                            }

                            // Выводим остаток буфера
                            if (lineBuffer.Length > 0)
                            {
                                progress?.Report($"  {lineBuffer}");
                                lineBuffer.Clear();
                            }
                            break;
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed events
                    }

                    currentData = null;
                }
            }

            stopwatch.Stop();
            progress?.Report($"[YandexAgent] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            if (fullResponse.Length > 0)
            {
                result.IsSuccess = true;
                result.Response = fullResponse.ToString();
                progress?.Report($"[YandexAgent] Total response: {fullResponse.Length} chars");
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent returned empty response";
                progress?.Report($"[YandexAgent] ERROR: Empty response");
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
            _logger.LogError(ex, "YandexAgent generation error");
        }

        return result;
    }

    public async Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // YandexAgent использует PreviousResponseId из sessionContext если есть
        if (sessionContext?.PreviousResponseId != null && _lastResponseId == null)
        {
            _lastResponseId = sessionContext.PreviousResponseId;
            _logger.LogDebug("[YandexAgent Chat] Using PreviousResponseId from sessionContext: {Id}", sessionContext.PreviousResponseId);
        }

        // Retry logic: до 2 повторных попыток при ошибке API
        const int maxRetries = 2;
        var messageList = messages.ToList();

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var result = await ChatAsyncInternal(messageList, tools, sessionContext, progress, cancellationToken);

            // Успех или отмена пользователем - возвращаем сразу
            if (result.IsSuccess || cancellationToken.IsCancellationRequested)
            {
                return result;
            }

            // Если это была последняя попытка - возвращаем ошибку
            if (attempt >= maxRetries)
            {
                return result;
            }

            // Логируем retry
            var delay = (attempt + 1) * 1000; // 1s, 2s
            _logger.LogWarning("[YandexAgent Chat] Ошибка: {Error}. Повтор {Attempt}/{MaxRetries} через {Delay}ms...",
                result.ErrorMessage, attempt + 1, maxRetries, delay);
            progress?.Report($"[YandexAgent Chat] ⚠️ Ошибка, повтор {attempt + 1}/{maxRetries}...");

            // НЕ сбрасываем previous_response_id - история хранится 30 дней
            // Сброс происходит только при status != completed (в ChatAsyncInternal)

            await Task.Delay(delay, cancellationToken);
        }

        // Не должны сюда попасть, но на всякий случай
        return new LlmGenerationResult { IsSuccess = false, ErrorMessage = "Unexpected retry loop exit" };
    }

    private async Task<LlmGenerationResult> ChatAsyncInternal(
        List<LlmChatMessage> messageList,
        IEnumerable<ToolDefinition>? tools,
        LlmSessionContext? sessionContext,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = new LlmGenerationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent API key is not configured";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_config.FolderId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent Folder ID is not configured";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_config.AgentId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent Agent ID is not configured";
                return result;
            }
            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 120);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Конвертируем tools в формат Yandex API
            var yandexTools = tools != null ? ConvertTools(tools).ToList() : null;

            // Определяем что отправлять в input:
            // - Если есть previous_response_id, YandexAgent уже знает историю - отправляем только новое
            // - Если нет previous_response_id, отправляем весь контекст
            object inputData;
            int inputItemsCount;

            if (_lastResponseId != null)
            {
                // Есть история - отправляем только последние сообщения после предыдущего ответа
                // Это могут быть: tool results или новое сообщение пользователя
                var newMessages = GetNewMessagesForContinuation(messageList);
                inputData = newMessages;
                inputItemsCount = newMessages.Count;
            }
            else
            {
                // Новый диалог - отправляем весь контекст
                var inputList = ConvertMessagesToInput(messageList);
                inputData = inputList;
                inputItemsCount = inputList.Count;
            }

            // Определяем reasoning параметры если режим включен
            string? reasoningEffort = null;
            ReasoningOptionsDto? reasoningOptions = null;
            if (CurrentReasoningMode == Core.Configuration.ReasoningMode.EnabledHidden)
            {
                reasoningEffort = CurrentReasoningEffort switch
                {
                    Core.Configuration.ReasoningEffort.Low => "low",
                    Core.Configuration.ReasoningEffort.Medium => "medium",
                    Core.Configuration.ReasoningEffort.High => "high",
                    _ => "low"
                };
                reasoningOptions = new ReasoningOptionsDto { Mode = "ENABLED_HIDDEN" };
            }

            // Определяем max_output_tokens
            // При включённом reasoning модель тратит токены на "размышления"
            // Увеличиваем лимит чтобы хватило и на reasoning и на ответ с tool calls
            // По умолчанию используем значение из конфига, или 16384 при reasoning, или 4096 без reasoning
            int? maxOutputTokens = null;
            if (_config.MaxTokens > 0)
            {
                maxOutputTokens = _config.MaxTokens;
            }
            else if (reasoningEffort != null)
            {
                // При reasoning нужно больше токенов для "размышлений" + ответа
                maxOutputTokens = 16384;
            }

            // REST Assistant API формат запроса
            var request = new RestAssistantChatRequest
            {
                Prompt = new PromptInfo { Id = _config.AgentId },
                Input = inputData,
                Stream = true,
                PreviousResponseId = _lastResponseId,
                Tools = yandexTools,
                ReasoningEffort = reasoningEffort,
                ReasoningOptions = reasoningOptions,
                MaxOutputTokens = maxOutputTokens
            };

            // Compact JSON для экономии токенов на входе (tools могут быть объёмными)
            var requestJson = JsonSerializer.Serialize(request, LlmJsonOptions.Compact);

            // Подсчёт размера контекста для диагностики
            var contextChars = messageList.Sum(m => m.Content?.Length ?? 0);
            var estimatedTokens = contextChars / 4; // Грубая оценка: ~4 символа на токен

            // Логирование
            _logger.LogInformation("[YandexAgent Chat] ========================================");
            _logger.LogInformation("[YandexAgent Chat] >>> ЗАПРОС К /v1/responses");
            _logger.LogInformation("[YandexAgent Chat] Agent: {AgentId}", _config.AgentId);
            _logger.LogInformation("[YandexAgent Chat] PreviousResponseId: {Id}", _lastResponseId ?? "(null - new conversation)");
            _logger.LogInformation("[YandexAgent Chat] Input items: {Count} (total history: {TotalCount}), Tools: {ToolsCount}",
                inputItemsCount, messageList.Count, yandexTools?.Count ?? 0);
            _logger.LogInformation("[YandexAgent Chat] Timeout: {Timeout}s", timeoutSeconds);
            if (maxOutputTokens.HasValue)
            {
                _logger.LogInformation("[YandexAgent Chat] MaxOutputTokens: {MaxTokens}", maxOutputTokens.Value);
            }
            if (!string.IsNullOrEmpty(reasoningEffort))
            {
                _logger.LogInformation("[YandexAgent Chat] ReasoningEffort: {ReasoningEffort}, ReasoningOptions.Mode: {Mode}",
                    reasoningEffort, reasoningOptions?.Mode ?? "null");
            }

            // Логируем только размер запроса, без полного дампа
            _logger.LogDebug("[YandexAgent Chat] Request JSON size: {Length} chars", requestJson.Length);

            progress?.Report($"[YandexAgent Chat] >>> ЗАПРОС К /v1/responses");
            progress?.Report($"[YandexAgent Chat] Agent: {_config.AgentId}");
            if (_lastResponseId != null)
            {
                progress?.Report($"[YandexAgent Chat] Продолжение диалога: отправляю {inputItemsCount} новых элементов");
            }
            else
            {
                progress?.Report($"[YandexAgent Chat] Новый диалог: ~{contextChars} chars (~{estimatedTokens} tokens), Tools: {yandexTools?.Count ?? 0}");
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, YandexAgentApiUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            httpRequest.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
            httpRequest.Headers.Add("x-folder-id", _config.FolderId);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                stopwatch.Stop();
                progress?.Report($"[YandexAgent Chat] === TIMEOUT ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexAgent timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexAgent returned {response.StatusCode}: {errorContent}";
                _logger.LogError("[YandexAgent Chat] Ошибка HTTP: {StatusCode}, Body: {Body}", response.StatusCode, errorContent);
                progress?.Report($"[YandexAgent Chat] ERROR: {response.StatusCode}");
                return result;
            }

            // Читаем SSE streaming ответ
            progress?.Report($"[YandexAgent Chat] === STREAMING RESPONSE ===");
            var fullResponse = new StringBuilder();
            string? responseId = null;
            var toolCalls = new List<LlmToolCall>();
            LlmTokenUsage? usage = null;

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            string? currentData = null;

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);
                if (line == null) continue;

                // SSE формат: "data:{json}" затем "event:event_type"
                if (line.StartsWith("data:"))
                {
                    currentData = line.Substring(5);
                }
                else if (line.StartsWith("event:") && currentData != null)
                {
                    var eventType = line.Substring(6);

                    try
                    {
                        if (eventType == "response.output_text.delta")
                        {
                            var eventObj = JsonSerializer.Deserialize<StreamEventWithId>(currentData, LlmJsonOptions.ForParsing);

                            if (eventObj?.Delta != null)
                            {
                                fullResponse.Append(eventObj.Delta);

                                // Отправляем каждую дельту сразу для real-time streaming в UI
                                // Формат "  delta" - два пробела в начале сигнализируют что это текстовая дельта
                                progress?.Report($"  {eventObj.Delta}");
                            }
                        }
                        else if (eventType == "response.output_item.done")
                        {
                            // Проверяем на function_call
                            var itemEvent = JsonSerializer.Deserialize<OutputItemDoneEvent>(currentData, LlmJsonOptions.ForParsing);

                            if (itemEvent?.Item?.Type == "function_call")
                            {
                                // Приоритетно используем id (уникальный идентификатор), затем call_id
                                var toolCall = new LlmToolCall
                                {
                                    Id = itemEvent.Item.Id ?? itemEvent.Item.CallId ?? Guid.NewGuid().ToString(),
                                    Name = itemEvent.Item.Name ?? "",
                                    Arguments = itemEvent.Item.Arguments ?? "{}",
                                    IsParsedFromText = false // Native API tool call
                                };
                                toolCalls.Add(toolCall);

                                _logger.LogInformation("[YandexAgent Chat] [NATIVE] Tool call: {Name} (id={Id})",
                                    toolCall.Name, toolCall.Id);
                            }
                        }
                        else if (eventType == "response.completed")
                        {
                            var eventObj = JsonSerializer.Deserialize<StreamEventWithId>(currentData, LlmJsonOptions.ForParsing);

                            var status = eventObj?.Response?.Status;
                            var id = eventObj?.Response?.Id;
                            var outputText = eventObj?.Response?.OutputText;
                            var apiUsage = eventObj?.Response?.Usage;

                            // Парсим usage если доступен
                            if (apiUsage != null)
                            {
                                usage = new LlmTokenUsage(
                                    PromptTokens: apiUsage.InputTokens,
                                    CompletionTokens: apiUsage.OutputTokens,
                                    PrecachedPromptTokens: null,
                                    ReasoningTokens: null,
                                    TotalTokens: apiUsage.TotalTokens > 0 ? apiUsage.TotalTokens : apiUsage.InputTokens + apiUsage.OutputTokens
                                );
                                _logger.LogDebug("[YandexAgent Chat] Usage: input={Input}, output={Output}, total={Total}",
                                    apiUsage.InputTokens, apiUsage.OutputTokens, usage.TotalTokens);
                            }

                            _logger.LogInformation("[YandexAgent Chat] response.completed: id={Id}, status={Status}, outputText={OutputLen} chars",
                                id ?? "(null)", status ?? "(null)", outputText?.Length ?? 0);

                            // Диагностика: показываем полный JSON при пустом ответе
                            if (fullResponse.Length == 0 && string.IsNullOrEmpty(outputText))
                            {
                                _logger.LogWarning("[YandexAgent Chat] ДИАГНОСТИКА: Пустой response.completed! Full data: {Data}",
                                    currentData.Length > 500 ? currentData[..500] + "..." : currentData);
                                progress?.Report($"[YandexAgent Chat] ⚠️ Пустой ответ! status={status ?? "null"}");
                            }

                            // Сохраняем ID только если статус completed
                            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                            {
                                responseId = id;
                            }
                            else
                            {
                                // Статус incomplete/failed - сбрасываем previous_response_id
                                // Это единственный случай когда нужен сброс (response пришёл, но не завершён)
                                _logger.LogWarning("[YandexAgent Chat] Response status={Status} (не completed!). Сбрасываю previous_response_id.", status);
                                progress?.Report($"[YandexAgent Chat] ⚠️ status={status} - сброс диалога");
                                _lastResponseId = null;
                            }

                            if (outputText != null && fullResponse.Length == 0)
                            {
                                fullResponse.Append(outputText);
                            }

                            break;
                        }
                        else if (eventType == "response.failed" || eventType == "error")
                        {
                            // Обработка ошибки от API
                            _logger.LogError("[YandexAgent Chat] API Error event: {EventType}, Data: {Data}",
                                eventType, currentData);

                            // Попробуем извлечь сообщение об ошибке
                            string errorMessage;
                            try
                            {
                                using var doc = JsonDocument.Parse(currentData);

                                // Пробуем разные форматы ошибок
                                if (doc.RootElement.TryGetProperty("error", out var errProp))
                                {
                                    // Формат: {"error": {"message": "...", "code": "..."}} или {"error": "..."}
                                    if (errProp.ValueKind == JsonValueKind.Object)
                                    {
                                        var msg = errProp.TryGetProperty("message", out var msgProp)
                                            ? msgProp.GetString()
                                            : null;
                                        var code = errProp.TryGetProperty("code", out var codeProp)
                                            ? codeProp.GetString()
                                            : null;
                                        errorMessage = !string.IsNullOrEmpty(msg)
                                            ? (code != null ? $"{code}: {msg}" : msg)
                                            : currentData;
                                    }
                                    else
                                    {
                                        errorMessage = errProp.GetString() ?? currentData;
                                    }
                                }
                                else if (doc.RootElement.TryGetProperty("message", out var msgProp))
                                {
                                    errorMessage = msgProp.GetString() ?? currentData;
                                }
                                else
                                {
                                    errorMessage = currentData;
                                }
                            }
                            catch
                            {
                                errorMessage = currentData;
                            }

                            // ВАЖНО: Показываем содержимое ошибки пользователю!
                            progress?.Report($"[YandexAgent Chat] ❌ API ERROR: {errorMessage}");

                            // Проверяем специфичные ошибки, требующие сброса диалога
                            if (errorMessage.Contains("tool results does not match") ||
                                errorMessage.Contains("tool calls"))
                            {
                                // Ошибка сопоставления tool results - сбрасываем previous_response_id
                                // чтобы следующая попытка отправила полный контекст
                                _logger.LogWarning("[YandexAgent Chat] Tool results mismatch - resetting conversation");
                                progress?.Report($"[YandexAgent Chat] ⚠️ Сброс диалога из-за ошибки tool results");
                                _lastResponseId = null;
                            }

                            result.ErrorMessage = $"API Error: {errorMessage}";
                            result.IsSuccess = false;
                            break;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("[YandexAgent Chat] JSON parse error for event {EventType}: {Error}",
                            eventType, ex.Message);
                    }

                    currentData = null;
                }
            }

            stopwatch.Stop();
            progress?.Report($"[YandexAgent Chat] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            // Диагностика при пустом ответе
            if (fullResponse.Length == 0 && toolCalls.Count == 0 && result.ErrorMessage == null)
            {
                _logger.LogWarning("[YandexAgent Chat] Empty response! responseId: {ResponseId}, lastResponseId: {LastResponseId}",
                    responseId ?? "(null)", _lastResponseId ?? "(null)");
                progress?.Report($"[YandexAgent Chat] ДИАГНОСТИКА: Пустой ответ, responseId={responseId ?? "null"}, lastResponseId={_lastResponseId ?? "null"}");
            }

            // Сохраняем ID для следующего запроса в этом диалоге
            if (!string.IsNullOrEmpty(responseId))
            {
                _lastResponseId = responseId;
                _logger.LogInformation("[YandexAgent Chat] Saved response ID for next request: {Id}", responseId);
            }
            // НЕ сбрасываем _lastResponseId при ошибке API (rate limit и т.п.)
            // История хранится 30 дней, retry с тем же previous_response_id должен работать
            // Сброс только при явном status=incomplete в response.completed (происходит выше)

            // Подсчитываем native tool calls
            var nativeToolCallsCount = toolCalls.Count;

            // Парсим текстовые tool calls [TOOL_CALL_START] если native не получены
            bool hasTruncatedToolCall = false;
            if (toolCalls.Count == 0 && fullResponse.Length > 0)
            {
                var (textToolCalls, truncated) = TryParseTextToolCallsWithDiagnostics(fullResponse.ToString());
                hasTruncatedToolCall = truncated;
                if (textToolCalls.Count > 0)
                {
                    toolCalls.AddRange(textToolCalls);
                    foreach (var tc in textToolCalls)
                    {
                        _logger.LogInformation("[YandexAgent Chat] [FALLBACK] Tool call: {Name} (id={Id})",
                            tc.Name, tc.Id);
                    }
                }
            }

            var fallbackToolCallsCount = toolCalls.Count - nativeToolCallsCount;

            // Компактное логирование ответа (только при DEBUG)
            _logger.LogDebug("[YandexAgent Chat] Response: id={ResponseId}, text={TextLen} chars, native_tools={NativeCount}, fallback_tools={FallbackCount}",
                responseId ?? "(null)", fullResponse.Length, nativeToolCallsCount, fallbackToolCallsCount);

            // Если есть tool calls - возвращаем их
            if (toolCalls.Count > 0)
            {
                // Извлекаем текст до tool call (рассуждения модели)
                var responseText = ExtractTextBeforeToolCall(fullResponse.ToString());

                result.IsSuccess = true;
                result.Response = responseText;
                result.ResponseId = responseId;
                result.ToolCalls = toolCalls;
                result.Usage = usage;

                // Итоговое логирование с разбивкой native/fallback
                if (fallbackToolCallsCount > 0)
                {
                    _logger.LogInformation("[YandexAgent Chat] <<< TOOL CALLS: {Count} (native={Native}, fallback={Fallback})",
                        toolCalls.Count, nativeToolCallsCount, fallbackToolCallsCount);
                }
                else
                {
                    _logger.LogInformation("[YandexAgent Chat] <<< TOOL CALLS: {Count} (all native)", toolCalls.Count);
                }
            }
            else if (hasTruncatedToolCall)
            {
                // Обнаружен обрезанный tool call - ответ модели был усечён
                // Извлекаем текст до обрезанного tool call для показа пользователю
                var responseText = ExtractTextBeforeToolCall(fullResponse.ToString());

                result.IsSuccess = false;
                result.Response = responseText; // Показываем что модель успела написать
                result.Usage = usage;
                result.ErrorMessage = "Ответ модели был обрезан. Возможно, превышен лимит токенов. Попробуйте переформулировать запрос короче.";
                _logger.LogWarning("[YandexAgent Chat] <<< ОТВЕТ ОБРЕЗАН (truncated tool call)");
                progress?.Report("[YandexAgent Chat] ⚠️ Ответ модели обрезан");
            }
            else if (fullResponse.Length > 0)
            {
                result.IsSuccess = true;
                result.Response = fullResponse.ToString();
                result.ResponseId = responseId;
                result.Usage = usage;
                _logger.LogInformation("[YandexAgent Chat] <<< ОТВЕТ ПОЛУЧЕН");
                progress?.Report($"[YandexAgent Chat] Total response: {fullResponse.Length} chars");
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent returned empty response";
                _logger.LogWarning("[YandexAgent Chat] <<< ПУСТОЙ ОТВЕТ");
                progress?.Report($"[YandexAgent Chat] ERROR: Empty response");
            }

            // Логируем использование токенов в БД
            if (usage != null)
            {
                try
                {
                    await _tokenUsageService.LogUsageAsync(
                        provider: "YandexAgent",
                        model: _config.AgentId ?? "unknown",
                        aiFunction: AiFunctionNames.Chat,
                        usage: usage,
                        sessionId: sessionContext?.SessionId,
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[YandexAgent Chat] Failed to log token usage: {Message}", ex.Message);
                }
            }

            _logger.LogInformation("[YandexAgent Chat] ========================================");
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
            _logger.LogError(ex, "[YandexAgent Chat] Исключение: {Message}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Конвертация истории сообщений в формат input для REST API
    /// </summary>
    private List<object> ConvertMessagesToInput(List<LlmChatMessage> messages)
    {
        var inputList = new List<object>();

        foreach (var msg in messages)
        {
            if (msg.Role == "user")
            {
                // Обычное текстовое сообщение пользователя
                inputList.Add(new { type = "message", role = "user", content = msg.Content });
            }
            else if (msg.Role == "assistant")
            {
                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    // Assistant с вызовами инструментов - добавляем function_call элементы
                    foreach (var tc in msg.ToolCalls)
                    {
                        inputList.Add(new
                        {
                            type = "function_call",
                            call_id = tc.Id,
                            name = tc.Name,
                            arguments = tc.Arguments
                        });
                    }
                }
                else if (!string.IsNullOrEmpty(msg.Content))
                {
                    // Обычное текстовое сообщение ассистента
                    inputList.Add(new { type = "message", role = "assistant", content = msg.Content });
                }
            }
            else if (msg.Role == "tool")
            {
                // Результат выполнения инструмента
                inputList.Add(new
                {
                    type = "function_call_output",
                    call_id = msg.ToolCallId,
                    output = msg.Content
                });
            }
            else if (msg.Role == "system")
            {
                // Системный промпт - добавляем как первое сообщение
                inputList.Insert(0, new { type = "message", role = "system", content = msg.Content });
            }
        }

        return inputList;
    }

    /// <summary>
    /// Получает только новые сообщения для продолжения диалога
    /// Когда есть previous_response_id, YandexAgent уже знает историю
    /// Нужно отправить: function_call + function_call_output пары, или новое сообщение пользователя
    /// ВАЖНО: API требует чтобы function_call СРАЗУ следовал function_call_output с тем же call_id
    /// Порядок: [function_call(a), function_call_output(a), function_call(b), function_call_output(b)]
    /// </summary>
    private List<object> GetNewMessagesForContinuation(List<LlmChatMessage> messages)
    {
        var result = new List<object>();

        // Собираем tool results (role="tool") в словарь по call_id
        var toolResults = new Dictionary<string, string>();
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role == "assistant") break; // Дошли до assistant
            if (msg.Role == "tool" && msg.ToolCallId != null)
            {
                toolResults[msg.ToolCallId] = msg.Content;
            }
        }

        // Ищем assistant с tool calls
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];

            if (msg.Role == "assistant")
            {
                // Если у assistant есть tool calls - добавляем пары [function_call, function_call_output]
                if (msg.ToolCalls?.Count > 0)
                {
                    // Проверяем были ли tool calls parsed из текста
                    var isParsedFromText = msg.ToolCalls[0].IsParsedFromText;

                    if (isParsedFromText)
                    {
                        // Parsed tool calls - модель вывела JSON в тексте, не native API
                        // Отправляем результат как user message
                        foreach (var tc in msg.ToolCalls)
                        {
                            if (toolResults.TryGetValue(tc.Id, out var output))
                            {
                                // Находим исходный вопрос пользователя
                                var lastUserQuestion = FindLastUserQuestion(messages, i);

                                // Проверяем успешность tool result
                                var toolResultMsg = messages.FirstOrDefault(m => m.Role == "tool" && m.ToolCallId == tc.Id);
                                var isError = toolResultMsg?.IsToolError ?? false;

                                string content;
                                if (isError)
                                {
                                    content = LoadPromptTemplate("prompt_tool_error.txt")
                                        .Replace("{{TOOL_NAME}}", tc.Name)
                                        .Replace("{{TOOL_OUTPUT}}", output);
                                }
                                else
                                {
                                    content = LoadPromptTemplate("prompt_tool_result.txt")
                                        .Replace("{{TOOL_NAME}}", tc.Name)
                                        .Replace("{{TOOL_OUTPUT}}", output)
                                        .Replace("{{USER_QUESTION}}", lastUserQuestion);
                                }

                                result.Add(new { type = "message", role = "user", content });
                                _logger.LogDebug("[YandexAgent] Parsed tool result sent as user message (isError={IsError})", isError);
                            }
                        }
                    }
                    else
                    {
                        // Native tool calls - используем function_call / function_call_output
                        foreach (var tc in msg.ToolCalls)
                        {
                            // Сначала function_call
                            result.Add(new
                            {
                                type = "function_call",
                                call_id = tc.Id,
                                name = tc.Name,
                                arguments = tc.Arguments
                            });

                            // Сразу за ним function_call_output
                            if (toolResults.TryGetValue(tc.Id, out var output))
                            {
                                result.Add(new
                                {
                                    type = "function_call_output",
                                    call_id = tc.Id,
                                    output = output
                                });
                            }
                        }
                    }
                }
                break;
            }

            if (msg.Role == "user")
            {
                // Новое сообщение пользователя - добавляем в начало
                result.Insert(0, new { type = "message", role = "user", content = msg.Content });
            }
            // tool messages обрабатываются выше через toolResults
        }

        // Если ничего не нашли (странная ситуация) - отправляем последнее сообщение
        if (result.Count == 0 && messages.Count > 0)
        {
            var last = messages[^1];
            if (last.Role == "user")
            {
                result.Add(new { type = "message", role = "user", content = last.Content });
            }
        }

        return result;
    }

    /// <summary>
    /// Найти последний вопрос пользователя перед указанным индексом
    /// </summary>
    private string FindLastUserQuestion(List<LlmChatMessage> messages, int currentIndex)
    {
        for (int i = currentIndex - 1; i >= 0; i--)
        {
            if (messages[i].Role == "user" && !string.IsNullOrWhiteSpace(messages[i].Content))
            {
                var content = messages[i].Content;
                // Пропускаем сообщения с результатами tool
                if (!content.TrimStart().StartsWith("{") && !content.Contains("=== РЕЗУЛЬТАТ"))
                {
                    return content;
                }
            }
        }
        return "(вопрос не найден)";
    }

    /// <summary>
    /// Конвертация tools в формат Yandex API
    /// </summary>
    private IEnumerable<YandexAgentTool> ConvertTools(IEnumerable<ToolDefinition> tools)
    {
        return tools.Select(t => new YandexAgentTool
        {
            Type = "function",
            Name = t.Name,
            Description = t.Description,
            Parameters = t.ParametersSchema
        });
    }

    // DTOs для REST Assistant API
    private class RestAssistantRequest
    {
        [JsonPropertyName("prompt")]
        public PromptInfo Prompt { get; set; } = new();

        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
    }

    private class PromptInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Переменные для подстановки в промпт агента.
        /// Агент должен содержать плейсхолдеры вида {{ИМЯ_ПЕРЕМЕННОЙ}}
        /// </summary>
        [JsonPropertyName("variables")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Variables { get; set; }
    }

    private class RestAssistantResponse
    {
        [JsonPropertyName("output_text")]
        public string? OutputText { get; set; }

        [JsonPropertyName("output")]
        public OutputItem[]? Output { get; set; }
    }

    private class OutputItem
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    // DTOs для SSE streaming событий
    private class StreamEvent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("delta")]
        public string? Delta { get; set; }

        [JsonPropertyName("content_index")]
        public int? ContentIndex { get; set; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; set; }

        // Для response.completed
        [JsonPropertyName("response")]
        public StreamResponseInfo? Response { get; set; }
    }

    private class StreamResponseInfo
    {
        [JsonPropertyName("output_text")]
        public string? OutputText { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    // DTOs для Chat API с previous_response_id и tools
    private class RestAssistantChatRequest
    {
        [JsonPropertyName("prompt")]
        public PromptInfo Prompt { get; set; } = new();

        [JsonPropertyName("input")]
        public object Input { get; set; } = string.Empty; // string или List<object>

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("previous_response_id")]
        public string? PreviousResponseId { get; set; }

        [JsonPropertyName("tools")]
        public List<YandexAgentTool>? Tools { get; set; }

        /// <summary>
        /// Уровень рассуждений: "low", "medium", "high"
        /// Yandex Cloud параметр для режима рассуждений
        /// </summary>
        [JsonPropertyName("reasoning_effort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningEffort { get; set; }

        /// <summary>
        /// Опции режима рассуждений с параметром mode: "DISABLED" или "ENABLED_HIDDEN"
        /// Yandex Cloud параметр для нативного API (некоторые модели используют этот формат)
        /// </summary>
        [JsonPropertyName("reasoning_options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReasoningOptionsDto? ReasoningOptions { get; set; }

        /// <summary>
        /// Максимальное количество токенов в ответе модели.
        /// Важно при использовании reasoning mode, т.к. "размышления" тоже расходуют токены.
        /// По умолчанию API может иметь низкий лимит, что приводит к обрезанию tool calls.
        /// </summary>
        [JsonPropertyName("max_output_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxOutputTokens { get; set; }
    }

    /// <summary>
    /// DTO для параметра reasoning_options в Yandex Cloud API
    /// </summary>
    private class ReasoningOptionsDto
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "DISABLED";
    }

    // DTO для tools в формате Yandex API
    private class YandexAgentTool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public object Parameters { get; set; } = new { };
    }

    // DTO для парсинга function_call из streaming
    private class OutputItemDoneEvent
    {
        [JsonPropertyName("item")]
        public FunctionCallItem? Item { get; set; }
    }

    private class FunctionCallItem
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("call_id")]
        public string? CallId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
    }

    private class StreamEventWithId
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("delta")]
        public string? Delta { get; set; }

        [JsonPropertyName("content_index")]
        public int? ContentIndex { get; set; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; set; }

        [JsonPropertyName("response")]
        public StreamResponseInfoWithId? Response { get; set; }
    }

    private class StreamResponseInfoWithId
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("output_text")]
        public string? OutputText { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("usage")]
        public YandexAgentUsage? Usage { get; set; }
    }

    /// <summary>
    /// Статистика использования токенов от YandexAgent API
    /// </summary>
    private class YandexAgentUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    /// <summary>
    /// Парсит текстовые tool calls в формате [TOOL_CALL_START]name\n{json}\n[TOOL_CALL_END]
    /// YandexAgent модель иногда выводит вызовы инструментов в таком текстовом формате.
    /// Возвращает кортеж: (список tool calls, флаг обнаружения обрезанного tool call)
    /// </summary>
    private (List<LlmToolCall> ToolCalls, bool HasTruncated) TryParseTextToolCallsWithDiagnostics(string text)
    {
        var result = new List<LlmToolCall>();
        bool hasTruncated = false;

        try
        {
            // Ищем паттерн: [TOOL_CALL_START]tool_name\n{...json...}
            // Опционально: [TOOL_CALL_END]
            const string startTag = "[TOOL_CALL_START]";
            const string endTag = "[TOOL_CALL_END]";

            var startIndex = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            while (startIndex >= 0)
            {
                var afterTag = startIndex + startTag.Length;

                // Ищем имя инструмента (до конца строки или до {)
                var nameEnd = text.IndexOfAny(new[] { '\n', '\r', '{' }, afterTag);
                if (nameEnd < 0)
                {
                    // Нет имени инструмента - вероятно обрезка после [TOOL_CALL_START]
                    hasTruncated = true;
                    _logger.LogWarning("[YandexAgent] TRUNCATED: [TOOL_CALL_START] found but no tool name or JSON");
                    break;
                }

                var toolName = text.Substring(afterTag, nameEnd - afterTag).Trim();
                if (string.IsNullOrEmpty(toolName))
                {
                    startIndex = text.IndexOf(startTag, afterTag, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                // Ищем JSON - начинается с {
                var jsonStart = text.IndexOf('{', nameEnd);
                if (jsonStart < 0)
                {
                    // Нет начала JSON - обрезка после имени инструмента
                    hasTruncated = true;
                    _logger.LogWarning("[YandexAgent] TRUNCATED: Tool {ToolName} found but no JSON start", toolName);
                    break;
                }

                // Ищем конец JSON - либо до [TOOL_CALL_END], либо до следующего [TOOL_CALL_START], либо до конца
                var endTagIndex = text.IndexOf(endTag, jsonStart, StringComparison.OrdinalIgnoreCase);
                var nextStartIndex = text.IndexOf(startTag, jsonStart, StringComparison.OrdinalIgnoreCase);

                int jsonEnd;
                if (endTagIndex >= 0 && (nextStartIndex < 0 || endTagIndex < nextStartIndex))
                {
                    // Есть [TOOL_CALL_END] до следующего [TOOL_CALL_START]
                    jsonEnd = endTagIndex;
                }
                else if (nextStartIndex >= 0)
                {
                    // Есть следующий [TOOL_CALL_START]
                    jsonEnd = nextStartIndex;
                }
                else
                {
                    // До конца строки
                    jsonEnd = text.Length;
                }

                // Извлекаем JSON и находим закрывающую }
                var jsonCandidate = text.Substring(jsonStart, jsonEnd - jsonStart).Trim();

                // Парсим JSON, находя сбалансированные скобки
                var jsonText = ExtractBalancedJson(jsonCandidate);
                if (!string.IsNullOrEmpty(jsonText))
                {
                    // Валидируем JSON
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonText);

                        result.Add(new LlmToolCall
                        {
                            Id = Guid.NewGuid().ToString("N")[..8],
                            Name = toolName,
                            Arguments = jsonText
                        });

                        _logger.LogDebug("[YandexAgent] Parsed text tool call: {Name}({Args})",
                            toolName, jsonText.Length > 100 ? jsonText[..100] + "..." : jsonText);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("[YandexAgent] Invalid JSON in tool call {Name}: {Error}",
                            toolName, ex.Message);
                    }
                }
                else
                {
                    // JSON не сбалансирован - вероятно обрезан из-за лимита токенов
                    hasTruncated = true;
                    _logger.LogWarning("[YandexAgent] TRUNCATED tool call detected! Tool: {ToolName}, JSON candidate ({Length} chars): {Json}",
                        toolName, jsonCandidate.Length, jsonCandidate.Length > 200 ? jsonCandidate[..200] + "..." : jsonCandidate);
                }

                // Ищем следующий
                startIndex = text.IndexOf(startTag, jsonStart + 1, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[YandexAgent] Error parsing text tool calls: {Error}", ex.Message);
        }

        // Если не нашли [TOOL_CALL_START] формат, пробуем markdown code block
        if (result.Count == 0 && !hasTruncated)
        {
            var codeBlockToolCalls = TryParseMarkdownCodeBlockToolCalls(text);
            if (codeBlockToolCalls.Count > 0)
            {
                result.AddRange(codeBlockToolCalls);
            }
        }

        // Если не нашли code block, пробуем inline JSON формат
        if (result.Count == 0 && !hasTruncated)
        {
            var inlineToolCalls = TryParseInlineJsonToolCalls(text);
            if (inlineToolCalls.Count > 0)
            {
                result.AddRange(inlineToolCalls);
            }
        }

        return (result, hasTruncated);
    }

    /// <summary>
    /// Парсит tool calls из markdown code blocks: ```json {...} ``` или ``` {...} ```
    /// Формат JSON: {"name": "tool_name", "arguments": {...}} или {"table": "...", ...}
    /// </summary>
    private List<LlmToolCall> TryParseMarkdownCodeBlockToolCalls(string text)
    {
        var result = new List<LlmToolCall>();

        try
        {
            // Паттерн: ```json\n{...}\n``` или ```\n{...}\n```
            var codeBlockPattern = new System.Text.RegularExpressions.Regex(
                @"```(?:json)?\s*\n?\s*(\{[\s\S]*?\})\s*\n?\s*```",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var matches = codeBlockPattern.Matches(text);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var jsonStr = match.Groups[1].Value.Trim();

                try
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;

                    string? toolName = null;
                    string? arguments = null;

                    // Формат 1: {"name": "query", "arguments": {...}} или {"name": "query", "parameters": {...}}
                    if (root.TryGetProperty("name", out var nameProp))
                    {
                        toolName = nameProp.GetString();

                        if (root.TryGetProperty("arguments", out var argsProp))
                        {
                            arguments = argsProp.GetRawText();
                        }
                        else if (root.TryGetProperty("parameters", out var paramsProp))
                        {
                            arguments = paramsProp.GetRawText();
                        }
                    }
                    // Формат 2: {"table": "Receipts", ...} - прямой вызов query
                    else if (root.TryGetProperty("table", out _))
                    {
                        toolName = "query";
                        arguments = jsonStr;
                    }

                    if (!string.IsNullOrEmpty(toolName) && !string.IsNullOrEmpty(arguments))
                    {
                        result.Add(new LlmToolCall
                        {
                            Id = Guid.NewGuid().ToString("N")[..8],
                            Name = toolName,
                            Arguments = arguments,
                            IsParsedFromText = true
                        });

                        _logger.LogInformation("[YandexAgent] Parsed tool call from markdown code block: {Name}", toolName);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug("[YandexAgent] Invalid JSON in markdown code block: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[YandexAgent] Error parsing markdown code block tool calls: {Error}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Парсит tool calls из inline JSON в тексте (без code blocks)
    /// Формат: {"name": "update_basket", "parameters": {"operations": [...]}}
    /// или {"name": "update_basket", "description": "...", "parameters": {"operations": [...]}}
    /// </summary>
    private List<LlmToolCall> TryParseInlineJsonToolCalls(string text)
    {
        var result = new List<LlmToolCall>();

        try
        {
            // Ищем JSON-подобные структуры, начинающиеся с {"name":
            var namePattern = new System.Text.RegularExpressions.Regex(
                @"\{""name""\s*:\s*""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var matches = namePattern.Matches(text);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var startIndex = match.Index;
                var toolName = match.Groups[1].Value;

                // Извлекаем полный JSON объект с балансировкой скобок
                var jsonText = ExtractBalancedJson(text[startIndex..]);
                if (string.IsNullOrEmpty(jsonText))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    var root = doc.RootElement;

                    string? arguments = null;

                    // Ищем arguments или parameters
                    if (root.TryGetProperty("arguments", out var argsProp))
                    {
                        arguments = argsProp.GetRawText();
                    }
                    else if (root.TryGetProperty("parameters", out var paramsProp))
                    {
                        arguments = paramsProp.GetRawText();
                    }

                    if (!string.IsNullOrEmpty(arguments))
                    {
                        result.Add(new LlmToolCall
                        {
                            Id = Guid.NewGuid().ToString("N")[..8],
                            Name = toolName,
                            Arguments = arguments,
                            IsParsedFromText = true
                        });

                        _logger.LogInformation("[YandexAgent] Parsed tool call from inline JSON: {Name}", toolName);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug("[YandexAgent] Invalid inline JSON: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[YandexAgent] Error parsing inline JSON tool calls: {Error}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Извлекает JSON с балансировкой скобок из начала строки
    /// </summary>
    private static string? ExtractBalancedJson(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith("{"))
            return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[..(i + 1)];
                }
            }
        }

        return null; // Не сбалансировано
    }

    /// <summary>
    /// Извлекает текст до [TOOL_CALL_START] (рассуждения модели)
    /// </summary>
    private static string ExtractTextBeforeToolCall(string text)
    {
        const string startTag = "[TOOL_CALL_START]";
        var index = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);

        if (index > 0)
        {
            return text[..index].Trim();
        }

        return index == 0 ? "" : text;
    }

    /// <summary>
    /// Загружает шаблон промпта из файла. Ищет в текущей директории и в директории сборки.
    /// </summary>
    private string LoadPromptTemplate(string fileName)
    {
        // Пути для поиска файла
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(AppContext.BaseDirectory, "..", fileName),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("[YandexAgent] Loading prompt template from: {Path}", path);
                return File.ReadAllText(path);
            }
        }

        _logger.LogWarning("[YandexAgent] Prompt template not found: {FileName}, searched in: {Paths}",
            fileName, string.Join(", ", searchPaths));

        // Fallback для каждого шаблона
        return fileName switch
        {
            "prompt_tool_result.txt" => @"=== РЕЗУЛЬТАТ ИНСТРУМЕНТА {{TOOL_NAME}} ===

{{TOOL_OUTPUT}}

=== ВОПРОС ПОЛЬЗОВАТЕЛЯ ===
{{USER_QUESTION}}

=== ТВОЯ ЗАДАЧА ===
Используя данные выше, ответь на вопрос пользователя.",
            "prompt_tool_error.txt" => @"Вызов инструмента {{TOOL_NAME}} завершился с ОШИБКОЙ:

{{TOOL_OUTPUT}}

Проанализируй ошибку и исправь вызов инструмента.",
            _ => $"Template {fileName} not found"
        };
    }
}
