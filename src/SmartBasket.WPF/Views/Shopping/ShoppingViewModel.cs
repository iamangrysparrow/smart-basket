using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using AiWebSniffer.Core.Interfaces;
using AiWebSniffer.WebView2;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using SmartBasket.Core.Shopping;
using SmartBasket.Data;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Shopping;

namespace SmartBasket.WPF.Views.Shopping;

/// <summary>
/// ViewModel для модуля закупок (этап 1: формирование черновика)
/// </summary>
public partial class ShoppingViewModel : ObservableObject
{
    // =====================================================================
    // ТЕСТОВЫЙ РЕЖИМ: установить в true для пропуска авторизации и заполнения тестовой корзины
    // =====================================================================
    private const bool TEST_MODE = true;
    // =====================================================================

    private readonly IShoppingSessionService _sessionService;
    private readonly IShoppingChatService _shoppingChatService;
    private readonly IDbContextFactory<SmartBasketDbContext> _dbContextFactory;
    private readonly ILogger<ShoppingViewModel> _logger;
    private readonly object _itemsLock = new();
    private readonly object _messagesLock = new();

    private WebView2? _webView;
    private IWebViewContext? _webViewContext;
    private CancellationTokenSource? _planningCts;
    private CancellationTokenSource? _chatCts;

    public ShoppingViewModel(
        IShoppingSessionService sessionService,
        IShoppingChatService shoppingChatService,
        IDbContextFactory<SmartBasketDbContext> dbContextFactory,
        ILogger<ShoppingViewModel> logger)
    {
        _sessionService = sessionService;
        _shoppingChatService = shoppingChatService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        // Подписка на события сервиса
        _sessionService.ItemAdded += OnItemAdded;
        _sessionService.ItemRemoved += OnItemRemoved;
        _sessionService.ItemUpdated += OnItemUpdated;
        _sessionService.SessionChanged += OnSessionChanged;

        _logger.LogDebug("[ShoppingViewModel] Created");
    }

    private readonly object _progressLock = new();
    private readonly object _logLock = new();

    private readonly object _receiptsLock = new();
    private readonly object _storeAuthLock = new();

