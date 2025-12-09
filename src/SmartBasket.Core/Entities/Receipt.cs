namespace SmartBasket.Core.Entities;

/// <summary>
/// Чек из магазина
/// </summary>
public class Receipt : BaseEntity
{
    /// <summary>
    /// Номер чека
    /// </summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>
    /// Название магазина
    /// </summary>
    public required string Shop { get; set; }

    /// <summary>
    /// Дата чека
    /// </summary>
    public DateTime ReceiptDate { get; set; }

    /// <summary>
    /// ID письма для дедупликации
    /// </summary>
    public string? EmailId { get; set; }

    /// <summary>
    /// Итоговая сумма чека
    /// </summary>
    public decimal? Total { get; set; }

    /// <summary>
    /// Статус обработки
    /// </summary>
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Parsed;

    // Navigation properties
    public ICollection<ReceiptItem> Items { get; set; } = new List<ReceiptItem>();
}

public enum ReceiptStatus
{
    Parsed,
    Archived,
    Error
}
