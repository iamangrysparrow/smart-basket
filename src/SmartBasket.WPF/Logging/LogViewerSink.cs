using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Windows.Data;
using Serilog.Core;
using Serilog.Events;
using SmartBasket.WPF.Models;

namespace SmartBasket.WPF.Logging;

/// <summary>
/// Serilog Sink для отправки логов в UI (ObservableCollection).
/// Логи автоматически добавляются в коллекцию, которую можно привязать к ListBox/DataGrid.
/// Поддерживает streaming агрегацию для Ollama RAW CHUNK сообщений.
/// </summary>
public sealed class LogViewerSink : ILogEventSink
{
    private static LogViewerSink? _instance;
    private static readonly object _lock = new();

    // Streaming aggregation state
    private bool _isStreaming;
    private readonly StringBuilder _streamingBuffer = new();
    private string? _streamingPrefix;
    private DateTime _streamingStartTime;
    private int _chunkCounter; // DEBUG: счётчик chunk'ов
    private LogEntry? _currentStreamingEntry; // Текущая streaming запись для обновления

    // Markers for streaming detection
    private const string StreamingStartMarker = "STREAMING RESPONSE START";
    private const string StreamingEndMarker = "STREAMING RESPONSE END";
    private const string RawChunkMarker = "RAW CHUNK: ";

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

    /// <summary>
    /// Известные источники логов для фильтрации
    /// </summary>
    private readonly HashSet<string> _knownSources = new();

    /// <summary>
    /// Получить список известных источников
    /// </summary>
    public IReadOnlyCollection<string> KnownSources => _knownSources;

    /// <summary>
    /// Событие при добавлении нового источника
    /// </summary>
    public event EventHandler? SourcesChanged;

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
        var message = logEvent.RenderMessage();
        var timestamp = logEvent.Timestamp.LocalDateTime;
        var level = MapLevel(logEvent.Level);

        // Всегда добавляем в полный лог (raw)
        var rawEntry = new LogEntry
        {
            Timestamp = timestamp,
            Level = level,
            Message = message
        };
        lock (_fullLogLock)
        {
            FullLog.Add(rawEntry);
        }

