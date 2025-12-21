namespace SmartBasket.Core.Shopping;

/// <summary>
/// Сессия формирования закупочной корзины.
/// Содержит все данные текущего процесса: от черновика до оформления.
/// </summary>
public class ShoppingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ShoppingSessionState State { get; set; } = ShoppingSessionState.Drafting;

    /// <summary>
    /// ID conversation в YandexAgent (Responses API)
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Черновик списка покупок (этап 1)
    /// </summary>
    public List<DraftItem> DraftItems { get; set; } = new();

    /// <summary>
    /// Результаты поиска по магазинам (этап 2)
    /// Key = StoreId (например, "kuper-auchan")
    /// </summary>
    public Dictionary<string, StoreSearchResult> StoreResults { get; set; } = new();

    /// <summary>
    /// Сформированные корзины по магазинам (этап 3)
    /// Key = StoreId
    /// </summary>
    public Dictionary<string, PlannedBasket> PlannedBaskets { get; set; } = new();

    /// <summary>
    /// Выбранный магазин для оформления
    /// </summary>
    public string? SelectedStore { get; set; }

    /// <summary>
    /// URL корзины в магазине после оформления
    /// </summary>
    public string? CheckoutUrl { get; set; }
}

/// <summary>
/// Состояние сессии закупок
/// </summary>
public enum ShoppingSessionState
{
    /// <summary>
    /// Формирование черновика списка (чат с AI)
    /// </summary>
    Drafting,

    /// <summary>
    /// Поиск товаров в магазинах (WebView)
    /// </summary>
    Planning,

    /// <summary>
    /// Анализ и сравнение корзин
    /// </summary>
    Analyzing,

    /// <summary>
    /// Финализация — добавление в корзину магазина
    /// </summary>
    Finalizing,

    /// <summary>
    /// Завершено
    /// </summary>
    Completed
}
