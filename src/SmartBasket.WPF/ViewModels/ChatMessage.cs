using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// Сообщение в чате (пользователя или ассистента)
/// </summary>
public partial class ChatMessage : ObservableObject
{
    /// <summary>
    /// Это сообщение пользователя?
    /// </summary>
    public bool IsUser { get; init; }

    /// <summary>
    /// Текст сообщения пользователя
    /// </summary>
    public string? UserText { get; init; }

    /// <summary>
    /// Время создания сообщения
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>
    /// Части ответа ассистента (для IsUser=false)
    /// </summary>
    public ObservableCollection<AssistantResponsePart>? Parts { get; init; }

    /// <summary>
    /// Это системное сообщение (ошибка)?
    /// </summary>
    public bool IsSystem { get; init; }

    /// <summary>
    /// Текст системного сообщения
    /// </summary>
    public string? SystemText { get; init; }

    /// <summary>
    /// Для UI - является ли сообщение от ассистента
    /// </summary>
    public bool IsAssistant => !IsUser && !IsSystem;
}
