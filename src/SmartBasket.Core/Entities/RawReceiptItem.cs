namespace SmartBasket.Core.Entities;

/// <summary>
/// Распознанный товар из чека до категоризации
/// </summary>
public class RawReceiptItem : BaseEntity
{
    public Guid ReceiptId { get; set; }

    /// <summary>
    /// Сырое название товара из чека
    /// </summary>
    public required string RawName { get; set; }

    /// <summary>
    /// Сырой объем/вес из чека
    /// </summary>
    public string? RawVolume { get; set; }

    /// <summary>
    /// Сырая цена из чека
    /// </summary>
    public string? RawPrice { get; set; }

    /// <summary>
    /// Количество
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Единица измерения (шт, кг, л, г, мл)
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Статус категоризации
    /// </summary>
    public CategorizationStatus CategorizationStatus { get; set; } = CategorizationStatus.Pending;

    /// <summary>
    /// ID товарной позиции после категоризации
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// JSON с предложениями от Ollama
    /// </summary>
    public string? OllamaSuggestions { get; set; }

    // Navigation properties
    public Receipt? Receipt { get; set; }
    public Item? Item { get; set; }
}

public enum CategorizationStatus
{
    Pending,
    Suggested,
    Confirmed,
    Skipped
}