        // Обработка streaming агрегации для UI
        ProcessForUi(message, timestamp, level);
    }

    /// <summary>
    /// Обработка сообщения для UI с агрегацией streaming
    /// </summary>
    private void ProcessForUi(string message, DateTime timestamp, Models.LogLevel level)
    {
        // Проверяем начало streaming
        if (message.Contains(StreamingStartMarker))
        {
            _isStreaming = true;
            _streamingBuffer.Clear();
            _streamingStartTime = timestamp;
            _chunkCounter = 0; // DEBUG: сброс счётчика
            // Извлекаем prefix (например "[Ollama Chat]")
            var prefixEnd = message.IndexOf(StreamingStartMarker);
            _streamingPrefix = prefixEnd > 0 ? message[..prefixEnd].Trim() : null;

            // Показываем START маркер
            AddToUi(message, timestamp, level);
            return;
        }

        // Проверяем конец streaming
        if (message.Contains(StreamingEndMarker))
        {
            // Flush остаток буфера
            FlushStreamingBuffer(timestamp, level);
            _isStreaming = false;
            _streamingPrefix = null;

            // Показываем END маркер
            AddToUi(message, timestamp, level);
            return;
        }

        // Если в режиме streaming - обрабатываем RAW CHUNK
        if (_isStreaming && message.Contains(RawChunkMarker))
        {
            _chunkCounter++;

            // Извлекаем JSON напрямую — всё после "RAW CHUNK: "
            var chunkIndex = message.IndexOf(RawChunkMarker);
            if (chunkIndex >= 0)
            {
                var jsonStr = message[(chunkIndex + RawChunkMarker.Length)..].Trim();

                // Serilog форматирует строку с кавычками и экранированием: "{\"model\":...}"
                // Нужно убрать внешние кавычки и сделать unescape
                if (jsonStr.StartsWith('"') && jsonStr.EndsWith('"'))
                {
                    jsonStr = jsonStr[1..^1]; // убираем кавычки
                    jsonStr = jsonStr.Replace("\\\"", "\""); // unescape кавычек
                }

                var content = ExtractContentFromChunk(jsonStr);

                if (content != null)
                {
                    _streamingBuffer.Append(content);

                    // Обновляем streaming entry в реальном времени
                    UpdateStreamingEntry(timestamp, level);
                }
                return; // RAW CHUNK не показываем напрямую
            }
        }

        // Обычное сообщение - просто добавляем
        AddToUi(message, timestamp, level);
    }

    /// <summary>
    /// Извлечь content из JSON chunk
    /// </summary>
    private static string? ExtractContentFromChunk(string jsonStr)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonStr);

            // Ollama format: {"message":{"content":"..."}}
            if (doc.RootElement.TryGetProperty("message", out var msgElement) &&
                msgElement.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString();
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга
        }
        return null;
    }

    /// <summary>
    /// Обновить streaming entry текущим содержимым буфера.
    /// При появлении \n — завершённые строки добавляются как отдельные записи,
    /// текущая строка обновляется только незавершённой частью.
    /// </summary>
    private void UpdateStreamingEntry(DateTime timestamp, Models.LogLevel level)
    {
        var text = _streamingBuffer.ToString();

        // Проверяем есть ли завершённые строки (с \n)
        var lastNewline = text.LastIndexOf('\n');

        if (lastNewline >= 0)
        {
            // Есть завершённые строки — выводим их как отдельные записи
            var completeText = text[..lastNewline];
            var lines = completeText.Split('\n');

            lock (_logEntriesLock)
            {
                foreach (var line in lines)
                {
                    // Финализируем текущую streaming entry этой строкой
                    var displayLine = _streamingPrefix != null
                        ? $"{_streamingPrefix} > {line}"
                        : $"> {line}";

                    if (_currentStreamingEntry != null)
                    {
                        // Обновляем текущую entry финальным значением
                        _currentStreamingEntry.Message = displayLine;
                        _currentStreamingEntry = null; // Больше не обновляем её
                    }
                    else
                    {
                        // Добавляем новую запись
                        var entry = new LogEntry
                        {
                            Timestamp = timestamp,
                            Level = level,
                            Message = displayLine
                        };
                        LogEntries.Add(entry);
                    }
                }
            }

            // Оставляем в буфере только незавершённую часть
            _streamingBuffer.Clear();
            _streamingBuffer.Append(text[(lastNewline + 1)..]);

            // Если есть незавершённая часть — создаём новую streaming entry
            if (_streamingBuffer.Length > 0)
            {
                var displayText = _streamingPrefix != null
                    ? $"{_streamingPrefix} > {_streamingBuffer}"
                    : $"> {_streamingBuffer}";

                lock (_logEntriesLock)
                {
                    _currentStreamingEntry = new LogEntry
                    {
                        Timestamp = timestamp,
                        Level = level,
                        Message = displayText
                    };
                    LogEntries.Add(_currentStreamingEntry);
                }
            }
        }
        else
        {
            // Нет завершённых строк — просто обновляем текущую entry
            var displayText = _streamingPrefix != null
                ? $"{_streamingPrefix} > {_streamingBuffer}"
                : $"> {_streamingBuffer}";

            lock (_logEntriesLock)
            {
                if (_currentStreamingEntry == null)
                {
                    // Создаём новую streaming entry
                    _currentStreamingEntry = new LogEntry
                    {
                        Timestamp = timestamp,
                        Level = level,
                        Message = displayText
                    };
                    LogEntries.Add(_currentStreamingEntry);
                }
                else
                {
                    // Обновляем существующую (INotifyPropertyChanged обновит UI)
                    _currentStreamingEntry.Message = displayText;
                }
            }
        }
    }

    /// <summary>
    /// Завершить streaming (при END маркере)
    /// </summary>
    private void FlushStreamingBuffer(DateTime timestamp, Models.LogLevel level)
    {
        // Финальное обновление если есть что показать
        if (_streamingBuffer.Length > 0)
        {
            var remaining = _streamingBuffer.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(remaining))
            {
                var displayText = _streamingPrefix != null
                    ? $"{_streamingPrefix} > {remaining}"
                    : $"> {remaining}";

                lock (_logEntriesLock)
                {
                    if (_currentStreamingEntry != null)
                    {
                        _currentStreamingEntry.Message = displayText;
                    }
                    else
                    {
                        var entry = new LogEntry
                        {
                            Timestamp = timestamp,
                            Level = level,
                            Message = displayText
                        };
                        LogEntries.Add(entry);
                    }
                }
            }
        }

        // Сброс состояния
        _streamingBuffer.Clear();
        _currentStreamingEntry = null;
    }

    /// <summary>
    /// Добавить запись в UI коллекцию.
    /// Многострочные сообщения разбиваются на отдельные записи.
    /// Prefix вида [Source] сохраняется для всех строк.
    /// </summary>
    private void AddToUi(string message, DateTime timestamp, Models.LogLevel level)
    {
        // Убираем JSON escape-последовательности для читаемости
        var unescaped = UnescapeJsonString(message);

        // Разбиваем по переносам строк — каждая строка станет отдельной записью в ListBox
        var lines = unescaped.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        // Извлекаем source из первой строки для применения ко всем записям
        string? source = null;
        if (lines.Length > 0)
        {
            source = ExtractSource(lines[0]);
        }

        lock (_logEntriesLock)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Пропускаем пустые строки
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Для строк после первой добавляем prefix если его нет
                var finalLine = line;
                var lineSource = ExtractSource(line) ?? source ?? string.Empty;

                if (i > 0 && source != null && !line.TrimStart().StartsWith("["))
                {
                    finalLine = $"[{source}] {line}";
                }

                // Регистрируем новый источник
                if (!string.IsNullOrEmpty(lineSource) && !_knownSources.Contains(lineSource))
                {
                    _knownSources.Add(lineSource);
                    SourcesChanged?.Invoke(this, EventArgs.Empty);
                }

                var entry = new LogEntry
                {
                    Timestamp = timestamp,
                    Level = level,
                    Source = lineSource,
                    Message = finalLine
                };
                LogEntries.Add(entry);
            }

            // Ограничиваем количество записей в UI для производительности
            while (LogEntries.Count > _maxUiEntries)
            {
                LogEntries.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Извлекает источник из строки вида "[Source Name] message"
    /// </summary>
    private static string? ExtractSource(string line)
    {
        // Ищем паттерн [Something] в начале строки
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("["))
        {
            var endBracket = trimmed.IndexOf(']');
            if (endBracket > 0)
            {
                // Возвращаем содержимое без скобок
                return trimmed[1..endBracket];
            }
        }
        return null;
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

    /// <summary>
    /// Убирает JSON escape-последовательности для читаемости в логе
    /// </summary>
    private static string UnescapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        return s
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t");
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
