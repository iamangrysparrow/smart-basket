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
    /// Исходный HTML для переиспользования
    /// </summary>
    public string? RawContent { get; set; }

    /// <summary>
    /// Итоговая сумма чека
    /// </summary>
    public decimal? Total { get; set; }

    /// <summary>
    /// Статус обработки: raw, parsed, categorized, archived
    /// </summary>
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Raw;

    // Navigation properties
    public ICollection<Good> Goods { get; set; } = new List<Good>();
    public ICollection<RawReceiptItem> RawItems { get; set; } = new List<RawReceiptItem>();
}

public enum ReceiptStatus
{
    Raw,
    Parsed,
    Categorized,
    Archived,
    Error
}
