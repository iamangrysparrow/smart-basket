using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Llm;

namespace SmartBasket.Services.Ollama;

public class ProductClassificationService : IProductClassificationService
{
    private readonly ILlmProviderFactory _providerFactory;
    private readonly ILogger<ProductClassificationService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;

    public ProductClassificationService(ILlmProviderFactory providerFactory, ILogger<ProductClassificationService> logger)
    {
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null;
    }

    public async Task<ProductClassificationResult> ClassifyAsync(
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

            // Получаем провайдер для классификации
            var provider = _providerFactory.GetProviderForOperation(LlmOperationType.Classification);
            progress?.Report($"  [Classify] Using provider: {provider.Name}");
            progress?.Report($"  [Classify] Classifying {itemNames.Count} items with {existingProducts.Count} existing products...");

            var prompt = BuildPrompt(itemNames, existingProducts, progress);
            progress?.Report($"  [Classify] Prompt ready: {prompt.Length} chars");
            progress?.Report($"  [Classify] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [Classify] === PROMPT END ===");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Classify] Sending request via {provider.Name}...");

            var llmResult = await provider.GenerateAsync(
                prompt,
                maxTokens: 4000,
                temperature: 0.1,
                progress: progress,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            progress?.Report($"  [Classify] Response received in {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!llmResult.IsSuccess || string.IsNullOrEmpty(llmResult.Response))
            {
                result.IsSuccess = false;
                result.Message = llmResult.ErrorMessage ?? "Empty response from LLM";
                return result;
            }

            result.RawResponse = llmResult.Response;
            progress?.Report($"  [Classify] Total response: {llmResult.Response.Length} chars");

            // Extract JSON object from response
            var jsonMatch = Regex.Match(llmResult.Response, @"\{[\s\S]*\}", RegexOptions.Multiline);
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
        catch (Exception ex)
        {
            progress?.Report($"  [Classify] Error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Classification error");
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
}
