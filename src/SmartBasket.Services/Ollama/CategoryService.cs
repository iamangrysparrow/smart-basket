using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartBasket.Services.Llm;

namespace SmartBasket.Services.Ollama;

public class CategoryService : ICategoryService
{
    private readonly ILlmProviderFactory _providerFactory;
    private readonly ILogger<CategoryService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;

    public CategoryService(ILlmProviderFactory providerFactory, ILogger<CategoryService> logger)
    {
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null;
    }

    public async Task<CategorizationResult> CategorizeItemsAsync(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> categories,
        IReadOnlyList<CategoryExample>? examples = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CategorizationResult();

        try
        {
            if (itemNames.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No items to categorize";
                return result;
            }

            if (categories.Count == 0)
            {
                result.IsSuccess = false;
                result.Message = "No categories available";
                return result;
            }

            // Получаем текущий провайдер
            var provider = _providerFactory.GetCurrentProvider();
            progress?.Report($"  [Category] Using provider: {provider.Name}");

            var examplesCount = examples?.Count ?? 0;
            progress?.Report($"  [Category] Categorizing {itemNames.Count} items with {categories.Count} categories and {examplesCount} examples...");

            var prompt = BuildCategorizationPrompt(itemNames, categories, examples, progress);
            progress?.Report($"  [Category] Prompt ready: {prompt.Length} chars");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Category] Sending request via {provider.Name}...");

            var llmResult = await provider.GenerateAsync(
                prompt,
                maxTokens: 2048,
                temperature: 0.1, // Low temperature for consistent categorization
                progress: progress,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            progress?.Report($"  [Category] Response received in {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!llmResult.IsSuccess || string.IsNullOrEmpty(llmResult.Response))
            {
                result.IsSuccess = false;
                result.Message = llmResult.ErrorMessage ?? "Empty response from LLM";
                return result;
            }

            result.RawResponse = llmResult.Response;

            // Extract JSON array from response
            var jsonMatch = Regex.Match(llmResult.Response, @"\[[\s\S]*\]", RegexOptions.Multiline);
            if (jsonMatch.Success)
            {
                try
                {
                    var parsedItems = JsonSerializer.Deserialize<List<LlmCategorizedItem>>(
                        jsonMatch.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (parsedItems != null)
                    {
                        for (int i = 0; i < parsedItems.Count && i < itemNames.Count; i++)
                        {
                            var parsed = parsedItems[i];
                            result.Items.Add(new CategorizedItem
                            {
                                OriginalName = itemNames[i],
                                ItemName = parsed.ItemName ?? itemNames[i],
                                ProductName = parsed.Product,
                                Confidence = parsed.Confidence
                            });
                        }

                        result.IsSuccess = true;
                        result.Message = $"Categorized {result.Items.Count} items";
                        progress?.Report($"  [Category] {result.Message}");
                    }
                }
                catch (JsonException ex)
                {
                    progress?.Report($"  [Category] JSON parse error: {ex.Message}");
                    result.IsSuccess = false;
                    result.Message = $"Failed to parse JSON: {ex.Message}";
                }
            }
            else
            {
                progress?.Report("  [Category] No JSON array found in response");
                result.IsSuccess = false;
                result.Message = "No JSON found in response";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress?.Report("  [Category] Cancelled by user");
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"  [Category] Error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Categorization error");
        }

        return result;
    }

    private string BuildCategorizationPrompt(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> categories,
        IReadOnlyList<CategoryExample>? examples = null,
        IProgress<string>? progress = null)
    {
        // Try to load template from file
        if (!string.IsNullOrEmpty(_promptTemplatePath) && File.Exists(_promptTemplatePath))
        {
            try
            {
                _promptTemplate = File.ReadAllText(_promptTemplatePath);
                progress?.Report($"  [Category] Loaded template from: {_promptTemplatePath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Category] Failed to load template: {ex.Message}");
                _promptTemplate = null;
            }
        }

        var categoriesList = string.Join("\n", categories.Select(c => $"- {c}"));
        var itemsList = string.Join("\n", itemNames.Select((item, idx) => $"{idx + 1}. {item}"));
        var examplesSection = BuildExamplesSection(examples, categories);

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            return _promptTemplate
                .Replace("{{CATEGORIES}}", categoriesList)
                .Replace("{{ITEMS}}", itemsList)
                .Replace("{{EXAMPLES}}", examplesSection);
        }

        // Default prompt
        return $@"Категоризируй товарные позиции из чека.

КАТЕГОРИИ ПОЛЬЗОВАТЕЛЯ:
{categoriesList}
{examplesSection}
ТОВАРЫ ДЛЯ КАТЕГОРИЗАЦИИ:
{itemsList}

ПРАВИЛА:
1. Для каждого товара определи:
   - item_name: нормализованное название
   - product: категория из списка выше (ТОЛЬКО из списка!)
   - confidence: уверенность от 0 до 100
2. Если товар не подходит ни под одну категорию - укажи product: null

ФОРМАТ ОТВЕТА (строго JSON массив):
[{{""item_name"":"""",""product"":"""",""confidence"":0}}]

Категоризируй товары:";
    }

    private string BuildExamplesSection(IReadOnlyList<CategoryExample>? examples, IReadOnlyList<string> categories)
    {
        if (examples == null || examples.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("ПРИМЕРЫ КАТЕГОРИЗАЦИИ (используй как образец):");

        // Group examples by category
        var examplesByCategory = examples
            .GroupBy(e => e.CategoryName)
            .ToDictionary(g => g.Key, g => g.Take(2).ToList());

        foreach (var category in categories)
        {
            if (examplesByCategory.TryGetValue(category, out var categoryExamples) && categoryExamples.Count > 0)
            {
                sb.AppendLine($"{category}:");
                foreach (var ex in categoryExamples)
                {
                    sb.AppendLine($"  - \"{ex.OriginalItemName}\" → \"{ex.NormalizedItemName}\" → {category}");
                }
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    // DTO for parsing LLM response
    private class LlmCategorizedItem
    {
        [JsonPropertyName("item_name")]
        public string? ItemName { get; set; }

        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("confidence")]
        public int Confidence { get; set; }
    }
}
