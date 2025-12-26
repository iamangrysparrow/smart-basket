using System.Runtime.CompilerServices;
using AiWebSniffer.Core.Interfaces;
using AiWebSniffer.Core.Models;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Shopping;

namespace SmartBasket.Services.Shopping.Operations;

/// <summary>
/// Реализация операции сборки корзин.
/// Оркестрирует: поиск в парсерах → AI выбор товара → формирование корзин.
/// </summary>
public class BasketBuilderOperation : IBasketBuilderOperation
{
    private readonly IShoppingSessionService _sessionService;
    private readonly IProductMatcherOperation _productMatcher;
    private readonly ILogger<BasketBuilderOperation> _logger;

    private readonly Dictionary<string, PlannedBasket> _builtBaskets = new();

    public BasketBuilderOperation(
        IShoppingSessionService sessionService,
        IProductMatcherOperation productMatcher,
        ILogger<BasketBuilderOperation> logger)
    {
        _sessionService = sessionService;
        _productMatcher = productMatcher;
        _logger = logger;
    }

    public async IAsyncEnumerable<WorkflowProgress> BuildBasketsAsync(
        IReadOnlyList<DraftItem> items,
        IWebViewContext webViewContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _builtBaskets.Clear();

        var storeConfigs = _sessionService.GetStoreConfigs();
        var stores = storeConfigs.Values
            .OrderBy(c => c.Priority)
            .ToList();

        _logger.LogInformation("[BasketBuilder] Starting build for {ItemCount} items in {StoreCount} stores",
            items.Count, stores.Count);

        yield return new SystemMessageProgress(
            $"Начинаю поиск {items.Count} товаров в {stores.Count} магазинах...", false);

        foreach (var config in stores)
        {
            ct.ThrowIfCancellationRequested();

            // Словарь для AI выбора: DraftItemId -> (ProductSearchResult?, Quantity, Reasoning)
            var aiSelections = new Dictionary<Guid, (ProductSearchResult? Selected, int Quantity, string? Reasoning, List<ProductSearchResult> Alternatives)>();

            _logger.LogInformation("[BasketBuilder] Processing store: {Store}", config.StoreName);

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                // === ПОИСК ===
                yield return new SearchStartedProgress(item.Name, config.StoreName, config.Color);

                // Выполняем поиск ВНЕ try-catch, потом решаем что yield
                List<ProductSearchResult>? searchResults = null;
                string? searchError = null;

                try
                {
                    searchResults = await SearchInStoreAsync(
                        webViewContext, config.StoreId, item.Name, config.SearchLimit, ct);
                }
                catch (Exception ex)
                {
                    searchError = ex.Message;
                    _logger.LogWarning(ex, "[BasketBuilder] Search failed for '{Item}' in {Store}",
                        item.Name, config.StoreName);
                }

                // Теперь можем yield — вне try-catch
                if (searchError != null)
                {
                    yield return new SearchFailedProgress(
                        item.Name, config.StoreName, config.Color, searchError);
                    continue;
                }

                if (searchResults == null || searchResults.Count == 0)
                {
                    yield return new SearchCompletedProgress(
                        item.Name, config.StoreName, config.Color, 0, new List<ProductSearchResult>());

                    _logger.LogDebug("[BasketBuilder] No results for '{Item}' in {Store}",
                        item.Name, config.StoreName);
                    continue;
                }

                yield return new SearchCompletedProgress(
                    item.Name, config.StoreName, config.Color, searchResults.Count, searchResults);

                _logger.LogDebug("[BasketBuilder] Found {Count} results for '{Item}' in {Store}",
                    searchResults.Count, item.Name, config.StoreName);

                // === AI ВЫБОР ===
                yield return new ProductSelectionStartedProgress(item.Name, config.StoreName, config.Color);

                // Выполняем AI выбор ВНЕ try-catch, потом решаем что yield
                ProductSelectionResult? selectionResult = null;
                string? selectionError = null;

                try
                {
                    selectionResult = await _productMatcher.SelectProductAsync(
                        item, searchResults, ct: ct);
                }
                catch (Exception ex)
                {
                    selectionError = ex.Message;
                    _logger.LogWarning(ex, "[BasketBuilder] AI selection failed for '{Item}' in {Store}",
                        item.Name, config.StoreName);
                }

                // Теперь можем yield — вне try-catch
                if (selectionError != null)
                {
                    yield return new ProductSelectionFailedProgress(
                        item.Name, config.StoreName, config.Color, $"Ошибка AI: {selectionError}");
                    continue;
                }

                if (selectionResult != null && selectionResult.Success && selectionResult.Selected != null)
                {
                    yield return new ProductSelectionCompletedProgress(
                        item.Name,
                        config.StoreName,
                        config.Color,
                        selectionResult.Selected,
                        selectionResult.Reason,
                        selectionResult.Alternatives);

                    aiSelections[item.Id] = (
                        selectionResult.Selected,
                        (int)item.Quantity,
                        selectionResult.Reason,
                        selectionResult.Alternatives);

                    _logger.LogInformation("[BasketBuilder] Selected '{Product}' for '{Item}' in {Store}",
                        selectionResult.Selected.Name, item.Name, config.StoreName);
                }
                else
                {
                    var reason = selectionResult?.Reason ?? "Неизвестная ошибка";
                    yield return new ProductSelectionFailedProgress(
                        item.Name, config.StoreName, config.Color, reason);

                    _logger.LogDebug("[BasketBuilder] No suitable product for '{Item}' in {Store}: {Reason}",
                        item.Name, config.StoreName, reason);
                }
            }

            // Формируем PlannedBasket для магазина
            var basket = BuildPlannedBasket(config, items, aiSelections);
            _builtBaskets[config.StoreId] = basket;

            _logger.LogInformation("[BasketBuilder] Completed {Store}: {Found}/{Total} items, total {Price:N0} ₽",
                config.StoreName, basket.ItemsFound, basket.ItemsTotal, basket.TotalPrice);
        }