    /// <summary>
    /// Включить синхронизацию коллекций для thread-safe доступа
    /// </summary>
    public void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(DraftItems, _itemsLock);
        BindingOperations.EnableCollectionSynchronization(Messages, _messagesLock);
        BindingOperations.EnableCollectionSynchronization(GroupedItems, _itemsLock);
        BindingOperations.EnableCollectionSynchronization(StoreProgress, _progressLock);
        BindingOperations.EnableCollectionSynchronization(PlanningLog, _logLock);
        BindingOperations.EnableCollectionSynchronization(RecentReceipts, _receiptsLock);
        BindingOperations.EnableCollectionSynchronization(StoreAuthStatuses, _storeAuthLock);
    }

    /// <summary>
    /// Установить WebView2 для использования парсерами
    /// </summary>
    public void SetWebView(WebView2 webView)
    {
        _webView = webView;
        _webViewContext = new WebViewContext(webView);
        WebViewReady = true;
        _logger.LogInformation("[ShoppingViewModel] WebView2 context set");
    }

    /// <summary>
    /// WebView2 готов к использованию
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPlanningCommand))]
    private bool _webViewReady;

    #region Observable Properties

    /// <summary>
    /// Текущее состояние сессии
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraftingState))]
    [NotifyPropertyChangedFor(nameof(ShowDrafting))]
    [NotifyPropertyChangedFor(nameof(ShowBasketPanel))]
    [NotifyPropertyChangedFor(nameof(ShowProgressPanel))]
    [NotifyPropertyChangedFor(nameof(ShowPlanning))]
    [NotifyPropertyChangedFor(nameof(ShowAnalyzing))]
    [NotifyPropertyChangedFor(nameof(ShowFinalizing))]
    [NotifyPropertyChangedFor(nameof(ShowCompleted))]
    [NotifyPropertyChangedFor(nameof(StateDisplayText))]
    [NotifyCanExecuteChangedFor(nameof(StartPlanningCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCartCommand))]
    private ShoppingSessionState _state = ShoppingSessionState.Drafting;

    /// <summary>
    /// Есть ли активная сессия
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    [NotifyPropertyChangedFor(nameof(ShowDrafting))]
    [NotifyPropertyChangedFor(nameof(ShowBasketPanel))]
    [NotifyPropertyChangedFor(nameof(ShowProgressPanel))]
    private bool _hasSession;

    /// <summary>
    /// Товары в черновике
    /// </summary>
    public ObservableCollection<DraftItem> DraftItems { get; } = new();

    /// <summary>
    /// Товары сгруппированные по категориям
    /// </summary>
    public ObservableCollection<DraftItemGroup> GroupedItems { get; } = new();

    /// <summary>
    /// История сообщений чата
    /// </summary>
    public ObservableCollection<ShoppingChatMessage> Messages { get; } = new();

    /// <summary>
    /// Текст ввода пользователя
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _userInput = string.Empty;

    /// <summary>
    /// Идёт обработка запроса
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartPlanningCommand))]
    private bool _isProcessing;

    /// <summary>
    /// Общее количество товаров в корзине
    /// </summary>
    [ObservableProperty]
    private int _totalItemsCount;

    /// <summary>
    /// Общее количество единиц товаров
    /// </summary>
    [ObservableProperty]
    private string _totalQuantityText = "0 позиций";

    // Planning state properties

    /// <summary>
    /// Прогресс по магазинам
    /// </summary>
    public ObservableCollection<StoreProgressItem> StoreProgress { get; } = new();

    /// <summary>
    /// Лог операций планирования
    /// </summary>
    public ObservableCollection<string> PlanningLog { get; } = new();

    /// <summary>
    /// Текст текущей операции
    /// </summary>
    [ObservableProperty]
    private string _currentOperationText = "Подготовка...";

    /// <summary>
    /// Общий прогресс (0-100)
    /// </summary>
    [ObservableProperty]
    private int _overallProgress;

    // Analyzing state properties

    /// <summary>
    /// Корзины для сравнения
    /// </summary>
    public ObservableCollection<BasketCardViewModel> Baskets { get; } = new();

    /// <summary>
    /// Выбранная корзина для оформления
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCartCommand))]
    private BasketCardViewModel? _selectedBasket;

    /// <summary>
    /// URL корзины после оформления
    /// </summary>
    [ObservableProperty]
    private string? _checkoutUrl;

    /// <summary>
    /// Название выбранного магазина
    /// </summary>
    [ObservableProperty]
    private string? _selectedStoreName;

    /// <summary>
    /// Итоговая сумма заказа
    /// </summary>
    [ObservableProperty]
    private decimal _orderTotal;

    // Welcome screen statistics

    /// <summary>
    /// Расходы за месяц
    /// </summary>
    [ObservableProperty]
    private decimal _monthlyExpenses;

    /// <summary>
    /// Процент изменения расходов по сравнению с прошлым месяцем
    /// </summary>
    [ObservableProperty]
    private int _monthlyExpensesChange;

    /// <summary>
    /// Расходы за прошлый месяц (для сравнения)
    /// </summary>
    [ObservableProperty]
    private decimal _lastMonthExpenses;

    /// <summary>
    /// Средний чек
    /// </summary>
    [ObservableProperty]
    private decimal _averageReceipt;

    /// <summary>
    /// Процент изменения среднего чека
    /// </summary>
    [ObservableProperty]
    private int _averageReceiptChange;

    /// <summary>
    /// Количество покупок за месяц
    /// </summary>
    [ObservableProperty]
    private int _purchasesCount;

    /// <summary>
    /// Последние чеки для отображения
    /// </summary>
    public ObservableCollection<RecentReceiptItem> RecentReceipts { get; } = new();

    /// <summary>
    /// Статусы авторизации в магазинах
    /// </summary>
    public ObservableCollection<StoreAuthStatus> StoreAuthStatuses { get; } = new();

    /// <summary>
    /// Есть ли хотя бы один авторизованный магазин
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartSession))]
    [NotifyCanExecuteChangedFor(nameof(StartSessionCommand))]
    private bool _hasAnyAuthenticatedStore;

    /// <summary>
    /// Идёт проверка авторизации
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartSession))]
    [NotifyCanExecuteChangedFor(nameof(StartSessionCommand))]
    private bool _isCheckingAuth;

    /// <summary>
    /// Идёт инициализация (загрузка статистики + проверка авторизации)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    private bool _isInitializing = true;

    /// <summary>
    /// Текст текущего этапа инициализации
    /// </summary>
    [ObservableProperty]
    private string _initializingText = "Инициализация...";

    /// <summary>
    /// Показывать оверлей авторизации (полноэкранный WebView)
    /// </summary>
    [ObservableProperty]
    private bool _showAuthOverlay;

    /// <summary>
    /// Магазин, в котором идёт авторизация
    /// </summary>
    [ObservableProperty]
    private StoreAuthStatus? _authenticatingStore;

    /// <summary>
    /// URL магазина для авторизации
    /// </summary>
    [ObservableProperty]
    private string? _authStoreUrl;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Показывать экран приветствия (нет сессии и инициализация завершена)
    /// </summary>
    public bool ShowWelcome => !HasSession && !IsInitializing;

    /// <summary>
    /// Показывать экран формирования списка (включая Planning — для TabControl)
    /// </summary>
    public bool ShowDrafting => HasSession &&
        (State == ShoppingSessionState.Drafting || State == ShoppingSessionState.Planning);

    /// <summary>
    /// Показывать панель корзины справа (только Drafting)
    /// </summary>
    public bool ShowBasketPanel => HasSession && State == ShoppingSessionState.Drafting;

    /// <summary>
    /// Показывать панель прогресса справа (только Planning)
    /// </summary>
    public bool ShowProgressPanel => HasSession && State == ShoppingSessionState.Planning;

    /// <summary>
    /// Показывать экран планирования (поиск в магазинах) — УСТАРЕЛО, используется ShowProgressPanel
    /// </summary>
    [Obsolete("Use ShowProgressPanel instead")]
    public bool ShowPlanning => HasSession && State == ShoppingSessionState.Planning;

    /// <summary>
    /// Показывать экран анализа (сравнение корзин)
    /// </summary>
    public bool ShowAnalyzing => HasSession && State == ShoppingSessionState.Analyzing;

    /// <summary>
    /// Показывать экран оформления заказа
    /// </summary>
    public bool ShowFinalizing => HasSession && State == ShoppingSessionState.Finalizing;

    /// <summary>
    /// Показывать экран завершения
    /// </summary>
    public bool ShowCompleted => HasSession && State == ShoppingSessionState.Completed;

    /// <summary>
    /// Этап формирования списка
    /// </summary>
    public bool IsDraftingState => State == ShoppingSessionState.Drafting;

    /// <summary>
    /// Можно ли отправить сообщение
    /// </summary>
    public bool CanSend => !IsProcessing && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>
    /// Можно ли начать сессию (есть хотя бы один авторизованный магазин)
    /// </summary>
    public bool CanStartSession => HasAnyAuthenticatedStore && !IsCheckingAuth;

    /// <summary>
    /// Можно ли начать планирование
    /// </summary>
    public bool CanStartPlanning => DraftItems.Count > 0 && State == ShoppingSessionState.Drafting && !IsProcessing && WebViewReady;

    /// <summary>
    /// Текст текущего состояния
    /// </summary>
    public string StateDisplayText => State switch
    {
        ShoppingSessionState.Drafting => "Формирование списка",
        ShoppingSessionState.Planning => "Поиск в магазинах",
        ShoppingSessionState.Analyzing => "Анализ результатов",
        ShoppingSessionState.Finalizing => "Оформление заказа",
        ShoppingSessionState.Completed => "Заказ оформлен",
        _ => "Неизвестно"
    };

    #endregion

    #region Commands

    /// <summary>
    /// Инициализировать экран приветствия (загрузить статистику, статусы магазинов)
    /// </summary>
    [RelayCommand]
    private async Task InitializeWelcomeScreenAsync()
    {
        if (_webViewContext == null)
        {
            _logger.LogWarning("[ShoppingViewModel] WebView not ready, cannot initialize welcome screen");
            return;
        }

        _logger.LogInformation("[ShoppingViewModel] Initializing welcome screen (TEST_MODE={TestMode})", TEST_MODE);

        // В тестовом режиме сразу запускаем сессию с тестовыми данными
        if (TEST_MODE)
        {
            await InitializeTestModeAsync();
            return;
        }

        IsInitializing = true;
        InitializingText = "Инициализация...";

        try
        {
            // Инициализируем магазины (из конфигурации)
            InitializeStoreStatuses();

            // Загружаем статистику из БД
            InitializingText = "Загрузка статистики...";
            await LoadStatisticsAsync();

            // Проверяем авторизацию во всех магазинах (с прогрессом)
            await CheckStoreAuthWithProgressAsync();
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Инициализация тестового режима — сразу создаёт сессию с заполненной корзиной
    /// </summary>
    private async Task InitializeTestModeAsync()
    {
        _logger.LogInformation("[ShoppingViewModel] TEST MODE: Initializing with test basket");

        IsInitializing = true;
        InitializingText = "Тестовый режим: загрузка...";

        try
        {
            // Инициализируем магазины (они нужны для поиска)
            InitializeStoreStatuses();

            // В тестовом режиме считаем все магазины авторизованными
            foreach (var store in StoreAuthStatuses)
            {
                store.IsChecking = false;
                store.IsAuthenticated = true;
            }
            HasAnyAuthenticatedStore = true;

            // Создаём сессию
            var session = await Task.Run(() => _sessionService.StartNewSessionAsync());
            HasSession = true;
            State = session.State;

            // Начинаем conversation в ShoppingChatService
            try
            {
                await Task.Run(() => _shoppingChatService.StartConversationAsync(session));
                _logger.LogInformation("[ShoppingViewModel] TEST MODE: Chat conversation started");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ShoppingViewModel] TEST MODE: Failed to start chat conversation");
            }

            // Заполняем корзину тестовыми данными
            AddTestBasketItems();

            // Добавляем системное сообщение
            AddSystemMessage("🧪 ТЕСТОВЫЙ РЕЖИМ\n\nКорзина заполнена 28 товарами.\nМагазины считаются авторизованными.\n\nНажмите «Собрать корзины» для теста поиска.");

            _logger.LogInformation("[ShoppingViewModel] TEST MODE: Ready with {Count} items", DraftItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] TEST MODE: Initialization failed");
            AddSystemMessage($"Ошибка инициализации тестового режима: {ex.Message}");
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Добавляет тестовые товары в корзину (28 позиций)
    /// </summary>
    private void AddTestBasketItems()
    {
        var testItems = new[]
        {
            ("Шампиньоны", 1m, "кг", "Свежие овощи"),
            ("Молоко 1,5%", 1m, "л", "Молоко"),
            ("Томаты", 0.5m, "кг", "Свежие овощи"),
            ("Лук репчатый", 0.5m, "кг", "Свежие овощи"),
            ("Спагетти", 1m, "уп", "Макароны"),
            ("Борщ с курицей", 2m, "уп", "Готовые продукты"),
            ("Колбаса варёная докторская", 0.3m, "кг", "Мясо и птица"),
            ("Сок апельсиновый", 1m, "л", "Соки"),
            ("Вода питьевая газированная", 1.5m, "л", "Вода и газированные напитки"),
            ("Огурцы", 0.5m, "кг", "Свежие овощи"),
            ("Морковь", 0.5m, "кг", "Свежие овощи"),
            ("Сыр полутвёрдый 45%", 0.2m, "кг", "Сыры"),
            ("Яблоки", 1m, "кг", "Свежие фрукты"),
            ("Бедра цыплёнка-бройлера на кости с кожей", 1m, "кг", "Куриное мясо"),
            ("Конфеты шоколадные с суфлейной начинкой", 0.3m, "кг", "Прочие продукты"),
            ("Голень куриная с кожей", 0.5m, "кг", "Куриное мясо"),
            ("Варенье малиновое", 0.2m, "л", "Прочие продукты"),
            ("Сок томатный", 1m, "л", "Соки"),
            ("Картофель", 1m, "кг", "Свежие овощи"),
            ("Сметана 15%", 0.2m, "л", "Кисломолочные продукты"),
            ("Яйцо куриное", 10m, "шт", "Яйца"),
            ("Сахар белый песок", 0.5m, "кг", "Прочие продукты"),
            ("Кофе молотый", 0.2m, "кг", "Кофе и чай"),
            ("Батон пшеничный в нарезке", 1m, "уп", "Хлебобулочные изделия"),
            ("Подсолнечное масло рафинированное дезодорированное", 0.5m, "л", "Масла"),
            ("Туалетная бумага", 2m, "уп", "Салфетки и бумага"),
            ("Свёкла", 0.3m, "кг", "Свежие овощи"),
            ("Горошек консервированный", 2m, "уп", "Консервированные овощи")
        };

        foreach (var (name, qty, unit, category) in testItems)
        {
            _sessionService.AddItem(name, qty, unit, category);
        }

        _logger.LogInformation("[ShoppingViewModel] TEST MODE: Added {Count} test items", testItems.Length);
    }

    /// <summary>
    /// Проверка авторизации с отображением прогресса
    /// </summary>
    private async Task CheckStoreAuthWithProgressAsync()
    {
        if (_webViewContext == null) return;

        _logger.LogInformation("[ShoppingViewModel] Checking store auth status with progress");
        IsCheckingAuth = true;

        try
        {
            var stores = StoreAuthStatuses.ToList();

            // Отмечаем все магазины как "проверяется"
            foreach (var store in stores)
            {
                store.IsChecking = true;
            }

            InitializingText = "Проверка авторизации в магазинах...";

            // Проверяем авторизацию ОДИН раз для всех магазинов
            Dictionary<string, bool> authStatuses;
            try
            {
                authStatuses = await _sessionService.CheckStoreAuthStatusAsync(_webViewContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ShoppingViewModel] Failed to check auth status");
                // При ошибке отмечаем все как неавторизованные
                foreach (var store in stores)
                {
                    store.IsChecking = false;
                    store.IsAuthenticated = false;
                }
                HasAnyAuthenticatedStore = false;
                return;
            }

            // Обновляем статусы на основе результатов
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var store in stores)
                {
                    if (authStatuses.TryGetValue(store.StoreId, out var isAuth))
                    {
                        store.IsAuthenticated = isAuth;
                    }
                    store.IsChecking = false;
                }
            });

            HasAnyAuthenticatedStore = StoreAuthStatuses.Any(s => s.IsAuthenticated);
            _logger.LogInformation("[ShoppingViewModel] Auth check complete. Any authenticated: {HasAuth}",
                HasAnyAuthenticatedStore);
        }
        finally
        {
            IsCheckingAuth = false;
        }
    }

    /// <summary>
    /// Инициализировать статусы магазинов из конфигурации
    /// </summary>
    private void InitializeStoreStatuses()
    {
        var storeConfigs = _sessionService.GetStoreConfigs();

        Application.Current.Dispatcher.Invoke(() =>
        {
            StoreAuthStatuses.Clear();
            foreach (var config in storeConfigs.Values.OrderBy(c => c.Priority))
            {
                StoreAuthStatuses.Add(new StoreAuthStatus
                {
                    StoreId = config.StoreId,
                    StoreName = config.StoreName,
                    Color = config.Color ?? "#7C4DFF",
                    IsAuthenticated = false,
                    IsChecking = true
                });
            }
        });
    }

    /// <summary>
    /// Загрузить статистику из БД
    /// </summary>
    private async Task LoadStatisticsAsync()
    {
        _logger.LogDebug("[ShoppingViewModel] Loading statistics from DB");

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // Определяем периоды (UTC для PostgreSQL!)
            var now = DateTime.UtcNow;
            var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastMonthStart = currentMonthStart.AddMonths(-1);
            var lastMonthEnd = currentMonthStart.AddDays(-1);

            // Расходы за текущий месяц
            var currentMonthReceipts = await db.Receipts
                .Where(r => r.ReceiptDate >= currentMonthStart && r.Total.HasValue)
                .ToListAsync();

            var currentMonthTotal = currentMonthReceipts.Sum(r => r.Total ?? 0);
            var currentMonthCount = currentMonthReceipts.Count;

            // Расходы за прошлый месяц
            var lastMonthReceipts = await db.Receipts
                .Where(r => r.ReceiptDate >= lastMonthStart && r.ReceiptDate <= lastMonthEnd && r.Total.HasValue)
                .ToListAsync();

            var lastMonthTotal = lastMonthReceipts.Sum(r => r.Total ?? 0);
            var lastMonthCount = lastMonthReceipts.Count;

            // Вычисляем изменение расходов
            int expensesChange = 0;
            if (lastMonthTotal > 0)
            {
                expensesChange = (int)Math.Round((currentMonthTotal - lastMonthTotal) / lastMonthTotal * 100);
            }

            // Средний чек
            var currentAvg = currentMonthCount > 0 ? currentMonthTotal / currentMonthCount : 0;
            var lastAvg = lastMonthCount > 0 ? lastMonthTotal / lastMonthCount : 0;

            int avgChange = 0;
            if (lastAvg > 0)
            {
                avgChange = (int)Math.Round((currentAvg - lastAvg) / lastAvg * 100);
            }

            // Обновляем свойства
            MonthlyExpenses = currentMonthTotal;
            LastMonthExpenses = lastMonthTotal;
            MonthlyExpensesChange = expensesChange;
            AverageReceipt = currentAvg;
            AverageReceiptChange = avgChange;
            PurchasesCount = currentMonthCount;

            // Последние 5 чеков
            var recentReceipts = await db.Receipts
                .Where(r => r.Total.HasValue)
                .OrderByDescending(r => r.ReceiptDate)
                .Take(5)
                .Select(r => new { r.Shop, r.ReceiptDate, r.Total })
                .ToListAsync();

            // Цвета магазинов
            var shopColors = GetShopColors();

            Application.Current.Dispatcher.Invoke(() =>
            {
                RecentReceipts.Clear();
                foreach (var receipt in recentReceipts)
                {
                    var color = shopColors.TryGetValue(NormalizeShopName(receipt.Shop), out var c)
                        ? c
                        : "#7C4DFF"; // default accent color

                    RecentReceipts.Add(new RecentReceiptItem
                    {
                        Shop = receipt.Shop,
                        Date = receipt.ReceiptDate,
                        Total = receipt.Total ?? 0,
                        Color = color
                    });
                }
            });

            _logger.LogInformation(
                "[ShoppingViewModel] Statistics loaded: {MonthTotal:N0}₽ ({Count} receipts), avg {Avg:N0}₽",
                currentMonthTotal, currentMonthCount, currentAvg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] Failed to load statistics");
        }
    }

    /// <summary>
    /// Получить цвета магазинов из конфигурации
    /// </summary>
    private Dictionary<string, string> GetShopColors()
    {
        var storeConfigs = _sessionService.GetStoreConfigs();
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in storeConfigs.Values)
        {
            if (!string.IsNullOrEmpty(config.Color))
            {
                colors[config.StoreId] = config.Color;
                colors[config.StoreName] = config.Color;
            }
        }

        // Fallback colors for common shops
        if (!colors.ContainsKey("samokat")) colors["samokat"] = "#FF3366";
        if (!colors.ContainsKey("lavka")) colors["lavka"] = "#FFCC00";
        if (!colors.ContainsKey("kuper")) colors["kuper"] = "#FF6B00";
        if (!colors.ContainsKey("Самокат")) colors["Самокат"] = "#FF3366";
        if (!colors.ContainsKey("Яндекс.Лавка")) colors["Яндекс.Лавка"] = "#FFCC00";
        if (!colors.ContainsKey("Kuper")) colors["Kuper"] = "#FF6B00";

        return colors;
    }

    /// <summary>
    /// Нормализовать название магазина для сопоставления с цветами
    /// </summary>
    private static string NormalizeShopName(string shop)
    {
        if (string.IsNullOrEmpty(shop)) return shop;

        var lower = shop.ToLowerInvariant();
        if (lower.Contains("самокат") || lower.Contains("samokat")) return "Самокат";
        if (lower.Contains("лавка") || lower.Contains("lavka") || lower.Contains("yandex")) return "Яндекс.Лавка";
        if (lower.Contains("kuper") || lower.Contains("ашан") || lower.Contains("ashan")) return "Kuper";
        if (lower.Contains("vprok") || lower.Contains("впрок") || lower.Contains("perekrestok") || lower.Contains("перекрёсток")) return "Vprok";

        return shop;
    }

    /// <summary>
    /// Начать авторизацию в магазине (открыть WebView)
    /// </summary>
    [RelayCommand]
    private void LoginToStore(StoreAuthStatus store)
    {
        if (store == null) return;

        _logger.LogInformation("[ShoppingViewModel] Opening login for store: {Store}", store.StoreId);

        // Получаем URL магазина из конфигурации
        var storeConfigs = _sessionService.GetStoreConfigs();
        if (storeConfigs.TryGetValue(store.StoreId, out var config))
        {
            AuthenticatingStore = store;
            AuthStoreUrl = config.BaseUrl;
            ShowAuthOverlay = true;

            _logger.LogDebug("[ShoppingViewModel] Auth URL: {Url}", AuthStoreUrl);
        }
        else
        {
            _logger.LogWarning("[ShoppingViewModel] Store config not found: {Store}", store.StoreId);
        }
    }

    /// <summary>
    /// Завершить авторизацию (закрыть WebView и перепроверить)
    /// </summary>
    [RelayCommand]
    private async Task FinishLoginAsync()
    {
        _logger.LogInformation("[ShoppingViewModel] Finishing login for store: {Store}", AuthenticatingStore?.StoreId);

        var storeToCheck = AuthenticatingStore;

        // Закрываем оверлей
        ShowAuthOverlay = false;
        AuthenticatingStore = null;
        AuthStoreUrl = null;

        // Перепроверяем авторизацию
        if (storeToCheck != null && _webViewContext != null)
        {
            storeToCheck.IsChecking = true;

            try
            {
                var authStatuses = await _sessionService.CheckStoreAuthStatusAsync(_webViewContext);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (authStatuses.TryGetValue(storeToCheck.StoreId, out var isAuth))
                    {
                        storeToCheck.IsAuthenticated = isAuth;
                        _logger.LogInformation("[ShoppingViewModel] Store {Store} auth status: {IsAuth}",
                            storeToCheck.StoreId, isAuth);
                    }

                    storeToCheck.IsChecking = false;
                    HasAnyAuthenticatedStore = StoreAuthStatuses.Any(s => s.IsAuthenticated);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ShoppingViewModel] Failed to check auth after login");
                storeToCheck.IsChecking = false;
            }
        }
    }

    /// <summary>
    /// Проверить авторизацию во всех магазинах
    /// </summary>
    [RelayCommand]
    private async Task CheckStoreAuthAsync()
    {
        if (_webViewContext == null)
        {
            _logger.LogWarning("[ShoppingViewModel] WebView not ready, cannot check auth");
            return;
        }

        _logger.LogInformation("[ShoppingViewModel] Checking store auth status");
        IsCheckingAuth = true;

        try
        {
            var authStatuses = await _sessionService.CheckStoreAuthStatusAsync(_webViewContext);

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var store in StoreAuthStatuses)
                {
                    store.IsChecking = false;
                    if (authStatuses.TryGetValue(store.StoreId, out var isAuth))
                    {
                        store.IsAuthenticated = isAuth;
                    }
                }

                HasAnyAuthenticatedStore = StoreAuthStatuses.Any(s => s.IsAuthenticated);
                _logger.LogInformation("[ShoppingViewModel] Auth check complete. Any authenticated: {HasAuth}",
                    HasAnyAuthenticatedStore);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] Failed to check store auth");
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var store in StoreAuthStatuses)
                {
                    store.IsChecking = false;
                    store.IsAuthenticated = false;
                }
                HasAnyAuthenticatedStore = false;
            });
        }
        finally
        {
            IsCheckingAuth = false;
        }
    }

    /// <summary>
    /// Начать новую сессию закупок
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private async Task StartSessionAsync()
    {
        _logger.LogInformation("[ShoppingViewModel] Starting new shopping session");

        IsProcessing = true;

        try
        {
            // Проверяем доступность YandexAgent в background потоке чтобы не блокировать UI
            var (isAvailable, availabilityMessage) = await Task.Run(() => _shoppingChatService.CheckAvailabilityAsync());
            if (!isAvailable)
            {
                _logger.LogWarning("[ShoppingViewModel] YandexAgent not available: {Message}", availabilityMessage);
                AddSystemMessage($"⚠️ AI недоступен: {availabilityMessage}\n\nВы можете добавлять товары вручную.");
            }

            var session = await Task.Run(() => _sessionService.StartNewSessionAsync());
            HasSession = true;
            State = session.State;

            _logger.LogInformation("[ShoppingViewModel] Session started: {SessionId}", session.Id);

            // Начинаем conversation в ShoppingChatService (в background потоке)
            if (isAvailable)
            {
                try
                {
                    await Task.Run(() => _shoppingChatService.StartConversationAsync(session));
                    _logger.LogInformation("[ShoppingViewModel] Chat conversation started");

                    // Отправляем скрытое сообщение чтобы модель приветствовала пользователя
                    // Само сообщение не показывается в UI, только ответ модели
                    _chatCts?.Cancel();
                    _chatCts = new CancellationTokenSource();
                    await SendHiddenMessageAsync(
                        "Представься и приветствуй пользователя. Кратко расскажи о своих возможностях и пригласи начать работу.",
                        _chatCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("[ShoppingViewModel] Initial greeting cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ShoppingViewModel] Failed to start chat conversation");
                    AddSystemMessage($"⚠️ Не удалось запустить AI чат: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] Failed to start session");
            AddSystemMessage($"Ошибка: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Отменить текущий запрос к AI
    /// </summary>
    [RelayCommand]
    private void CancelChat()
    {
        _logger.LogDebug("[ShoppingViewModel] Cancelling chat request");
        _chatCts?.Cancel();
    }

    /// <summary>
    /// Отправить сообщение в чат
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;

        var message = UserInput.Trim();
        UserInput = string.Empty;

        _logger.LogDebug("[ShoppingViewModel] User message: {Message}", message);

        // Добавляем сообщение пользователя
        AddUserMessage(message);

        IsProcessing = true;
        _chatCts?.Cancel();
        _chatCts = new CancellationTokenSource();

        try
        {
            // Если AI conversation активен — используем ShoppingChatService
            if (_shoppingChatService.ConversationId != null)
            {
                await SendViaChatServiceAsync(message, _chatCts.Token);
            }
            else
            {
                // Fallback: простой парсинг без AI
                await SendWithFallbackParsingAsync(message);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[ShoppingViewModel] Chat message cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] Error processing message");
            AddSystemMessage($"Ошибка: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Отправить сообщение через ShoppingChatService с tool calling
    /// </summary>
    private async Task SendViaChatServiceAsync(string message, CancellationToken cancellationToken)
    {
        var assistantMessage = new ShoppingChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now
        };

        ShoppingResponsePart? currentFinalAnswer = null;

        // Добавляем пустое сообщение для streaming
        await Application.Current.Dispatcher.InvokeAsync(() => Messages.Add(assistantMessage));

        // Создаём Progress без захвата SynchronizationContext чтобы не блокировать UI
        // Используем DispatcherProgress который явно вызывает InvokeAsync с низким приоритетом
        var progress = new DispatcherProgress<ChatProgress>(Application.Current.Dispatcher, p =>
        {
            System.Diagnostics.Debug.WriteLine($"[ShoppingViewModel HANDLER-1] SWITCH ENTER: Type={p.Type}, ToolName={p.ToolName}");
            switch (p.Type)
            {
                case ChatProgressType.TextDelta:
                    // Добавляем текст к FinalAnswer части
                    if (currentFinalAnswer == null)
                    {
                        currentFinalAnswer = new ShoppingResponsePart { IsToolCall = false };
                        assistantMessage.Parts.Add(currentFinalAnswer);
                    }
                    currentFinalAnswer.Text += p.Text;
                    // Также обновляем Text для обратной совместимости
                    assistantMessage.Text += p.Text;
                    break;

                case ChatProgressType.ToolCall:
                    System.Diagnostics.Debug.WriteLine($"[ShoppingViewModel HANDLER] ToolCall ENTER: {p.ToolName}");
                    _logger.LogInformation("[ShoppingViewModel] UI received ToolCall: {Tool}", p.ToolName);
                    System.Diagnostics.Debug.WriteLine($"[ShoppingViewModel HANDLER] ToolCall AFTER LOG: {p.ToolName}");

                    // Если до tool call был текст, содержащий JSON с tool call - очищаем его
                    // YandexGPT иногда дублирует tool call в виде JSON в тексте
                    if (currentFinalAnswer != null)
                    {
                        var cleanedText = CleanToolCallJsonFromText(currentFinalAnswer.Text, p.ToolName);
                        if (string.IsNullOrWhiteSpace(cleanedText))
                        {
                            // Текст был только JSON tool call - удаляем эту часть
                            assistantMessage.Parts.Remove(currentFinalAnswer);
                        }
                        else
                        {
                            currentFinalAnswer.Text = cleanedText;
                        }
                    }

                    // Создаём новую часть для tool call
                    var toolPart = new ShoppingResponsePart
                    {
                        IsToolCall = true,
                        ToolName = p.ToolName,
                        ToolArgs = p.ToolArgs
                    };
                    assistantMessage.Parts.Add(toolPart);
                    // Сбрасываем текущий FinalAnswer - после tool call может быть новый текст
                    currentFinalAnswer = null;
                    break;

                case ChatProgressType.ToolResult:
                    _logger.LogDebug("[ShoppingViewModel] Tool result: {Tool} success={Success}", p.ToolName, p.ToolSuccess);
                    // Ищем последний tool call с таким именем без результата
                    var lastToolCall = assistantMessage.Parts
                        .LastOrDefault(x => x.IsToolCall && x.ToolName == p.ToolName && x.ToolResult == null);
                    if (lastToolCall != null)
                    {
                        lastToolCall.ToolResult = p.ToolResult;
                        lastToolCall.ToolSuccess = p.ToolSuccess;
                    }
                    break;

                case ChatProgressType.Complete:
                    _logger.LogDebug("[ShoppingViewModel] Chat complete");
                    // Очищаем пустые FinalAnswer части и tool call JSON из финального текста
                    CleanupEmptyParts(assistantMessage);
                    break;
            }
        });

        // ВАЖНО: выполняем SendAsync через Task.Run чтобы перенести всю синхронную работу
        // (сериализация JSON, подготовка сообщений, логирование) в background поток.
        // Без этого UI замирает на время подготовки запроса к AI.
        var response = await Task.Run(
            () => _shoppingChatService.SendAsync(message, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
            _logger.LogWarning("[ShoppingViewModel] Chat failed: {Error}", response.ErrorMessage);
            // Обновляем сообщение с ошибкой если не было streaming данных
            // Используем !progress.HasReported вместо Parts.Count == 0 из-за race condition
            if (string.IsNullOrEmpty(assistantMessage.Text) && !progress.HasReported)
            {
                var errorPart = new ShoppingResponsePart
                {
                    IsToolCall = false,
                    Text = $"Ошибка: {response.ErrorMessage}"
                };
                await Application.Current.Dispatcher.InvokeAsync(() => assistantMessage.Parts.Add(errorPart));
                assistantMessage.Text = errorPart.Text;
                assistantMessage.IsError = true;
            }
        }
        else if (!progress.HasReported && !string.IsNullOrEmpty(response.Content))
        {
            // Если streaming не сработал (ни один Report не был вызван), но ответ есть — показываем его
            // Используем progress.HasReported вместо Parts.Count == 0, т.к. Parts заполняется асинхронно
            // через Dispatcher и может быть пустым из-за race condition
            _logger.LogDebug("[ShoppingViewModel] No streaming reports, using final response");
            var finalPart = new ShoppingResponsePart
            {
                IsToolCall = false,
                Text = response.Content
            };
            await Application.Current.Dispatcher.InvokeAsync(() => assistantMessage.Parts.Add(finalPart));
            assistantMessage.Text = response.Content;
        }
    }

    /// <summary>
    /// Отправить скрытое сообщение модели (не показывается в UI, только ответ)
    /// Используется для инициации диалога, когда нужно чтобы модель приветствовала пользователя
    /// </summary>
    private async Task SendHiddenMessageAsync(string message, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[ShoppingViewModel] Sending hidden message: {Message}", message);

        var assistantMessage = new ShoppingChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now
        };

        ShoppingResponsePart? currentFinalAnswer = null;

        // Добавляем пустое сообщение для streaming (ответ модели будет виден)
        await Application.Current.Dispatcher.InvokeAsync(() => Messages.Add(assistantMessage));

        var progress = new DispatcherProgress<ChatProgress>(Application.Current.Dispatcher, p =>
        {
            System.Diagnostics.Debug.WriteLine($"[ShoppingViewModel HANDLER-2] SWITCH ENTER: Type={p.Type}, ToolName={p.ToolName}");
            switch (p.Type)
            {
                case ChatProgressType.TextDelta:
                    if (currentFinalAnswer == null)
                    {
                        currentFinalAnswer = new ShoppingResponsePart { IsToolCall = false };
                        assistantMessage.Parts.Add(currentFinalAnswer);
                    }
                    currentFinalAnswer.Text += p.Text;
                    assistantMessage.Text += p.Text;
                    break;

                case ChatProgressType.ToolCall:
                    _logger.LogInformation("[ShoppingViewModel] Hidden msg: UI received ToolCall: {Tool}", p.ToolName);
                    // Если до tool call был текст с JSON - очищаем
                    if (currentFinalAnswer != null)
                    {
                        var cleanedText = CleanToolCallJsonFromText(currentFinalAnswer.Text, p.ToolName);
                        if (string.IsNullOrWhiteSpace(cleanedText))
                        {
                            assistantMessage.Parts.Remove(currentFinalAnswer);
                        }
                        else
                        {
                            currentFinalAnswer.Text = cleanedText;
                        }
                    }
                    // Создаём карточку tool call
                    var toolPart = new ShoppingResponsePart
                    {
                        IsToolCall = true,
                        ToolName = p.ToolName,
                        ToolArgs = p.ToolArgs
                    };
                    assistantMessage.Parts.Add(toolPart);
                    currentFinalAnswer = null;
                    break;

                case ChatProgressType.ToolResult:
                    _logger.LogDebug("[ShoppingViewModel] Hidden msg: Tool result: {Tool} success={Success}", p.ToolName, p.ToolSuccess);
                    var lastToolCall = assistantMessage.Parts
                        .LastOrDefault(x => x.IsToolCall && x.ToolName == p.ToolName && x.ToolResult == null);
                    if (lastToolCall != null)
                    {
                        lastToolCall.ToolResult = p.ToolResult;
                        lastToolCall.ToolSuccess = p.ToolSuccess;
                    }
                    break;

                case ChatProgressType.Complete:
                    CleanupEmptyParts(assistantMessage);
                    break;
            }
        });

        // Отправляем сообщение модели в background потоке
        var response = await Task.Run(
            () => _shoppingChatService.SendAsync(message, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
            _logger.LogWarning("[ShoppingViewModel] Hidden message failed: {Error}", response.ErrorMessage);
            // Удаляем пустое сообщение если ошибка
            if (string.IsNullOrEmpty(assistantMessage.Text) && !progress.HasReported)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Messages.Remove(assistantMessage));
            }
        }
        else if (!progress.HasReported && !string.IsNullOrEmpty(response.Content))
        {
            var finalPart = new ShoppingResponsePart
            {
                IsToolCall = false,
                Text = response.Content
            };
            await Application.Current.Dispatcher.InvokeAsync(() => assistantMessage.Parts.Add(finalPart));
            assistantMessage.Text = response.Content;
        }
    }

    /// <summary>
    /// Очищает текст от JSON tool call блоков
    /// </summary>
    private static string CleanToolCallJsonFromText(string text, string? toolName)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Паттерны для JSON tool call в тексте от YandexGPT
        // Например: ```json\n{"name": "query", ...}\n```
        // Или просто: {"name": "query", ...}

        var result = text;

        // Удаляем markdown code blocks с JSON
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"```json\s*\{[^}]*""name""\s*:\s*""[^""]*""[^`]*```",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Удаляем standalone JSON объекты с "name" (tool calls)
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"\{\s*""name""\s*:\s*""[^""]*""\s*,\s*""arguments""\s*:\s*\{[^}]*\}\s*\}",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Если указано имя инструмента - удаляем конкретные упоминания
        if (!string.IsNullOrEmpty(toolName))
        {
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $@"\{{\s*""name""\s*:\s*""{System.Text.RegularExpressions.Regex.Escape(toolName)}""[^}}]*\}}",
                "",
                System.Text.RegularExpressions.RegexOptions.Singleline);
        }

        return result.Trim();
    }

    /// <summary>
    /// Очищает пустые FinalAnswer части и финальный текст от JSON tool calls
    /// </summary>
    private void CleanupEmptyParts(ShoppingChatMessage message)
    {
        // Удаляем пустые FinalAnswer части
        var emptyParts = message.Parts
            .Where(p => !p.IsToolCall && string.IsNullOrWhiteSpace(p.Text))
            .ToList();
        foreach (var part in emptyParts)
        {
            message.Parts.Remove(part);
        }

        // Очищаем текст в оставшихся FinalAnswer частях от tool call JSON
        foreach (var part in message.Parts.Where(p => !p.IsToolCall))
        {
            var cleanedText = CleanToolCallJsonFromText(part.Text, null);
            if (cleanedText != part.Text)
            {
                part.Text = cleanedText;
            }
        }

        // Удаляем части, которые стали пустыми после очистки
        emptyParts = message.Parts
            .Where(p => !p.IsToolCall && string.IsNullOrWhiteSpace(p.Text))
            .ToList();
        foreach (var part in emptyParts)
        {
            message.Parts.Remove(part);
        }
    }

    /// <summary>
    /// Fallback: простой парсинг товаров без AI
    /// </summary>
    private async Task SendWithFallbackParsingAsync(string message)
    {
        await Task.Delay(100); // Небольшая задержка для UX

        var items = ParseItemsFromMessage(message);

        if (items.Count > 0)
        {
            foreach (var (name, qty, unit) in items)
            {
                _sessionService.AddItem(name, qty, unit, GuessCategory(name));
            }

            AddAssistantMessage($"Добавил в список: {string.Join(", ", items.Select(i => i.name))}");
        }
        else
        {
            AddAssistantMessage("Понял! Что-то ещё добавить в список?");
        }
    }

    /// <summary>
    /// Начать планирование (поиск в магазинах)
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartPlanning))]
    private async Task StartPlanningAsync()
    {
        if (_webViewContext == null)
        {
            AddSystemMessage("Ошибка: WebView2 не инициализирован");
            return;
        }

        _logger.LogInformation("[ShoppingViewModel] Starting planning phase with {Count} items", DraftItems.Count);

        // Отменяем предыдущий поиск если был
        _planningCts?.Cancel();
        _planningCts = new CancellationTokenSource();

        // Переключаемся в состояние Planning
        State = ShoppingSessionState.Planning;
        IsProcessing = true;

        // Инициализируем магазины из сервиса
        var storeConfigs = _sessionService.GetStoreConfigs();
        StoreProgress.Clear();
        PlanningLog.Clear();

        foreach (var config in storeConfigs.Values.OrderBy(c => c.Priority))
        {
            StoreProgress.Add(new StoreProgressItem
            {
                StoreName = config.StoreName,
                StoreId = config.StoreId,
                Color = config.Color
            });
        }

        AddPlanningLog("Начинаю поиск товаров...");
        CurrentOperationText = "Подготовка...";
        OverallProgress = 0;

        // Создаём Progress для получения обновлений
        var progress = new Progress<PlanningProgress>(OnPlanningProgress);

        // Progress для AI выбора (отображает процесс AI в чате)
        var aiProgress = new Progress<ChatProgress>(OnAiProgress);

        try
        {
            await _sessionService.StartPlanningAsync(_webViewContext, progress, aiProgress, _planningCts.Token);

            OverallProgress = 100;
            CurrentOperationText = "Поиск завершён!";
            AddPlanningLog("Все магазины обработаны");

            // Заполняем карточки корзин для анализа
            var plannedBaskets = _sessionService.GetAllBaskets();
            Baskets.Clear();

            foreach (var basket in plannedBaskets.Values.OrderBy(b => b.TotalPrice))
            {
                var storeConfig = _sessionService.GetStoreConfigs().GetValueOrDefault(basket.Store);

                Baskets.Add(new BasketCardViewModel
                {
                    StoreId = basket.Store,
                    StoreName = basket.StoreName,
                    TotalPrice = basket.TotalPrice,
                    ItemsFound = basket.ItemsFound,
                    ItemsTotal = basket.ItemsTotal,
                    DeliveryTime = basket.DeliveryTime ?? "1-2 часа",
                    DeliveryPrice = basket.DeliveryPrice ?? "Бесплатно",
                    Color = storeConfig?.Color ?? "#7C4DFF",
                    Items = basket.Items
                        .Where(i => i.Match != null)
                        .Select(i => new BasketItemViewModel
                        {
                            Name = i.Match!.ProductName,
                            Price = i.Match.Price,
                            Quantity = i.Quantity,
                            LineTotal = i.LineTotal
                        })
                        .ToList()
                });

                AddPlanningLog($"[{basket.StoreName}] Итого: {basket.TotalPrice:N0} ₽ ({basket.ItemsFound}/{basket.ItemsTotal} товаров)");
            }

            // Автоматически выбираем самую дешёвую корзину
            if (Baskets.Count > 0)
            {
                SelectBasket(Baskets[0]);
            }

            // Переходим в состояние Analyzing
            State = ShoppingSessionState.Analyzing;
            _logger.LogInformation("[ShoppingViewModel] Planning completed, {Count} baskets ready", Baskets.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[ShoppingViewModel] Planning cancelled");
            AddPlanningLog("Поиск отменён");
            CurrentOperationText = "Отменено";
            State = ShoppingSessionState.Drafting;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] Planning failed");
            AddPlanningLog($"ОШИБКА: {ex.Message}");
            CurrentOperationText = "Ошибка при поиске";
            State = ShoppingSessionState.Drafting;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Обработка прогресса поиска
    /// </summary>
    // Throttling для progress updates
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private const int ProgressUpdateIntervalMs = 50; // Обновлять UI не чаще чем раз в 50ms
    private PlanningProgress? _pendingProgress;

    private void OnPlanningProgress(PlanningProgress p)
    {
        // Progress<T> уже маршалит в UI поток, но мы добавляем throttling
        // чтобы не перегружать UI при быстрых обновлениях
        var now = DateTime.Now;

        // Для важных событий (Found, NotFound, Error) — всегда обрабатываем
        var isImportantEvent = p.Status is PlanningStatus.Found or PlanningStatus.NotFound or PlanningStatus.Error;

        if (!isImportantEvent && (now - _lastProgressUpdate).TotalMilliseconds < ProgressUpdateIntervalMs)
        {
            // Сохраняем последний pending progress чтобы не потерять
            _pendingProgress = p;
            return;
        }

        _lastProgressUpdate = now;
        _pendingProgress = null;

        // Используем BeginInvoke вместо Invoke чтобы не блокировать поток
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            // Обновляем общий прогресс
            OverallProgress = (int)p.ProgressPercent;
            CurrentOperationText = $"Ищу \"{p.ItemName}\" в {p.StoreName}";

            // Находим карточку магазина
            var storeCard = StoreProgress.FirstOrDefault(s => s.StoreId == p.Store);
            if (storeCard != null)
            {
                storeCard.ProgressPercent = (int)((double)p.CurrentItem / p.TotalItems * 100);

                storeCard.StatusText = p.Status switch
                {
                    PlanningStatus.Searching => $"Поиск: {p.ItemName}",
                    PlanningStatus.Selecting => $"AI выбирает: {p.ItemName}",
                    PlanningStatus.Found => $"Найдено: {p.CurrentItem}/{p.TotalItems}",
                    PlanningStatus.NotFound => $"Поиск: {p.CurrentItem}/{p.TotalItems}",
                    PlanningStatus.Error => $"Ошибка: {p.ErrorMessage}",
                    _ => storeCard.StatusText
                };
            }

            // Добавляем в лог только важные события
            if (isImportantEvent)
            {
                string? logMessage;
                if (p.Status == PlanningStatus.Found && !string.IsNullOrEmpty(p.Reasoning))
                {
                    logMessage = $"[{p.StoreName}] ✓ {p.ItemName} → {p.MatchedProduct} x{p.SelectedQuantity} ({p.Price:N0} ₽)\n   💡 {p.Reasoning}";
                }
                else
                {
                    logMessage = p.Status switch
                    {
                        PlanningStatus.Found => $"[{p.StoreName}] ✓ {p.ItemName} → {p.MatchedProduct} ({p.Price:N0} ₽)",
                        PlanningStatus.NotFound => $"[{p.StoreName}] ✗ {p.ItemName} — не найдено",
                        PlanningStatus.Error => $"[{p.StoreName}] ⚠ {p.ItemName} — {p.ErrorMessage}",
                        _ => null
                    };
                }

                if (logMessage != null)
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    PlanningLog.Add($"[{timestamp}] {logMessage}");
                }
            }
        });
    }

    /// <summary>
    /// Текущее сообщение AI при выборе товаров (для streaming)
    /// </summary>
    private ShoppingChatMessage? _currentAiSelectionMessage;

    /// <summary>
    /// Буфер текста для throttling TextDelta
    /// </summary>
    private readonly System.Text.StringBuilder _textDeltaBuffer = new();
    private DateTime _lastTextDeltaFlush = DateTime.MinValue;
    private const int TextDeltaFlushIntervalMs = 100; // Обновлять UI не чаще чем раз в 100ms

    /// <summary>
    /// Обработка прогресса AI выбора — выводит в чат и в лог (без блокировки UI)
    /// </summary>
    private void OnAiProgress(ChatProgress p)
    {
        // Используем BeginInvoke вместо Invoke чтобы не блокировать поток
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (p.Type)
            {
                case ChatProgressType.TextDelta:
                    // AI рассуждает вслух — буферизируем и показываем в чате с throttling
                    if (!string.IsNullOrEmpty(p.Text))
                    {
                        // Создаём сообщение если ещё нет
                        if (_currentAiSelectionMessage == null)
                        {
                            _currentAiSelectionMessage = new ShoppingChatMessage
                            {
                                Role = ChatRole.Assistant,
                                Text = "",
                                Timestamp = DateTime.Now
                            };
                            Messages.Add(_currentAiSelectionMessage);
                        }

                        // Добавляем в буфер
                        _textDeltaBuffer.Append(p.Text);

                        // Flush если прошло достаточно времени
                        var now = DateTime.Now;
                        if ((now - _lastTextDeltaFlush).TotalMilliseconds >= TextDeltaFlushIntervalMs)
                        {
                            _currentAiSelectionMessage.Text += _textDeltaBuffer.ToString();
                            _textDeltaBuffer.Clear();
                            _lastTextDeltaFlush = now;
                        }
                    }
                    break;

                case ChatProgressType.ToolCall:
                    // Flush буфер перед tool call
                    FlushTextDeltaBuffer();

                    // AI вызывает инструмент
                    if (p.ToolName == "select_product")
                    {
                        AddPlanningLogAsync($"🤖 AI вызывает select_product...");

                        // Добавляем карточку tool call в текущее сообщение
                        if (_currentAiSelectionMessage != null)
                        {
                            _currentAiSelectionMessage.Parts.Add(new ShoppingResponsePart
                            {
                                IsToolCall = true,
                                ToolName = p.ToolName,
                                ToolArgs = p.ToolArgs
                            });
                        }
                    }
                    break;

                case ChatProgressType.ToolResult:
                    // Результат инструмента
                    if (p.ToolName == "select_product")
                    {
                        if (p.ToolSuccess == true)
                        {
                            AddPlanningLogAsync($"✓ Товар выбран");
                        }
                        else
                        {
                            AddPlanningLogAsync($"✗ Ошибка выбора: {p.ToolResult}");
                        }

                        // Обновляем результат в карточке tool call
                        if (_currentAiSelectionMessage != null)
                        {
                            var lastToolCall = _currentAiSelectionMessage.Parts
                                .LastOrDefault(x => x.IsToolCall && x.ToolName == p.ToolName && x.ToolResult == null);
                            if (lastToolCall != null)
                            {
                                lastToolCall.ToolResult = p.ToolResult;
                                lastToolCall.ToolSuccess = p.ToolSuccess;
                            }
                        }
                    }
                    break;

                case ChatProgressType.Complete:
                    // Flush буфер и сбрасываем сообщение
                    FlushTextDeltaBuffer();
                    _currentAiSelectionMessage = null;
                    break;
            }
        });
    }

    /// <summary>
    /// Flush буфера текста в сообщение
    /// </summary>
    private void FlushTextDeltaBuffer()
    {
        if (_textDeltaBuffer.Length > 0 && _currentAiSelectionMessage != null)
        {
            _currentAiSelectionMessage.Text += _textDeltaBuffer.ToString();
            _textDeltaBuffer.Clear();
        }
    }

    /// <summary>
    /// Добавить запись в лог планирования (блокирующий вызов)
    /// </summary>
    private void AddPlanningLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Application.Current.Dispatcher.Invoke(() =>
        {
            lock (_logLock)
            {
                PlanningLog.Add($"[{timestamp}] {message}");
            }
        });
    }

    /// <summary>
    /// Добавить запись в лог планирования (асинхронный, не блокирует)
    /// </summary>
    private void AddPlanningLogAsync(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            lock (_logLock)
            {
                PlanningLog.Add($"[{timestamp}] {message}");
            }
        });
    }

    /// <summary>
    /// Удалить товар из списка
    /// </summary>
    [RelayCommand]
    private void RemoveItem(DraftItem item)
    {
        if (item == null) return;

        _logger.LogDebug("[ShoppingViewModel] Removing item: {Name}", item.Name);
        _sessionService.RemoveItem(item.Name);
    }

    /// <summary>
    /// Увеличить количество товара
    /// </summary>
    [RelayCommand]
    private void IncreaseQuantity(DraftItem item)
    {
        if (item == null) return;

        var newQty = item.Quantity + 1;
        _sessionService.UpdateItem(item.Name, newQty);
    }

    /// <summary>
    /// Уменьшить количество товара
    /// </summary>
    [RelayCommand]
    private void DecreaseQuantity(DraftItem item)
    {
        if (item == null) return;

        if (item.Quantity <= 1)
        {
            _sessionService.RemoveItem(item.Name);
        }
        else
        {
            var newQty = item.Quantity - 1;
            _sessionService.UpdateItem(item.Name, newQty);
        }
    }

    /// <summary>
    /// Очистить список
    /// </summary>
    [RelayCommand]
    private void ClearList()
    {
        var items = DraftItems.ToList();
        foreach (var item in items)
        {
            _sessionService.RemoveItem(item.Name);
        }

        AddSystemMessage("Список очищен");
    }

    /// <summary>
    /// Отменить планирование и вернуться к черновику
    /// </summary>
    [RelayCommand]
    private void CancelPlanning()
    {
        _logger.LogInformation("[ShoppingViewModel] Cancelling planning");

        // Отменяем операцию
        _planningCts?.Cancel();

        // Возвращаемся в состояние Drafting
        State = ShoppingSessionState.Drafting;

        // Очищаем прогресс
        StoreProgress.Clear();
        PlanningLog.Clear();
        OverallProgress = 0;
        CurrentOperationText = "Подготовка...";

        AddSystemMessage("Поиск отменён. Вы можете изменить список и попробовать снова.");
    }

    /// <summary>
    /// Выбрать корзину для оформления
    /// </summary>
    [RelayCommand]
    private void SelectBasket(BasketCardViewModel basket)
    {
        if (basket == null) return;

        // Снимаем выделение со всех корзин
        foreach (var b in Baskets)
        {
            b.IsSelected = false;
        }

        // Выделяем выбранную
        basket.IsSelected = true;
        SelectedBasket = basket;

        _logger.LogDebug("[ShoppingViewModel] Selected basket: {Store}", basket.StoreId);
    }

    /// <summary>
    /// Можно ли оформить заказ
    /// </summary>
    private bool CanCreateCart => SelectedBasket != null && State == ShoppingSessionState.Analyzing && !IsProcessing;

    /// <summary>
    /// Оформить заказ в выбранном магазине
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateCart))]
    private async Task CreateCartAsync()
    {
        if (SelectedBasket == null || _webViewContext == null) return;

        _logger.LogInformation("[ShoppingViewModel] Creating cart in {Store}", SelectedBasket.StoreId);

        IsProcessing = true;
        State = ShoppingSessionState.Finalizing;

        try
        {
            var url = await _sessionService.CreateCartAsync(_webViewContext, SelectedBasket.StoreId);

            CheckoutUrl = url;
            SelectedStoreName = SelectedBasket.StoreName;
            OrderTotal = SelectedBasket.TotalPrice;

            State = ShoppingSessionState.Completed;

            _logger.LogInformation("[ShoppingViewModel] Cart created: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] Failed to create cart");
            State = ShoppingSessionState.Analyzing;
            AddPlanningLog($"ОШИБКА оформления: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Открыть корзину в браузере
    /// </summary>
    [RelayCommand]
    private void OpenCheckoutUrl()
    {
        if (string.IsNullOrEmpty(CheckoutUrl)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = CheckoutUrl,
                UseShellExecute = true
            });

            _logger.LogInformation("[ShoppingViewModel] Opened checkout URL: {Url}", CheckoutUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingViewModel] Failed to open URL");
        }
    }

    /// <summary>
    /// Вернуться к редактированию списка
    /// </summary>
    [RelayCommand]
    private void BackToDrafting()
    {
        _logger.LogInformation("[ShoppingViewModel] Returning to drafting");

        State = ShoppingSessionState.Drafting;

        // Очищаем данные анализа
        Baskets.Clear();
        SelectedBasket = null;
        StoreProgress.Clear();
        PlanningLog.Clear();
    }

    /// <summary>
    /// Начать новую сессию (после завершения)
    /// </summary>
    [RelayCommand]
    private async Task StartNewSessionAsync()
    {
        _logger.LogInformation("[ShoppingViewModel] Starting new session after completion");

        // Очищаем всё
        DraftItems.Clear();
        GroupedItems.Clear();
        Messages.Clear();
        Baskets.Clear();
        SelectedBasket = null;
        CheckoutUrl = null;
        SelectedStoreName = null;
        OrderTotal = 0;
        StoreProgress.Clear();
        PlanningLog.Clear();

        HasSession = false;

        // Показываем экран приветствия
        await StartSessionAsync();
    }

    #endregion

    #region Event Handlers

    private void OnSessionChanged(object? sender, ShoppingSession session)
    {
        // Используем InvokeAsync с приоритетом Background чтобы не блокировать UI
        // и чтобы обновления происходили в том же порядке что и streaming текст
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            State = session.State;
            HasSession = true;
            _logger.LogDebug("[ShoppingViewModel] Session changed: State={State}", session.State);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnItemAdded(object? sender, DraftItem item)
    {
        // Используем InvokeAsync с приоритетом Background чтобы корзина обновлялась
        // в том же порядке что и streaming текст (FIFO очередь одного приоритета)
        // Это обеспечивает правильный порядок: сначала текст, потом корзина
        // потому что Report(TextDelta) вызывается ДО вызова tool (update_basket)
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lock (_itemsLock)
            {
                DraftItems.Add(item);
                UpdateGroupedItems();
                UpdateTotals();
            }
            StartPlanningCommand.NotifyCanExecuteChanged();
            _logger.LogDebug("[ShoppingViewModel] Item added: {Name}", item.Name);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnItemRemoved(object? sender, DraftItem item)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lock (_itemsLock)
            {
                var existing = DraftItems.FirstOrDefault(i => i.Id == item.Id);
                if (existing != null)
                {
                    DraftItems.Remove(existing);
                    UpdateGroupedItems();
                    UpdateTotals();
                }
            }
            StartPlanningCommand.NotifyCanExecuteChanged();
            _logger.LogDebug("[ShoppingViewModel] Item removed: {Name}", item.Name);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnItemUpdated(object? sender, DraftItem item)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lock (_itemsLock)
            {
                var existing = DraftItems.FirstOrDefault(i => i.Id == item.Id);
                if (existing != null)
                {
                    var index = DraftItems.IndexOf(existing);
                    DraftItems[index] = item;
                    UpdateGroupedItems();
                    UpdateTotals();
                }
            }
            _logger.LogDebug("[ShoppingViewModel] Item updated: {Name} -> {Qty} {Unit}", item.Name, item.Quantity, item.Unit);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    #endregion

    #region Helper Methods

    private void AddUserMessage(string text)
    {
        lock (_messagesLock)
        {
            Messages.Add(new ShoppingChatMessage
            {
                Role = ChatRole.User,
                Text = text,
                Timestamp = DateTime.Now
            });
        }
    }

    private void AddAssistantMessage(string text)
    {
        lock (_messagesLock)
        {
            Messages.Add(new ShoppingChatMessage
            {
                Role = ChatRole.Assistant,
                Text = text,
                Timestamp = DateTime.Now
            });
        }
    }

    private void AddSystemMessage(string text)
    {
        lock (_messagesLock)
        {
            Messages.Add(new ShoppingChatMessage
            {
                Role = ChatRole.System,
                Text = text,
                Timestamp = DateTime.Now
            });
        }
    }

    private void UpdateGroupedItems()
    {
        GroupedItems.Clear();

        var groups = DraftItems
            .GroupBy(i => i.Category ?? "Другое")
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            GroupedItems.Add(new DraftItemGroup
            {
                Category = group.Key,
                Items = new ObservableCollection<DraftItem>(group)
            });
        }
    }

    private void UpdateTotals()
    {
        TotalItemsCount = DraftItems.Count;
        var totalQty = DraftItems.Sum(i => i.Quantity);
        TotalQuantityText = $"{TotalItemsCount} {GetItemsWord(TotalItemsCount)}";
    }

    private static string GetItemsWord(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;

        if (mod100 >= 11 && mod100 <= 19)
            return "позиций";

        return mod10 switch
        {
            1 => "позиция",
            >= 2 and <= 4 => "позиции",
            _ => "позиций"
        };
    }

    /// <summary>
    /// Простой парсер товаров из текста
    /// </summary>
    private static List<(string name, decimal qty, string unit)> ParseItemsFromMessage(string message)
    {
        var result = new List<(string name, decimal qty, string unit)>();

        // Разбиваем по запятым, "и", переносам строк
        var parts = message
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p));

        foreach (var part in parts)
        {
            // Пропускаем слишком короткие части
            if (part.Length < 2) continue;

            // Пробуем найти количество в начале
            var match = System.Text.RegularExpressions.Regex.Match(
                part,
                @"^(\d+(?:[.,]\d+)?)\s*(кг|л|шт|г|мл)?\s+(.+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var qty = decimal.Parse(match.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.CultureInfo.InvariantCulture);
                var unit = match.Groups[2].Success ? match.Groups[2].Value.ToLower() : "шт";
                var name = match.Groups[3].Value.Trim();
                result.Add((name, qty, unit));
            }
            else
            {
                // Просто название товара
                result.Add((part, 1, "шт"));
            }
        }

        return result;
    }

    /// <summary>
    /// Простое определение категории по названию
    /// </summary>
    private static string? GuessCategory(string name)
    {
        var lower = name.ToLower();

        if (lower.Contains("молок") || lower.Contains("кефир") || lower.Contains("сметан") ||
            lower.Contains("творог") || lower.Contains("сыр") || lower.Contains("йогурт"))
            return "Молочные продукты";

        if (lower.Contains("хлеб") || lower.Contains("батон") || lower.Contains("булк"))
            return "Хлеб";

        if (lower.Contains("яйц"))
            return "Яйца";

        if (lower.Contains("яблок") || lower.Contains("банан") || lower.Contains("апельсин") ||
            lower.Contains("лимон") || lower.Contains("груш"))
            return "Фрукты";

        if (lower.Contains("картош") || lower.Contains("морков") || lower.Contains("лук") ||
            lower.Contains("помидор") || lower.Contains("огурц") || lower.Contains("капуст"))
            return "Овощи";

        if (lower.Contains("курица") || lower.Contains("мясо") || lower.Contains("говядин") ||
            lower.Contains("свинин") || lower.Contains("фарш"))
            return "Мясо";

        if (lower.Contains("рыб") || lower.Contains("лосось") || lower.Contains("сёмг"))
            return "Рыба";

        return null;
    }

    #endregion
}

