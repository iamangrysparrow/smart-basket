using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Ollama;

public class ProductClassificationService : IProductClassificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProductClassificationService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;

    public ProductClassificationService(IHttpClientFactory httpClientFactory, ILogger<ProductClassificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null;
    }

    public async Task<ProductClassificationResult> ClassifyAsync(
        OllamaSettings settings,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<ExistingProduct> existingProducts,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ProductClassificationResult();

        try
        {
            if (itemNames.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No items to classify";
                return result;
            }

            progress?.Report($"  [Classify] Classifying {itemNames.Count} items with {existingProducts.Count} existing products...");

            var prompt = BuildPrompt(itemNames, existingProducts, progress);
            progress?.Report($"  [Classify] Prompt ready: {prompt.Length} chars");
            progress?.Report($"  [Classify] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [Classify] === PROMPT END ===");

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(settings.TimeoutSeconds, 120);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var request = new OllamaGenerateRequest
            {
                Model = settings.Model,
                Prompt = prompt,
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = 0.1
                }
            };

            progress?.Report($"  [Classify] Sending STREAMING request to {settings.BaseUrl}/api/generate (model: {settings.Model}, timeout: {timeoutSeconds}s)...");

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
                stopwatch.Stop();
                progress?.Report($"  [Classify] TIMEOUT after {stopwatch.Elapsed.TotalSeconds:F1}s");
                result.IsSuccess = false;
                result.Message = $"Ollama timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                progress?.Report($"  [Classify] Error: {errorContent}");
                result.IsSuccess = false;
                result.Message = $"Ollama returned {response.StatusCode}";
                return result;
            }

            // Read streaming response
            progress?.Report($"  [Classify] === STREAMING RESPONSE ===");
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
            progress?.Report($"  [Classify] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");
            progress?.Report($"  [Classify] Total response: {fullResponse.Length} chars");

            var ollamaResponse = fullResponse.ToString();
            result.RawResponse = ollamaResponse;

            // Extract JSON object from response
            var jsonMatch = Regex.Match(ollamaResponse, @"\{[\s\S]*\}", RegexOptions.Multiline);
            if (jsonMatch.Success)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<ClassificationResponse>(
                        jsonMatch.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (parsed != null)
                    {
                        result.Products = parsed.Products;
                        result.Items = parsed.Items;
                        result.IsSuccess = true;
                        result.Message = $"Classified {result.Items.Count} items into {result.Products.Count} products";
                        progress?.Report($"  [Classify] {result.Message}");
                    }
                }
                catch (JsonException ex)
                {
                    progress?.Report($"  [Classify] JSON parse error: {ex.Message}");
                    result.IsSuccess = false;
                    result.Message = $"Failed to parse JSON: {ex.Message}";
                }
            }
            else
            {
                progress?.Report("  [Classify] No JSON object found in response");
                result.IsSuccess = false;
                result.Message = "No JSON found in response";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress?.Report("  [Classify] Cancelled by user");
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (OperationCanceledException)
        {
            progress?.Report("  [Classify] Timed out");
            result.IsSuccess = false;
            result.Message = "Classification request timed out";
        }
        catch (Exception ex)
        {
            progress?.Report($"  [Classify] Error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
        }

        return result;
    }

    private string BuildPrompt(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<ExistingProduct> existingProducts,
        IProgress<string>? progress = null)
    {
        // Try to load template from file
        if (!string.IsNullOrEmpty(_promptTemplatePath) && File.Exists(_promptTemplatePath))
        {
            try
            {
                _promptTemplate = File.ReadAllText(_promptTemplatePath);
                progress?.Report($"  [Classify] Loaded template from: {_promptTemplatePath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Classify] Failed to load template: {ex.Message}");
                _promptTemplate = null;
            }
        }

        var hierarchyText = BuildHierarchyText(existingProducts);
        var itemsList = string.Join("\n", itemNames.Select((item, idx) => $"{idx + 1}. {item}"));

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            return _promptTemplate
                .Replace("{{EXISTING_HIERARCHY}}", hierarchyText)
                .Replace("{{ITEMS}}", itemsList);
        }

        // Default prompt (fallback)
        return $@"Выдели продукты из списка товаров и построй иерархию.

СУЩЕСТВУЮЩАЯ ИЕРАРХИЯ ПРОДУКТОВ:
{hierarchyText}

ТОВАРЫ ДЛЯ КЛАССИФИКАЦИИ:
{itemsList}

ФОРМАТ ОТВЕТА (строго JSON):
{{""products"":[{{""name"":"""",""parent"":null,""is_new"":false}}],""items"":[{{""name"":"""",""product"":""""}}]}}

Классифицируй товары:";
    }

    private string BuildHierarchyText(IReadOnlyList<ExistingProduct> existingProducts)
    {
        if (existingProducts.Count == 0)
        {
            return "(пусто - создай новые продукты)";
        }

        var sb = new StringBuilder();

        // Group by parent for hierarchical display
        var rootProducts = existingProducts.Where(p => p.ParentId == null).ToList();
        var childrenByParent = existingProducts
            .Where(p => p.ParentId != null)
            .GroupBy(p => p.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var root in rootProducts)
        {
            sb.AppendLine($"- {root.Name}");

            if (childrenByParent.TryGetValue(root.Id, out var children))
            {
                foreach (var child in children)
                {
                    sb.AppendLine($"  - {child.Name}");

                    // Level 2 children
                    if (childrenByParent.TryGetValue(child.Id, out var grandChildren))
                    {
                        foreach (var grandChild in grandChildren)
                        {
                            sb.AppendLine($"    - {grandChild.Name}");
                        }
                    }
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    // DTOs for Ollama API
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
        public int NumPredict { get; set; } = 4096;

        [JsonPropertyName("num_ctx")]
        public int NumCtx { get; set; } = 8192;
    }

    private class OllamaStreamChunk
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
}
