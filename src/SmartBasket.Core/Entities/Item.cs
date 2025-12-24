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
    /// Единица измерения товара (г, кг, мл, л, шт)
    /// </summary>
    public string UnitId { get; set; } = "шт";

    /// <summary>
    /// Количество в единице измерения (700 для "700 г", 930 для "930 мл")
    /// </summary>
    public decimal UnitQuantity { get; set; } = 1;

    /// <summary>
    /// Количество в базовой ЕИ продукта (0.7 для 700г→кг, 0.93 для 930мл→л)
    /// </summary>
    public decimal BaseUnitQuantity { get; set; } = 1;

    /// <summary>
    /// Магазин, из которого товар впервые появился (для фильтрации)
    /// </summary>
    public string? Shop { get; set; }

    // Navigation properties
    public Product? Product { get; set; }
    public UnitOfMeasure? Unit { get; set; }
    public ICollection<ReceiptItem> ReceiptItems { get; set; } = new List<ReceiptItem>();
    public ICollection<ItemLabel> ItemLabels { get; set; } = new List<ItemLabel>();
}
