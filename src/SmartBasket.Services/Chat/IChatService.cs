using SmartBasket.Core.Configuration;
using SmartBasket.Services.Llm;

namespace SmartBasket.Services.Chat;

/// <summary>
/// Thread-safe progress reporter that does NOT capture SynchronizationContext.
/// Uses Action directly instead of posting to captured context.
/// ВАЖНО: используйте этот класс вместо Progress{T} в async методах,
/// чтобы избежать блокировки UI при ожидании await.
/// </summary>
public class ThreadSafeProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public ThreadSafeProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value)
    {
        _handler(value);
    }
}

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
        IProgress<ChatProgress>? progress = null,
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

    /// <summary>
    /// Принудительно передавать инструменты в системном промпте вместо native tool calling.
    /// Полезно для моделей которые плохо работают с native tools (YandexGPT).
    /// </summary>
    bool ForcePromptInjection { get; set; }
}
