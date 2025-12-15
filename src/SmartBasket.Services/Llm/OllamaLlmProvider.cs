using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для Ollama (локальные модели)
/// </summary>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaLlmProvider> _logger;
    private readonly AiProviderConfig _config;

    public string Name => $"Ollama/{_config.Model}";

    public bool SupportsConversationReset => false;

    public void ResetConversation()
    {
        // Ollama не хранит состояние — ничего не делаем
    }

    public OllamaLlmProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaLlmProvider> logger,
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
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var baseUrl = _config.BaseUrl ?? "http://localhost:11434";
            var response = await client.GetAsync($"{baseUrl}/api/tags", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (true, $"Ollama connected: {baseUrl}, model: {_config.Model}");
            }

            return (false, $"Ollama returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Ollama connection failed: {ex.Message}");
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
            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Использовать настройки из конфига, если параметры не переданы явно
            var actualMaxTokens = _config.MaxTokens ?? maxTokens;
            var actualTemperature = _config.Temperature > 0 ? _config.Temperature : temperature;
            var baseUrl = _config.BaseUrl ?? "http://localhost:11434";

            var request = new OllamaRequest
            {
                Model = _config.Model,
                Prompt = prompt,
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = actualTemperature,
                    NumPredict = actualMaxTokens
                }
            };

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
            var requestUrl = $"{baseUrl}/api/generate";

            // Детальное логирование запроса
            _logger.LogInformation("[Ollama] ========================================");
            _logger.LogInformation("[Ollama] >>> ЗАПРОС К OLLAMA");
            _logger.LogInformation("[Ollama] Config.Key: {Key}", _config.Key);
            _logger.LogInformation("[Ollama] Config.Model: {Model}", _config.Model);
            _logger.LogInformation("[Ollama] Config.BaseUrl: {BaseUrl}", _config.BaseUrl);
            _logger.LogInformation("[Ollama] URL: {Url}", requestUrl);
            _logger.LogInformation("[Ollama] Timeout: {Timeout}s", timeoutSeconds);
            _logger.LogInformation("[Ollama] Request JSON:\n{Json}", requestJson);

            // Логирование через progress (попадёт в системный лог)
            progress?.Report($"[Ollama] >>> ЗАПРОС К OLLAMA");
            progress?.Report($"[Ollama] Config.Key: {_config.Key}");
            progress?.Report($"[Ollama] Config.Model: {_config.Model}");
            progress?.Report($"[Ollama] URL: {requestUrl}");
            progress?.Report($"[Ollama] Timeout: {timeoutSeconds}s");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response;
            try
            {
                _logger.LogInformation("[Ollama] Отправляю HTTP запрос...");
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                _logger.LogInformation("[Ollama] HTTP ответ: {StatusCode}", response.StatusCode);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("[Ollama] Timeout после {Timeout}s", timeoutSeconds);
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("[Ollama] Ошибка HTTP: {StatusCode}, Body: {Body}", response.StatusCode, errorContent);
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama returned {response.StatusCode}: {errorContent}";
                return result;
            }

            // Read streaming response
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
                    var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk?.Response != null)
                    {
                        fullResponse.Append(chunk.Response);

                        // Buffer for line-by-line progress reporting
                        foreach (var ch in chunk.Response)
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

                    if (chunk?.Done == true)
                    {
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
                    // Skip malformed lines
                }
            }

            result.IsSuccess = true;
            result.Response = fullResponse.ToString();

            _logger.LogInformation("[Ollama] <<< ОТВЕТ ПОЛУЧЕН");
            _logger.LogInformation("[Ollama] Response length: {Length} chars", result.Response?.Length ?? 0);
            var responsePreview = result.Response?.Length > 500
                ? result.Response.Substring(0, 500) + "..."
                : result.Response;
            _logger.LogInformation("[Ollama] Response preview:\n{Preview}", responsePreview);
            _logger.LogInformation("[Ollama] ========================================");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[Ollama] Операция отменена пользователем");
            result.IsSuccess = false;
            result.ErrorMessage = "Operation cancelled by user";
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Ollama] Таймаут генерации");
            result.IsSuccess = false;
            result.ErrorMessage = "Generation timed out";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Ollama] Исключение: {Message}", ex.Message);
            result.IsSuccess = false;
            result.ErrorMessage = $"Error: {ex.Message}";
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
            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var actualMaxTokens = _config.MaxTokens ?? maxTokens;
            var actualTemperature = _config.Temperature > 0 ? _config.Temperature : temperature;
            var baseUrl = _config.BaseUrl ?? "http://localhost:11434";

            // Конвертируем сообщения в формат Ollama
            var ollamaMessages = messages.Select(m => new OllamaChatMessage
            {
                Role = m.Role,
                Content = m.Content
            }).ToArray();

            var request = new OllamaChatRequest
            {
                Model = _config.Model,
                Messages = ollamaMessages,
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = actualTemperature,
                    NumPredict = actualMaxTokens
                }
            };

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
            var requestUrl = $"{baseUrl}/api/chat";

            _logger.LogInformation("[Ollama Chat] ========================================");
            _logger.LogInformation("[Ollama Chat] >>> ЗАПРОС К OLLAMA /api/chat");
            _logger.LogInformation("[Ollama Chat] Config.Model: {Model}", _config.Model);
            _logger.LogInformation("[Ollama Chat] Messages count: {Count}", ollamaMessages.Length);
            _logger.LogInformation("[Ollama Chat] URL: {Url}", requestUrl);

            progress?.Report($"[Ollama Chat] >>> ЗАПРОС К /api/chat");
            progress?.Report($"[Ollama Chat] Model: {_config.Model}, Messages: {ollamaMessages.Length}");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("[Ollama Chat] Ошибка HTTP: {StatusCode}, Body: {Body}", response.StatusCode, errorContent);
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama returned {response.StatusCode}: {errorContent}";
                return result;
            }

            // Read streaming response
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
                    var chunk = JsonSerializer.Deserialize<OllamaChatStreamChunk>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk?.Message?.Content != null)
                    {
                        fullResponse.Append(chunk.Message.Content);

                        foreach (var ch in chunk.Message.Content)
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

                    if (chunk?.Done == true)
                    {
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
                    // Skip malformed lines
                }
            }

            result.IsSuccess = true;
            result.Response = fullResponse.ToString();

            _logger.LogInformation("[Ollama Chat] <<< ОТВЕТ ПОЛУЧЕН");
            _logger.LogInformation("[Ollama Chat] Response length: {Length} chars", result.Response?.Length ?? 0);
            _logger.LogInformation("[Ollama Chat] ========================================");
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
            _logger.LogError(ex, "[Ollama Chat] Исключение: {Message}", ex.Message);
            result.IsSuccess = false;
            result.ErrorMessage = $"Error: {ex.Message}";
        }

        return result;
    }

    // DTOs for /api/generate
    private class OllamaRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; } = 2000;
    }

    private class OllamaStreamChunk
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    // DTOs for /api/chat
    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public OllamaChatMessage[] Messages { get; set; } = Array.Empty<OllamaChatMessage>();

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class OllamaChatStreamChunk
    {
        [JsonPropertyName("message")]
        public OllamaChatMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
}
