using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Entities;
using SmartBasket.Data;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Ollama;
using SmartBasket.Services.Parsing;
using SmartBasket.Services.Sources;

namespace SmartBasket.Services;

/// <summary>
/// Результат сбора чеков
/// </summary>
public class CollectionResult
{
    public int TotalSources { get; set; }
    public int SourcesProcessed { get; set; }
    public int ReceiptsFetched { get; set; }
    public int ReceiptsParsed { get; set; }
    public int ReceiptsSaved { get; set; }
    public int ReceiptsSkipped { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; } = new();
    public List<Receipt> SavedReceipts { get; } = new();
}

/// <summary>
/// Интерфейс оркестратора сбора чеков
/// </summary>
public interface IReceiptCollectionService
{
    /// <summary>
    /// Собрать чеки из указанных источников или из всех включённых
    /// </summary>
    /// <param name="sourceNames">Имена источников (null = все включённые)</param>
    /// <param name="progress">Прогресс выполнения</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<CollectionResult> CollectAsync(
        IEnumerable<string>? sourceNames = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Оркестратор сбора чеков из источников
/// Workflow:
/// 1. Получить источники (по именам или все enabled)
/// 2. Для каждого источника:
///    - Fetch сырых данных
///    - Проверить дедупликацию
///    - Найти парсер по имени
///    - Если парсер требует AI → получить провайдер
///    - Парсинг → ParsedReceipt
///    - Классификация → Products/Items
///    - Сохранение в БД
/// 3. Вернуть статистику
/// </summary>
public class ReceiptCollectionService : IReceiptCollectionService
{
    private readonly IReceiptSourceFactory _sourceFactory;
    private readonly ReceiptTextParserFactory _parserFactory;
    private readonly IReceiptParsingService _llmParsingService;
    private readonly IProductClassificationService _classificationService;
    private readonly ILabelAssignmentService _labelAssignmentService;
    private readonly SmartBasketDbContext _dbContext;
    private readonly AppSettings _settings;
    private readonly ILogger<ReceiptCollectionService> _logger;

