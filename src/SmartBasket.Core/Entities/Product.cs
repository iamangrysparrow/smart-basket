namespace SmartBasket.Core.Entities;

/// <summary>
/// Продукт - группа товаров, генерируется AI (например, "Молоко", "Свинина", "Гречка")
/// Плоский справочник со ссылкой на категорию.
/// </summary>
public class Product : BaseEntity
{
    /// <summary>
    /// Название продукта (генерируется AI, можно переименовать)
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Ссылка на категорию продукта (опционально)
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Базовая единица измерения продукта (кг, л, шт, м, м²)
    /// </summary>
    public string BaseUnitId { get; set; } = "шт";

    // Navigation properties
    public ProductCategory? Category { get; set; }
    public UnitOfMeasure? BaseUnit { get; set; }
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<ProductLabel> ProductLabels { get; set; } = new List<ProductLabel>();
}
