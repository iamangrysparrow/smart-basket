using SmartBasket.Core.Shopping;
using SmartBasket.Services.Chat;

namespace SmartBasket.Services.Shopping;

/// <summary>
/// Специализированный сервис чата для модуля закупок.
/// Использует YandexAgent для stateful conversation с tool calling.
/// </summary>
public interface IShoppingChatService
{
    /// <summary>
    /// Начать новую conversation для сессии покупок
    /// </summary>
    /// <param name="session">Сессия закупок</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>ID conversation</returns>
    Task<string> StartConversationAsync(ShoppingSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отправить сообщение пользователя (streaming + tool calling)
    /// </summary>
    /// <param name="message">Текст сообщения</param>
    /// <param name="progress">Прогресс для streaming и tool calls</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Ответ чата</returns>
    Task<ChatResponse> SendAsync(
        string message,
        IProgress<ChatProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отправить инициализирующий промпт (анализ чеков и формирование списка)
    /// </summary>
    /// <param name="progress">Прогресс для streaming и tool calls</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Ответ чата</returns>
    Task<ChatResponse> SendInitialPromptAsync(
        IProgress<ChatProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ID текущей conversation (null если не начата)
    /// </summary>
    string? ConversationId { get; }

    /// <summary>
    /// Текущая сессия закупок
    /// </summary>
    ShoppingSession? CurrentSession { get; }

    /// <summary>
    /// Проверить доступность YandexAgent
    /// </summary>
    Task<(bool IsAvailable, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Очистить conversation и сбросить состояние
    /// </summary>
    void Reset();
}
