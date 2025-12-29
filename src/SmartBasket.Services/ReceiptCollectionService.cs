using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Entities;
using SmartBasket.Data;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Parsing;
using SmartBasket.Services.Products;
using SmartBasket.Services.Sources;
using SmartBasket.Services.Units;

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
///    - Выделение продуктов (Stage 1)
///    - Сохранение в БД
/// 3. Вернуть статистику
///
/// Классификация продуктов по категориям выполняется после загрузки всех чеков.
/// </summary>
public class ReceiptCollectionService : IReceiptCollectionService
{
    private readonly IReceiptSourceFactory _sourceFactory;
    private readonly ReceiptTextParserFactory _parserFactory;
    private readonly IProductExtractionService _extractionService;
    private readonly IProductClassificationService _classificationService;
    private readonly ILabelAssignmentService _labelAssignmentService;
    private readonly ILabelService _labelService;
    private readonly IUnitConversionService _unitConversionService;
    private readonly IAiSessionManager _sessionManager;
    private readonly SmartBasketDbContext _dbContext;
    private readonly AppSettings _settings;
    private readonly ILogger<ReceiptCollectionService> _logger;

    public ReceiptCollectionService(
        IReceiptSourceFactory sourceFactory,
        ReceiptTextParserFactory parserFactory,
        IProductExtractionService extractionService,
        IProductClassificationService classificationService,
        ILabelAssignmentService labelAssignmentService,
        ILabelService labelService,
        IUnitConversionService unitConversionService,
        IAiSessionManager sessionManager,
        SmartBasketDbContext dbContext,
        AppSettings settings,
        ILogger<ReceiptCollectionService> logger)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _extractionService = extractionService ?? throw new ArgumentNullException(nameof(extractionService));
        _classificationService = classificationService ?? throw new ArgumentNullException(nameof(classificationService));
        _labelAssignmentService = labelAssignmentService ?? throw new ArgumentNullException(nameof(labelAssignmentService));
        _labelService = labelService ?? throw new ArgumentNullException(nameof(labelService));
        _unitConversionService = unitConversionService ?? throw new ArgumentNullException(nameof(unitConversionService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
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

        // Создаём единую сессию для всех AI операций в рамках этого сбора
        // Это позволяет провайдерам кэшировать токены:
        // - GigaChat: X-Session-ID header
        // - YandexAgent: previous_response_id
        var sessionContext = _sessionManager.CreateSession("receipt-collection");
        progress?.Report($"[Session] Created AI session: {sessionContext.SessionId}");

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

                        // Stage 1: Выделение продуктов из названий товаров
                        var itemNames = parsed.Items.Select(i => i.Name).ToList();

                        // Найти уже существующие Items в БД (они уже связаны с Products)
                        var existingItemNames = await _dbContext.Items
                            .Where(i => itemNames.Contains(i.Name))
                            .Select(i => i.Name)
                            .ToListAsync(cancellationToken);

                        // Отфильтровать только НОВЫЕ товары для extraction
                        var newItemNames = itemNames
                            .Except(existingItemNames, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        ProductExtractionResult extraction;

                        if (newItemNames.Count > 0)
                        {
                            // Загрузить существующие продукты для передачи в extraction
                            var existingProductNames = await _dbContext.Products
                                .Select(p => p.Name)
                                .ToListAsync(cancellationToken);

                            // Загрузить справочник единиц измерения для передачи в extraction
                            var unitOfMeasures = await _dbContext.UnitOfMeasures
                                .Select(u => new UnitOfMeasureInfo
                                {
                                    Id = u.Id,
                                    Name = u.Name,
                                    BaseUnitId = u.BaseUnitId,
                                    IsBase = u.IsBase
                                })
                                .ToListAsync(cancellationToken);

                            try
                            {
                                progress?.Report($"  [Stage 1] Extracting products from {newItemNames.Count} NEW items (skipping {existingItemNames.Count} existing)...");
                                extraction = await _extractionService.ExtractAsync(
                                    newItemNames, existingProductNames, unitOfMeasures, sessionContext, progress, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Product extraction failed, using item names as products");
                                extraction = new ProductExtractionResult
                                {
                                    IsSuccess = false,
                                    Message = ex.Message,
                                    Items = newItemNames.Select(n => new ExtractedItem { Name = n, Product = n }).ToList()
                                };
                            }
                        }
                        else
                        {
                            // Все товары уже известны — extraction не нужен
                            progress?.Report($"  [Stage 1] All {itemNames.Count} items already exist, skipping extraction");
                            extraction = new ProductExtractionResult
                            {
                                IsSuccess = true,
                                Message = "All items already exist",
                                Items = new List<ExtractedItem>()
                            };
                        }

                        // Сохранение чека + создание Products и Items
                        var receipt = await SaveReceiptAsync(raw, parsed, extraction, sessionContext, progress, cancellationToken);
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
        progress?.Report($"=== Receipt collection complete ===");
        progress?.Report($"Sources: {result.SourcesProcessed}/{result.TotalSources}");
        progress?.Report($"Receipts: {result.ReceiptsSaved} saved, {result.ReceiptsSkipped} skipped, {result.Errors} errors");

        // Классификация некатегоризированных продуктов после загрузки всех чеков
        if (result.ReceiptsSaved > 0)
        {
            try
            {
                progress?.Report($"");
                progress?.Report($"=== Classifying uncategorized products ===");

                var classificationResult = await _classificationService.ClassifyAndApplyAsync(sessionContext, progress, cancellationToken);
                if (classificationResult.IsSuccess)
                {
                    progress?.Report($"Classification complete: {classificationResult.Message}");
                }
                else
                {
                    progress?.Report($"Classification failed: {classificationResult.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Product classification failed");
                progress?.Report($"[WARNING] Classification failed: {ex.Message}");
            }
        }

        return result;
    }

    private async Task<ParsedReceipt?> ParseRawReceiptAsync(
        RawReceipt raw,
        string parserName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("ParseRawReceiptAsync: parserName={ParserName}", parserName);
        progress?.Report($"  [Parse] Parser from config: {parserName}");

        // Если указан конкретный парсер (не "Auto") — используем его
        if (!string.IsNullOrEmpty(parserName) &&
            !parserName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            var parser = _parserFactory.GetParser(parserName);
            if (parser != null)
            {
                progress?.Report($"  [Parse] Using parser: {parser.Name}");

                // Для LLM парсера используем async метод
                if (parser is LlmUniversalParser llmParser)
                {
                    return await llmParser.ParseAsync(raw.Content, raw.Date, progress, cancellationToken);
                }

                // Regex парсер — синхронный
                var result = parser.Parse(raw.Content, raw.Date, raw.Subject);
                if (result.IsSuccess)
                {
                    progress?.Report($"  [Parse] Success: {result.Items.Count} items from {result.Shop}");
                    return result;
                }

                // Regex парсер не смог — fallback на LLM
                progress?.Report($"  [Parse] Parser {parserName} failed: {result.Message}, trying LLM fallback...");
            }
            else
            {
                progress?.Report($"  [Parse] Parser '{parserName}' not found, trying auto-detect...");
            }
        }

        // "Auto" режим: сначала пробуем regex-парсеры по CanParse()
        progress?.Report($"  [Parse] Auto-detect: trying regex parsers...");
        var regexResult = _parserFactory.TryParseWithRegex(raw.Content, raw.Date, raw.Subject);
        if (regexResult != null && regexResult.IsSuccess)
        {
            progress?.Report($"  [Parse] Regex parser success: {regexResult.Items.Count} items");
            return regexResult;
        }

        // Fallback на LLM
        progress?.Report($"  [Parse] No regex match, using LLM...");
        var llm = _parserFactory.GetLlmParser();
        return await llm.ParseAsync(raw.Content, raw.Date, progress, cancellationToken);
    }

    private async Task<Receipt> SaveReceiptAsync(
        RawReceipt raw,
        ParsedReceipt parsed,
        ProductExtractionResult extraction,
        LlmSessionContext? sessionContext,
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
        await ProcessReceiptItemsAsync(receipt, parsed, extraction, sessionContext, progress, cancellationToken);

        return receipt;
    }

    /// <summary>
    /// Обработка товаров чека: привязка к Products и создание Items.
    /// </summary>
    private async Task ProcessReceiptItemsAsync(
        Receipt receipt,
        ParsedReceipt parsed,
        ProductExtractionResult extraction,
        LlmSessionContext? sessionContext,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Создать lookup для extraction: itemName → ExtractedItem (полная информация)
        // ВАЖНО: сопоставляем по имени товара из extraction.Items[].Name, а не по индексу!
        var extractionLookup = new Dictionary<string, ExtractedItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractedItem in extraction.Items)
        {
            if (!string.IsNullOrWhiteSpace(extractedItem.Name) && !extractionLookup.ContainsKey(extractedItem.Name))
            {
                extractionLookup[extractedItem.Name] = extractedItem;
            }
        }

        // Загрузить существующие Products
        var existingProducts = await _dbContext.Products
            .ToDictionaryAsync(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // Нормализованный lookup для поиска (ё → е, lowercase)
        var normalizedLookup = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in existingProducts.Values)
        {
            normalizedLookup.TryAdd(p.Name, p);
            var normalized = NormalizeName(p.Name);
            if (normalized != p.Name)
            {
                normalizedLookup.TryAdd(normalized, p);
            }
        }

        // Создать Items и ReceiptItems
        var newItems = new List<Item>();

        foreach (var parsedItem in parsed.Items)
        {
            if (string.IsNullOrWhiteSpace(parsedItem.Name))
                continue;

            var itemName = parsedItem.Name.Trim();

            // Сначала проверим, существует ли уже Item в БД
            var existingItem = await _dbContext.Items
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.Name == itemName, cancellationToken);

            Product? product;
            Item item;

            if (existingItem != null)
            {
                // Item уже существует — используем его существующий Product
                item = existingItem;
                product = existingItem.Product!;
            }
            else
            {
                // Новый Item — нужно найти или создать Product

                // Найти информацию о продукте из extraction
                ExtractedItem? extractedItem = null;
                string productName;
                string baseUnitId = "шт"; // По умолчанию

                if (extractionLookup.TryGetValue(itemName, out extractedItem) &&
                    !string.IsNullOrWhiteSpace(extractedItem.Product))
                {
                    productName = extractedItem.Product.Trim();
                    baseUnitId = await _unitConversionService.NormalizeUnitAsync(extractedItem.BaseUnit);
                }
                else
                {
                    // Если продукт не выделен - используем само название товара как продукт
                    productName = itemName;
                }

                // Нормализуем название продукта к Title Case для единообразия
                var normalizedProductName = NormalizeName(productName);

                // Найти Product (с учётом нормализации)
                product = null;

                if (existingProducts.TryGetValue(normalizedProductName, out product) ||
                    normalizedLookup.TryGetValue(normalizedProductName, out product))
                {
                    // Найден существующий
                }
                else
                {
                    // Создаём новый Product с нормализованным именем
                    product = new Product
                    {
                        Name = normalizedProductName,
                        BaseUnitId = baseUnitId
                    };
                    _dbContext.Products.Add(product);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    existingProducts[normalizedProductName] = product;
                    normalizedLookup[normalizedProductName] = product;
                    progress?.Report($"  [Product] Created: {normalizedProductName} ({baseUnitId})");
                }

                // Создаём новый Item
                var itemUnitId = await _unitConversionService.NormalizeUnitAsync(parsedItem.Unit);
                var itemUnitQuantity = parsedItem.UnitQuantity ?? 1;
                var itemBaseUnitQuantity = await _unitConversionService.ConvertToBaseUnitAsync(
                    itemUnitQuantity, itemUnitId);

                item = new Item
                {
                    Name = itemName,
                    ProductId = product.Id,
                    UnitId = itemUnitId,
                    UnitQuantity = itemUnitQuantity,
                    BaseUnitQuantity = itemBaseUnitQuantity,
                    Shop = parsed.Shop ?? "Unknown"
                };
                _dbContext.Items.Add(item);
                await _dbContext.SaveChangesAsync(cancellationToken);
                newItems.Add(item);
            }

            // Нормализуем единицу количества в чеке
            var quantityUnitId = await _unitConversionService.NormalizeUnitAsync(parsedItem.QuantityUnit);

            // Вычисляем BaseUnitQuantity для ReceiptItem
            // Если QuantityUnit == "шт", то BaseUnitQuantity = Quantity × Item.BaseUnitQuantity
            // Если QuantityUnit == "кг"/"л", то BaseUnitQuantity = Quantity (уже в базовых единицах)
            decimal receiptBaseUnitQuantity;
            if (quantityUnitId == "шт")
            {
                // Штучный товар: умножаем на количество единиц товара в базовых
                receiptBaseUnitQuantity = parsedItem.Quantity * item.BaseUnitQuantity;
            }
            else
            {
                // Весовой/объёмный товар: конвертируем в базовые единицы
                receiptBaseUnitQuantity = await _unitConversionService.ConvertToBaseUnitAsync(
                    parsedItem.Quantity, quantityUnitId);
            }

            // Создать ReceiptItem
            var receiptItem = new ReceiptItem
            {
                ReceiptId = receipt.Id,
                ItemId = item.Id,
                Quantity = parsedItem.Quantity,
                QuantityUnitId = quantityUnitId,
                BaseUnitQuantity = receiptBaseUnitQuantity,
                Price = parsedItem.Price,
                Amount = parsedItem.Amount ?? (parsedItem.Price * parsedItem.Quantity)
            };
            _dbContext.ReceiptItems.Add(receiptItem);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Назначить метки новым товарам
        if (newItems.Count > 0)
        {
            await AssignLabelsAsync(newItems, existingProducts, sessionContext, progress, cancellationToken);
        }
    }

    private async Task AssignLabelsAsync(
        List<Item> items,
        Dictionary<string, Product> productLookup,
        LlmSessionContext? sessionContext,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Автоматически синхронизировать метки из файла
        var labelsFilePath = Path.Combine(AppContext.BaseDirectory, "user_labels.txt");
        var syncedCount = await _labelService.SyncFromFileAsync(labelsFilePath, cancellationToken);
        if (syncedCount > 0)
        {
            progress?.Report($"  [Labels] Synced {syncedCount} labels from file");
        }

        var availableLabels = await _dbContext.Labels
            .ToDictionaryAsync(l => l.Name, l => l, StringComparer.OrdinalIgnoreCase, cancellationToken);

        if (availableLabels.Count == 0)
        {
            progress?.Report("  [Labels] No labels in database (check user_labels.txt)");
            return;
        }

        progress?.Report($"  [Labels] Available labels: {availableLabels.Count}");

        var batchItems = items.Select(item =>
        {
            var product = productLookup.Values.FirstOrDefault(p => p.Id == item.ProductId);
            return new BatchItemInput
            {
                ItemName = item.Name,
                ProductName = product?.Name ?? item.Name
            };
        }).ToList();

        try
        {
            var result = await _labelAssignmentService.AssignLabelsBatchAsync(
                batchItems,
                availableLabels.Keys.ToList(),
                sessionContext,
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

    /// <summary>
    /// Нормализует имя продукта для единообразия:
    /// - Приводит к Title Case (первая буква каждого слова заглавная)
    /// - Заменяет ё на е
    /// </summary>
    private static string NormalizeName(string name)
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
