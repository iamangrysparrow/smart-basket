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

    public string Name => $"YandexAgent/{_config.AgentId}";

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
}
