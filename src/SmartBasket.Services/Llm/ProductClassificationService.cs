using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Entities;
using SmartBasket.Data;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Сервис классификации продуктов по иерархии.
/// Получает список продуктов и существующую иерархию категорий,
/// возвращает иерархию с новыми категориями и привязкой продуктов.
/// </summary>
public class ProductClassificationService : IProductClassificationService
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly IResponseParser _responseParser;
    private readonly SmartBasketDbContext _dbContext;
    private readonly ILogger<ProductClassificationService> _logger;
    private string? _systemPromptPath;
    private string? _userPromptPath;
    private bool _promptPathsInitialized;

    // Трекинг созданных категорий в текущей сессии (для удаления пустых)
    private readonly HashSet<Guid> _createdCategoryIds = new();

    // Кэш иерархии категорий из файла product_categories.txt: Number → (Name, ParentNumber)
    private Dictionary<int, (string Name, int? ParentNumber)>? _defaultCategoryHierarchy;

    public ProductClassificationService(
        IAiProviderFactory providerFactory,
        IResponseParser responseParser,
        SmartBasketDbContext dbContext,
        ILogger<ProductClassificationService> logger)
    {
        _providerFactory = providerFactory;
        _responseParser = responseParser;
        _dbContext = dbContext;
        _logger = logger;
    }

    public void SetPromptPaths(string systemPath, string userPath)
    {
        _systemPromptPath = systemPath;
        _userPromptPath = userPath;
        _promptPathsInitialized = true;
        _logger.LogDebug("Prompt paths set: system={System}, user={User}", systemPath, userPath);
    }

    /// <summary>
    /// Инициализировать пути к файлам промптов из директории приложения
    /// </summary>
    private void EnsurePromptPathsInitialized()
    {
        if (_promptPathsInitialized) return;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var systemPath = Path.Combine(appDir, "prompt_classify_products_system.txt");
        var userPath = Path.Combine(appDir, "prompt_classify_products_user.txt");

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

    public async Task<ProductClassificationResult> ClassifyAsync(
        IReadOnlyList<string> productNames,
        IReadOnlyList<ExistingCategory> existingCategories,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ProductClassificationResult();

        try
        {
            if (productNames.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No products to classify";
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
            progress?.Report($"  [Classify] Classifying {productNames.Count} products...");

            // Строим System/User сообщения с маппингом категорий
            var (systemPrompt, userPrompt, categoryMapping) = BuildMessages(productNames, existingCategories, progress);
            progress?.Report($"  [Classify] System prompt: {systemPrompt.Length} chars, User prompt: {userPrompt.Length} chars");
            progress?.Report($"  [Classify] === SYSTEM PROMPT START ===");
            progress?.Report(systemPrompt);
            progress?.Report($"  [Classify] === SYSTEM PROMPT END ===");
            progress?.Report($"  [Classify] === USER PROMPT START ===");
            progress?.Report(userPrompt);
            progress?.Report($"  [Classify] === USER PROMPT END ===");

            var messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Classify] Sending request via {provider.Name}...");

            var llmResult = await provider.ChatAsync(
                messages,
                tools: null,
                sessionContext: sessionContext,
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
            result.CategoryNumberToGuid = categoryMapping;
            progress?.Report($"  [Classify] Total response: {llmResult.Response.Length} chars");

            // Extract and parse JSON using unified ResponseParser
            var parseResult = _responseParser.ParseJsonObject<ClassificationResponse>(llmResult.Response, progress);

            if (parseResult.IsSuccess && parseResult.Data != null)
            {
                result.Products = parseResult.Data.Products;
                result.IsSuccess = true;

                var productsCount = result.Products.Count;
                result.Message = $"Got {productsCount} products classified";
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

    /// <summary>
    /// Классификация с кастомным промптом (объединённый system + user).
    /// Используется для реклассификации с отредактированным промптом из UI.
    /// Пытается извлечь маппинг категорий из промпта (номера → Guid).
    /// </summary>
    private async Task<ProductClassificationResult> ClassifyWithCustomPromptAsync(
        string customPrompt,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ProductClassificationResult();

        try
        {
            // Получаем провайдер для классификации
            var provider = _providerFactory.GetProviderForOperation(AiOperation.Classification);
            if (provider == null)
            {
                var errorMsg = "No AI provider configured for Classification operation.";
                _logger.LogError(errorMsg);
                progress?.Report($"  [Classify] ERROR: {errorMsg}");
                result.IsSuccess = false;
                result.Message = errorMsg;
                return result;
            }

            progress?.Report($"  [Classify] Using provider: {provider.Name}");
            progress?.Report($"  [Classify] Custom prompt: {customPrompt.Length} chars");

            // Пытаемся извлечь маппинг категорий из промпта (если он сгенерирован BuildMessages)
            var categoryMapping = await ExtractCategoryMappingFromPromptAsync(customPrompt, cancellationToken);
            result.CategoryNumberToGuid = categoryMapping;
            progress?.Report($"  [Classify] Extracted category mapping: {categoryMapping.Count} entries");

            // Разделяем промпт на system и user по двойному переносу строки
            var separatorIndex = customPrompt.IndexOf("\n\n", StringComparison.Ordinal);
            string systemPrompt, userPrompt;

            if (separatorIndex > 0)
            {
                systemPrompt = customPrompt[..separatorIndex].Trim();
                userPrompt = customPrompt[(separatorIndex + 2)..].Trim();
            }
            else
            {
                // Fallback: загружаем категории из файла для системного промпта
                var existingCategories = await GetExistingCategoriesAsync(cancellationToken);
                var (categoriesText, _) = LoadNumberedCategoriesFromFile(existingCategories);
                systemPrompt = GetDefaultSystemPrompt(categoriesText);
                userPrompt = customPrompt;
            }

            progress?.Report($"  [Classify] === SYSTEM PROMPT START ===");
            progress?.Report(systemPrompt);
            progress?.Report($"  [Classify] === SYSTEM PROMPT END ===");
            progress?.Report($"  [Classify] === USER PROMPT START ===");
            progress?.Report(userPrompt);
            progress?.Report($"  [Classify] === USER PROMPT END ===");

            var messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Classify] Sending request via {provider.Name}...");

            var llmResult = await provider.ChatAsync(
                messages,
                tools: null,
                sessionContext: null,
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

            // Parse JSON
            var parseResult = _responseParser.ParseJsonObject<ClassificationResponse>(llmResult.Response, progress);

            if (parseResult.IsSuccess && parseResult.Data != null)
            {
                result.Products = parseResult.Data.Products;
                result.IsSuccess = true;

                var productsCount = result.Products.Count;
                result.Message = $"Got {productsCount} products classified";
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
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Classification with custom prompt error");
        }

        return result;
    }

    /// <summary>
    /// Извлекает маппинг номер→Guid из промпта.
    /// Использует закэшированную карту из файла product_categories.txt.
    /// </summary>
    private async Task<Dictionary<int, Guid>> ExtractCategoryMappingFromPromptAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        // Загружаем существующие категории из БД
        var existingCategories = await GetExistingCategoriesAsync(cancellationToken);

        // Используем тот же метод что и BuildMessages
        var (_, mapping) = LoadNumberedCategoriesFromFile(existingCategories);

        return mapping;
    }

    /// <summary>
    /// Строит System и User промпты для ChatAsync.
    /// Возвращает промпты + маппинг номер категории → Guid.
    /// </summary>
    private (string SystemPrompt, string UserPrompt, Dictionary<int, Guid> CategoryMapping) BuildMessages(
        IReadOnlyList<string> productNames,
        IReadOnlyList<ExistingCategory> existingCategories,
        IProgress<string>? progress = null)
    {
        // Автоматически инициализировать пути к промптам, если не установлены
        EnsurePromptPathsInitialized();

        var productsList = string.Join("\n", productNames);

        // Загружаем пронумерованные категории из файла и строим маппинг
        var (numberedCategories, categoryMapping) = LoadNumberedCategoriesFromFile(existingCategories);
        progress?.Report($"  [Classify] Loaded categories from file, mapping: {categoryMapping.Count} entries");

        // Пробуем загрузить System/User промпты из файлов
        string? systemPrompt = null;
        string? userPrompt = null;

        if (!string.IsNullOrEmpty(_systemPromptPath) && File.Exists(_systemPromptPath))
        {
            try
            {
                systemPrompt = File.ReadAllText(_systemPromptPath);
                systemPrompt = systemPrompt
                    .Replace("{{CATEGORIES}}", numberedCategories);
                progress?.Report($"  [Classify] Loaded system prompt from: {_systemPromptPath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Classify] Failed to load system prompt: {ex.Message}");
            }
        }

        if (!string.IsNullOrEmpty(_userPromptPath) && File.Exists(_userPromptPath))
        {
            try
            {
                userPrompt = File.ReadAllText(_userPromptPath);
                userPrompt = userPrompt
                    .Replace("{{PRODUCTS}}", productsList);
                progress?.Report($"  [Classify] Loaded user prompt from: {_userPromptPath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Classify] Failed to load user prompt: {ex.Message}");
            }
        }

        // Fallback на дефолтные промпты
        systemPrompt ??= GetDefaultSystemPrompt(numberedCategories);
        userPrompt ??= GetDefaultUserPrompt(productsList);

        return (systemPrompt, userPrompt, categoryMapping);
    }

    /// <summary>
    /// Загружает текст категорий из файла product_categories.txt.
    /// Строит маппинг номер → Guid на основе поля Number в БД.
    /// </summary>
    private (string Text, Dictionary<int, Guid> Mapping) LoadNumberedCategoriesFromFile(
        IReadOnlyList<ExistingCategory> existingCategories)
    {
        // Строим маппинг номер → Guid из существующих категорий в БД (по полю Number)
        var mapping = existingCategories
            .Where(c => c.Number.HasValue)
            .ToDictionary(c => c.Number!.Value, c => c.Id);

        _logger.LogDebug("Built mapping from DB: {Count} categories with Number", mapping.Count);

        // Загружаем текст категорий из файла (для промпта)
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(appDir, "product_categories.txt");

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Categories file not found: {Path}", filePath);
            return ("", mapping);
        }

        var text = File.ReadAllText(filePath);
        return (text, mapping);
    }

    private static string GetDefaultSystemPrompt(string categoriesText)
    {
        return $@"Ты — эксперт по классификации продуктов в иерархическую систему категорий.
Основное правило — один товар принадлежит только одной категории.

КРИТИЧЕСКИ ВАЖНО:
- Каждый продукт из входного списка ОБЯЗАТЕЛЬНО должен быть в ответе
- Выбирай категорию по НОМЕРУ из списка ниже
- ПО УМОЛЧАНИЮ продукты — СВЕЖИЕ. Категории ""Заморозка"", ""Замороженные..."" использовать ТОЛЬКО если в названии продукта ЯВНО указано: ""замороженный"", ""заморозка"", ""frozen""
- Выбирай самый нижний уровень категории (лист дерева)
- Запрещено изменять названия продуктов — копируй как есть

КАТЕГОРИИ (выбирай по номеру):
{categoriesText}

ФОРМАТ ОТВЕТА (строго JSON):
{{""products"": [{{""name"": ""Название продукта"", ""category"": 15}}]}}

category — номер категории из списка (число)";
    }

    private static string GetDefaultUserPrompt(string productsList)
    {
        return $@"ПРОДУКТЫ ДЛЯ КЛАССИФИКАЦИИ:
{productsList}

Классифицируй ВСЕ продукты из списка. Для каждого укажи номер категории.";
    }

    private string BuildPrompt(
        IReadOnlyList<string> productNames,
        IReadOnlyList<ExistingCategory> existingCategories,
        IProgress<string>? progress = null)
    {
        // Legacy метод для обратной совместимости
        var (systemPrompt, userPrompt, _) = BuildMessages(productNames, existingCategories, progress);
        return systemPrompt + "\n\n" + userPrompt;
    }

    /// <summary>
    /// Строит текстовое представление существующей иерархии категорий.
    /// </summary>
    private string BuildHierarchyText(IReadOnlyList<ExistingCategory> existingCategories)
    {
        if (existingCategories.Count == 0)
        {
            return "(пусто - создай новые категории)";
        }

        var sb = new StringBuilder();

        // Группируем по родителю для иерархического отображения
        var rootCategories = existingCategories.Where(c => c.ParentName == null).ToList();
        var childrenByParent = existingCategories
            .Where(c => c.ParentName != null)
            .GroupBy(c => c.ParentName!)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var root in rootCategories)
        {
            AppendCategoryWithChildren(sb, root.Name, childrenByParent, 0);
        }

        return sb.ToString().TrimEnd();
    }

    private void AppendCategoryWithChildren(
        StringBuilder sb,
        string categoryName,
        Dictionary<string, List<ExistingCategory>> childrenByParent,
        int level)
    {
        var indent = new string(' ', level * 2);
        sb.AppendLine($"{indent}- {categoryName}");

        if (childrenByParent.TryGetValue(categoryName, out var children))
        {
            foreach (var child in children)
            {
                AppendCategoryWithChildren(sb, child.Name, childrenByParent, level + 1);
            }
        }
    }

    /// <summary>
    /// Классифицировать все некатегоризированные продукты и применить результат к БД.
    /// </summary>
    public async Task<ClassificationApplyResult> ClassifyAndApplyAsync(
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ClassificationApplyResult();
        _createdCategoryIds.Clear();

        try
        {
            // 1. Загрузить некатегоризированные продукты (без категории)
            var uncategorizedProducts = await _dbContext.Products
                .Where(p => p.CategoryId == null)
                .ToListAsync(cancellationToken);

            if (uncategorizedProducts.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No uncategorized products";
                progress?.Report("  [Classify] No uncategorized products to classify");
                return result;
            }

            progress?.Report($"  [Classify] Found {uncategorizedProducts.Count} uncategorized products");

            var productsToClassify = uncategorizedProducts
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 2. Синхронизировать номера категорий из файла (если ещё не проставлены)
            var synced = await SyncCategoryNumbersAsync(cancellationToken);
            if (synced > 0)
            {
                progress?.Report($"  [Classify] Synced {synced} category numbers from file");
            }

            // 3. Загрузить существующие категории (уже с Number)
            var existingCategories = await GetExistingCategoriesAsync(cancellationToken);
            progress?.Report($"  [Classify] Existing categories: {existingCategories.Count}, with Number: {existingCategories.Count(c => c.Number.HasValue)}");

            // 3. Вызвать LLM для классификации
            var classificationResult = await ClassifyAsync(
                productsToClassify,
                existingCategories,
                sessionContext,
                progress,
                cancellationToken);

            if (!classificationResult.IsSuccess)
            {
                result.IsSuccess = false;
                result.Message = classificationResult.Message;
                return result;
            }

            // 4. Применить результат к БД
            await ApplyClassificationResultAsync(classificationResult, progress, cancellationToken);

            // 5. Проверить оставшиеся некатегоризированные
            result.ProductsRemaining = await _dbContext.Products
                .CountAsync(p => p.CategoryId == null, cancellationToken);

            // 6. Удалить пустые категории (созданные в этой сессии)
            result.CategoriesDeleted = await DeleteEmptyCategoriesAsync(progress, cancellationToken);

            result.IsSuccess = true;
            result.ProductsClassified = productsToClassify.Count - result.ProductsRemaining;
            result.CategoriesCreated = _createdCategoryIds.Count;
            result.Message = $"Classified {result.ProductsClassified} products into {result.CategoriesCreated} new categories";

            if (result.ProductsRemaining > 0)
            {
                result.Message += $", {result.ProductsRemaining} remaining uncategorized";
            }
            if (result.CategoriesDeleted > 0)
            {
                result.Message += $", deleted {result.CategoriesDeleted} empty categories";
            }

            progress?.Report($"  [Classify] {result.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.IsSuccess = false;
            result.Message = "Operation cancelled";
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassifyAndApplyAsync failed");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Загрузить существующие категории из БД
    /// </summary>
    private async Task<IReadOnlyList<ExistingCategory>> GetExistingCategoriesAsync(CancellationToken ct)
    {
        var categories = await _dbContext.ProductCategories
            .Include(c => c.Parent)
            .ToListAsync(ct);

        return categories.Select(c => new ExistingCategory
        {
            Id = c.Id,
            Name = c.Name,
            ParentName = c.Parent?.Name,
            Number = c.Number
        }).ToList();
    }

    /// <summary>
    /// Применить результат классификации к БД.
    /// Использует маппинг номер категории → Guid из результата.
    /// Создаёт недостающие категории из файла product_categories.txt.
    /// </summary>
    private async Task ApplyClassificationResultAsync(
        ProductClassificationResult classificationResult,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var categoryMapping = new Dictionary<int, Guid>(classificationResult.CategoryNumberToGuid);
        progress?.Report($"  [Classify] Category mapping has {categoryMapping.Count} entries");

        // Загружаем иерархию категорий из файла (номер → (название, родительский номер))
        var hierarchy = LoadDefaultCategoryHierarchy();
        progress?.Report($"  [Classify] File hierarchy has {hierarchy.Count} categories");

        // Загрузить все категории из БД для создания недостающих
        var allCategories = await _dbContext.ProductCategories.ToListAsync(cancellationToken);
        var categoryLookup = allCategories.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        // Загрузить все продукты из БД для обновления
        var allProducts = await _dbContext.Products.ToListAsync(cancellationToken);
        var productLookup = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in allProducts)
        {
            // Добавляем по нормализованному имени
            var normalizedName = NormalizeProductName(p.Name);
            productLookup.TryAdd(normalizedName, p);
            // Также добавляем по оригинальному имени для обратной совместимости
            productLookup.TryAdd(p.Name, p);
        }

        progress?.Report($"  [Classify] Products in DB lookup: {productLookup.Count}");
        progress?.Report($"  [Classify] Products to classify: {classificationResult.Products.Count}");

        var updated = 0;
        var notFound = 0;
        var invalidCategory = 0;

        foreach (var productInfo in classificationResult.Products)
        {
            // Нормализуем имя продукта для поиска
            var normalizedProductName = NormalizeProductName(productInfo.Name);

            if (!productLookup.TryGetValue(normalizedProductName, out var product))
            {
                // Попробуем найти по оригинальному имени
                if (!productLookup.TryGetValue(productInfo.Name, out product))
                {
                    progress?.Report($"  [Classify] WARNING: Product not found in DB: '{productInfo.Name}'");
                    notFound++;
                    continue;
                }
            }

            // Получаем Guid категории по номеру
            if (productInfo.CategoryNumber <= 0)
            {
                progress?.Report($"  [Classify] WARNING: Invalid category number 0 for '{productInfo.Name}'");
                invalidCategory++;
                continue;
            }

            Guid categoryGuid;
            if (!categoryMapping.TryGetValue(productInfo.CategoryNumber, out categoryGuid))
            {
                // Категория не найдена в маппинге — попробуем создать из файла
                if (!hierarchy.TryGetValue(productInfo.CategoryNumber, out var categoryInfo))
                {
                    progress?.Report($"  [Classify] WARNING: Category number {productInfo.CategoryNumber} not found in file for '{productInfo.Name}'");
                    invalidCategory++;
                    continue;
                }

                // Создаём категорию (и её родителей рекурсивно) по номеру
                var category = await EnsureCategoryExistsByNumberAsync(
                    productInfo.CategoryNumber, hierarchy, categoryLookup, progress, cancellationToken);
                if (category == null)
                {
                    progress?.Report($"  [Classify] WARNING: Failed to create category #{productInfo.CategoryNumber} '{categoryInfo.Name}' for '{productInfo.Name}'");
                    invalidCategory++;
                    continue;
                }

                categoryGuid = category.Id;
                categoryMapping[productInfo.CategoryNumber] = categoryGuid;
                progress?.Report($"  [Classify] Created category #{productInfo.CategoryNumber}: {categoryInfo.Name}");
            }

            var oldCategoryId = product.CategoryId;
            product.CategoryId = categoryGuid;
            updated++;

            if (oldCategoryId != categoryGuid)
            {
                progress?.Report($"  [Classify] OK: '{productInfo.Name}' -> category #{productInfo.CategoryNumber} (Guid: {categoryGuid})");
            }
        }

        var changedCount = _dbContext.ChangeTracker.Entries<Product>().Count(e => e.State == EntityState.Modified);
        progress?.Report($"  [Classify] Summary: updated={updated}, not_found={notFound}, invalid_category={invalidCategory}");
        progress?.Report($"  [Classify] Saving {changedCount} modified products...");
        await _dbContext.SaveChangesAsync(cancellationToken);
        progress?.Report($"  [Classify] Saved successfully");
    }

    /// <summary>
    /// Построить промпт для классификации указанных продуктов.
    /// </summary>
    public async Task<string> BuildPromptForProductsAsync(
        IReadOnlyList<string> productNames,
        CancellationToken cancellationToken = default)
    {
        var existingCategories = await GetExistingCategoriesAsync(cancellationToken);
        return BuildPrompt(productNames, existingCategories, null);
    }

    /// <summary>
    /// Получить список продуктов категории и всех её потомков.
    /// </summary>
    public async Task<List<string>> GetCategoryProductNamesAsync(
        Guid? categoryId,
        CancellationToken cancellationToken = default)
    {
        if (categoryId == null)
        {
            // "Без категории" - продукты без CategoryId
            return await _dbContext.Products
                .Where(p => p.CategoryId == null)
                .Select(p => p.Name)
                .ToListAsync(cancellationToken);
        }

        // Собрать все ID категорий (включая потомков)
        var categoryIds = new HashSet<Guid> { categoryId.Value };
        await CollectDescendantCategoryIdsAsync(categoryId.Value, categoryIds, cancellationToken);

        // Получить продукты из всех этих категорий
        return await _dbContext.Products
            .Where(p => p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value))
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task CollectDescendantCategoryIdsAsync(
        Guid parentId,
        HashSet<Guid> result,
        CancellationToken cancellationToken)
    {
        var childIds = await _dbContext.ProductCategories
            .Where(c => c.ParentId == parentId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        foreach (var childId in childIds)
        {
            result.Add(childId);
            await CollectDescendantCategoryIdsAsync(childId, result, cancellationToken);
        }
    }

    /// <summary>
    /// Реклассифицировать продукты конкретной категории (включая все дочерние).
    /// </summary>
    public async Task<ClassificationApplyResult> ReclassifyCategoryAsync(
        Guid? categoryId,
        string? customPrompt = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ClassificationApplyResult();
        _createdCategoryIds.Clear();

        try
        {
            // 1. Получить продукты для реклассификации
            var productNames = await GetCategoryProductNamesAsync(categoryId, cancellationToken);

            if (productNames.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No products to reclassify";
                progress?.Report("  [Reclassify] No products found in category");
                return result;
            }

            progress?.Report($"  [Reclassify] Found {productNames.Count} products to reclassify");

            // 2. Сбросить CategoryId у всех этих продуктов (чтобы они были "некатегоризированными")
            var products = await _dbContext.Products
                .Where(p => productNames.Contains(p.Name))
                .ToListAsync(cancellationToken);

            foreach (var product in products)
            {
                product.CategoryId = null;
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            progress?.Report($"  [Reclassify] Reset CategoryId for {products.Count} products");

            // 3. Синхронизировать номера категорий из файла (если ещё не проставлены)
            var synced = await SyncCategoryNumbersAsync(cancellationToken);
            if (synced > 0)
            {
                progress?.Report($"  [Reclassify] Synced {synced} category numbers from file");
            }

            // 4. Загрузить существующие категории (уже с Number)
            var existingCategories = await GetExistingCategoriesAsync(cancellationToken);
            progress?.Report($"  [Reclassify] Existing categories: {existingCategories.Count}, with Number: {existingCategories.Count(c => c.Number.HasValue)}");

            // 4. Вызвать LLM для классификации
            ProductClassificationResult classificationResult;
            if (!string.IsNullOrWhiteSpace(customPrompt))
            {
                // Используем кастомный промпт из диалога редактирования
                progress?.Report($"  [Reclassify] Using custom prompt ({customPrompt.Length} chars)");
                classificationResult = await ClassifyWithCustomPromptAsync(
                    customPrompt,
                    progress,
                    cancellationToken);
            }
            else
            {
                // Используем стандартный промпт
                classificationResult = await ClassifyAsync(
                    productNames,
                    existingCategories,
                    sessionContext: null,  // ReclassifyCategory вызывается вне CollectAsync — нет сессии
                    progress,
                    cancellationToken);
            }

            if (!classificationResult.IsSuccess)
            {
                result.IsSuccess = false;
                result.Message = classificationResult.Message;
                return result;
            }

            // 5. Применить результат к БД
            await ApplyClassificationResultAsync(classificationResult, progress, cancellationToken);

            // 6. Проверить оставшиеся некатегоризированные
            result.ProductsRemaining = await _dbContext.Products
                .CountAsync(p => p.CategoryId == null, cancellationToken);

            // 7. Удалить пустые категории
            result.CategoriesDeleted = await DeleteEmptyCategoriesAsync(progress, cancellationToken);

            result.IsSuccess = true;
            result.ProductsClassified = productNames.Count - result.ProductsRemaining;
            result.CategoriesCreated = _createdCategoryIds.Count;
            result.Message = $"Reclassified {result.ProductsClassified} products";

            if (result.CategoriesCreated > 0)
            {
                result.Message += $", created {result.CategoriesCreated} new categories";
            }
            if (result.ProductsRemaining > 0)
            {
                result.Message += $", {result.ProductsRemaining} remaining uncategorized";
            }
            if (result.CategoriesDeleted > 0)
            {
                result.Message += $", deleted {result.CategoriesDeleted} empty categories";
            }

            progress?.Report($"  [Reclassify] {result.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.IsSuccess = false;
            result.Message = "Operation cancelled";
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReclassifyCategoryAsync failed");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Удалить пустые категории (созданные в этой сессии)
    /// </summary>
    private async Task<int> DeleteEmptyCategoriesAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var deleted = 0;

        // Удалить пустые категории созданные в этой сессии
        // Сортируем по глубине (сначала листья)
        var createdCategories = await _dbContext.ProductCategories
            .Where(c => _createdCategoryIds.Contains(c.Id))
            .Include(c => c.Products)
            .Include(c => c.Children)
            .ToListAsync(cancellationToken);

        // Удаляем листья (без детей и без продуктов)
        bool deletedAny;
        do
        {
            deletedAny = false;
            foreach (var category in createdCategories.ToList())
            {
                // Перезагрузить связи
                await _dbContext.Entry(category).Collection(c => c.Products).LoadAsync(cancellationToken);
                await _dbContext.Entry(category).Collection(c => c.Children).LoadAsync(cancellationToken);

                if ((category.Products == null || category.Products.Count == 0) &&
                    (category.Children == null || category.Children.Count == 0))
                {
                    _dbContext.ProductCategories.Remove(category);
                    createdCategories.Remove(category);
                    deleted++;
                    deletedAny = true;
                    progress?.Report($"  [Classify] Deleted empty category: {category.Name}");
                }
            }

            if (deletedAny)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        } while (deletedAny && createdCategories.Count > 0);

        return deleted;
    }

    /// <summary>
    /// Нормализует имя продукта для единообразия:
    /// - Приводит к Title Case (первая буква каждого слова заглавная)
    /// - Заменяет ё на е
    /// - Унифицирует кавычки (все типы → ")
    /// </summary>
    private static string NormalizeProductName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        // Заменяем ё на е
        name = name.Replace('ё', 'е').Replace('Ё', 'Е');

        // Унифицируем все типы кавычек в обычные двойные
        name = name
            .Replace('«', '"').Replace('»', '"')   // Французские кавычки
            .Replace('"', '"').Replace('"', '"')   // Типографские кавычки
            .Replace('„', '"')                      // Немецкие кавычки
            .Replace("\\\"", "\"");                 // Escaped кавычки из JSON

        // Title Case: первая буква каждого слова заглавная, остальные строчные
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) +
                           (words[i].Length > 1 ? words[i][1..].ToLowerInvariant() : "");
            }
        }
        return string.Join(" ", words);
    }

    /// <summary>
    /// Загружает иерархию категорий из файла product_categories.txt.
    /// Новый формат: "3. --Фрукты, ягоды" (номер, точка, дефисы для уровня, название)
    /// Возвращает словарь: Number -> (CategoryName, ParentNumber)
    /// Ключ - номер категории (уникален), чтобы не терять дубликаты имён.
    /// </summary>
    private Dictionary<int, (string Name, int? ParentNumber)> LoadDefaultCategoryHierarchy()
    {
        if (_defaultCategoryHierarchy != null)
            return _defaultCategoryHierarchy;

        _defaultCategoryHierarchy = new Dictionary<int, (string, int?)>();

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(appDir, "product_categories.txt");

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Default categories file not found: {Path}", filePath);
            return _defaultCategoryHierarchy;
        }

        try
        {
            var lines = File.ReadAllLines(filePath);
            var parentStack = new List<int>(); // Stack of parent numbers at each level

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Новый формат: "3. --Фрукты, ягоды"
                var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.\s*(-+)(.+)$");
                if (!match.Success)
                    continue;

                if (!int.TryParse(match.Groups[1].Value, out var number))
                    continue;

                var dashes = match.Groups[2].Value;
                var level = dashes.Length; // Количество дефисов = уровень
                var categoryName = match.Groups[3].Value.Trim();

                if (string.IsNullOrWhiteSpace(categoryName))
                    continue;

                // Level 1 = root (no parent), Level 2 = child of level 1, etc.
                int? parentNumber = null;
                if (level > 1 && parentStack.Count >= level - 1)
                {
                    parentNumber = parentStack[level - 2];
                }

                _defaultCategoryHierarchy[number] = (categoryName, parentNumber);

                // Update parent stack for this level
                while (parentStack.Count < level)
                    parentStack.Add(number);

                if (parentStack.Count >= level)
                    parentStack[level - 1] = number;
            }

            _logger.LogDebug("Loaded {Count} default categories from file", _defaultCategoryHierarchy.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load default categories from {Path}", filePath);
        }

        return _defaultCategoryHierarchy;
    }

    /// <summary>
    /// Создаёт категорию с полной цепочкой родителей ПО НОМЕРУ.
    /// Использует hierarchy: Number → (Name, ParentNumber).
    /// </summary>
    private async Task<ProductCategory?> EnsureCategoryExistsByNumberAsync(
        int categoryNumber,
        Dictionary<int, (string Name, int? ParentNumber)> hierarchy,
        Dictionary<string, ProductCategory> categoryLookup,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!hierarchy.TryGetValue(categoryNumber, out var info))
            return null;

        var categoryName = info.Name;

        // Сначала ищем в БД по Number (уникальный идентификатор)
        var existingByNumber = await _dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Number == categoryNumber, cancellationToken);
        if (existingByNumber != null)
        {
            categoryLookup.TryAdd(existingByNumber.Name, existingByNumber);
            return existingByNumber;
        }

        // Рекурсивно создать родителя по его номеру
        Guid? parentId = null;
        if (info.ParentNumber.HasValue)
        {
            var parentCategory = await EnsureCategoryExistsByNumberAsync(
                info.ParentNumber.Value, hierarchy, categoryLookup, progress, cancellationToken);
            parentId = parentCategory?.Id;
        }

        // Создать категорию с уникальным именем (добавляем суффикс если дубликат)
        var uniqueName = categoryName;
        if (categoryLookup.ContainsKey(categoryName))
        {
            // Для уникальности добавляем номер родителя
            if (info.ParentNumber.HasValue && hierarchy.TryGetValue(info.ParentNumber.Value, out var parentInfo))
            {
                uniqueName = $"{categoryName} ({parentInfo.Name})";
            }
            else
            {
                uniqueName = $"{categoryName} #{categoryNumber}";
            }
        }

        var newCategory = new ProductCategory
        {
            Name = uniqueName,
            ParentId = parentId,
            Number = categoryNumber
        };

        _dbContext.ProductCategories.Add(newCategory);
        await _dbContext.SaveChangesAsync(cancellationToken);

        categoryLookup[uniqueName] = newCategory;
        _createdCategoryIds.Add(newCategory.Id);

        progress?.Report($"  [Classify] Created category #{categoryNumber}: {uniqueName}" +
            (parentId.HasValue ? $" (parent: #{info.ParentNumber})" : " (root)"));

        return newCategory;
    }

    /// <summary>
    /// Синхронизировать номера категорий из файла product_categories.txt в БД.
    /// Обновляет поле Number для существующих категорий.
    /// Для дубликатов имён использует контекст родителя.
    /// </summary>
    public async Task<int> SyncCategoryNumbersAsync(CancellationToken cancellationToken = default)
    {
        var hierarchy = LoadDefaultCategoryHierarchy();
        if (hierarchy.Count == 0)
        {
            _logger.LogWarning("No categories loaded from file, nothing to sync");
            return 0;
        }

        // Строим обратный индекс: Name → List<Number> (для дубликатов имён)
        var nameToNumbers = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (number, info) in hierarchy)
        {
            if (!nameToNumbers.TryGetValue(info.Name, out var list))
            {
                list = new List<int>();
                nameToNumbers[info.Name] = list;
            }
            list.Add(number);
        }

        var categories = await _dbContext.ProductCategories
            .Include(c => c.Parent)
            .ToListAsync(cancellationToken);
        var updated = 0;

        foreach (var category in categories)
        {
            if (!nameToNumbers.TryGetValue(category.Name, out var possibleNumbers))
                continue;

            int? matchedNumber = null;

            if (possibleNumbers.Count == 1)
            {
                // Уникальное имя - просто берём номер
                matchedNumber = possibleNumbers[0];
            }
            else
            {
                // Дубликат имени - нужно сопоставить по родителю
                foreach (var num in possibleNumbers)
                {
                    var info = hierarchy[num];

                    // Если нет родителя в файле и нет в БД - совпадение
                    if (!info.ParentNumber.HasValue && category.ParentId == null)
                    {
                        matchedNumber = num;
                        break;
                    }

                    // Если есть родитель - сравниваем
                    if (info.ParentNumber.HasValue && category.Parent != null)
                    {
                        var parentInfo = hierarchy.GetValueOrDefault(info.ParentNumber.Value);
                        if (parentInfo.Name != null &&
                            category.Parent.Name.Equals(parentInfo.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedNumber = num;
                            break;
                        }
                    }
                }
            }

            if (matchedNumber.HasValue && category.Number != matchedNumber.Value)
            {
                category.Number = matchedNumber.Value;
                updated++;
                _logger.LogDebug("Synced category '{Name}' -> Number {Number}", category.Name, matchedNumber.Value);
            }
        }

        if (updated > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Synced {Count} category numbers from file", updated);
        }

        return updated;
    }
}
