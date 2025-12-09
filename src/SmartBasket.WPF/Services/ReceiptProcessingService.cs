using System.IO;
using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Entities;
using SmartBasket.Data;
using SmartBasket.Services.Ollama;

namespace SmartBasket.WPF.Services;

/// <summary>
/// Результат обработки одного чека
/// </summary>
public class ReceiptProcessingResult
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public Receipt? Receipt { get; set; }
    public int ItemsCreated { get; set; }
    public int ProductsCreated { get; set; }
    public int LabelsAssigned { get; set; }
}

/// <summary>
/// Сервис обработки чеков: создание Items, Products, ReceiptItems, Labels
/// </summary>
public class ReceiptProcessingService
{
    private readonly SmartBasketDbContext _dbContext;
    private readonly ILabelAssignmentService? _labelService;
    private OllamaSettings? _ollamaSettings;
    private string? _userLabelsPath;

    public ReceiptProcessingService(SmartBasketDbContext dbContext, ILabelAssignmentService? labelService = null)
    {
        _dbContext = dbContext;
        _labelService = labelService;
    }

    /// <summary>
    /// Установить настройки Ollama для назначения меток
    /// </summary>
    public void SetOllamaSettings(OllamaSettings settings)
    {
        _ollamaSettings = settings;
    }

    /// <summary>
    /// Установить путь к файлу user_labels.txt
    /// </summary>
    public void SetUserLabelsPath(string path)
    {
        _userLabelsPath = path;
    }

