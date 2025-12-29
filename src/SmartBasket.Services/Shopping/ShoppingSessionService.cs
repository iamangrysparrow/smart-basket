using AiWebSniffer.Core.Interfaces;
using AiWebSniffer.Core.Models;
using AiWebSniffer.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartBasket.Core.Shopping;
using SmartBasket.Services.Chat;

namespace SmartBasket.Services.Shopping;

/// <summary>
/// Сервис управления сессией закупок.
/// Singleton — одна активная сессия на приложение.
/// </summary>
public class ShoppingSessionService : IShoppingSessionService
{
    private readonly ILogger<ShoppingSessionService> _logger;
    private readonly ShoppingSettings _settings;
    private readonly IProductSelectorService _productSelector;
    private readonly Dictionary<string, IStoreParser> _parsers = new();
    private readonly Dictionary<string, StoreRuntimeConfig> _storeConfigs = new();
    private ShoppingSession? _currentSession;

    public ShoppingSession? CurrentSession => _currentSession;

    public event EventHandler<ShoppingSession>? SessionChanged;
    public event EventHandler<DraftItem>? ItemAdded;
    public event EventHandler<DraftItem>? ItemRemoved;
    public event EventHandler<DraftItem>? ItemUpdated;

    public ShoppingSessionService(
        IOptions<ShoppingSettings> settings,
        IProductSelectorService productSelector,
        ILogger<ShoppingSessionService> logger)
    {
        _logger = logger;
        _settings = settings.Value;
        _productSelector = productSelector;

        InitializeParsers();
    }

    /// <summary>
    /// Инициализация парсеров на основе настроек
    /// </summary>
    private void InitializeParsers()
    {
        _logger.LogInformation("[ShoppingSession] Initializing parsers from settings");

        // Kuper
        if (_settings.Stores.TryGetValue("kuper", out var kuperSettings) && kuperSettings.Enabled)
        {
            var slug = kuperSettings.StoreSlug ?? "auchan";
            var kuper = new KuperParser();
            kuper.Initialize($"https://kuper.ru/{slug}");
            _parsers["kuper"] = kuper;

            _storeConfigs["kuper"] = new StoreRuntimeConfig
            {
                StoreId = "kuper",
                StoreName = $"Kuper ({GetKuperStoreName(slug)})",
                BaseUrl = $"https://kuper.ru/{slug}",
                SearchLimit = kuperSettings.SearchLimit,
                Color = "#FF6B00",
                DeliveryTime = "1-2 часа",
                Priority = kuperSettings.Priority
            };

            _logger.LogInformation("[ShoppingSession] Kuper parser initialized: {Slug}", slug);
        }

        // Lavka
        if (_settings.Stores.TryGetValue("lavka", out var lavkaSettings) && lavkaSettings.Enabled)
        {
            var lavka = new LavkaParser();
            _parsers["lavka"] = lavka;

            _storeConfigs["lavka"] = new StoreRuntimeConfig
            {
                StoreId = "lavka",
                StoreName = "Яндекс.Лавка",
                BaseUrl = "https://lavka.yandex.ru",
                SearchLimit = lavkaSettings.SearchLimit,
                Color = "#FFCC00",
                DeliveryTime = "15-30 мин",
                Priority = lavkaSettings.Priority
            };

            _logger.LogInformation("[ShoppingSession] Lavka parser initialized");
        }

        // Samokat
        if (_settings.Stores.TryGetValue("samokat", out var samokatSettings) && samokatSettings.Enabled)
        {
            var samokat = new SamokatParser();
            _parsers["samokat"] = samokat;

            _storeConfigs["samokat"] = new StoreRuntimeConfig
            {
                StoreId = "samokat",
                StoreName = "Самокат",
                BaseUrl = "https://samokat.ru",
                SearchLimit = samokatSettings.SearchLimit,
                Color = "#FF3366",
                DeliveryTime = "15-30 мин",
                Priority = samokatSettings.Priority
            };

            _logger.LogInformation("[ShoppingSession] Samokat parser initialized");
        }

        _logger.LogInformation("[ShoppingSession] Initialized {Count} parsers", _parsers.Count);
    }

