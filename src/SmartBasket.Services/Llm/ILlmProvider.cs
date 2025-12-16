using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Вызов инструмента от LLM
/// </summary>
public class LlmToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";  // JSON
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
    /// <param name="maxTokens">Максимальное количество токенов в ответе</param>
    /// <param name="temperature">Температура генерации (0.0 - 1.0)</param>
    /// <param name="progress">Отчёт о прогрессе (для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        int maxTokens = 2000,
        double temperature = 0.1,
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
    /// <param name="maxTokens">Максимальное количество токенов в ответе</param>
    /// <param name="temperature">Температура генерации (0.0 - 1.0)</param>
    /// <param name="progress">Отчёт о прогрессе (для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        int maxTokens = 2000,
        double temperature = 0.7,
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
