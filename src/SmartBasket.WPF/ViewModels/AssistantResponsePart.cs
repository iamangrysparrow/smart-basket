using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// Тип части ответа ассистента
/// </summary>
public enum ResponsePartType
{
    /// <summary>
    /// Рассуждение (мысли модели до/между tool calls)
    /// </summary>
    Thinking,

    /// <summary>
    /// Вызов инструмента
    /// </summary>
    ToolCall,

    /// <summary>
    /// Финальный ответ
    /// </summary>
    FinalAnswer
}

/// <summary>
/// Часть ответа ассистента (рассуждение, вызов инструмента, или финальный ответ)
/// </summary>
public partial class AssistantResponsePart : ObservableObject
{
    /// <summary>
    /// Тип части ответа
    /// </summary>
    public ResponsePartType Type { get; init; }

    /// <summary>
    /// Текст (для Thinking и FinalAnswer)
    /// </summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>
    /// Название инструмента (для ToolCall)
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Аргументы инструмента в JSON (для ToolCall)
    /// </summary>
    public string? ToolArgs { get; init; }

    /// <summary>
    /// Результат выполнения инструмента (для ToolCall)
    /// </summary>
    [ObservableProperty]
    private string? _toolResult;

    /// <summary>
    /// Успешно ли выполнен инструмент (для ToolCall)
    /// </summary>
    [ObservableProperty]
    private bool? _toolSuccess;

    /// <summary>
    /// Развёрнута ли часть в UI
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Для UI - является ли эта часть Thinking
    /// </summary>
    public bool IsThinking => Type == ResponsePartType.Thinking;

    /// <summary>
    /// Для UI - является ли эта часть ToolCall
    /// </summary>
    public bool IsToolCall => Type == ResponsePartType.ToolCall;

    /// <summary>
    /// Для UI - является ли эта часть FinalAnswer
    /// </summary>
    public bool IsFinalAnswer => Type == ResponsePartType.FinalAnswer;
}
