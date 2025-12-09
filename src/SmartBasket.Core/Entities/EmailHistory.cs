namespace SmartBasket.Core.Entities;

/// <summary>
/// История обработанных писем для дедупликации
/// </summary>
public class EmailHistory : BaseEntity
{
    /// <summary>
    /// Уникальный идентификатор письма
    /// </summary>
    public required string EmailId { get; set; }

    public string? Sender { get; set; }

    public string? Subject { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Статус: processed, failed, skipped
    /// </summary>
    public EmailProcessingStatus Status { get; set; } = EmailProcessingStatus.Processed;

    /// <summary>
    /// Сообщение об ошибке (если есть)
    /// </summary>
    public string? ErrorMessage { get; set; }
}

public enum EmailProcessingStatus
{
    Processed,
    Failed,
    Skipped
}