/// <summary>
/// Сообщение в чате Shopping
/// </summary>
public class ShoppingChatMessage : ObservableObject
{
    private ChatRole _role;
    private string _text = string.Empty;
    private DateTime _timestamp;
    private bool _isError;

    public ChatRole Role
    {
        get => _role;
        set => SetProperty(ref _role, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(Content));
            }
        }
    }

    /// <summary>
    /// Alias for Text (for compatibility with ChatProgress)
    /// </summary>
    public string Content
    {
        get => Text;
        set => Text = value;
    }

    public DateTime Timestamp
    {
        get => _timestamp;
        set => SetProperty(ref _timestamp, value);
    }

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    /// <summary>
    /// Части ответа ассистента (tool calls, final answer)
    /// </summary>
    public ObservableCollection<ShoppingResponsePart> Parts { get; } = new();

    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool IsSystem => Role == ChatRole.System;
}

/// <summary>
/// Часть ответа в Shopping чате (tool call или финальный ответ)
/// </summary>
public class ShoppingResponsePart : ObservableObject
{
    private string _text = string.Empty;
    private string? _toolResult;
    private bool? _toolSuccess;
    private bool _isExpanded;

    /// <summary>
    /// Тип части: ToolCall или FinalAnswer
    /// </summary>
    public bool IsToolCall { get; init; }

