using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Ollama;

public class LabelAssignmentService : ILabelAssignmentService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LabelAssignmentService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;

    public LabelAssignmentService(IHttpClientFactory httpClientFactory, ILogger<LabelAssignmentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null;
    }

    public async Task<LabelAssignmentResult> AssignLabelsAsync(
        OllamaSettings settings,
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LabelAssignmentResult();

        try
        {
            if (availableLabels.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No labels available";
                return result;
            }

            var prompt = BuildPrompt(itemName, productName, availableLabels, progress);
            progress?.Report($"  [Labels] Assigning labels for: {itemName}");
            progress?.Report($"  [Labels] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [Labels] === PROMPT END ===");

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(settings.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var request = new OllamaGenerateRequest
            {
                Model = settings.Model,
                Prompt = prompt,
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = 0.1,
                    NumPredict = 256 // Labels are short
                }
            };

            progress?.Report($"  [Labels] Sending STREAMING request to {settings.BaseUrl}/api/generate (model: {settings.Model}, timeout: {timeoutSeconds}s)...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl}/api/generate")
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
                progress?.Report($"  [Labels] TIMEOUT after {timeoutSeconds}s");
                result.IsSuccess = false;
                result.Message = $"Ollama timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                progress?.Report($"  [Labels] Error: {errorContent}");
                result.IsSuccess = false;
                result.Message = $"Ollama returned {response.StatusCode}";
                return result;
            }

            // Read streaming response
            progress?.Report($"  [Labels] === STREAMING RESPONSE ===");
            var fullResponse = new StringBuilder();
            var lineBuffer = new StringBuilder(); // Buffer for accumulating text until newline

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

                        // Buffer text and output line by line (like receipt parsing)
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
                        // Output any remaining text in buffer
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

            stopwatch.Stop();
            progress?.Report($"  [Labels] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            var ollamaResponse = fullResponse.ToString();
            result.RawResponse = ollamaResponse;
            progress?.Report($"  [Labels] Total response: {fullResponse.Length} chars");

            // Extract JSON array from response
            var jsonMatch = Regex.Match(ollamaResponse, @"\[[\s\S]*?\]", RegexOptions.Multiline);
            if (jsonMatch.Success)
            {
                try
                {
                    var labels = JsonSerializer.Deserialize<List<string>>(jsonMatch.Value);
                    if (labels != null)
                    {
                        // Filter to only include valid labels
                        var validLabels = labels
                            .Where(l => availableLabels.Contains(l, StringComparer.OrdinalIgnoreCase))
                            .ToList();

                        result.AssignedLabels = validLabels;
                        result.IsSuccess = true;
                        result.Message = validLabels.Count > 0
                            ? $"Assigned {validLabels.Count} labels"
                            : "No labels assigned";
                    }
                }
                catch (JsonException ex)
                {
                    result.IsSuccess = false;
                    result.Message = $"Failed to parse JSON: {ex.Message}";
                }
            }
            else
            {
                // No labels assigned is OK
                result.IsSuccess = true;
                result.Message = "No labels assigned";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only rethrow if the external token was cancelled (user cancel)
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (OperationCanceledException)
        {
            // Internal timeout - don't throw, just return failure
            result.IsSuccess = false;
            result.Message = "Label assignment timed out";
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
        }

        return result;
    }

    private string BuildPrompt(
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
        IProgress<string>? progress = null)
    {
        // Try to load template from file
        if (!string.IsNullOrEmpty(_promptTemplatePath) && File.Exists(_promptTemplatePath))
        {
            try
            {
                _promptTemplate = File.ReadAllText(_promptTemplatePath);
            }
            catch
            {
                _promptTemplate = null;
            }
        }

        var labelsList = string.Join("\n", availableLabels.Select(l => $"- {l}"));

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            return _promptTemplate
                .Replace("{{LABELS}}", labelsList)
                .Replace("{{ITEM_NAME}}", itemName)
                .Replace("{{PRODUCT_NAME}}", productName);
        }

        // Default prompt (fallback)
        return $@"Назначь подходящие метки для товара.

ДОСТУПНЫЕ МЕТКИ:
{labelsList}

ТОВАР: {itemName}
ПРОДУКТ: {productName}

ФОРМАТ ОТВЕТА (строго JSON массив):
[""Метка1"", ""Метка2""]

Назначь метки:";
    }

    // DTOs
    private class OllamaGenerateRequest
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
        public int NumPredict { get; set; } = 256;
    }

    private class OllamaStreamChunk
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
}
