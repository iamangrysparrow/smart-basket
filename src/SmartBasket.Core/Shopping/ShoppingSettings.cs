namespace SmartBasket.Core.Shopping;

/// <summary>
/// Настройки модуля закупок из appsettings.json
/// </summary>
public class ShoppingSettings
{
    /// <summary>
    /// Настройки магазинов
    /// Key = StoreId (kuper, lavka, samokat)
    /// </summary>
    public Dictionary<string, StoreSettings> Stores { get; set; } = new();
}

/// <summary>
/// Настройки конкретного магазина
/// </summary>
public class StoreSettings
{
    /// <summary>
    /// Магазин включён
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Slug магазина для агрегаторов (Kuper: "auchan", "lenta", "5ka")
    /// </summary>
    public string? StoreSlug { get; set; }

    /// <summary>
    /// Максимум результатов поиска
    /// </summary>
    public int SearchLimit { get; set; } = 3;

    /// <summary>
    /// Приоритет магазина (меньше = выше приоритет)
    /// </summary>
    public int Priority { get; set; } = 100;
}

/// <summary>
/// Runtime конфигурация магазина (заполняется при инициализации парсера)
/// </summary>
public class StoreRuntimeConfig
{
    /// <summary>
    /// ID магазина (kuper, lavka, samokat)
    /// </summary>
    public string StoreId { get; set; } = "";

    /// <summary>
    /// Отображаемое имя магазина
    /// </summary>
    public string StoreName { get; set; } = "";

    /// <summary>
    /// Базовый URL магазина (для Kuper включает slug)
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Максимум результатов поиска
    /// </summary>
    public int SearchLimit { get; set; } = 3;

    /// <summary>
    /// Цвет бренда (для UI)
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Время доставки (строка)
    /// </summary>
    public string? DeliveryTime { get; set; }

    /// <summary>
    /// Пользователь авторизован в магазине
    /// </summary>
    public bool IsAuthorized { get; set; }

    /// <summary>
    /// Приоритет магазина
    /// </summary>
    public int Priority { get; set; } = 100;
}
