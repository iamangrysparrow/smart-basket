using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace SmartBasket.WPF.Models;

/// <summary>
/// Уровни логирования
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// Запись лога с уровнем и временной меткой.
/// Поддерживает INotifyPropertyChanged для streaming обновлений.
/// PropertyChanged вызывается через Dispatcher для корректной работы из background threads.
/// </summary>
public class LogEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }

    /// <summary>
    /// Источник сообщения (например "Ollama Chat", "AI Chat", "ChatService")
    /// </summary>
    public string Source { get; init; } = string.Empty;

    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        set
        {
            if (_message != value)
            {
                _message = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedMessage));
                OnPropertyChanged(nameof(FullMessage));
            }
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler == null) return;

        // Вызываем PropertyChanged через UI Dispatcher для корректной работы из background threads
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => handler(this, new PropertyChangedEventArgs(propertyName)));
        }
        else
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Форматированная строка для отображения
    /// </summary>
    public string FormattedMessage => $"[{Timestamp:HH:mm:ss}] {Message}";

    /// <summary>
    /// Полная строка с уровнем для экспорта
    /// </summary>
    public string FullMessage => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";

    /// <summary>
    /// Создать запись из строки, автоматически определяя уровень
    /// </summary>
    public static LogEntry FromMessage(string message, LogLevel? level = null)
    {
        var detectedLevel = level ?? DetectLevel(message);
        return new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = detectedLevel,
            Message = message
        };
    }

    /// <summary>
    /// Определить уровень по содержимому сообщения
    /// </summary>
    private static LogLevel DetectLevel(string message)
    {
        if (string.IsNullOrEmpty(message))
            return LogLevel.Debug;

        var upper = message.ToUpperInvariant();

        if (upper.Contains("ERROR") || upper.Contains("EXCEPTION") || upper.Contains("FAIL"))
            return LogLevel.Error;

        if (upper.Contains("WARN"))
            return LogLevel.Warning;

        if (upper.Contains("SUCCESS") || upper.Contains("COMPLETE") || upper.Contains("SAVED") ||
            upper.Contains("CONNECTED") || upper.Contains("FOUND") || upper.Contains("PARSED"))
            return LogLevel.Info;

        return LogLevel.Debug;
    }
}
