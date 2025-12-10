namespace SmartBasket.Services.Sources;

/// <summary>
/// Сырые данные чека из источника (до парсинга)
/// </summary>
/// <param name="Content">Сырые данные (HTML, JSON, текст)</param>
/// <param name="ContentType">Тип контента: "text/html", "application/json", "text/plain"</param>
/// <param name="Date">Дата получения</param>
/// <param name="ExternalId">Внешний идентификатор для дедупликации (например, email message-id)</param>
public record RawReceipt(
    string Content,
    string ContentType,
    DateTime Date,
    string? ExternalId = null
);
