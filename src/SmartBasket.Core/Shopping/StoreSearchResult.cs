namespace SmartBasket.Core.Shopping;

/// <summary>
/// Результаты поиска всех товаров в одном магазине (этап 2).
/// </summary>
public class StoreSearchResult
{
    /// <summary>
    /// ID магазина (например, "kuper-auchan")
    /// </summary>
    public string Store { get; set; } = "";

    /// <summary>
    /// Отображаемое имя магазина
    /// </summary>
    public string StoreName { get; set; } = "";

    /// <summary>
    /// Результаты поиска по каждому товару из черновика.
    /// Key = DraftItem.Id
    /// Value = список найденных товаров (ProductMatch)
    /// </summary>
    public Dictionary<Guid, List<ProductMatch>> ItemMatches { get; set; } = new();

    /// <summary>
    /// Поиск завершён
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Количество найденных товаров (хотя бы один ProductMatch в наличии)
    /// </summary>
    public int FoundCount { get; set; }

    /// <summary>
    /// Общее количество товаров в черновике
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Процент найденных товаров
    /// </summary>
    public double FoundPercent => TotalCount > 0 ? (double)FoundCount / TotalCount * 100 : 0;
}
