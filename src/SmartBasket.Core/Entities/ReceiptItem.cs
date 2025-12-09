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
    /// Количество
    /// </summary>
    public decimal Quantity { get; set; } = 1;

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
}
