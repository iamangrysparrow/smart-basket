namespace SmartBasket.Services.Chat;

/// <summary>
/// Тип события прогресса чата
/// </summary>
public enum ChatProgressType
{
    /// <summary>
    /// Дельта текста от модели (streaming)
    /// </summary>
    TextDelta,

    /// <summary>
    /// Вызов инструмента
    /// </summary>
    ToolCall,

    /// <summary>
    /// Результат выполнения инструмента
    /// </summary>
    ToolResult,

    /// <summary>
    /// Ответ завершён
    /// </summary>
    Complete
}

/// <summary>
/// Типизированное событие прогресса чата
/// </summary>
public sealed record ChatProgress(
    ChatProgressType Type,
    string? Text = null,
    string? ToolName = null,
    string? ToolArgs = null,
    string? ToolResult = null,
    bool? ToolSuccess = null
);
