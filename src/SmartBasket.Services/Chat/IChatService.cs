using SmartBasket.Core.Configuration;
using SmartBasket.Services.Llm;

namespace SmartBasket.Services.Chat;

/// <summary>
/// Результат отправки сообщения в чат
/// </summary>
public record ChatResponse(
    string Content,
    bool Success,
    string? ErrorMessage = null
);

/// <summary>
/// Сервис чата с поддержкой tool calling
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Отправить сообщение и получить ответ
    /// </summary>
    Task<ChatResponse> SendAsync(
        string userMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// История сообщений текущего диалога
    /// </summary>
    IReadOnlyList<LlmChatMessage> History { get; }

    /// <summary>
    /// Текущий ключ провайдера
    /// </summary>
    string? CurrentProviderKey { get; }

    /// <summary>
    /// Установить провайдер по ключу (например "Ollama - mistral")
    /// </summary>
    void SetProvider(string providerKey);

    /// <summary>
    /// Очистить историю диалога
    /// </summary>
    void ClearHistory();

    /// <summary>
    /// Добавить системное сообщение
    /// </summary>
    void SetSystemPrompt(string systemPrompt);

    /// <summary>
    /// Поддерживает ли текущий провайдер режим рассуждений
    /// </summary>
    bool SupportsReasoning { get; }

    /// <summary>
    /// Текущий режим рассуждений
    /// </summary>
    ReasoningMode CurrentReasoningMode { get; }

    /// <summary>
    /// Текущий уровень рассуждений
    /// </summary>
    ReasoningEffort CurrentReasoningEffort { get; }

    /// <summary>
    /// Установить параметры режима рассуждений для текущего провайдера
    /// </summary>
    /// <param name="mode">Режим рассуждений (null = использовать из конфигурации)</param>
    /// <param name="effort">Уровень рассуждений (null = использовать из конфигурации)</param>
    void SetReasoningParameters(ReasoningMode? mode, ReasoningEffort? effort);
}