    private static string GetKuperStoreName(string slug) => slug.ToLower() switch
    {
        "auchan" => "АШАН",
        "magnit_express" => "Магнит",
        "5ka" => "Пятёрочка",
        "lenta" => "Лента",
        "vkusvill" => "ВкусВилл",
        "metro" => "METRO",
        _ => slug
    };

    /// <summary>
    /// Получить список инициализированных магазинов
    /// </summary>
    public IReadOnlyDictionary<string, StoreRuntimeConfig> GetStoreConfigs() => _storeConfigs;

    /// <summary>
    /// Проверить статус авторизации во всех магазинах
    /// </summary>
    public async Task<Dictionary<string, bool>> CheckStoreAuthStatusAsync(
        IWebViewContext webViewContext,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[ShoppingSession] Checking auth status for {Count} stores", _parsers.Count);

        var result = new Dictionary<string, bool>();

        foreach (var (storeId, parser) in _parsers)
        {
            try
            {
                _logger.LogDebug("[ShoppingSession] Checking auth for {Store}", storeId);
                var isAuthenticated = await parser.CheckAuthAsync(webViewContext, ct);
                result[storeId] = isAuthenticated;
                _logger.LogInformation("[ShoppingSession] {Store} auth status: {Status}",
                    storeId, isAuthenticated ? "Авторизован" : "Требуется вход");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ShoppingSession] Failed to check auth for {Store}", storeId);
                result[storeId] = false;
            }
        }

