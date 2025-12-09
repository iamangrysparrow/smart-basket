namespace SmartBasket.Core.Entities;

/// <summary>
/// Продукт - группа товаров, генерируется AI (например, "Молоко", "Свинина", "Гречка")
/// Поддерживает иерархию через ParentId
/// </summary>
public class Product : BaseEntity
{
    /// <summary>
    /// Ссылка на родительский продукт для иерархии
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Название продукта (генерируется AI, можно переименовать)
    /// </summary>
    public required string Name { get; set; }

    // Navigation properties
    public Product? Parent { get; set; }
    public ICollection<Product> Children { get; set; } = new List<Product>();
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<ProductLabel> ProductLabels { get; set; } = new List<ProductLabel>();
}
