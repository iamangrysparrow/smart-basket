using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

public class ProductClassificationService : IProductClassificationService
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly IResponseParser _responseParser;
    private readonly ILogger<ProductClassificationService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;
    private string? _customPrompt;

    public ProductClassificationService(
        IAiProviderFactory providerFactory,
        IResponseParser responseParser,
        ILogger<ProductClassificationService> logger)
    {
        _providerFactory = providerFactory;
        _responseParser = responseParser;
        _logger = logger;
    }

    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null;
    }

    public void SetCustomPrompt(string? prompt)
    {
        _customPrompt = prompt;
        _logger.LogDebug("Custom prompt set: {HasPrompt}", !string.IsNullOrWhiteSpace(prompt));
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
            _logger.LogDebug("Getting provider for Classification operation");
            var provider = _providerFactory.GetProviderForOperation(AiOperation.Classification);
            if (provider == null)
            {
                var errorMsg = "No AI provider configured for Classification operation. Check AiOperations.Classification in settings.";
                _logger.LogError(errorMsg);
                progress?.Report($"  [Classify] ERROR: {errorMsg}");
                result.IsSuccess = false;
                result.Message = errorMsg;
                return result;
            }
            _logger.LogDebug("Got provider: {ProviderName}", provider.Name);
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

            // Extract and parse JSON using unified ResponseParser
            var parseResult = _responseParser.ParseJsonObject<ClassificationResponse>(llmResult.Response, progress);

            if (parseResult.IsSuccess && parseResult.Data != null)
            {
                result.Products = parseResult.Data.Products;
                result.Items = parseResult.Data.Items;
                result.IsSuccess = true;
                result.Message = $"Classified {result.Items.Count} items into {result.Products.Count} products";
                progress?.Report($"  [Classify] {result.Message} (method: {parseResult.ExtractionMethod})");
            }
            else
            {
                progress?.Report($"  [Classify] JSON parse error: {parseResult.ErrorMessage}");
                result.IsSuccess = false;
                result.Message = parseResult.ErrorMessage ?? "Failed to parse JSON";
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
        var hierarchyText = BuildHierarchyText(existingProducts);
        var hierarchyJson = BuildHierarchyJson(existingProducts);
        var itemsList = string.Join("\n", itemNames.Select((item, idx) => $"{idx + 1}. {item}"));

        // Priority 1: Custom prompt (from settings)
        if (!string.IsNullOrWhiteSpace(_customPrompt))
        {
            progress?.Report($"  [Classify] Using custom prompt ({_customPrompt.Length} chars)");
            return _customPrompt
                .Replace("{{EXISTING_HIERARCHY}}", hierarchyText)
                .Replace("{{EXISTING_HIERARCHY_JSON}}", hierarchyJson)
                .Replace("{{ITEMS}}", itemsList);
        }

        // Priority 2: Template from file
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

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            return _promptTemplate
                .Replace("{{EXISTING_HIERARCHY}}", hierarchyText)
                .Replace("{{EXISTING_HIERARCHY_JSON}}", hierarchyJson)
                .Replace("{{ITEMS}}", itemsList);
        }

        // Priority 3: Default prompt (fallback)
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

    /// <summary>
    /// Строит JSON-представление иерархии продуктов для умных моделей.
    /// Формат: [{"id": 1, "name": "Молочные продукты", "parent_id": null}, ...]
    /// </summary>
    private string BuildHierarchyJson(IReadOnlyList<ExistingProduct> existingProducts)
    {
        if (existingProducts.Count == 0)
        {
            return "[]";
        }

        var jsonProducts = existingProducts.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            parent_id = p.ParentId
        });

        return JsonSerializer.Serialize(jsonProducts, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