    /// <summary>
    /// Получить существующую иерархию продуктов для передачи в Ollama
    /// </summary>
    public async Task<List<ExistingProduct>> GetExistingProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await _dbContext.Products
            .Include(p => p.Parent)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return products.Select(p => new ExistingProduct
        {
            Id = p.Id,
            Name = p.Name,
            ParentId = p.ParentId,
            ParentName = p.Parent?.Name
        }).ToList();
    }

    /// <summary>
    /// Обработать результат классификации: создать/найти Products и Items, связать с чеком
    /// </summary>
    public async Task<ReceiptProcessingResult> ProcessClassificationAsync(
        Receipt receipt,
        ParsedReceipt parsedReceipt,
        ProductClassificationResult classification,
        string shop,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ReceiptProcessingResult { Receipt = receipt };

        try
        {
            // 0. Убедиться что метки загружены в БД
            await EnsureLabelsLoadedAsync(progress, cancellationToken);

            // 1. Обработать Products (создать новые, найти существующие)
            var productMap = await ProcessProductsAsync(classification.Products, progress, cancellationToken);
            result.ProductsCreated = productMap.Values.Count(p => p.Item2);

            // 2. Обработать Items и создать ReceiptItems (возвращает список новых Items для назначения меток)
            var (itemsCreated, newItems) = await ProcessItemsAsync(
                receipt,
                parsedReceipt.Items,
                classification.Items,
                productMap,
                shop,
                progress,
                cancellationToken);

            result.ItemsCreated = itemsCreated;

            // 3. Назначить метки для новых товаров
            if (newItems.Count > 0 && _labelService != null && _ollamaSettings != null)
            {
                var labelsAssigned = await AssignLabelsToNewItemsAsync(newItems, productMap, progress, cancellationToken);
                result.LabelsAssigned = labelsAssigned;
            }

            result.IsSuccess = true;
            result.Message = $"Created {result.ProductsCreated} products, {result.ItemsCreated} items, {result.LabelsAssigned} labels assigned";
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            progress?.Report($"  [Process] Error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Обработать продукты: найти существующие или создать новые
    /// </summary>
    private async Task<Dictionary<string, (Product Product, bool IsNew)>> ProcessProductsAsync(
        List<ClassifiedProduct> classifiedProducts,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var productMap = new Dictionary<string, (Product, bool)>(StringComparer.OrdinalIgnoreCase);

        // Загрузить все существующие продукты
        var existingProducts = await _dbContext.Products
            .ToDictionaryAsync(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        // Сортируем: сначала без parent (корневые), потом с parent
        var sortedProducts = classifiedProducts
            .OrderBy(p => p.Parent != null ? 1 : 0)
            .ThenBy(p => p.Name)
            .ToList();

        foreach (var cp in sortedProducts)
        {
            if (string.IsNullOrWhiteSpace(cp.Name))
                continue;

            // Проверить существующий
            if (existingProducts.TryGetValue(cp.Name, out var existing))
            {
                productMap[cp.Name] = (existing, false);
                progress?.Report($"  [Product] Found existing: {cp.Name}");
                continue;
            }

            // Создать новый
            var newProduct = new Product { Name = cp.Name };

            // Найти parent если указан
            if (!string.IsNullOrWhiteSpace(cp.Parent))
            {
                // Сначала ищем в уже обработанных
                if (productMap.TryGetValue(cp.Parent, out var parentTuple))
                {
                    newProduct.ParentId = parentTuple.Item1.Id;
                }
                // Потом в существующих
                else if (existingProducts.TryGetValue(cp.Parent, out var parentProduct))
                {
                    newProduct.ParentId = parentProduct.Id;
                }
                else
                {
                    progress?.Report($"  [Product] Warning: parent '{cp.Parent}' not found for '{cp.Name}'");
                }
            }

            _dbContext.Products.Add(newProduct);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            productMap[cp.Name] = (newProduct, true);
            existingProducts[cp.Name] = newProduct; // Добавить в локальный кэш

            progress?.Report($"  [Product] Created: {cp.Name}" + (newProduct.ParentId.HasValue ? $" (parent: {cp.Parent})" : ""));
        }

        return productMap;
    }

    /// <summary>
    /// Обработать товары: найти или создать Items, создать ReceiptItems
    /// Возвращает количество созданных Items и список новых Items (для назначения меток)
    /// </summary>
    private async Task<(int ItemsCreated, List<Item> NewItems)> ProcessItemsAsync(
        Receipt receipt,
        List<ParsedReceiptItem> parsedItems,
        List<ClassifiedItem> classifiedItems,
        Dictionary<string, (Product Product, bool IsNew)> productMap,
        string shop,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var itemsCreated = 0;
        var newItems = new List<Item>();

        // Создать lookup для классификации по имени товара
        var classificationLookup = classifiedItems
            .ToDictionary(ci => ci.Name, ci => ci.Product, StringComparer.OrdinalIgnoreCase);

        foreach (var parsedItem in parsedItems)
        {
            if (string.IsNullOrWhiteSpace(parsedItem.Name))
                continue;

            // Найти продукт для этого товара
            string? productName = null;
            if (classificationLookup.TryGetValue(parsedItem.Name, out var pn))
            {
                productName = pn;
            }

            Product? product = null;
            if (!string.IsNullOrWhiteSpace(productName) && productMap.TryGetValue(productName, out var tuple))
            {
                product = tuple.Item1;
            }

            if (product == null)
            {
                progress?.Report($"  [Item] Warning: no product for '{parsedItem.Name}', creating default");
                // Создать дефолтный продукт "Не категоризировано"
                product = await GetOrCreateUncategorizedProductAsync(cancellationToken);
                if (!productMap.ContainsKey(product.Name))
                {
                    productMap[product.Name] = (product, false);
                }
            }

            // Найти или создать Item
            var item = await _dbContext.Items
                .Include(i => i.ItemLabels)
                .FirstOrDefaultAsync(i => i.Name == parsedItem.Name, cancellationToken)
                .ConfigureAwait(false);

            var isNewItem = item == null;

            if (item == null)
            {
                item = new Item
                {
                    Name = parsedItem.Name,
                    ProductId = product.Id,
                    UnitOfMeasure = parsedItem.UnitOfMeasure ?? parsedItem.Unit ?? "шт",
                    UnitQuantity = parsedItem.UnitQuantity ?? 1,
                    Shop = shop
                };
                _dbContext.Items.Add(item);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                itemsCreated++;
                newItems.Add(item);

                progress?.Report($"  [Item] Created: {item.Name} -> {product.Name}");
            }
            else
            {
                // Обновить ProductId если изменился
                if (item.ProductId != product.Id)
                {
                    item.ProductId = product.Id;
                    progress?.Report($"  [Item] Updated product: {item.Name} -> {product.Name}");
                }
            }

            // Создать ReceiptItem
            var receiptItem = new ReceiptItem
            {
                ReceiptId = receipt.Id,
                ItemId = item.Id,
                Quantity = parsedItem.Quantity,
                Price = parsedItem.Price,
                Amount = parsedItem.Amount ?? (parsedItem.Price * parsedItem.Quantity)
            };
            _dbContext.ReceiptItems.Add(receiptItem);
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (itemsCreated, newItems);
    }

    private async Task<Product> GetOrCreateUncategorizedProductAsync(CancellationToken cancellationToken)
    {
        const string uncategorizedName = "Не категоризировано";

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Name == uncategorizedName, cancellationToken)
            .ConfigureAwait(false);

        if (product == null)
        {
            product = new Product { Name = uncategorizedName };
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return product;
    }

    /// <summary>
    /// Убедиться что метки загружены в БД из user_labels.txt
    /// </summary>
    private async Task EnsureLabelsLoadedAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        // Проверить есть ли метки в БД
        var labelsCount = await _dbContext.Labels.CountAsync(cancellationToken).ConfigureAwait(false);
        if (labelsCount > 0)
            return;

        // Если меток нет - попытаться загрузить из файла
        if (string.IsNullOrEmpty(_userLabelsPath) || !File.Exists(_userLabelsPath))
        {
            progress?.Report("  [Labels] No user_labels.txt found, skipping label loading");
            return;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(_userLabelsPath, cancellationToken).ConfigureAwait(false);
            var labels = lines
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (labels.Count == 0)
            {
                progress?.Report("  [Labels] user_labels.txt is empty");
                return;
            }

            foreach (var labelName in labels)
            {
                _dbContext.Labels.Add(new Label { Name = labelName });
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report($"  [Labels] Loaded {labels.Count} labels from user_labels.txt");
        }
        catch (Exception ex)
        {
            progress?.Report($"  [Labels] Error loading labels: {ex.Message}");
        }
    }

    /// <summary>
    /// Назначить метки для новых товаров через Ollama
    /// </summary>
    private async Task<int> AssignLabelsToNewItemsAsync(
        List<Item> newItems,
        Dictionary<string, (Product Product, bool IsNew)> productMap,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (_labelService == null || _ollamaSettings == null)
            return 0;

        // Получить все доступные метки
        var availableLabels = await _dbContext.Labels
            .Select(l => l.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (availableLabels.Count == 0)
        {
            progress?.Report("  [Labels] No labels in database, skipping label assignment");
            return 0;
        }

        // Создать lookup для меток по имени
        var labelsLookup = await _dbContext.Labels
            .ToDictionaryAsync(l => l.Name, l => l, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var totalLabelsAssigned = 0;

        foreach (var item in newItems)
        {
            try
            {
                // Найти название продукта для этого товара
                var product = productMap.Values
                    .FirstOrDefault(p => p.Item1.Id == item.ProductId);
                var productName = product.Item1?.Name ?? "Не категоризировано";

                var result = await _labelService.AssignLabelsAsync(
                    _ollamaSettings,
                    item.Name,
                    productName,
                    availableLabels,
                    progress,
                    cancellationToken);

                if (result.IsSuccess && result.AssignedLabels.Count > 0)
                {
                    foreach (var labelName in result.AssignedLabels)
                    {
                        if (labelsLookup.TryGetValue(labelName, out var label))
                        {
                            _dbContext.ItemLabels.Add(new ItemLabel
                            {
                                ItemId = item.Id,
                                LabelId = label.Id
                            });
                            totalLabelsAssigned++;
                        }
                    }

                    progress?.Report($"  [Labels] {item.Name}: {string.Join(", ", result.AssignedLabels)}");
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Labels] Error assigning labels to '{item.Name}': {ex.Message}");
            }
        }

        if (totalLabelsAssigned > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return totalLabelsAssigned;
    }
}
