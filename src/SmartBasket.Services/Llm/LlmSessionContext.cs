namespace SmartBasket.Services.Llm;

/// <summary>
/// Контекст сессии для LLM провайдеров.
/// Позволяет провайдерам использовать механизмы кэширования токенов:
/// - GigaChat: X-Session-ID header
/// - YandexAgent: previous_response_id для stateful API
/// </summary>
public class LlmSessionContext
{
    /// <summary>
    /// Идентификатор сессии (UUID v4).
    /// Используется GigaChat для кэширования токенов через X-Session-ID header.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// ID предыдущего ответа (для YandexAgent stateful API).
    /// При указании YandexAgent продолжит диалог с предыдущего места.
    /// </summary>
    public string? PreviousResponseId { get; set; }

    /// <summary>
    /// Тип операции для логирования и отладки.
    /// Например: "receipt-collection", "classification", "chat"
    /// </summary>
    public string? OperationType { get; set; }

    /// <summary>
    /// Дополнительные заголовки для HTTP запросов.
    /// Позволяет провайдерам добавлять кастомные заголовки.
    /// </summary>
    public Dictionary<string, string>? CustomHeaders { get; set; }

    /// <summary>
    /// Создать пустой контекст (без сессии)
    /// </summary>
    public static LlmSessionContext Empty => new();

    /// <summary>
    /// Создать новый контекст с уникальным SessionId.
    /// Формат: {operationType}-{yyyyMMdd-HHmmss}-{shortGuid}
    /// Например: receipt-collection-20251229-143022-a1b2c3d4
    /// </summary>
    public static LlmSessionContext Create(string? operationType = null)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var shortGuid = Guid.NewGuid().ToString("N")[..8]; // первые 8 символов
        var prefix = string.IsNullOrEmpty(operationType) ? "session" : operationType;

        return new LlmSessionContext
        {
            SessionId = $"{prefix}-{timestamp}-{shortGuid}",
            OperationType = operationType
        };
    }

    /// <summary>
    /// Создать копию контекста с обновлённым PreviousResponseId
    /// </summary>
    public LlmSessionContext WithPreviousResponseId(string? responseId)
    {
        return new LlmSessionContext
        {
            SessionId = SessionId,
            PreviousResponseId = responseId,
            OperationType = OperationType,
            CustomHeaders = CustomHeaders != null
                ? new Dictionary<string, string>(CustomHeaders)
                : null
        };
    }
}
