using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для Yandex AI Agent (через REST Assistant API)
/// https://rest-assistant.api.cloud.yandex.net/v1/responses
/// </summary>
public class YandexAgentLlmProvider : ILlmProvider
{
    // REST Assistant API endpoint для Yandex AI Studio агентов
    private const string YandexAgentApiUrl = "https://rest-assistant.api.cloud.yandex.net/v1/responses";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YandexAgentLlmProvider> _logger;
    private readonly AiProviderConfig _config;

    // ID последнего ответа для поддержки истории диалога
    private string? _lastResponseId;

    public string Name => $"YandexAgent/{_config.AgentId}";

    public bool SupportsConversationReset => true;

    public void ResetConversation()
    {
        _lastResponseId = null;
        _logger.LogInformation("[YandexAgent] Conversation reset, previous_response_id cleared");
    }

    public YandexAgentLlmProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<YandexAgentLlmProvider> logger,
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
                maxTokens: 50,
                temperature: 0.1,
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

    public async Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        int maxTokens = 2000,
        double temperature = 0.1,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
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
            var request = new RestAssistantRequest
            {
                Prompt = new PromptInfo { Id = _config.AgentId },
                Input = prompt,
                Stream = true  // Включаем streaming
            };

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });

            // Логирование в формате ARCHITECTURE-AI.md
            progress?.Report($"[YandexAgent] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"[YandexAgent] === PROMPT END ===");
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
                        var eventObj = JsonSerializer.Deserialize<StreamEvent>(currentData,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
        int maxTokens = 2000,
        double temperature = 0.7,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
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

            // Берём последнее сообщение пользователя
            var messageList = messages.ToList();
            var lastUserMessage = messageList.LastOrDefault(m => m.Role == "user");
            if (lastUserMessage == null)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "No user message found in chat history";
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 120);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // REST Assistant API формат запроса с previous_response_id для истории
            var request = new RestAssistantChatRequest
            {
                Prompt = new PromptInfo { Id = _config.AgentId },
                Input = lastUserMessage.Content,
                Stream = true,
                PreviousResponseId = _lastResponseId  // null при первом запросе
            };

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            // Логирование
            _logger.LogInformation("[YandexAgent Chat] ========================================");
            _logger.LogInformation("[YandexAgent Chat] >>> ЗАПРОС К /v1/responses");
            _logger.LogInformation("[YandexAgent Chat] Agent: {AgentId}", _config.AgentId);
            _logger.LogInformation("[YandexAgent Chat] PreviousResponseId: {Id}", _lastResponseId ?? "(null - new conversation)");
            _logger.LogInformation("[YandexAgent Chat] Messages count: {Count}", messageList.Count);

            progress?.Report($"[YandexAgent Chat] >>> ЗАПРОС К /v1/responses");
            progress?.Report($"[YandexAgent Chat] Agent: {_config.AgentId}");
            progress?.Report($"[YandexAgent Chat] PreviousResponseId: {_lastResponseId ?? "(new conversation)"}");

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
            var lineBuffer = new StringBuilder();
            string? responseId = null;

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
                        var eventObj = JsonSerializer.Deserialize<StreamEventWithId>(currentData,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (eventType == "response.output_text.delta" && eventObj?.Delta != null)
                        {
                            fullResponse.Append(eventObj.Delta);

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
                            // Получаем ID ответа для следующего запроса
                            responseId = eventObj?.Response?.Id;

                            if (eventObj?.Response?.OutputText != null && fullResponse.Length == 0)
                            {
                                fullResponse.Append(eventObj.Response.OutputText);
                            }

                            if (lineBuffer.Length > 0)
                            {
                                progress?.Report($"  {lineBuffer}");
                                lineBuffer.Clear();
                            }

                            _logger.LogInformation("[YandexAgent Chat] response.completed, id: {Id}", responseId ?? "(null)");
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
            progress?.Report($"[YandexAgent Chat] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            if (fullResponse.Length > 0)
            {
                result.IsSuccess = true;
                result.Response = fullResponse.ToString();
                result.ResponseId = responseId;

                // Сохраняем ID для следующего запроса в этом диалоге
                if (!string.IsNullOrEmpty(responseId))
                {
                    _lastResponseId = responseId;
                    _logger.LogInformation("[YandexAgent Chat] Saved response ID for next request: {Id}", responseId);
                }

                progress?.Report($"[YandexAgent Chat] Total response: {fullResponse.Length} chars");
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexAgent returned empty response";
                progress?.Report($"[YandexAgent Chat] ERROR: Empty response");
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
            _logger.LogError(ex, "[YandexAgent Chat] Исключение: {Message}", ex.Message);
        }

        return result;
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

    // DTOs для Chat API с previous_response_id
    private class RestAssistantChatRequest
    {
        [JsonPropertyName("prompt")]
        public PromptInfo Prompt { get; set; } = new();

        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("previous_response_id")]
        public string? PreviousResponseId { get; set; }
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
    }
}
