using System.IO;
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
    private string? _systemPromptPath;
    private string? _userPromptPath;
    private bool _promptPathsInitialized;

    public ProductExtractionService(
        IAiProviderFactory providerFactory,
        IResponseParser responseParser,
        ILogger<ProductExtractionService> logger)
    {
        _providerFactory = providerFactory;
        _responseParser = responseParser;
        _logger = logger;
    }

    public void SetPromptPaths(string systemPath, string userPath)
    {
        _systemPromptPath = systemPath;
        _userPromptPath = userPath;
        _promptPathsInitialized = true;
        _logger.LogDebug("Prompt paths set: system={SystemPath}, user={UserPath}", systemPath, userPath);
    }

    /// <summary>
    /// Инициализировать пути к файлам промптов из директории приложения
    /// </summary>
    private void EnsurePromptPathsInitialized()
    {
        if (_promptPathsInitialized) return;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var systemPath = Path.Combine(appDir, "prompt_extract_products_system.txt");
        var userPath = Path.Combine(appDir, "prompt_extract_products_user.txt");

        if (File.Exists(systemPath))
        {
            _systemPromptPath = systemPath;
            _logger.LogDebug("Auto-detected system prompt: {Path}", systemPath);
        }

        if (File.Exists(userPath))
        {
            _userPromptPath = userPath;
            _logger.LogDebug("Auto-detected user prompt: {Path}", userPath);
        }

        _promptPathsInitialized = true;
    }

    public async Task<ProductExtractionResult> ExtractAsync(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string>? existingProducts = null,
        IReadOnlyList<UnitOfMeasureInfo>? unitOfMeasures = null,
        LlmSessionContext? sessionContext = null,
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

            var (systemPrompt, userPrompt) = BuildMessages(itemNames, existingProducts, unitOfMeasures, progress);
            progress?.Report($"  [Extract] System prompt: {systemPrompt.Length} chars, User prompt: {userPrompt.Length} chars");
            progress?.Report($"  [Extract] === SYSTEM PROMPT ===");
            progress?.Report(systemPrompt);
            progress?.Report($"  [Extract] === USER PROMPT ===");
            progress?.Report(userPrompt);
            progress?.Report($"  [Extract] === END ===");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Extract] Sending request via {provider.Name}...");

            var messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            };

            var llmResult = await provider.ChatAsync(
                messages,
                tools: null,
                sessionContext: sessionContext,
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

    private (string SystemPrompt, string UserPrompt) BuildMessages(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string>? existingProducts,
        IReadOnlyList<UnitOfMeasureInfo>? unitOfMeasures,
        IProgress<string>? progress = null)
    {
        // Автоматически инициализировать пути к промптам, если не установлены
        EnsurePromptPathsInitialized();

        var itemsList = string.Join("\n", itemNames.Select((item, idx) => $"{idx + 1}. {item}"));
        var productsList = existingProducts != null && existingProducts.Count > 0
            ? string.Join("\n", existingProducts.Select(p => $"- {p}"))
            : "(нет существующих продуктов)";

        var unitsList = BuildUnitsReference(unitOfMeasures);

        // Load from files
        string systemPrompt;
        string userPrompt;

        if (!string.IsNullOrEmpty(_systemPromptPath) && File.Exists(_systemPromptPath) &&
            !string.IsNullOrEmpty(_userPromptPath) && File.Exists(_userPromptPath))
        {
            try
            {
                systemPrompt = File.ReadAllText(_systemPromptPath);
                userPrompt = File.ReadAllText(_userPromptPath)
                    .Replace("{{ITEMS}}", itemsList)
                    .Replace("{{PRODUCTS}}", productsList)
                    .Replace("{{UNITS}}", unitsList);
                progress?.Report($"  [Extract] Loaded prompts from files");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Extract] Failed to load prompts: {ex.Message}, using defaults");
                (systemPrompt, userPrompt) = GetDefaultPrompts(itemsList, productsList, unitsList);
            }
        }
        else
        {
            progress?.Report($"  [Extract] Prompt files not configured, using defaults");
            (systemPrompt, userPrompt) = GetDefaultPrompts(itemsList, productsList, unitsList);
        }

        return (systemPrompt, userPrompt);
    }

    private static (string System, string User) GetDefaultPrompts(string itemsList, string productsList, string unitsList)
    {
        var system = @"Ты — эксперт по нормализации названий товаров в стандартизированные продукты.

Правила выделения продукта:
- Удали бренды, торговые марки, производителей
- Удали вес, объём, количество штук (700 г, 930 мл, 5 шт)
- Удали маркировки (БЗМЖ, С0, Халяль)
- Сохрани форму/состояние (замороженный, охлаждённый, молотый)
- Сохрани жирность для молочных продуктов (10%, 2.5%)
- Сохрани вкусовые добавки

Правила определения base_unit:
- Для весовых продуктов (овощи, фрукты, мясо, крупы): кг
- Для жидкостей (молоко, соки, напитки, масло): л
- Для штучных товаров (яйца, хлеб, выпечка): шт
- Для тканей, верёвок: м
- Для плитки, напольных покрытий: м²

Формат ответа (строго JSON):
{""items"":[{""name"":""название товара"",""product"":""продукт"",""base_unit"":""кг""}]}";

        var user = $@"Справочник единиц измерения:
{unitsList}

Список продуктов, которые уже используются:
{productsList}

При назначении товару продукта, проверь, есть ли уже подходящий продукт в списке.
Если есть, то используй этот продукт.

Список товаров:
{itemsList}

Выдели продукты из товаров.";

        return (system, user);
    }

    private static string BuildUnitsReference(IReadOnlyList<UnitOfMeasureInfo>? unitOfMeasures)
    {
        if (unitOfMeasures == null || unitOfMeasures.Count == 0)
        {
            return @"Базовые единицы: кг (вес), л (объём), шт (штуки), м (длина), м² (площадь)";
        }

        var baseUnits = unitOfMeasures.Where(u => u.IsBase).ToList();
        var derivedUnits = unitOfMeasures.Where(u => !u.IsBase).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Базовые единицы (используй для base_unit):");
        foreach (var unit in baseUnits)
        {
            sb.AppendLine($"  - {unit.Id} ({unit.Name})");
        }

        if (derivedUnits.Count > 0)
        {
            sb.AppendLine("Производные единицы (НЕ использовать для base_unit):");
            foreach (var unit in derivedUnits)
            {
                sb.AppendLine($"  - {unit.Id} → {unit.BaseUnitId}");
            }
        }

        return sb.ToString();
    }
}