    public ReceiptCollectionService(
        IReceiptSourceFactory sourceFactory,
        ReceiptTextParserFactory parserFactory,
        IReceiptParsingService llmParsingService,
        IProductClassificationService classificationService,
        ILabelAssignmentService labelAssignmentService,
        SmartBasketDbContext dbContext,
        AppSettings settings,
        ILogger<ReceiptCollectionService> logger)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _llmParsingService = llmParsingService ?? throw new ArgumentNullException(nameof(llmParsingService));
        _classificationService = classificationService ?? throw new ArgumentNullException(nameof(classificationService));
        _labelAssignmentService = labelAssignmentService ?? throw new ArgumentNullException(nameof(labelAssignmentService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CollectionResult> CollectAsync(
        IEnumerable<string>? sourceNames = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CollectionResult();

        // 1. Получить источники
        IReadOnlyList<IReceiptSource> sources;
        if (sourceNames != null && sourceNames.Any())
        {
            sources = sourceNames
                .Select(name => _sourceFactory.GetByName(name))
                .Where(s => s != null)
                .Cast<IReceiptSource>()
                .ToList();
        }
        else
        {
            sources = _sourceFactory.CreateAllEnabled();
        }

        result.TotalSources = sources.Count;
        progress?.Report($"=== Starting collection from {sources.Count} source(s) ===");

        if (sources.Count == 0)
        {
            progress?.Report("No sources configured or enabled");
            return result;
        }

        // 2. Обработать каждый источник
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report($"");
            progress?.Report($"--- Source: {source.Name} ({source.Type}) ---");

            try
            {
                // 2.1 Fetch сырых данных
                var rawReceipts = await source.FetchAsync(progress, cancellationToken);
                result.ReceiptsFetched += rawReceipts.Count;
                progress?.Report($"Fetched {rawReceipts.Count} raw receipts");

                // 2.2 Обработать каждый чек
                foreach (var raw in rawReceipts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Проверить дедупликацию
                        if (!string.IsNullOrEmpty(raw.ExternalId))
                        {
                            var exists = await _dbContext.EmailHistory
                                .AnyAsync(e => e.EmailId == raw.ExternalId, cancellationToken);

                            if (exists)
                            {
                                progress?.Report($"  [SKIP] Already processed: {raw.ExternalId}");
                                result.ReceiptsSkipped++;
                                continue;
                            }
                        }

                        // Парсинг
                        var parsed = await ParseRawReceiptAsync(raw, source.ParserName, progress, cancellationToken);

                        if (parsed == null || !parsed.IsSuccess || parsed.Items.Count == 0)
                        {
                            progress?.Report($"  [SKIP] Parse failed: {parsed?.Message ?? "null result"}");
                            await SaveEmailHistoryAsync(raw, EmailProcessingStatus.Skipped, parsed?.Message);
                            result.ReceiptsSkipped++;
                            continue;
                        }

                        result.ReceiptsParsed++;

                        // Классификация
                        var existingProducts = await GetExistingProductsAsync(cancellationToken);
                        var itemNames = parsed.Items.Select(i => i.Name).ToList();

                        ProductClassificationResult classification;
                        try
                        {
                            classification = await _classificationService.ClassifyAsync(
                                itemNames, existingProducts, progress, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Classification failed, using defaults");
                            classification = new ProductClassificationResult
                            {
                                IsSuccess = false,
                                Message = ex.Message
                            };
                        }

                        // Сохранение
                        var receipt = await SaveReceiptAsync(raw, parsed, classification, progress, cancellationToken);
                        result.SavedReceipts.Add(receipt);
                        result.ReceiptsSaved++;

                        await SaveEmailHistoryAsync(raw, EmailProcessingStatus.Processed);

                        progress?.Report($"  [OK] Saved: {parsed.Shop}, {parsed.Items.Count} items");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing receipt from {Source}", source.Name);
                        result.Errors++;
                        result.ErrorMessages.Add($"{source.Name}: {ex.Message}");
                        progress?.Report($"  [ERROR] {ex.Message}");

                        if (!string.IsNullOrEmpty(raw.ExternalId))
                        {
                            await SaveEmailHistoryAsync(raw, EmailProcessingStatus.Failed, ex.Message);
                        }
                    }
                }

                result.SourcesProcessed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching from source {Source}", source.Name);
                result.Errors++;
                result.ErrorMessages.Add($"{source.Name} fetch: {ex.Message}");
                progress?.Report($"[ERROR] Source {source.Name} failed: {ex.Message}");
            }
        }

        progress?.Report($"");
        progress?.Report($"=== Collection complete ===");
        progress?.Report($"Sources: {result.SourcesProcessed}/{result.TotalSources}");
        progress?.Report($"Receipts: {result.ReceiptsSaved} saved, {result.ReceiptsSkipped} skipped, {result.Errors} errors");

        return result;
    }

    private async Task<ParsedReceipt?> ParseRawReceiptAsync(
        RawReceipt raw,
        string parserName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Сначала пробуем regex-парсеры
        var regexResult = _parserFactory.TryParse(raw.Content, raw.Date);
        if (regexResult != null && regexResult.IsSuccess)
        {
            progress?.Report($"  [Parse] Regex parser success: {regexResult.Items.Count} items");
            return regexResult;
        }

        // Fallback на LLM
        progress?.Report($"  [Parse] No regex match, using LLM...");
        return await _llmParsingService.ParseReceiptAsync(raw.Content, raw.Date, progress, cancellationToken);
    }

    private async Task<List<ExistingProduct>> GetExistingProductsAsync(CancellationToken cancellationToken)
    {
        var products = await _dbContext.Products
            .Include(p => p.Parent)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return products.Select(p => new ExistingProduct
        {
            Id = p.Id,
            Name = p.Name,
            ParentId = p.ParentId,
            ParentName = p.Parent?.Name
        }).ToList();
    }

    private async Task<Receipt> SaveReceiptAsync(
        RawReceipt raw,
        ParsedReceipt parsed,
        ProductClassificationResult classification,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var receiptDate = parsed.Date ?? raw.Date;

        var receipt = new Receipt
        {
            Shop = parsed.Shop ?? "Unknown",
            ReceiptDate = DateTime.SpecifyKind(receiptDate, DateTimeKind.Utc),
            ReceiptNumber = parsed.OrderNumber,
            Total = parsed.Total,
            EmailId = raw.ExternalId,
            Status = ReceiptStatus.Parsed
        };

        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Обработать Products и Items
        await ProcessReceiptItemsAsync(receipt, parsed, classification, progress, cancellationToken);

        return receipt;
    }

    private async Task ProcessReceiptItemsAsync(
        Receipt receipt,
        ParsedReceipt parsed,
        ProductClassificationResult classification,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Создать lookup для классификации
        var classificationLookup = classification.Items
            .ToDictionary(ci => ci.Name, ci => ci.Product, StringComparer.OrdinalIgnoreCase);

        // Загрузить существующие Products
        var existingProducts = await _dbContext.Products
            .ToDictionaryAsync(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // Создать новые Products из классификации
        foreach (var cp in classification.Products.OrderBy(p => p.Parent != null ? 1 : 0))
        {
            if (string.IsNullOrWhiteSpace(cp.Name) || existingProducts.ContainsKey(cp.Name.Trim()))
                continue;

            var newProduct = new Product { Name = cp.Name.Trim() };

            if (!string.IsNullOrWhiteSpace(cp.Parent) && existingProducts.TryGetValue(cp.Parent.Trim(), out var parent))
            {
                newProduct.ParentId = parent.Id;
            }

            _dbContext.Products.Add(newProduct);
            await _dbContext.SaveChangesAsync(cancellationToken);
            existingProducts[newProduct.Name] = newProduct;
            progress?.Report($"  [Product] Created: {newProduct.Name}");
        }

        // Обеспечить наличие дефолтного продукта
        if (!existingProducts.ContainsKey("Не категоризировано"))
        {
            var uncategorized = new Product { Name = "Не категоризировано" };
            _dbContext.Products.Add(uncategorized);
            await _dbContext.SaveChangesAsync(cancellationToken);
            existingProducts["Не категоризировано"] = uncategorized;
        }

        // Создать Items и ReceiptItems
        var newItems = new List<Item>();

        foreach (var parsedItem in parsed.Items)
        {
            if (string.IsNullOrWhiteSpace(parsedItem.Name))
                continue;

            var itemName = parsedItem.Name.Trim();

            // Найти Product
            string? productName = null;
            if (classificationLookup.TryGetValue(itemName, out var pn))
            {
                productName = pn?.Trim();
            }

            Product product;
            if (!string.IsNullOrWhiteSpace(productName) && existingProducts.TryGetValue(productName, out var p))
            {
                product = p;
            }
            else
            {
                product = existingProducts["Не категоризировано"];
            }

            // Найти или создать Item
            var item = await _dbContext.Items
                .FirstOrDefaultAsync(i => i.Name == itemName, cancellationToken);

            if (item == null)
            {
                item = new Item
                {
                    Name = itemName,
                    ProductId = product.Id,
                    UnitOfMeasure = parsedItem.UnitOfMeasure ?? parsedItem.Unit ?? "шт",
                    UnitQuantity = parsedItem.UnitQuantity ?? 1,
                    Shop = parsed.Shop ?? "Unknown"
                };
                _dbContext.Items.Add(item);
                await _dbContext.SaveChangesAsync(cancellationToken);
                newItems.Add(item);
            }
            else if (item.ProductId != product.Id)
            {
                item.ProductId = product.Id;
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

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Назначить метки новым товарам
        if (newItems.Count > 0)
        {
            await AssignLabelsAsync(newItems, existingProducts, progress, cancellationToken);
        }
    }

    private async Task AssignLabelsAsync(
        List<Item> items,
        Dictionary<string, Product> productLookup,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var availableLabels = await _dbContext.Labels
            .ToDictionaryAsync(l => l.Name, l => l, StringComparer.OrdinalIgnoreCase, cancellationToken);

        if (availableLabels.Count == 0)
        {
            progress?.Report("  [Labels] No labels in database");
            return;
        }

        var batchItems = items.Select(item =>
        {
            var product = productLookup.Values.FirstOrDefault(p => p.Id == item.ProductId);
            return new BatchItemInput
            {
                ItemName = item.Name,
                ProductName = product?.Name ?? "Не категоризировано"
            };
        }).ToList();

        try
        {
            var result = await _labelAssignmentService.AssignLabelsBatchAsync(
                batchItems,
                availableLabels.Keys.ToList(),
                progress,
                cancellationToken);

            if (!result.IsSuccess)
            {
                progress?.Report($"  [Labels] Assignment failed: {result.Message}");
                return;
            }

            var itemsLookup = items.ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);

            foreach (var itemResult in result.Results)
            {
                if (!itemsLookup.TryGetValue(itemResult.ItemName, out var item))
                    continue;

                foreach (var labelName in itemResult.Labels)
                {
                    if (availableLabels.TryGetValue(labelName, out var label))
                    {
                        _dbContext.ItemLabels.Add(new ItemLabel
                        {
                            ItemId = item.Id,
                            LabelId = label.Id
                        });
                    }
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            progress?.Report($"  [Labels] Error: {ex.Message}");
        }
    }

    private async Task SaveEmailHistoryAsync(RawReceipt raw, EmailProcessingStatus status, string? errorMessage = null)
    {
        if (string.IsNullOrEmpty(raw.ExternalId))
            return;

        var history = new EmailHistory
        {
            EmailId = raw.ExternalId,
            Sender = "unknown",
            Subject = "Receipt",
            ReceivedAt = raw.Date,
            ProcessedAt = DateTime.UtcNow,
            Status = status,
            ErrorMessage = errorMessage
        };

        _dbContext.EmailHistory.Add(history);
        await _dbContext.SaveChangesAsync();
    }
}
