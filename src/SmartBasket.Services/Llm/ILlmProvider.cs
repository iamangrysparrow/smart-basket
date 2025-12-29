using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Статистика использования токенов от LLM провайдера
/// </summary>
public record LlmTokenUsage(
    /// <summary>Токены промпта (входные)</summary>
    int PromptTokens,
    /// <summary>Токены ответа (выходные)</summary>
    int CompletionTokens,
    /// <summary>Кэшированные токены промпта (GigaChat)</summary>
    int? PrecachedPromptTokens,
    /// <summary>Токены на размышление (YandexGPT reasoning)</summary>
    int? ReasoningTokens,
    /// <summary>Итоговое количество токенов</summary>
    int TotalTokens
);

/// <summary>
/// Вызов инструмента от LLM
/// </summary>
public class LlmToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";  // JSON

    /// <summary>
    /// True если tool call был распарсен из текста модели (не native tool calling).
    /// Это важно для форматирования ответа - модели без native tools ожидают ответ как user message.
    /// </summary>
    public bool IsParsedFromText { get; set; }
}

/// <summary>
/// Сообщение чата для LLM
/// </summary>
public class LlmChatMessage
{
    /// <summary>
    /// Роль: user, assistant, system, tool
    /// </summary>
    public string Role { get; set; } = "user";

    /// <summary>
    /// Содержимое сообщения
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Вызовы инструментов (для assistant сообщений)
    /// </summary>
    public List<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// ID вызова инструмента (для role="tool")
    /// </summary>
    public string? ToolCallId { get; set; }

    /// <summary>
    /// True если tool result содержит ошибку (для role="tool").
    /// Используется для формирования правильной инструкции модели:
    /// - При ошибке: "Исправь вызов инструмента"
    /// - При успехе: "Ответь на вопрос пользователя"
    /// </summary>
    public bool IsToolError { get; set; }
}

/// <summary>
/// Результат генерации текста LLM
/// </summary>
public class LlmGenerationResult
{
    public bool IsSuccess { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// ID ответа (для провайдеров с stateful API, например YandexAgent)
    /// </summary>
    public string? ResponseId { get; set; }

    /// <summary>
    /// Вызовы инструментов (если LLM решил вызвать инструменты)
    /// </summary>
    public List<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Есть ли вызовы инструментов в ответе
    /// </summary>
    public bool HasToolCalls => ToolCalls?.Count > 0;

    /// <summary>
    /// Статистика использования токенов
    /// </summary>
    public LlmTokenUsage? Usage { get; set; }
}

/// <summary>
/// Общий интерфейс для LLM провайдеров (Ollama, YandexGPT, etc.)
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Название провайдера для отображения
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Проверить подключение к LLM
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сгенерировать текст по prompt
    /// </summary>
    /// <param name="prompt">Prompt для генерации</param>
    /// <param name="sessionContext">Контекст сессии для кэширования токенов (опционально)</param>
    /// <param name="progress">Отчёт о прогрессе (для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Поддерживает ли провайдер native tool calling
    /// </summary>
    bool SupportsTools { get; }

    /// <summary>
    /// Чат с историей сообщений
    /// </summary>
    /// <param name="messages">История сообщений (user, assistant, system, tool)</param>
    /// <param name="tools">Доступные инструменты (опционально)</param>
    /// <param name="sessionContext">Контекст сессии для кэширования токенов (опционально)</param>
    /// <param name="progress">Отчёт о прогрессе (для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Поддерживает ли провайдер сброс истории диалога
    /// </summary>
    bool SupportsConversationReset { get; }

    /// <summary>
    /// Сбросить историю диалога (для провайдеров со stateful API)
    /// </summary>
    void ResetConversation();
}
