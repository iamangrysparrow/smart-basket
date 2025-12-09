namespace SmartBasket.Services.Email;

public class EmailMessage
{
    public required string MessageId { get; set; }
    public string? From { get; set; }
    public string? Subject { get; set; }
    public DateTime Date { get; set; }
    public string? TextBody { get; set; }
    public string? HtmlBody { get; set; }

    /// <summary>
    /// Возвращает тело письма (предпочтительно HTML, если есть)
    /// </summary>
    public string? Body => HtmlBody ?? TextBody;
}
