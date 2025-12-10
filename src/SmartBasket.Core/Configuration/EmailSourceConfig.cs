namespace SmartBasket.Core.Configuration;

/// <summary>
/// Конфигурация email-источника чеков
/// </summary>
public class EmailSourceConfig
{
    /// <summary>
    /// IMAP сервер
    /// </summary>
    public string ImapServer { get; set; } = string.Empty;

    /// <summary>
    /// Порт IMAP сервера
    /// </summary>
    public int ImapPort { get; set; } = 993;

    /// <summary>
    /// Использовать SSL
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Имя пользователя (email)
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Пароль (или app-password)
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Фильтр по адресу отправителя
    /// </summary>
    public string? SenderFilter { get; set; }

    /// <summary>
    /// Фильтр по теме письма
    /// </summary>
    public string? SubjectFilter { get; set; }

    /// <summary>
    /// Папка для поиска писем
    /// </summary>
    public string Folder { get; set; } = "INBOX";

    /// <summary>
    /// Количество дней для поиска писем
    /// </summary>
    public int SearchDaysBack { get; set; } = 30;
}
