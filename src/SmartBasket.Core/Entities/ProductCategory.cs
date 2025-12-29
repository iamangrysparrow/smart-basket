namespace SmartBasket.Core.Entities;

/// <summary>
/// Категория продуктов - иерархический справочник.
/// Например: "Молочные продукты" -> "Молоко", "Сыры"
/// </summary>
public class ProductCategory : BaseEntity
{
    /// <summary>
    /// Название категории
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Номер категории из файла product_categories.txt.
    /// Используется для маппинга при классификации LLM.
    /// </summary>
    public int? Number { get; set; }

    /// <summary>
    /// Ссылка на родительскую категорию для иерархии
    /// </summary>
    public Guid? ParentId { get; set; }

    // Navigation properties
    public ProductCategory? Parent { get; set; }
    public ICollection<ProductCategory> Children { get; set; } = new List<ProductCategory>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