        yield return new SystemMessageProgress(
            $"Поиск завершён. Собрано {_builtBaskets.Count} корзин.", false);
    }

    public IReadOnlyDictionary<string, PlannedBasket> GetBuiltBaskets() => _builtBaskets;

    /// <summary>
    /// Поиск товара в магазине через парсер
    /// </summary>
    private async Task<List<ProductSearchResult>> SearchInStoreAsync(
        IWebViewContext webViewContext,
        string storeId,
        string query,
        int limit,
        CancellationToken ct)
    {
        var session = _sessionService.CurrentSession;
        if (session == null)
            throw new InvalidOperationException("No active shopping session");

        IStoreParser parser = storeId switch
        {
            "kuper" => CreateKuperParser(),
            "lavka" => new AiWebSniffer.Parsers.LavkaParser(),
            "samokat" => new AiWebSniffer.Parsers.SamokatParser(),
            _ => throw new InvalidOperationException($"Unknown store: {storeId}")
        };

        return await parser.SearchAsync(webViewContext, query, limit, ct);
    }

    private IStoreParser CreateKuperParser()
    {
        var configs = _sessionService.GetStoreConfigs();
        if (configs.TryGetValue("kuper", out var config))
        {
            var parser = new AiWebSniffer.Parsers.KuperParser();
            parser.Initialize(config.BaseUrl);
            return parser;
        }
        throw new InvalidOperationException("Kuper store not configured");
    }

    /// <summary>
    /// Сформировать PlannedBasket из результатов поиска и AI выбора
    /// </summary>
    private PlannedBasket BuildPlannedBasket(
        StoreRuntimeConfig config,
        IReadOnlyList<DraftItem> items,
        Dictionary<Guid, (ProductSearchResult? Selected, int Quantity, string? Reasoning, List<ProductSearchResult> Alternatives)> aiSelections)
    {
        var plannedItems = new List<PlannedItem>();
        decimal total = 0;

        foreach (var item in items)
        {
            PlannedItem plannedItem;

            if (aiSelections.TryGetValue(item.Id, out var selection) && selection.Selected != null)
            {
                var selectedProduct = selection.Selected;
                var lineTotal = selectedProduct.Price * selection.Quantity;
                total += lineTotal;

                plannedItem = new PlannedItem
                {
                    DraftItemId = item.Id,
                    DraftItemName = item.Name,
                    Match = new ProductMatch
                    {
                        ProductId = selectedProduct.Id,
                        ProductName = selectedProduct.Name,
                        Price = selectedProduct.Price,
                        PackageSize = selectedProduct.Quantity,
                        PackageUnit = selectedProduct.Unit,
                        InStock = selectedProduct.InStock,
                        ImageUrl = selectedProduct.ImageUrl,
                        ProductUrl = selectedProduct.ProductUrl,
                        IsSelected = true
                    },
                    Quantity = selection.Quantity,
                    LineTotal = lineTotal,
                    Reasoning = selection.Reasoning
                };
            }
            else
            {
                plannedItem = new PlannedItem
                {
                    DraftItemId = item.Id,
                    DraftItemName = item.Name,
                    Match = null,
                    Quantity = 0,
                    LineTotal = 0
                };
            }

            plannedItems.Add(plannedItem);
        }

        return new PlannedBasket
        {
            Store = config.StoreId,
            StoreName = config.StoreName,
            Items = plannedItems,
            TotalPrice = total,
            ItemsFound = plannedItems.Count(i => i.Match != null),
            ItemsTotal = items.Count,
            DeliveryTime = config.DeliveryTime,
            DeliveryPrice = "Бесплатно"
        };
    }
}