    /// <summary>
    /// Название инструмента (для ToolCall)
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Аргументы инструмента (для ToolCall)
    /// </summary>
    public string? ToolArgs { get; init; }

    /// <summary>
    /// Результат выполнения инструмента
    /// </summary>
    public string? ToolResult
    {
        get => _toolResult;
        set => SetProperty(ref _toolResult, value);
    }

    /// <summary>
    /// Успешно ли выполнен инструмент
    /// </summary>
    public bool? ToolSuccess
    {
        get => _toolSuccess;
        set => SetProperty(ref _toolSuccess, value);
    }

    /// <summary>
    /// Текст (для FinalAnswer)
    /// </summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    /// <summary>
    /// Развёрнута ли часть в UI
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// Для UI - является ли FinalAnswer
    /// </summary>
    public bool IsFinalAnswer => !IsToolCall;
}

/// <summary>
/// Роль в чате
/// </summary>
public enum ChatRole
{
    User,
    Assistant,
    System
}

/// <summary>
/// IProgress реализация которая не захватывает SynchronizationContext
/// и батчит текстовые обновления для плавного отображения без задержек
/// </summary>
public class DispatcherProgress<T> : IProgress<T>
{
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private readonly Action<T> _handler;
    private readonly Action? _onReport;
    private readonly object _lock = new();
    private readonly System.Text.StringBuilder _textBuffer = new();
    private bool _updateScheduled;

