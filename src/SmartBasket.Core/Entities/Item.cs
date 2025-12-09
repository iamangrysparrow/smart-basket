namespace SmartBasket.Core.Entities;

/// <summary>
/// Товарная позиция - конкретный вариант продукта (например, "Молоко 3.2%", "Молоко 1.5%")
/// </summary>
public class Item : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>
    /// Название товара из чека
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Коэффициент для приведения к единицам продукта
    /// Например, если продукт в литрах, а товар 500мл, то unit_ratio = 0.5
    /// </summary>
    public decimal UnitRatio { get; set; } = 1;

    /// <summary>
    /// Магазин-источник
    /// </summary>
    public string? Shop { get; set; }

    // Navigation properties
    public Product? Product { get; set; }
    public ICollection<Good> Goods { get; set; } = new List<Good>();
}
