namespace SmartBasket.Core.Entities;

/// <summary>
/// Продукт - пользовательская категория товаров (например, "Молоко", "Мясо на суп")
/// </summary>
public class Product : BaseEntity
{
    public required string Name { get; set; }

    /// <summary>
    /// Единица измерения: "л", "шт", "г", "кг"
    /// </summary>
    public required string Unit { get; set; }

    /// <summary>
    /// Пороговое значение для уведомлений
    /// </summary>
    public decimal Threshold { get; set; }

    /// <summary>
    /// Средний расход в день (рассчитывается автоматически)
    /// </summary>
    public decimal? AvgDailyConsumption { get; set; }

    // Navigation properties
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<ConsumptionHistory> ConsumptionHistory { get; set; } = new List<ConsumptionHistory>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}
