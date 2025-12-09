namespace SmartBasket.Core.Entities;

/// <summary>
/// Метка - пользовательская категория для группировки товаров и продуктов
/// Примеры: "Молоко для кофе", "Мясо для пельменей", "Папа доволен", "Чистый дом"
/// </summary>
public class Label : BaseEntity
{
    /// <summary>
    /// Название метки
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Цвет метки в HEX формате для UI (например, "#FF5733")
    /// </summary>
    public string? Color { get; set; }

    // Navigation properties
    public ICollection<ProductLabel> ProductLabels { get; set; } = new List<ProductLabel>();
    public ICollection<ItemLabel> ItemLabels { get; set; } = new List<ItemLabel>();
}