        return result;
    }

    #region Этап 1: Формирование черновика

    public Task<ShoppingSession> StartNewSessionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[ShoppingSession] Starting new session");

        _currentSession = new ShoppingSession();

        _logger.LogInformation("[ShoppingSession] Session {SessionId} created", _currentSession.Id);

        SessionChanged?.Invoke(this, _currentSession);

        return Task.FromResult(_currentSession);
    }

    public void AddItem(string name, decimal quantity, string unit, string? category = null, string? categoryPath = null)
    {
        if (_currentSession == null)
        {
            _logger.LogWarning("[ShoppingSession] AddItem called without active session");
            throw new InvalidOperationException("No active session. Call StartNewSessionAsync first.");
        }

        // Проверяем, нет ли уже такого товара (по частичному совпадению)
        var existing = FindItem(name);
        if (existing != null)
        {
            _logger.LogDebug("[ShoppingSession] Item '{Name}' already exists, updating quantity", name);
            existing.Quantity += quantity;
            ItemUpdated?.Invoke(this, existing);
            return;
        }

        var item = new DraftItem
        {
            Name = name,
            Quantity = quantity,
            Unit = unit,
            Category = category,
            CategoryPath = categoryPath,
            Source = DraftItemSource.Manual
        };

        _currentSession.DraftItems.Add(item);

        _logger.LogInformation("[ShoppingSession] Added item: {Name} {Qty} {Unit} [{Category}] (path: {CategoryPath})",
            name, quantity, unit, category ?? "без категории", categoryPath ?? "-");

        ItemAdded?.Invoke(this, item);
    }

    public bool RemoveItem(string name)
    {
        if (_currentSession == null)
        {
            _logger.LogWarning("[ShoppingSession] RemoveItem called without active session");
            return false;
        }

        var item = FindItem(name);
        if (item == null)
        {
            _logger.LogDebug("[ShoppingSession] Item '{Name}' not found for removal", name);
            return false;
        }

        _currentSession.DraftItems.Remove(item);

        _logger.LogInformation("[ShoppingSession] Removed item: {Name}", item.Name);

        ItemRemoved?.Invoke(this, item);
        return true;
    }

    public bool UpdateItem(string name, decimal quantity, string? unit = null)
    {
        if (_currentSession == null)
        {
            _logger.LogWarning("[ShoppingSession] UpdateItem called without active session");
            return false;
        }

        var item = FindItem(name);
        if (item == null)
        {
            _logger.LogDebug("[ShoppingSession] Item '{Name}' not found for update", name);
            return false;
        }

        var oldQty = item.Quantity;
        var oldUnit = item.Unit;

        item.Quantity = quantity;
        if (unit != null)
        {
            item.Unit = unit;
        }

        _logger.LogInformation("[ShoppingSession] Updated item: {Name} {OldQty} {OldUnit} → {NewQty} {NewUnit}",
            item.Name, oldQty, oldUnit, item.Quantity, item.Unit);

        ItemUpdated?.Invoke(this, item);
        return true;
    }

    public List<DraftItem> GetCurrentItems()
    {
        return _currentSession?.DraftItems.ToList() ?? new List<DraftItem>();
    }

    /// <summary>
    /// Найти товар по имени (частичное совпадение, case-insensitive)
    /// </summary>
    private DraftItem? FindItem(string name)
    {
        if (_currentSession == null) return null;

        // Сначала ищем точное совпадение
        var exact = _currentSession.DraftItems
            .FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Затем частичное совпадение
        return _currentSession.DraftItems
            .FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                                  name.Contains(i.Name, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Этап 2: Планирование (поиск в магазинах)

    public async Task StartPlanningAsync(
        IWebViewContext webViewContext,
        IProgress<PlanningProgress>? progress = null,
        IProgress<ChatProgress>? aiProgress = null,
        CancellationToken ct = default)
    {
        if (_currentSession == null)
            throw new InvalidOperationException("No active session");

        _logger.LogInformation("[ShoppingSession] Starting planning phase with {ItemCount} items in {StoreCount} stores",
            _currentSession.DraftItems.Count, _parsers.Count);

        _currentSession.State = ShoppingSessionState.Planning;
        SessionChanged?.Invoke(this, _currentSession);

        var items = _currentSession.DraftItems;
        var stores = _storeConfigs.Values
            .OrderBy(c => c.Priority)
            .Select(c => c.StoreId)
            .ToList();

        // Словарь для хранения количества, рассчитанного AI для каждого товара
        var aiSelections = new Dictionary<Guid, (string? ProductId, int Quantity, string? Reasoning)>();

        var storeIndex = 0;
        foreach (var storeId in stores)
        {
            storeIndex++;
            ct.ThrowIfCancellationRequested();

            var config = _storeConfigs[storeId];
            var parser = _parsers[storeId];

            _logger.LogInformation("[ShoppingSession] Starting search in {Store} ({StoreName})",
                storeId, config.StoreName);

            var searchResult = new StoreSearchResult
            {
                Store = storeId,
                StoreName = config.StoreName,
                TotalCount = items.Count
            };

            var itemIndex = 0;
            foreach (var item in items)
            {
                itemIndex++;
                ct.ThrowIfCancellationRequested();

                progress?.Report(new PlanningProgress
                {
                    Store = storeId,
                    StoreName = config.StoreName,
                    ItemName = item.Name,
                    CurrentItem = itemIndex,
                    TotalItems = items.Count,
                    CurrentStore = storeIndex,
                    TotalStores = stores.Count,
                    Status = PlanningStatus.Searching
                });

                try
                {
                    _logger.LogDebug("[ShoppingSession] Searching '{Item}' in {Store}", item.Name, storeId);

                    var results = await parser.SearchAsync(
                        webViewContext,
                        item.Name,
                        config.SearchLimit,
                        ct);

                    // Преобразуем результаты в ProductMatch
                    var matches = results.Select((r, i) => new ProductMatch
                    {
                        ProductId = r.Id,
                        ProductName = r.Name,
                        Price = r.Price,
                        PackageSize = r.Quantity,
                        PackageUnit = r.Unit,
                        InStock = r.InStock,
                        ImageUrl = r.ImageUrl,
                        ProductUrl = r.ProductUrl,
                        MatchScore = 1.0f - (i * 0.1f),
                        IsSelected = false // Сначала все невыбранные
                    }).ToList();

                    // AI выбор лучшего товара
                    string? selectedProductId = null;
                    int selectedQuantity = (int)item.Quantity;
                    string? reasoning = null;

                    if (matches.Any())
                    {
                        // Уведомляем о начале AI выбора
                        progress?.Report(new PlanningProgress
                        {
                            Store = storeId,
                            StoreName = config.StoreName,
                            ItemName = item.Name,
                            CurrentItem = itemIndex,
                            TotalItems = items.Count,
                            CurrentStore = storeIndex,
                            TotalStores = stores.Count,
                            Status = PlanningStatus.Selecting
                        });

                        _logger.LogDebug("[ShoppingSession] AI selecting best product for '{Item}' from {Count} results",
                            item.Name, matches.Count);

                        // Вызываем AI для выбора лучшего товара
                        var selection = await _productSelector.SelectBestProductAsync(
                            item,
                            results,
                            storeId,
                            config.StoreName,
                            _currentSession.LlmSessionId,
                            aiProgress,
                            ct);

                        if (selection != null)
                        {
                            selectedProductId = selection.SelectedProductId;
                            selectedQuantity = selection.Quantity;
                            reasoning = selection.Reasoning;

                            _logger.LogInformation(
                                "[ShoppingSession] AI selected: ProductId={ProductId}, Qty={Qty} for '{Item}'",
                                selectedProductId ?? "null", selectedQuantity, item.Name);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[ShoppingSession] AI returned null for '{Item}', falling back to first result",
                                item.Name);
                            // Fallback: выбираем первый товар в наличии
                            selectedProductId = matches.FirstOrDefault(m => m.InStock)?.ProductId;
                        }

                        // Отмечаем выбранный товар
                        foreach (var match in matches)
                        {
                            match.IsSelected = match.ProductId == selectedProductId;
                        }

                        // Сохраняем выбор AI для использования в BuildPlannedBasket
                        aiSelections[item.Id] = (selectedProductId, selectedQuantity, reasoning);
                    }

                    searchResult.ItemMatches[item.Id] = matches;
                    if (matches.Any(m => m.InStock)) searchResult.FoundCount++;

                    // Находим выбранный товар для отчёта
                    var selectedMatch = matches.FirstOrDefault(m => m.IsSelected);

                    progress?.Report(new PlanningProgress
                    {
                        Store = storeId,
                        StoreName = config.StoreName,
                        ItemName = item.Name,
                        CurrentItem = itemIndex,
                        TotalItems = items.Count,
                        CurrentStore = storeIndex,
                        TotalStores = stores.Count,
                        Status = selectedMatch != null ? PlanningStatus.Found : PlanningStatus.NotFound,
                        MatchedProduct = selectedMatch?.ProductName,
                        Price = selectedMatch?.Price,
                        Reasoning = reasoning,
                        SelectedQuantity = selectedQuantity
                    });

                    _logger.LogDebug("[ShoppingSession] Found {Count} matches for '{Item}' in {Store}",
                        matches.Count, item.Name, storeId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ShoppingSession] Failed to search '{Item}' in {Store}",
                        item.Name, storeId);

                    searchResult.ItemMatches[item.Id] = new List<ProductMatch>();

                    progress?.Report(new PlanningProgress
                    {
                        Store = storeId,
                        StoreName = config.StoreName,
                        ItemName = item.Name,
                        CurrentItem = itemIndex,
                        TotalItems = items.Count,
                        CurrentStore = storeIndex,
                        TotalStores = stores.Count,
                        Status = PlanningStatus.Error,
                        ErrorMessage = ex.Message
                    });
                }
            }

            searchResult.IsComplete = true;
            _currentSession.StoreResults[storeId] = searchResult;

            // Формируем PlannedBasket с учётом AI выбора
            var basket = BuildPlannedBasket(storeId, config, searchResult, aiSelections);
            _currentSession.PlannedBaskets[storeId] = basket;

            _logger.LogInformation("[ShoppingSession] Completed {Store}: {Found}/{Total} items, total {Price:C}",
                storeId, searchResult.FoundCount, searchResult.TotalCount, basket.TotalPrice);
        }

        _currentSession.State = ShoppingSessionState.Analyzing;
        SessionChanged?.Invoke(this, _currentSession);

        _logger.LogInformation("[ShoppingSession] Planning completed for all stores");
    }

    public Task StartPlanningAsync(IProgress<PlanningProgress>? progress = null, CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "Use StartPlanningAsync(IWebViewContext, IProgress, CancellationToken) instead. " +
            "WebViewContext is required for parser operations.");
    }

    private PlannedBasket BuildPlannedBasket(
        string storeId,
        StoreRuntimeConfig config,
        StoreSearchResult searchResult,
        Dictionary<Guid, (string? ProductId, int Quantity, string? Reasoning)>? aiSelections = null)
    {
        var items = _currentSession!.DraftItems;
        var plannedItems = new List<PlannedItem>();
        decimal total = 0;

        foreach (var item in items)
        {
            var matches = searchResult.ItemMatches.GetValueOrDefault(item.Id) ?? new();
            var selected = matches.FirstOrDefault(m => m.IsSelected && m.InStock);

            // Используем количество из AI выбора, если есть
            int quantity = (int)item.Quantity;
            string? reasoning = null;
            if (aiSelections != null && aiSelections.TryGetValue(item.Id, out var aiSelection))
            {
                quantity = aiSelection.Quantity;
                reasoning = aiSelection.Reasoning;
            }

            var lineTotal = selected != null ? selected.Price * quantity : 0;
            total += lineTotal;

            plannedItems.Add(new PlannedItem
            {
                DraftItemId = item.Id,
                DraftItemName = item.Name,
                Match = selected,
                Quantity = quantity,
                LineTotal = lineTotal,
                Reasoning = reasoning
            });
        }

        return new PlannedBasket
        {
            Store = storeId,
            StoreName = config.StoreName,
            Items = plannedItems,
            TotalPrice = total,
            ItemsFound = plannedItems.Count(i => i.Match != null),
            ItemsTotal = plannedItems.Count,
            DeliveryTime = config.DeliveryTime,
            DeliveryPrice = total >= 1000 ? "Бесплатно" : "99 ₽"
        };
    }

    #endregion

    #region Этап 3: Анализ

    public PlannedBasket? GetBasket(string store)
    {
        return _currentSession?.PlannedBaskets.GetValueOrDefault(store);
    }

    public Dictionary<string, PlannedBasket> GetAllBaskets()
    {
        return _currentSession?.PlannedBaskets ?? new Dictionary<string, PlannedBasket>();
    }

    #endregion

    #region Этап 4: Оформление

    public async Task<string?> CreateCartAsync(
        IWebViewContext webViewContext,
        string store,
        CancellationToken ct = default)
    {
        if (_currentSession == null)
            throw new InvalidOperationException("No active session");

        var basket = _currentSession.PlannedBaskets.GetValueOrDefault(store);
        if (basket == null)
            throw new ArgumentException($"No basket for store {store}");

        if (!_parsers.TryGetValue(store, out var parser))
            throw new ArgumentException($"No parser for store {store}");

        _logger.LogInformation("[ShoppingSession] Creating cart in {Store} with {Count} items",
            store, basket.Items.Count);

        _currentSession.State = ShoppingSessionState.Finalizing;
        SessionChanged?.Invoke(this, _currentSession);

        try
        {
            // Очистить корзину
            _logger.LogDebug("[ShoppingSession] Clearing cart in {Store}", store);
            await parser.ClearCartAsync(webViewContext, ct);

            // Добавить товары
            foreach (var item in basket.Items.Where(i => i.Match != null))
            {
                _logger.LogDebug("[ShoppingSession] Adding {Product} x{Qty} to cart",
                    item.Match!.ProductName, item.Quantity);

                await parser.AddToCartAsync(
                    webViewContext,
                    item.Match.ProductId,
                    item.Quantity,
                    ct);
            }

            // Получить URL
            var url = await parser.GetCartUrlAsync(webViewContext, ct);

            _currentSession.CheckoutUrl = url;
            _currentSession.SelectedStore = store;
            _currentSession.State = ShoppingSessionState.Completed;

            SessionChanged?.Invoke(this, _currentSession);

            _logger.LogInformation("[ShoppingSession] Cart created in {Store}: {Url}", store, url);

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingSession] Failed to create cart in {Store}", store);
            _currentSession.State = ShoppingSessionState.Analyzing;
            SessionChanged?.Invoke(this, _currentSession);
            throw;
        }
    }

    public Task<string?> CreateCartAsync(string store, CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "Use CreateCartAsync(IWebViewContext, string, CancellationToken) instead. " +
            "WebViewContext is required for parser operations.");
    }

    #endregion
}