    /// <summary>
    /// Флаг, указывающий что был хотя бы один вызов Report.
    /// Устанавливается СИНХРОННО в момент вызова Report, до InvokeAsync.
    /// </summary>
    public bool HasReported { get; private set; }

    public DispatcherProgress(System.Windows.Threading.Dispatcher dispatcher, Action<T> handler, Action? onReport = null)
    {
        _dispatcher = dispatcher;
        _handler = handler;
        _onReport = onReport;
    }

    public void Report(T value)
    {
        // Устанавливаем флаг СИНХРОННО
        HasReported = true;
        _onReport?.Invoke();

        // Для ChatProgress с TextDelta используем батчинг
        if (value is ChatProgress chatProgress && chatProgress.Type == ChatProgressType.TextDelta)
        {
            lock (_lock)
            {
                // Накапливаем текст в буфере
                _textBuffer.Append(chatProgress.Text);

                if (!_updateScheduled)
                {
                    _updateScheduled = true;
                    // Планируем flush буфера на UI поток с приоритетом Render
                    _dispatcher.InvokeAsync(() =>
                    {
                        string textToFlush;
                        lock (_lock)
                        {
                            textToFlush = _textBuffer.ToString();
                            _textBuffer.Clear();
                            _updateScheduled = false;
                        }

                        if (!string.IsNullOrEmpty(textToFlush))
                        {
                            // Создаём ChatProgress с накопленным текстом
                            var batchedProgress = new ChatProgress(ChatProgressType.TextDelta) { Text = textToFlush };
                            _handler((T)(object)batchedProgress);
                        }
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
            }
        }
        else
        {
            // Для остальных типов (ToolCall, ToolResult, Complete) сначала flush буфера
            string? pendingText = null;
            lock (_lock)
            {
                if (_textBuffer.Length > 0)
                {
                    pendingText = _textBuffer.ToString();
                    _textBuffer.Clear();
                }
            }

            // Логируем ДО InvokeAsync чтобы понять, вызывается ли вообще
            if (value is ChatProgress cp)
            {
                System.Diagnostics.Debug.WriteLine($"[DispatcherProgress] BEFORE InvokeAsync: Type={cp.Type}, ToolName={cp.ToolName}");
            }

            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Логируем что callback начал выполняться
                    if (value is ChatProgress cpInner)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DispatcherProgress] INSIDE InvokeAsync: Type={cpInner.Type}, ToolName={cpInner.ToolName}");
                    }

                    // Сначала flush оставшийся текст
                    if (!string.IsNullOrEmpty(pendingText))
                    {
                        var batchedProgress = new ChatProgress(ChatProgressType.TextDelta) { Text = pendingText };
                        _handler((T)(object)batchedProgress);
                    }
                    // Затем обрабатываем текущее событие
                    _handler(value);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DispatcherProgress] ERROR in handler: {ex}");
                }
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }
    }
}

