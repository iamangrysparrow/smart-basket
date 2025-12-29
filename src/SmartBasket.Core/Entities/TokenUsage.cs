namespace SmartBasket.Core.Entities;

/// <summary>
/// Статистика использования токенов AI провайдерами.
/// </summary>
public class TokenUsage
{
    public int Id { get; set; }

    /// <summary>
    /// Название провайдера (GigaChat, YandexGPT, Ollama)
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Используемая модель (GigaChat-Pro, yandexgpt-lite, llama3.2:3b)
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Дата и время запроса (UTC)
    /// </summary>
    public DateTime DateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Уникальный ID запроса
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// ID сессии (для группировки запросов в рамках одного диалога)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Название AI функции (Chat, Classification, Labels, Parsing, Shopping, ShoppingChat)
    /// </summary>
    public string AiFunction { get; set; } = string.Empty;

    /// <summary>
    /// Токены промпта (входные)
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// Токены ответа (выходные)
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// Кэшированные токены промпта (GigaChat)
    /// </summary>
    public int? PrecachedPromptTokens { get; set; }

    /// <summary>
    /// Токены на размышление (YandexGPT reasoning)
    /// </summary>
    public int? ReasoningTokens { get; set; }

    /// <summary>
    /// Итоговое количество токенов для оплаты
    /// </summary>
    public int TotalTokens { get; set; }
}
