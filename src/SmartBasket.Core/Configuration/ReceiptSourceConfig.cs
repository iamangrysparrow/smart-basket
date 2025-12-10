namespace SmartBasket.Core.Configuration;

/// <summary>
/// Конфигурация источника чеков
/// </summary>
public class ReceiptSourceConfig
{
    /// <summary>
    /// Уникальное имя источника
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Тип источника (Email, REST, FileSystem)
    /// </summary>
    public SourceType Type { get; set; } = SourceType.Email;

    /// <summary>
    /// Имя парсера для обработки данных из этого источника
    /// </summary>
    public string Parser { get; set; } = string.Empty;

    /// <summary>
    /// Включен ли источник
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Конфигурация email-источника (если Type == Email)
    /// </summary>
    public EmailSourceConfig? Email { get; set; }

    // TODO: Добавить RestSourceConfig для Type == REST
    // TODO: Добавить FileSystemSourceConfig для Type == FileSystem
}