/// <summary>
/// Группа товаров по категории
/// </summary>
public class DraftItemGroup
{
    public string Category { get; set; } = string.Empty;
    public ObservableCollection<DraftItem> Items { get; set; } = new();
}

/// <summary>
/// Прогресс поиска в магазине
/// </summary>
public class StoreProgressItem : ObservableObject
{
    private string _storeId = string.Empty;
    private string _storeName = string.Empty;
    private int _progressPercent;
    private string _statusText = "Ожидание...";
    private string? _color;

    public string StoreId
    {
        get => _storeId;
        set => SetProperty(ref _storeId, value);
    }

    public string StoreName
    {
        get => _storeName;
        set => SetProperty(ref _storeName, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string? Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }
}

/// <summary>
/// Карточка корзины для сравнения
/// </summary>
public class BasketCardViewModel : ObservableObject
{
    private string _storeId = string.Empty;
    private string _storeName = string.Empty;
    private decimal _totalPrice;
    private int _itemsFound;
    private int _itemsTotal;
    private string _deliveryTime = string.Empty;
    private string _deliveryPrice = string.Empty;
    private string _color = "#7C4DFF";
    private bool _isSelected;
    private List<BasketItemViewModel> _items = new();

    public string StoreId
    {
        get => _storeId;
        set => SetProperty(ref _storeId, value);
    }

