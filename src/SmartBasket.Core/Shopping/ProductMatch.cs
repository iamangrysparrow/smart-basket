namespace SmartBasket.Core.Shopping;

/// <summary>
/// Найденный товар в магазине (результат поиска).
/// Один DraftItem может иметь несколько ProductMatch в каждом магазине.
/// </summary>
public class ProductMatch
{
    /// <summary>
    /// ID товара в магазине (для добавления в корзину)
    /// </summary>
    public string ProductId { get; set; } = "";

    /// <summary>
    /// Название товара в магазине
    /// </summary>
    public string ProductName { get; set; } = "";

    /// <summary>
    /// Цена за единицу
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Размер упаковки (например, 1 для 1л молока)
    /// </summary>
    public decimal? PackageSize { get; set; }

    /// <summary>
    /// Единица упаковки (л, кг, шт)
    /// </summary>
    public string? PackageUnit { get; set; }

    /// <summary>
    /// В наличии
    /// </summary>
    public bool InStock { get; set; } = true;

    /// <summary>
    /// URL изображения товара
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Оценка соответствия запросу (0.0 - 1.0)
    /// </summary>
    public float MatchScore { get; set; }

    /// <summary>
    /// Выбран пользователем для корзины
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// URL страницы товара в магазине
    /// </summary>
    public string? ProductUrl { get; set; }
}
