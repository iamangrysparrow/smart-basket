namespace SmartBasket.Core.Configuration;

public class EmailSettings
{
    public string ImapServer { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Фильтр по адресу отправителя (может быть пустым)
    /// </summary>
    public string? SenderFilter { get; set; }

    /// <summary>
    /// Фильтр по теме письма (может быть пустым)
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
