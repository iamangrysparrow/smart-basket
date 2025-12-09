namespace SmartBasket.Core.Entities;

/// <summary>
/// Товар - уникальное название товара из чека (справочник)
/// Например: "Молоко Parmalat 2.5% 1л", "Свинина шейка охл. кг"
/// </summary>
public class Item : BaseEntity
{
    /// <summary>
    /// Ссылка на продукт (обязательная)
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Название товара из чека
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Единица измерения: "шт", "кг", "л", "г", "мл"
    /// </summary>
    public string? UnitOfMeasure { get; set; }

    /// <summary>
    /// Количество в единице (например, 0.5 для "500мл", 1 для "1л")
    /// </summary>
    public decimal? UnitQuantity { get; set; }

    /// <summary>
    /// Магазин, из которого товар впервые появился (для фильтрации)
    /// </summary>
    public string? Shop { get; set; }

    // Navigation properties
    public Product? Product { get; set; }
    public ICollection<ReceiptItem> ReceiptItems { get; set; } = new List<ReceiptItem>();
    public ICollection<ItemLabel> ItemLabels { get; set; } = new List<ItemLabel>();
}
