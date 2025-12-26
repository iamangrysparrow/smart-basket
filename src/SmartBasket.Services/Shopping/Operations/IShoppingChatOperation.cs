using SmartBasket.Core.Shopping;

namespace SmartBasket.Services.Shopping.Operations;

/// <summary>
/// Интерфейс операции чата для модуля закупок.
/// Инкапсулирует всю логику AI: промпты, tool calling, парсинг.
/// ViewModel не знает деталей реализации — только получает события.
/// </summary>
public interface IShoppingChatOperation
{
    /// <summary>
    /// Проверить доступность AI провайдера
    /// </summary>
    Task<(bool IsAvailable, string Message)> CheckAvailabilityAsync(CancellationToken ct = default);

    /// <summary>
    /// Начать новую conversation
    /// </summary>
    Task<string> StartConversationAsync(ShoppingSession session, CancellationToken ct = default);

    /// <summary>
    /// Обработать сообщение пользователя.
    /// Возвращает поток событий (streaming текст, tool calls, результаты).
    /// </summary>
    IAsyncEnumerable<WorkflowProgress> ProcessMessageAsync(
        string message,
        CancellationToken ct = default);

    /// <summary>
    /// Отправить инициализирующий промпт (приветствие + анализ чеков)
    /// </summary>
    IAsyncEnumerable<WorkflowProgress> SendInitialPromptAsync(CancellationToken ct = default);

    /// <summary>
    /// Сбросить conversation
    /// </summary>
    void Reset();

    /// <summary>
    /// ID текущей conversation
    /// </summary>
    string? ConversationId { get; }

    /// <summary>
    /// Текущая сессия
    /// </summary>
    ShoppingSession? CurrentSession { get; }
}
