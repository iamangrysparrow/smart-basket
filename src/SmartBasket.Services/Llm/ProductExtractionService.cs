using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Сервис выделения продуктов из названий товаров.
/// Этап 1: Item → Product (нормализация названия, удаление брендов/объёмов/маркировок).
/// </summary>
public class ProductExtractionService : IProductExtractionService
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly IResponseParser _responseParser;
    private readonly ILogger<ProductExtractionService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;
    private string? _customPrompt;

    public ProductExtractionService(
        IAiProviderFactory providerFactory,
        IResponseParser responseParser,
        ILogger<ProductExtractionService> logger)
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

    public async Task<ProductExtractionResult> ExtractAsync(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string>? existingProducts = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ProductExtractionResult();

        try
        {
            if (itemNames.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No items to extract";
                return result;
            }

            // Получаем провайдер для выделения продуктов
            _logger.LogDebug("Getting provider for ProductExtraction operation");
            var provider = _providerFactory.GetProviderForOperation(AiOperation.ProductExtraction);
            if (provider == null)
            {
                var errorMsg = "No AI provider configured for ProductExtraction operation. Check AiOperations.ProductExtraction in settings.";
                _logger.LogError(errorMsg);
                progress?.Report($"  [Extract] ERROR: {errorMsg}");
                result.IsSuccess = false;
                result.Message = errorMsg;
                return result;
            }
            _logger.LogDebug("Got provider: {ProviderName}", provider.Name);
            progress?.Report($"  [Extract] Using provider: {provider.Name}");
            progress?.Report($"  [Extract] Extracting products from {itemNames.Count} items...");
            if (existingProducts != null && existingProducts.Count > 0)
            {
                progress?.Report($"  [Extract] Existing products provided: {existingProducts.Count}");
            }

            var prompt = BuildPrompt(itemNames, existingProducts, progress);
            progress?.Report($"  [Extract] Prompt ready: {prompt.Length} chars");
            progress?.Report($"  [Extract] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [Extract] === PROMPT END ===");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Extract] Sending request via {provider.Name}...");

            var llmResult = await provider.GenerateAsync(
                prompt,
                progress: progress,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            progress?.Report($"  [Extract] Response received in {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!llmResult.IsSuccess || string.IsNullOrEmpty(llmResult.Response))
            {
                result.IsSuccess = false;
                result.Message = llmResult.ErrorMessage ?? "Empty response from LLM";
                return result;
            }

            result.RawResponse = llmResult.Response;
            progress?.Report($"  [Extract] Total response: {llmResult.Response.Length} chars");

            // Extract and parse JSON using unified ResponseParser
            var parseResult = _responseParser.ParseJsonObject<ExtractionResponse>(llmResult.Response, progress);

            if (parseResult.IsSuccess && parseResult.Data != null)
            {
                result.Items = parseResult.Data.Items;
                result.IsSuccess = true;
                result.Message = $"Extracted {result.Items.Count} products from items";
                progress?.Report($"  [Extract] {result.Message} (method: {parseResult.ExtractionMethod})");
            }
            else
            {
                progress?.Report($"  [Extract] JSON parse error: {parseResult.ErrorMessage}");
                result.IsSuccess = false;
                result.Message = parseResult.ErrorMessage ?? "Failed to parse JSON";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress?.Report("  [Extract] Cancelled by user");
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"  [Extract] Error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Product extraction error");
        }

        return result;
    }

    private string BuildPrompt(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string>? existingProducts,
        IProgress<string>? progress = null)
    {
        var itemsList = string.Join("\n", itemNames.Select((item, idx) => $"{idx + 1}. {item}"));
        var productsList = existingProducts != null && existingProducts.Count > 0
            ? string.Join("\n", existingProducts.Select(p => $"- {p}"))
            : "(нет существующих продуктов)";

        // Priority 1: Custom prompt (from settings)
        if (!string.IsNullOrWhiteSpace(_customPrompt))
        {
            progress?.Report($"  [Extract] Using custom prompt ({_customPrompt.Length} chars)");
            return _customPrompt
                .Replace("{{ITEMS}}", itemsList)
                .Replace("{{PRODUCTS}}", productsList);
        }

        // Priority 2: Template from file
        if (!string.IsNullOrEmpty(_promptTemplatePath) && File.Exists(_promptTemplatePath))
        {
            try
            {
                _promptTemplate = File.ReadAllText(_promptTemplatePath);
                progress?.Report($"  [Extract] Loaded template from: {_promptTemplatePath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Extract] Failed to load template: {ex.Message}");
                _promptTemplate = null;
            }
        }

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            return _promptTemplate
                .Replace("{{ITEMS}}", itemsList)
                .Replace("{{PRODUCTS}}", productsList);
        }

        // Priority 3: Default prompt (fallback)
        return $@"Выдели продукты из товаров.

Правила:
- Удали бренды, торговые марки, производителей
- Удали вес, объём, количество штук (700 г, 930 мл, 5 шт)
- Удали маркировки (БЗМЖ, С0, Халяль)
- Сохрани форму/состояние (замороженный, охлаждённый, молотый)
- Сохрани жирность для молочных продуктов (10%, 2.5%)
- Сохрани вкусовые добавки

Список существующих продуктов (используй их при совпадении):
{productsList}

ТОВАРЫ:
{itemsList}

ФОРМАТ ОТВЕТА (строго JSON):
{{""items"":[{{""name"":""Полное название товара"",""product"":""Выделенный продукт""}}]}}

Выдели продукты:";
    }
}
