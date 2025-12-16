using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Llm;
using SmartBasket.WPF.Services;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// Сообщение в чате с поддержкой streaming обновлений
/// </summary>
public class ChatMessage : ObservableObject
{
    public string Role { get; init; } = string.Empty;

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public DateTime Timestamp { get; init; } = DateTime.Now;

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";
}

/// <summary>
/// ViewModel для AI чата с поддержкой tool calling через ChatService
/// </summary>
public partial class AiChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly IAiProviderFactory _aiProviderFactory;
    private readonly AppSettings? _appSettings;
    private readonly SettingsService? _settingsService;
    private readonly Action<string>? _log;
    private readonly object _messagesLock = new();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Дефолтный системный промпт
    /// </summary>
    private const string DefaultSystemPrompt = @"Ты — умный помощник приложения Smart Basket для учёта домашних расходов.

СЕГОДНЯШНЯЯ ДАТА: {{TODAY}}

У тебя есть доступ к инструментам для работы с базой данных чеков пользователя.

ПРАВИЛА:
1. Когда спрашивают про ""последние N месяцев"" — считай от сегодняшней даты назад
2. При запросе чеков ВСЕГДА используй инструмент get_receipts
3. Если инструмент вернул пустой результат (receipts: []) — сообщи пользователю что данных за этот период нет
4. НЕ ПОВТОРЯЙ вызов инструмента с теми же параметрами если уже получил ответ
5. После получения данных от инструмента — ответь пользователю на основе этих данных
6. Отвечай кратко и по делу на русском языке";

    public AiChatViewModel(
        IChatService chatService,
        IAiProviderFactory aiProviderFactory,
        AppSettings? appSettings = null,
        SettingsService? settingsService = null,
        Action<string>? log = null)
    {
        _chatService = chatService;
        _aiProviderFactory = aiProviderFactory;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _log = log;

        Log("=== AI Chat ViewModel создан ===");

        // Загружаем системный промпт из конфигурации
        LoadSystemPrompt();

        // Загружаем список провайдеров
        var providers = _aiProviderFactory.GetAvailableProviders();
        Log($"Доступные провайдеры ({providers.Count}):");
        foreach (var provider in providers)
        {
            AvailableProviders.Add(provider);
            Log($"  - {provider}");
        }

        // Выбираем первый по умолчанию
        if (AvailableProviders.Count > 0)
        {
            var defaultProvider = AvailableProviders[0];
            _chatService.SetProvider(defaultProvider);
            SelectedProvider = defaultProvider;
            Log($"Выбран провайдер по умолчанию: {SelectedProvider}");
        }
    }

    /// <summary>
    /// Загрузить системный промпт из конфигурации
    /// </summary>
    private void LoadSystemPrompt()
    {
        string? configPrompt = null;

        // Пробуем загрузить из Prompts["Chat"]
        if (_appSettings?.AiOperations?.Prompts.TryGetValue("Chat", out var prompt) == true)
        {
            configPrompt = prompt;
            Log($"Системный промпт загружен из конфигурации ({prompt.Length} символов)");
        }

        // Используем дефолтный если нет в конфигурации
        var effectivePrompt = configPrompt ?? DefaultSystemPrompt;

        // Сохраняем шаблон (с плейсхолдером) для редактирования
        _systemPromptTemplate = effectivePrompt;

        // Подставляем текущую дату для отправки в LLM
        SystemPrompt = effectivePrompt.Replace("{{TODAY}}", DateTime.Now.ToString("yyyy-MM-dd"));
        _chatService.SetSystemPrompt(SystemPrompt);
        Log($"Системный промпт установлен ({SystemPrompt.Length} символов)");
    }

    /// <summary>
    /// Шаблон промпта (с плейсхолдером {{TODAY}}) для сохранения
    /// </summary>
    private string _systemPromptTemplate = string.Empty;

    private void Log(string message)
    {
        _log?.Invoke($"[AI Chat] {message}");
    }

    /// <summary>
    /// Включить синхронизацию коллекции для thread-safe доступа
    /// </summary>
    public void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(Messages, _messagesLock);
    }

    /// <summary>
    /// Список доступных провайдеров
    /// </summary>
    public ObservableCollection<string> AvailableProviders { get; } = new();

    /// <summary>
    /// Выбранный провайдер
    /// </summary>
    [ObservableProperty]
    private string? _selectedProvider;

    /// <summary>
    /// Текущий системный промпт (с подставленной датой)
    /// </summary>
    [ObservableProperty]
    private string _systemPrompt = string.Empty;

    /// <summary>
    /// История сообщений
    /// </summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>
    /// Текст ввода пользователя
    /// </summary>
    [ObservableProperty]
    private string _userInput = string.Empty;

    /// <summary>
    /// Флаг обработки запроса
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private bool _isProcessing;

    /// <summary>
    /// Можно ли отправить сообщение
    /// </summary>
    public bool CanSend => !IsProcessing && !string.IsNullOrWhiteSpace(UserInput) && !string.IsNullOrEmpty(SelectedProvider);

    /// <summary>
    /// Статус соединения
    /// </summary>
    [ObservableProperty]
    private string _connectionStatus = "Готов";

    /// <summary>
    /// Есть ли провайдеры
    /// </summary>
    public bool HasProviders => AvailableProviders.Count > 0;

    partial void OnUserInputChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProviderChanged(string? oldValue, string? newValue)
    {
        SendMessageCommand.NotifyCanExecuteChanged();

        if (!string.IsNullOrEmpty(newValue))
        {
            Log($">>> Провайдер изменён на: {newValue}");

            // Устанавливаем провайдер в ChatService
            _chatService.SetProvider(newValue);
            Log($"    ChatService.SetProvider('{newValue}')");

            // Очищаем историю ChatService и UI при смене провайдера
            _chatService.ClearHistory();
            Log($"    История ChatService очищена");

            if (Messages.Count > 0)
            {
                lock (_messagesLock)
                {
                    Messages.Clear();
                }
                Log($"    История UI очищена (смена провайдера)");
            }

            ConnectionStatus = $"Провайдер: {newValue}";
        }
    }

    /// <summary>
    /// Отправить сообщение через ChatService с tool calling
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || string.IsNullOrEmpty(SelectedProvider))
            return;

        var userMessage = UserInput.Trim();
        UserInput = string.Empty;

        Log("========================================");
        Log($">>> ОТПРАВКА СООБЩЕНИЯ через ChatService");
        Log($"    Провайдер (ключ): {SelectedProvider}");
        Log($"    Сообщение: {userMessage}");

        // Добавляем сообщение пользователя в UI
        lock (_messagesLock)
        {
            Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = userMessage
            });
        }

        IsProcessing = true;
        ConnectionStatus = "Думаю...";
        _cts = new CancellationTokenSource();

        // Создаём временное сообщение ассистента для streaming
        ChatMessage? streamingMessage = null;
        var streamingContent = new System.Text.StringBuilder();

        // Получаем Dispatcher для UI обновлений
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Throttling: обновляем UI не чаще чем раз в 100ms для предотвращения зависания
        var lastUiUpdate = DateTime.MinValue;
        var uiUpdateInterval = TimeSpan.FromMilliseconds(100);
        var pendingUiUpdate = false;
        var contentLock = new object();

        try
        {
            // ThreadSafeProgress не захватывает SynchronizationContext - предотвращает зависание UI
            var progressReporter = new ThreadSafeProgress<string>(msg =>
            {
                // Не логируем текстовые дельты (начинаются с "  ") - они спамят лог
                if (!msg.StartsWith("  ") || msg.StartsWith("  ["))
                {
                    Log($"    {msg}");
                }

                // Обновляем статус на основе прогресса (это легко, можно сразу)
                if (msg.Contains("Выполняю") || msg.Contains("Tool call"))
                {
                    dispatcher.BeginInvoke(() => ConnectionStatus = msg);
                }

                // Показываем вызов инструмента пользователю
                if (msg.StartsWith("Выполняю ") || msg.Contains("🔧 Tool call:"))
                {
                    string toolName;
                    if (msg.StartsWith("Выполняю "))
                    {
                        toolName = msg.Replace("Выполняю ", "").TrimEnd('.', ' ');
                    }
                    else
                    {
                        var idx = msg.IndexOf("🔧 Tool call:");
                        toolName = idx >= 0 ? msg[(idx + "🔧 Tool call:".Length)..].Trim() : "инструмент";
                    }

                    lock (contentLock)
                    {
                        // Добавляем перенос строки после tool call для читаемости
                        streamingContent.Append($"🔧 Вызываю инструмент: {toolName}\n");
                    }

                    // Tool call показываем сразу
                    dispatcher.BeginInvoke(() =>
                    {
                        if (streamingMessage == null)
                        {
                            streamingMessage = new ChatMessage { Role = "assistant", Content = "" };
                            lock (_messagesLock) { Messages.Add(streamingMessage); }
                        }
                        lock (contentLock)
                        {
                            streamingMessage.Content = streamingContent.ToString();
                        }
                        ConnectionStatus = $"Выполняю {toolName}...";
                    });
                }

                // Распознаём дельты текста (начинаются с "  " без "[")
                if (msg.StartsWith("  ") && !msg.StartsWith("  ["))
                {
                    var delta = msg.Substring(2);

                    lock (contentLock)
                    {
                        // Append без Line - дельты уже содержат \n если нужно
                        streamingContent.Append(delta);
                    }

                    // Throttling: проверяем нужно ли обновить UI
                    var now = DateTime.UtcNow;
                    var shouldUpdate = false;

                    lock (contentLock)
                    {
                        if (now - lastUiUpdate >= uiUpdateInterval)
                        {
                            lastUiUpdate = now;
                            shouldUpdate = true;
                            pendingUiUpdate = false;
                        }
                        else
                        {
                            pendingUiUpdate = true;
                        }
                    }

                    if (shouldUpdate)
                    {
                        dispatcher.BeginInvoke(() =>
                        {
                            if (streamingMessage == null)
                            {
                                streamingMessage = new ChatMessage { Role = "assistant", Content = "" };
                                lock (_messagesLock) { Messages.Add(streamingMessage); }
                                ConnectionStatus = "Получаю ответ...";
                            }
                            lock (contentLock)
                            {
                                streamingMessage.Content = streamingContent.ToString();
                            }
                        });
                    }
                }
            });

            // Фоновая задача для финального обновления UI после throttling
            _ = Task.Run(async () =>
            {
                while (IsProcessing)
                {
                    await Task.Delay(150);
                    bool needsUpdate;
                    lock (contentLock) { needsUpdate = pendingUiUpdate; pendingUiUpdate = false; }

                    if (needsUpdate && streamingMessage != null)
                    {
                        dispatcher.BeginInvoke(() =>
                        {
                            lock (contentLock)
                            {
                                streamingMessage.Content = streamingContent.ToString();
                            }
                        });
                    }
                }
            });

            Log($"    Отправляю в ChatService...");
            // КРИТИЧНО: Запускаем в Task.Run чтобы освободить UI поток (WPF_RULES #3)
            // ChatService содержит синхронные операции которые могут блокировать UI
            var result = await Task.Run(async () =>
                await _chatService.SendAsync(userMessage, progressReporter, _cts.Token));

            Log($"    Ответ получен:");
            Log($"    Success: {result.Success}");
            Log($"    ErrorMessage: {result.ErrorMessage ?? "(null)"}");
            if (!string.IsNullOrEmpty(result.Content))
            {
                var preview = result.Content.Length > 500
                    ? result.Content.Substring(0, 500) + "..."
                    : result.Content;
                Log($"    Content: {preview}");
            }

            if (result.Success && !string.IsNullOrEmpty(result.Content))
            {
                // Если есть streaming сообщение — обновляем финальным контентом
                if (streamingMessage != null)
                {
                    streamingMessage.Content = result.Content;
                }
                else
                {
                    // Иначе добавляем новое сообщение
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage
                        {
                            Role = "assistant",
                            Content = result.Content
                        });
                    }
                }
                ConnectionStatus = "Готов";
                Log($"    Сообщение добавлено в чат");
            }
            else
            {
                // Удаляем streaming сообщение если ошибка
                if (streamingMessage != null)
                {
                    lock (_messagesLock)
                    {
                        Messages.Remove(streamingMessage);
                    }
                }
                AddSystemMessage($"Ошибка: {result.ErrorMessage ?? "Неизвестная ошибка"}");
                ConnectionStatus = "Ошибка";
            }
        }
        catch (OperationCanceledException)
        {
            Log($"    Запрос отменён пользователем");
            AddSystemMessage("Запрос отменён");
            ConnectionStatus = "Отменено";
        }
        catch (Exception ex)
        {
            Log($"    ИСКЛЮЧЕНИЕ: {ex.GetType().Name}: {ex.Message}");
            Log($"    StackTrace: {ex.StackTrace}");
            AddSystemMessage($"Ошибка: {ex.Message}");
            ConnectionStatus = "Ошибка";
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            Log("========================================");
        }
    }

    /// <summary>
    /// Отменить текущий запрос
    /// </summary>
    [RelayCommand]
    private void CancelRequest()
    {
        _cts?.Cancel();
        ConnectionStatus = "Отмена...";
    }

    /// <summary>
    /// Очистить историю чата
    /// </summary>
    [RelayCommand]
    private void ClearChat()
    {
        // Очищаем историю ChatService
        _chatService.ClearHistory();
        Log($"История ChatService очищена");

        lock (_messagesLock)
        {
            Messages.Clear();
        }
        ConnectionStatus = "Чат очищен";
    }

    /// <summary>
    /// Применить новый системный промпт и перезапустить чат
    /// </summary>
    [RelayCommand]
    private void ApplySystemPrompt()
    {
        // SystemPrompt содержит текст из редактора (может быть с {{TODAY}} или без)
        // Сохраняем как шаблон
        _systemPromptTemplate = SystemPrompt;

        // Подставляем текущую дату для отправки в LLM
        var effectivePrompt = SystemPrompt.Replace("{{TODAY}}", DateTime.Now.ToString("yyyy-MM-dd"));

        _chatService.SetSystemPrompt(effectivePrompt);
        _chatService.ClearHistory();

        lock (_messagesLock)
        {
            Messages.Clear();
        }

        // Сохраняем в настройки
        SavePromptToSettings();

        Log($"Системный промпт обновлён ({effectivePrompt.Length} символов)");
        Log($"История чата очищена");
        ConnectionStatus = "Промпт обновлён, чат перезапущен";
    }

    /// <summary>
    /// Сохранить промпт в appsettings.json
    /// </summary>
    private void SavePromptToSettings()
    {
        if (_appSettings == null || _settingsService == null)
        {
            Log("Не удалось сохранить промпт: настройки не инициализированы");
            return;
        }

        try
        {
            // Инициализируем AiOperations если null
            _appSettings.AiOperations ??= new AiOperationsConfig();

            // Сохраняем шаблон (с плейсхолдером {{TODAY}})
            _appSettings.AiOperations.Prompts["Chat"] = _systemPromptTemplate;

            // Сохраняем в файл
            _settingsService.Save(_appSettings);
            Log($"Промпт сохранён в appsettings.json");
        }
        catch (Exception ex)
        {
            Log($"Ошибка сохранения промпта: {ex.Message}");
        }
    }

    private void AddSystemMessage(string content)
    {
        lock (_messagesLock)
        {
            Messages.Add(new ChatMessage
            {
                Role = "system",
                Content = content
            });
        }
    }
}
