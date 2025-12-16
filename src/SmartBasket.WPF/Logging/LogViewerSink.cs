using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Serilog.Core;
using Serilog.Events;
using SmartBasket.WPF.Models;

namespace SmartBasket.WPF.Logging;

/// <summary>
/// Serilog Sink для отправки логов в UI (ObservableCollection).
/// Логи автоматически добавляются в коллекцию, которую можно привязать к ListBox/DataGrid.
/// </summary>
public sealed class LogViewerSink : ILogEventSink
{
    private static LogViewerSink? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Синглтон экземпляр sink'а
    /// </summary>
    public static LogViewerSink Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new LogViewerSink();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Коллекция записей лога для привязки к UI
    /// </summary>
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    /// <summary>
    /// Полный лог (не обрезанный) для сохранения в файл
    /// </summary>
    public List<LogEntry> FullLog { get; } = new();

    private readonly object _logEntriesLock = new();
    private readonly object _fullLogLock = new();

    /// <summary>
    /// Максимальное количество записей в UI.
    /// Default: 10000. Настраивается через SetMaxUiEntries() из AppSettings.MaxUiLogEntries.
    /// </summary>
    private int _maxUiEntries = 10000;

    private LogViewerSink()
    {
    }

    /// <summary>
    /// Установить лимит записей в UI из настроек.
    /// Вызывать при старте приложения из App.xaml.cs.
    /// </summary>
    public void SetMaxUiEntries(int maxEntries)
    {
        _maxUiEntries = maxEntries > 0 ? maxEntries : 10000;
    }

    /// <summary>
    /// Включить синхронизацию коллекции для многопоточного доступа.
    /// Вызывать из UI потока!
    /// </summary>
    public void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(LogEntries, _logEntriesLock);
    }

    /// <summary>
    /// Получить фильтрованное представление для UI
    /// </summary>
    public ICollectionView GetFilteredView()
    {
        return CollectionViewSource.GetDefaultView(LogEntries);
    }

    /// <summary>
    /// Обработка события лога от Serilog
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        var entry = new LogEntry
        {
            Timestamp = logEvent.Timestamp.LocalDateTime,
            Level = MapLevel(logEvent.Level),
            Message = logEvent.RenderMessage()
        };

        // Добавляем в полный лог (всегда)
        lock (_fullLogLock)
        {
            FullLog.Add(entry);
        }

        // Добавляем в UI коллекцию (с лимитом)
        lock (_logEntriesLock)
        {
            LogEntries.Add(entry);

            // Ограничиваем количество записей в UI для производительности
            while (LogEntries.Count > _maxUiEntries)
            {
                LogEntries.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Очистить лог
    /// </summary>
    public void Clear()
    {
        lock (_fullLogLock)
        {
            FullLog.Clear();
        }
        lock (_logEntriesLock)
        {
            LogEntries.Clear();
        }
    }

    /// <summary>
    /// Получить все записи в виде строк для сохранения
    /// </summary>
    public string[] GetFullLogLines()
    {
        lock (_fullLogLock)
        {
            return FullLog.Select(e => e.FullMessage).ToArray();
        }
    }

    /// <summary>
    /// Получить видимые записи для копирования
    /// </summary>
    public string[] GetVisibleLogLines(Func<LogEntry, bool>? filter = null)
    {
        lock (_logEntriesLock)
        {
            var entries = filter != null
                ? LogEntries.Where(filter)
                : LogEntries;

            return entries.Select(e => e.FormattedMessage).ToArray();
        }
    }

    private static Models.LogLevel MapLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => Models.LogLevel.Debug,
            LogEventLevel.Debug => Models.LogLevel.Debug,
            LogEventLevel.Information => Models.LogLevel.Info,
            LogEventLevel.Warning => Models.LogLevel.Warning,
            LogEventLevel.Error => Models.LogLevel.Error,
            LogEventLevel.Fatal => Models.LogLevel.Error,
            _ => Models.LogLevel.Debug
        };
    }
}

/// <summary>
/// Extension методы для настройки LogViewerSink
/// </summary>
public static class LogViewerSinkExtensions
{
    public static Serilog.LoggerConfiguration LogViewer(
        this Serilog.Configuration.LoggerSinkConfiguration sinkConfiguration,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Debug)
    {
        return sinkConfiguration.Sink(LogViewerSink.Instance, restrictedToMinimumLevel);
    }
}
