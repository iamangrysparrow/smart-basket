namespace SmartBasket.Core.Entities;

/// <summary>
/// Товарная позиция - конкретная строка в чеке
/// </summary>
public class ReceiptItem : BaseEntity
{
    /// <summary>
    /// Ссылка на товар из справочника
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Ссылка на чек
    /// </summary>
    public Guid ReceiptId { get; set; }

    /// <summary>
    /// Количество в чеке
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Единица измерения количества в чеке (шт, кг, л)
    /// </summary>
    public string QuantityUnitId { get; set; } = "шт";

    /// <summary>
    /// Количество в базовой ЕИ продукта (пересчёт)
    /// Например: 6 шт огурцов по 180г = 1.08 кг
    /// </summary>
    public decimal BaseUnitQuantity { get; set; }

    /// <summary>
    /// Цена за единицу
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Сумма (количество × цена)
    /// </summary>
    public decimal? Amount { get; set; }

    // Navigation properties
    public Item? Item { get; set; }
    public Receipt? Receipt { get; set; }
    public UnitOfMeasure? QuantityUnit { get; set; }
}
