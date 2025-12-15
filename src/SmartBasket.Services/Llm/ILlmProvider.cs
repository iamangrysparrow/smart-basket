namespace SmartBasket.Services.Llm;

/// <summary>
/// Сообщение чата для LLM
/// </summary>
public class LlmChatMessage
{
    /// <summary>
    /// Роль: user, assistant, system
    /// </summary>
    public string Role { get; set; } = "user";

    /// <summary>
    /// Содержимое сообщения
    /// </summary>
    public string Content { get; set; } = string.Empty;
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
    /// Чат с историей сообщений
    /// </summary>
    /// <param name="messages">История сообщений (user, assistant, system)</param>
    /// <param name="maxTokens">Максимальное количество токенов в ответе</param>
    /// <param name="temperature">Температура генерации (0.0 - 1.0)</param>
    /// <param name="progress">Отчёт о прогрессе (для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
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
