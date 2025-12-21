using AiWebSniffer.Core.Interfaces;
using SmartBasket.Core.Shopping;

namespace SmartBasket.Services.Shopping;

/// <summary>
/// Сервис управления сессией закупок.
/// Управляет жизненным циклом: черновик → планирование → анализ → оформление.
/// </summary>
public interface IShoppingSessionService
{
    /// <summary>
    /// Текущая активная сессия (null если нет)
    /// </summary>
    ShoppingSession? CurrentSession { get; }

    /// <summary>
    /// Событие: сессия изменилась (создана, изменено состояние)
    /// </summary>
    event EventHandler<ShoppingSession>? SessionChanged;

    /// <summary>
    /// Событие: товар добавлен в черновик
    /// </summary>
    event EventHandler<DraftItem>? ItemAdded;

    /// <summary>
    /// Событие: товар удалён из черновика
    /// </summary>
    event EventHandler<DraftItem>? ItemRemoved;

    /// <summary>
    /// Событие: товар изменён в черновике
    /// </summary>
    event EventHandler<DraftItem>? ItemUpdated;

    /// <summary>
    /// Получить список инициализированных магазинов
    /// </summary>
    IReadOnlyDictionary<string, StoreRuntimeConfig> GetStoreConfigs();

    #region Этап 1: Формирование черновика

    /// <summary>
    /// Начать новую сессию закупок
    /// </summary>
    Task<ShoppingSession> StartNewSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Добавить товар в черновик
    /// </summary>
    void AddItem(string name, decimal quantity, string unit, string? category = null);

    /// <summary>
    /// Удалить товар из черновика по имени (частичное совпадение)
    /// </summary>
    /// <returns>true если товар найден и удалён</returns>
    bool RemoveItem(string name);

    /// <summary>
    /// Изменить количество товара
    /// </summary>
    /// <returns>true если товар найден и изменён</returns>
    bool UpdateItem(string name, decimal quantity, string? unit = null);

    /// <summary>
    /// Получить текущий список товаров в черновике
    /// </summary>
    List<DraftItem> GetCurrentItems();

    #endregion

    #region Этап 2: Планирование (поиск в магазинах)

    /// <summary>
    /// Запустить поиск товаров во всех магазинах
    /// </summary>
    /// <param name="webViewContext">Контекст WebView2 для парсеров</param>
    /// <param name="progress">Прогресс поиска</param>
    /// <param name="ct">Токен отмены</param>
    Task StartPlanningAsync(
        IWebViewContext webViewContext,
        IProgress<PlanningProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Запустить поиск товаров (без WebView - выбросит исключение)
    /// </summary>
    [Obsolete("Use StartPlanningAsync(IWebViewContext, IProgress, CancellationToken) instead")]
    Task StartPlanningAsync(IProgress<PlanningProgress>? progress = null, CancellationToken ct = default);

    #endregion

    #region Этап 3: Анализ

    /// <summary>
    /// Получить сформированную корзину для магазина
    /// </summary>
    PlannedBasket? GetBasket(string store);

    /// <summary>
    /// Получить все сформированные корзины
    /// </summary>
    Dictionary<string, PlannedBasket> GetAllBaskets();

    #endregion

    #region Этап 4: Оформление

    /// <summary>
    /// Оформить заказ в выбранном магазине
    /// </summary>
    /// <param name="webViewContext">Контекст WebView2 для парсера</param>
    /// <param name="store">ID магазина</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>URL корзины в магазине</returns>
    Task<string?> CreateCartAsync(
        IWebViewContext webViewContext,
        string store,
        CancellationToken ct = default);

    /// <summary>
    /// Оформить заказ (без WebView - выбросит исключение)
    /// </summary>
    [Obsolete("Use CreateCartAsync(IWebViewContext, string, CancellationToken) instead")]
    Task<string?> CreateCartAsync(string store, CancellationToken ct = default);

    #endregion
}

/// <summary>
/// Прогресс планирования (поиска товаров)
/// </summary>
public class PlanningProgress
{
    /// <summary>
    /// ID магазина
    /// </summary>
    public string Store { get; set; } = "";

    /// <summary>
    /// Название магазина
    /// </summary>
    public string StoreName { get; set; } = "";

    /// <summary>
    /// Название текущего товара
    /// </summary>
    public string ItemName { get; set; } = "";

    /// <summary>
    /// Номер текущего товара (1-based)
    /// </summary>
    public int CurrentItem { get; set; }

    /// <summary>
    /// Общее количество товаров
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Номер текущего магазина (1-based)
    /// </summary>
    public int CurrentStore { get; set; }

    /// <summary>
    /// Общее количество магазинов
    /// </summary>
    public int TotalStores { get; set; }

    /// <summary>
    /// Статус поиска текущего товара
    /// </summary>
    public PlanningStatus Status { get; set; }

    /// <summary>
    /// Товар найден
    /// </summary>
    public bool ItemFound => Status == PlanningStatus.Found;

    /// <summary>
    /// Название найденного товара в магазине
    /// </summary>
    public string? MatchedProduct { get; set; }

    /// <summary>
    /// Цена найденного товара
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Сообщение об ошибке (если Status == Error)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Общий прогресс (количество обработанных позиций)
    /// </summary>
    public int TotalProgress => (CurrentStore - 1) * TotalItems + CurrentItem;

    /// <summary>
    /// Общее количество операций
    /// </summary>
    public int TotalOperations => TotalStores * TotalItems;

    /// <summary>
    /// Процент выполнения (0-100)
    /// </summary>
    public double ProgressPercent => TotalOperations > 0 ? (double)TotalProgress / TotalOperations * 100 : 0;
}

/// <summary>
/// Статус поиска товара
/// </summary>
public enum PlanningStatus
{
    /// <summary>
    /// Ожидает поиска
    /// </summary>
    Pending,

    /// <summary>
    /// Идёт поиск
    /// </summary>
    Searching,

    /// <summary>
    /// Товар найден
    /// </summary>
    Found,

    /// <summary>
    /// Товар не найден
    /// </summary>
    NotFound,

    /// <summary>
    /// Ошибка поиска
    /// </summary>
    Error
}
