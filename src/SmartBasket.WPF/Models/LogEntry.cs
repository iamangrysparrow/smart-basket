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
/// Запись лога с уровнем и временной меткой
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;

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
