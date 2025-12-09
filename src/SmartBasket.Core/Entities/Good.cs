namespace SmartBasket.Core.Entities;

/// <summary>
/// Товар - конкретная единица из чека (купленный экземпляр)
/// </summary>
public class Good : BaseEntity
{
    public Guid ItemId { get; set; }
    public Guid ReceiptId { get; set; }

    /// <summary>
    /// Количество (например, 2 пакета)
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Цена за единицу
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Дата покупки
    /// </summary>
    public DateTime PurchaseDate { get; set; }

    // Navigation properties
    public Item? Item { get; set; }
    public Receipt? Receipt { get; set; }
}
