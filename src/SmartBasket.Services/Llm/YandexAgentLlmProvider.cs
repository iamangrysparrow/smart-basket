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

            // REST Assistant API формат запроса
            var request = new RestAssistantRequest
            {
                Prompt = new PromptInfo { Id = _config.AgentId },
                Input = prompt
            };

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });

            // Логирование в формате ARCHITECTURE-AI.md
            progress?.Report($"[YandexAgent] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"[YandexAgent] === PROMPT END ===");
            progress?.Report($"[YandexAgent] Sending request to {YandexAgentApiUrl} (agent: {_config.AgentId}, timeout: {timeoutSeconds}s)...");
            progress?.Report($"[YandexAgent] Request JSON: {requestJson}");

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
                response = await client.SendAsync(httpRequest, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                stopwatch.Stop();
                progress?.Report($"[YandexAgent] === TIMEOUT ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexAgent timeout after {timeoutSeconds}s";
                return result;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            progress?.Report($"[YandexAgent] === RESPONSE ===");
            progress?.Report(responseContent);
            progress?.Report($"[YandexAgent] === END RESPONSE ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            if (!response.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"YandexAgent returned {response.StatusCode}: {responseContent}";
                _logger.LogError("YandexAgent error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                progress?.Report($"[YandexAgent] ERROR: {response.StatusCode}");
                return result;
            }

            progress?.Report($"[YandexAgent] Total response: {responseContent.Length} chars");

            // Парсим ответ REST Assistant API
            try
            {
                var responseObj = JsonSerializer.Deserialize<RestAssistantResponse>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (responseObj != null && !string.IsNullOrEmpty(responseObj.OutputText))
                {
                    result.IsSuccess = true;
                    result.Response = responseObj.OutputText;
                    progress?.Report($"[YandexAgent] Parsed output_text: {responseObj.OutputText.Length} chars");
                }
                else if (responseObj?.Output != null && responseObj.Output.Length > 0)
                {
                    // Альтернативный формат с массивом output
                    var textParts = responseObj.Output
                        .Where(o => o.Type == "text" && !string.IsNullOrEmpty(o.Text))
                        .Select(o => o.Text);
                    var fullText = string.Join("", textParts);

                    if (!string.IsNullOrEmpty(fullText))
                    {
                        result.IsSuccess = true;
                        result.Response = fullText;
                        progress?.Report($"[YandexAgent] Parsed output array: {fullText.Length} chars");
                    }
                    else
                    {
                        result.IsSuccess = false;
                        result.ErrorMessage = "YandexAgent returned empty response";
                        progress?.Report($"[YandexAgent] ERROR: Empty response in output array");
                    }
                }
                else
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"YandexAgent returned unexpected format: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                    progress?.Report($"[YandexAgent] ERROR: Unexpected response format");
                }
            }
            catch (JsonException ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Failed to parse YandexAgent response: {ex.Message}";
                _logger.LogError(ex, "Failed to parse YandexAgent response: {Content}", responseContent);
                progress?.Report($"[YandexAgent] ERROR: JSON parse failed - {ex.Message}");
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
}