    public string StoreName
    {
        get => _storeName;
        set => SetProperty(ref _storeName, value);
    }

    public decimal TotalPrice
    {
        get => _totalPrice;
        set => SetProperty(ref _totalPrice, value);
    }

    public int ItemsFound
    {
        get => _itemsFound;
        set => SetProperty(ref _itemsFound, value);
    }

    public int ItemsTotal
    {
        get => _itemsTotal;
        set => SetProperty(ref _itemsTotal, value);
    }

    public string DeliveryTime
    {
        get => _deliveryTime;
        set => SetProperty(ref _deliveryTime, value);
    }

    public string DeliveryPrice
    {
        get => _deliveryPrice;
        set => SetProperty(ref _deliveryPrice, value);
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public List<BasketItemViewModel> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    /// <summary>
    /// Все товары найдены
    /// </summary>
    public bool IsComplete => ItemsFound == ItemsTotal;

    /// <summary>
    /// Текст статуса товаров
    /// </summary>
    public string ItemsStatusText => $"{ItemsFound}/{ItemsTotal} товаров";

    /// <summary>
    /// Процент найденных товаров
    /// </summary>
    public double FoundPercent => ItemsTotal > 0 ? (double)ItemsFound / ItemsTotal * 100 : 0;
}

/// <summary>
/// Товар в корзине для отображения
/// </summary>
public class BasketItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>
/// Последний чек для отображения на экране приветствия
/// </summary>
public class RecentReceiptItem
{
    public string Shop { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public string Color { get; set; } = "#7C4DFF";

    public string DateText
    {
        get
        {
            var today = DateTime.Today;
            if (Date.Date == today)
                return $"Сегодня, {Date:HH:mm}";
            if (Date.Date == today.AddDays(-1))
                return $"Вчера, {Date:HH:mm}";
            return Date.ToString("d MMM, HH:mm");
        }
    }
}

/// <summary>
/// Статус авторизации в магазине
/// </summary>
public class StoreAuthStatus : ObservableObject
{
    private string _storeId = string.Empty;
    private string _storeName = string.Empty;
    private bool _isAuthenticated;
    private string _color = "#7C4DFF";
    private bool _isChecking;

    public string StoreId
    {
        get => _storeId;
        set => SetProperty(ref _storeId, value);
    }

    public string StoreName
    {
        get => _storeName;
        set => SetProperty(ref _storeName, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set
        {
            if (SetProperty(ref _isAuthenticated, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => IsChecking ? "Проверка..." : (IsAuthenticated ? "Авторизован" : "Требуется вход");
}
