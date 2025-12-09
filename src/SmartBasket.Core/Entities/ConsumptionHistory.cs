namespace SmartBasket.Core.Entities;

/// <summary>
/// История потребления для расчета среднего расхода
/// </summary>
public class ConsumptionHistory : BaseEntity
{
    public Guid ProductId { get; set; }

    public DateTime Date { get; set; }

    /// <summary>
    /// Потребленное количество
    /// </summary>
    public decimal QuantityConsumed { get; set; }

    /// <summary>
    /// Источник данных: calculated, manual
    /// </summary>
    public string Source { get; set; } = "calculated";

    // Navigation properties
    public Product? Product { get; set; }
}
