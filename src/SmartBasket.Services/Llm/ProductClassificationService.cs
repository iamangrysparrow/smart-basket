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
    private string? _promptTemplate;
    private string? _promptTemplatePath;
    private string? _customPrompt;

    // Трекинг созданных категорий в текущей сессии (для удаления пустых)
    private readonly HashSet<Guid> _createdCategoryIds = new();

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
        IReadOnlyList<string> productNames,
        IReadOnlyList<ExistingCategory> existingCategories,
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

            var prompt = BuildPrompt(productNames, existingCategories, progress);
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
                result.IsSuccess = true;

                var categories = result.Products.Count(p => !p.IsProduct);
                var products = result.Products.Count(p => p.IsProduct);
                result.Message = $"Got {categories} categories, {products} products";
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
        IReadOnlyList<string> productNames,
        IReadOnlyList<ExistingCategory> existingCategories,
        IProgress<string>? progress = null)
    {
        // Список продуктов для классификации
        var productsList = string.Join("\n", productNames);

        // Существующая иерархия категорий (без продуктов)
        var hierarchyText = BuildHierarchyText(existingCategories);

        // Priority 1: Custom prompt (from settings)
        if (!string.IsNullOrWhiteSpace(_customPrompt))
        {
            progress?.Report($"  [Classify] Using custom prompt ({_customPrompt.Length} chars)");
            return _customPrompt
                .Replace("{{EXISTING_HIERARCHY}}", hierarchyText)
                .Replace("{{PRODUCTS}}", productsList);
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
                .Replace("{{PRODUCTS}}", productsList);
        }

        // Priority 3: Default prompt (fallback)
        return $@"Классифицируй продукты и построй иерархию категорий.

СУЩЕСТВУЮЩАЯ ИЕРАРХИЯ КАТЕГОРИЙ:
{hierarchyText}

ПРОДУКТЫ ДЛЯ КЛАССИФИКАЦИИ:
{productsList}

ПРАВИЛА:
1. Каждый продукт из входного списка ДОЛЖЕН быть в ответе с ""product"": true
2. Используй существующие категории если подходят
3. Создавай новые категории только если нужно
4. ЗАПРЕЩЕНО использовать ""Не категоризировано"", ""Другое"", ""Прочее""

ФОРМАТ ОТВЕТА (строго JSON):
{{""products"":[{{""name"":""Название"",""parent"":null или ""имя-родителя"",""product"":true/false}}]}}

Классифицируй ВСЕ {productNames.Count} продуктов:";
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

            // 2. Загрузить существующие категории
            var existingCategories = await GetExistingCategoriesAsync(cancellationToken);
            progress?.Report($"  [Classify] Existing categories: {existingCategories.Count}");

            // 3. Вызвать LLM для классификации
            var classificationResult = await ClassifyAsync(
                productsToClassify,
                existingCategories,
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
            ParentName = c.Parent?.Name
        }).ToList();
    }

    /// <summary>
    /// Применить результат классификации к БД
    /// </summary>
    private async Task ApplyClassificationResultAsync(
        ProductClassificationResult classificationResult,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Построить lookup категорий
        var categoryLookup = await _dbContext.ProductCategories
            .ToDictionaryAsync(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // Сначала создаём все категории (product: false)
        var categoriesToCreate = classificationResult.Products
            .Where(p => !p.IsProduct)
            .ToList();

        foreach (var categoryInfo in categoriesToCreate)
        {
            if (categoryLookup.ContainsKey(categoryInfo.Name))
                continue; // Уже существует

            // Найти родителя если указан
            Guid? parentId = null;
            if (!string.IsNullOrWhiteSpace(categoryInfo.Parent))
            {
                if (categoryLookup.TryGetValue(categoryInfo.Parent, out var parent))
                {
                    parentId = parent.Id;
                }
                else
                {
                    // Родитель ещё не создан - создадим его
                    var parentCategory = new ProductCategory { Name = categoryInfo.Parent };
                    _dbContext.ProductCategories.Add(parentCategory);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    categoryLookup[categoryInfo.Parent] = parentCategory;
                    _createdCategoryIds.Add(parentCategory.Id);
                    progress?.Report($"  [Classify] Created parent category: {categoryInfo.Parent}");
                    parentId = parentCategory.Id;
                }
            }

            var newCategory = new ProductCategory
            {
                Name = categoryInfo.Name,
                ParentId = parentId
            };
            _dbContext.ProductCategories.Add(newCategory);
            await _dbContext.SaveChangesAsync(cancellationToken);
            categoryLookup[categoryInfo.Name] = newCategory;
            _createdCategoryIds.Add(newCategory.Id);
            progress?.Report($"  [Classify] Created category: {categoryInfo.Name}");
        }

        // Теперь привязываем продукты к категориям
        var productsToUpdate = classificationResult.Products
            .Where(p => p.IsProduct && !string.IsNullOrWhiteSpace(p.Parent))
            .ToList();

        progress?.Report($"  [Classify] Products to update: {productsToUpdate.Count}");
        progress?.Report($"  [Classify] Categories in lookup: {string.Join(", ", categoryLookup.Keys)}");

        // Загрузить продукты из БД - используем нормализованные имена (lowercase) для поиска
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

        foreach (var productInfo in productsToUpdate)
        {
            // Нормализуем имя продукта для поиска
            var normalizedProductName = NormalizeProductName(productInfo.Name);
            progress?.Report($"  [Classify] Processing: '{productInfo.Name}' (normalized: '{normalizedProductName}') -> parent='{productInfo.Parent}'");

            if (!productLookup.TryGetValue(normalizedProductName, out var product))
            {
                progress?.Report($"  [Classify] ERROR: Product not found in DB: '{productInfo.Name}'");
                // Попробуем найти похожие
                var similar = productLookup.Keys
                    .Where(k => k.Contains(normalizedProductName.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
                    .Take(3);
                if (similar.Any())
                {
                    progress?.Report($"  [Classify]   Similar products: {string.Join(", ", similar)}");
                }
                continue;
            }

            if (!categoryLookup.TryGetValue(productInfo.Parent!, out var category))
            {
                progress?.Report($"  [Classify] ERROR: Category not found: '{productInfo.Parent}'");
                continue;
            }

            var oldCategoryId = product.CategoryId;
            product.CategoryId = category.Id;
            progress?.Report($"  [Classify] OK: {normalizedProductName} -> {productInfo.Parent} (old: {oldCategoryId}, new: {category.Id})");
        }

        var changedCount = _dbContext.ChangeTracker.Entries<Product>().Count(e => e.State == EntityState.Modified);
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

            // 3. Загрузить существующие категории (для контекста LLM)
            var existingCategories = await GetExistingCategoriesAsync(cancellationToken);
            progress?.Report($"  [Reclassify] Existing categories: {existingCategories.Count}");

            // 4. Вызвать LLM для классификации
            var classificationResult = await ClassifyAsync(
                productNames,
                existingCategories,
                progress,
                cancellationToken);

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

            // Сбросить кастомный промпт после использования
            _customPrompt = null;
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
    /// </summary>
    private static string NormalizeProductName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        // Заменяем ё на е
        name = name.Replace('ё', 'е').Replace('Ё', 'Е');

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
}
