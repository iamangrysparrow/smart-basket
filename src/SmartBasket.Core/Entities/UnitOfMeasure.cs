namespace SmartBasket.Core.Entities;

/// <summary>
/// Единица измерения (справочник)
/// </summary>
public class UnitOfMeasure
{
    /// <summary>
    /// Код ЕИ: г, кг, мл, л, шт, мм, см, м, см², м²
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Полное название: грамм, килограмм, миллилитр и т.д.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// FK → базовая ЕИ (кг, л, шт, м, м²)
    /// </summary>
    public required string BaseUnitId { get; set; }

    /// <summary>
    /// Коэффициент пересчёта к базовой ЕИ (0.001 для г→кг, 1 для базовых)
    /// </summary>
    public decimal Coefficient { get; set; } = 1;

    /// <summary>
    /// Признак базовой ЕИ (кг, л, шт, м, м²)
    /// </summary>
    public bool IsBase { get; set; }

    // Navigation
    public UnitOfMeasure? BaseUnit { get; set; }
}
