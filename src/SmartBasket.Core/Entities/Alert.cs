namespace SmartBasket.Core.Entities;

/// <summary>
/// Уведомления о необходимости пополнения
/// </summary>
public class Alert : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>
    /// Статус: Alert, Acknowledged, Resolved
    /// </summary>
    public AlertStatus Status { get; set; } = AlertStatus.Alert;

    public string? Message { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    // Navigation properties
    public Product? Product { get; set; }
}

public enum AlertStatus
{
    Alert,
    Acknowledged,
    Resolved
}
