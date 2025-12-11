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

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json")
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
        }

        return result;
    }

    // DTOs
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
}
