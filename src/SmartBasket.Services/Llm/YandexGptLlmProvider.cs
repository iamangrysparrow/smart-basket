using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для YandexGPT (Yandex Cloud) с поддержкой стриминга
/// https://cloud.yandex.ru/docs/yandexgpt/api-ref/v1/TextGeneration/completion
/// </summary>
public class YandexGptLlmProvider : ILlmProvider
{
    private const string YandexGptApiUrl = "https://llm.api.cloud.yandex.net/foundationModels/v1/completion";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YandexGptLlmProvider> _logger;
    private readonly YandexGptSettings _settings;

    public string Name => "YandexGPT";

    public YandexGptLlmProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<YandexGptLlmProvider> logger,
        YandexGptSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _settings = settings;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                return (false, "YandexGPT API key is not configured");
            }

            if (string.IsNullOrWhiteSpace(_settings.FolderId))
            {
                return (false, "YandexGPT Folder ID is not configured");
            }

            // Делаем тестовый запрос
            var result = await GenerateAsync(
                "Привет! Ответь одним словом: Работает",
                maxTokens: 10,
                temperature: 0.1,
                cancellationToken: cancellationToken);

            if (result.IsSuccess)
            {
                return (true, $"YandexGPT connected, model: {_settings.Model}");
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
        int maxTokens = 2000,
        double temperature = 0.1,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LlmGenerationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT API key is not configured";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_settings.FolderId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "YandexGPT Folder ID is not configured";
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_settings.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Формируем modelUri
            // Для обычных моделей: gpt://{folder_id}/yandexgpt-lite/latest
            // Для Alice и других general моделей: gpt://{folder_id}/general:alice (без /latest)
            string modelUri;
            if (_settings.Model.StartsWith("general:"))
            {
                // General модели (Alice и др.) - без /latest
                modelUri = $"gpt://{_settings.FolderId}/{_settings.Model}";
            }
            else
            {
                // Обычные YandexGPT модели - с /latest
                modelUri = $"gpt://{_settings.FolderId}/{_settings.Model}/latest";
            }

            var request = new YandexGptRequest
            {
                ModelUri = modelUri,
                CompletionOptions = new YandexCompletionOptions
                {
                    Stream = true, // Включаем стриминг
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

            // Добавляем авторизацию
            // Поддерживаем оба формата: IAM token и API key
            if (_settings.ApiKey.StartsWith("t1.") || _settings.ApiKey.StartsWith("y"))
            {
                // IAM token
                httpRequest.Headers.Add("Authorization", $"Bearer {_settings.ApiKey}");
            }
            else
            {
                // API key
                httpRequest.Headers.Add("Authorization", $"Api-Key {_settings.ApiKey}");
            }

            httpRequest.Headers.Add("x-folder-id", _settings.FolderId);

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

            // Читаем streaming ответ
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
                    // YandexGPT streaming возвращает JSON объекты построчно
                    var chunk = JsonSerializer.Deserialize<YandexStreamChunk>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk?.Result?.Alternatives?.Length > 0)
                    {
                        var alternative = chunk.Result.Alternatives[0];
                        var text = alternative.Message?.Text ?? string.Empty;

                        // YandexGPT отправляет полный текст в каждом чанке (накопленный)
                        // Нужно вычислить дельту
                        var newText = text;
                        if (text.Length > fullResponse.Length)
                        {
                            newText = text.Substring(fullResponse.Length);
                        }
                        else
                        {
                            // Полный текст в финальном сообщении
                            newText = text.Length == fullResponse.Length ? "" : text;
                        }

                        if (!string.IsNullOrEmpty(newText))
                        {
                            // Буферизация для построчного вывода
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

                        // Обновляем полный ответ
                        if (text.Length > fullResponse.Length)
                        {
                            fullResponse.Clear();
                            fullResponse.Append(text);
                        }

                        // Проверяем статус завершения
                        if (alternative.Status == "ALTERNATIVE_STATUS_FINAL" ||
                            alternative.Status == "ALTERNATIVE_STATUS_COMPLETE")
                        {
                            // Выводим остаток буфера
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

    // Streaming response chunk
    private class YandexStreamChunk
    {
        [JsonPropertyName("result")]
        public YandexGptResult? Result { get; set; }
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

    private class YandexAlternative
    {
        [JsonPropertyName("message")]
        public YandexMessage? Message { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
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
