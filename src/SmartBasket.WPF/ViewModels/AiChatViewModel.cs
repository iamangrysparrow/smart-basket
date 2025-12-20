using System.Collections.ObjectModel;
using System.Text;
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

    /// <summary>
    /// Поддерживает ли текущий провайдер режим рассуждений
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReasoningControlsVisible))]
    private bool _supportsReasoning;

    /// <summary>
    /// Видимость контролов режима рассуждений
    /// </summary>
    public bool ReasoningControlsVisible => SupportsReasoning;

    /// <summary>
    /// Включен ли режим рассуждений
    /// </summary>
    [ObservableProperty]
    private bool _isReasoningEnabled;

    /// <summary>
    /// Доступные уровни рассуждений
    /// </summary>
    public ObservableCollection<string> ReasoningEffortOptions { get; } = new()
    {
        "low",
        "medium",
        "high"
    };

    /// <summary>
    /// Выбранный уровень рассуждений
    /// </summary>
    [ObservableProperty]
    private string _selectedReasoningEffort = "low";

    /// <summary>
    /// Принудительно передавать инструменты в системном промпте вместо native tool calling.
    /// Полезно для моделей которые плохо работают с native tools (YandexGPT).
    /// </summary>
    [ObservableProperty]
    private bool _forcePromptInjection;

    partial void OnForcePromptInjectionChanged(bool value)
    {
        _chatService.ForcePromptInjection = value;
        Log($"ForcePromptInjection = {value}");
    }

    partial void OnUserInputChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsReasoningEnabledChanged(bool value)
    {
        UpdateReasoningParameters();
        Log($"Режим рассуждений {(value ? "включен" : "выключен")}");
    }

    partial void OnSelectedReasoningEffortChanged(string value)
    {
        UpdateReasoningParameters();
        Log($"Уровень рассуждений изменён на: {value}");
    }

    /// <summary>
    /// Обновить параметры режима рассуждений в ChatService
    /// </summary>
    private void UpdateReasoningParameters()
    {
        if (!SupportsReasoning) return;

        var mode = IsReasoningEnabled
            ? ReasoningMode.EnabledHidden
            : ReasoningMode.Disabled;

        var effort = SelectedReasoningEffort switch
        {
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            _ => ReasoningEffort.Low
        };

        _chatService.SetReasoningParameters(mode, effort);
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

            // Проверяем поддержку режима рассуждений для нового провайдера
            SupportsReasoning = _chatService.SupportsReasoning;
            Log($"    SupportsReasoning: {SupportsReasoning}");

            // Если режим рассуждений поддерживается, восстанавливаем текущие настройки
            if (SupportsReasoning)
            {
                UpdateReasoningParameters();
            }

            ConnectionStatus = $"Провайдер: {newValue}";
        }
    }

    // Текущее сообщение ассистента (для streaming обновлений)
    private ChatMessage? _currentAssistantMessage;
    // Текущая часть Thinking (для накопления текста)
    private AssistantResponsePart? _currentThinkingPart;
    // StringBuilder для накопления текста между UI обновлениями
    private readonly StringBuilder _thinkingBuffer = new();

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
                IsUser = true,
                UserText = userMessage
            });
        }

        IsProcessing = true;
        ConnectionStatus = "Думаю...";
        _cts = new CancellationTokenSource();

        // Создаём сообщение ассистента с пустой коллекцией Parts
        _currentAssistantMessage = new ChatMessage
        {
            IsUser = false,
            Parts = new ObservableCollection<AssistantResponsePart>()
        };
        _currentThinkingPart = null;
        _thinkingBuffer.Clear();

        lock (_messagesLock)
        {
            Messages.Add(_currentAssistantMessage);
        }

        // Получаем Dispatcher для UI обновлений
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Throttling для TextDelta
        var lastUiUpdate = DateTime.MinValue;
        var uiUpdateInterval = TimeSpan.FromMilliseconds(100);
        var pendingUiUpdate = false;
        var bufferLock = new object();

        try
        {
            // ThreadSafeProgress для ChatProgress
            var progressReporter = new ThreadSafeProgress<ChatProgress>(progress =>
            {
                switch (progress.Type)
                {
                    case ChatProgressType.TextDelta:
                        HandleTextDelta(progress.Text, dispatcher, ref lastUiUpdate, uiUpdateInterval, bufferLock, ref pendingUiUpdate);
                        break;

                    case ChatProgressType.ToolCall:
                        HandleToolCall(progress.ToolName!, progress.ToolArgs, dispatcher);
                        break;

                    case ChatProgressType.ToolResult:
                        HandleToolResult(progress.ToolName!, progress.ToolResult, progress.ToolSuccess, dispatcher);
                        break;

                    case ChatProgressType.Complete:
                        HandleComplete(dispatcher);
                        break;
                }
            });

            // Фоновая задача для финального обновления UI после throttling
            _ = Task.Run(async () =>
            {
                while (IsProcessing)
                {
                    await Task.Delay(150);
                    bool needsUpdate;
                    lock (bufferLock) { needsUpdate = pendingUiUpdate; pendingUiUpdate = false; }

                    if (needsUpdate && _currentThinkingPart != null)
                    {
                        dispatcher.BeginInvoke(() =>
                        {
                            lock (bufferLock)
                            {
                                _currentThinkingPart.Text = _thinkingBuffer.ToString();
                            }
                        });
                    }
                }
            });

            Log($"    Отправляю в ChatService...");
            var result = await Task.Run(async () =>
                await _chatService.SendAsync(userMessage, progressReporter, _cts.Token));

            Log($"    Ответ получен:");
            Log($"    Success: {result.Success}");
            Log($"    ErrorMessage: {result.ErrorMessage ?? "(null)"}");

            if (result.Success)
            {
                // Финальное обновление текста из результата (если есть)
                if (!string.IsNullOrEmpty(result.Content) && _currentThinkingPart != null)
                {
                    _currentThinkingPart.Text = result.Content;
                }
                ConnectionStatus = "Готов";
                Log($"    Сообщение обработано");
            }
            else
            {
                // Добавляем ошибку как системное сообщение
                lock (_messagesLock)
                {
                    Messages.Remove(_currentAssistantMessage);
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
            _currentAssistantMessage = null;
            _currentThinkingPart = null;
            _cts?.Dispose();
            _cts = null;
            Log("========================================");
        }
    }

    /// <summary>
    /// Обработка дельты текста от модели
    /// </summary>
    private void HandleTextDelta(string? text, Dispatcher dispatcher, ref DateTime lastUiUpdate,
        TimeSpan uiUpdateInterval, object bufferLock, ref bool pendingUiUpdate)
    {
        // Захватываем ссылку до BeginInvoke
        var parts = _currentAssistantMessage?.Parts;
        if (string.IsNullOrEmpty(text) || parts == null)
            return;

        // Если нет текущего Thinking — создаём
        if (_currentThinkingPart == null)
        {
            _currentThinkingPart = new AssistantResponsePart
            {
                Type = ResponsePartType.Thinking,
                IsExpanded = true // Во время streaming — развёрнуто
            };

            var thinkingPart = _currentThinkingPart;
            dispatcher.BeginInvoke(() =>
            {
                parts.Add(thinkingPart);
                ConnectionStatus = "Получаю ответ...";
            });
        }

        // Накапливаем текст
        lock (bufferLock)
        {
            _thinkingBuffer.Append(text);
        }

        // Throttling: обновляем UI не чаще чем раз в interval
        var now = DateTime.UtcNow;
        bool shouldUpdate;

        lock (bufferLock)
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
                shouldUpdate = false;
            }
        }

        if (shouldUpdate)
        {
            var thinkingPart = _currentThinkingPart;
            dispatcher.BeginInvoke(() =>
            {
                lock (bufferLock)
                {
                    if (thinkingPart != null)
                        thinkingPart.Text = _thinkingBuffer.ToString();
                }
            });
        }
    }

    /// <summary>
    /// Обработка вызова инструмента
    /// </summary>
    private void HandleToolCall(string toolName, string? toolArgs, Dispatcher dispatcher)
    {
        Log($"    Tool call: {toolName}");

        // Захватываем ссылку до BeginInvoke
        var parts = _currentAssistantMessage?.Parts;
        if (parts == null)
            return;

        // Текущий Thinking (если есть) остаётся как есть
        // Следующий текст будет новым Thinking
        _currentThinkingPart = null;
        _thinkingBuffer.Clear();

        // Создаём ToolCall part
        var toolCallPart = new AssistantResponsePart
        {
            Type = ResponsePartType.ToolCall,
            ToolName = toolName,
            ToolArgs = toolArgs,
            IsExpanded = true // Во время выполнения — развёрнуто
        };

        dispatcher.BeginInvoke(() =>
        {
            parts.Add(toolCallPart);
            ConnectionStatus = $"Выполняю {toolName}...";
        });
    }

    /// <summary>
    /// Обработка результата инструмента
    /// </summary>
    private void HandleToolResult(string toolName, string? toolResult, bool? toolSuccess, Dispatcher dispatcher)
    {
        Log($"    Tool result: {toolName}, success={toolSuccess}");

        // Захватываем ссылку до BeginInvoke
        var parts = _currentAssistantMessage?.Parts;
        if (parts == null)
            return;

        // Находим последний ToolCall с таким именем
        dispatcher.BeginInvoke(() =>
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var part = parts[i];
                if (part.Type == ResponsePartType.ToolCall && part.ToolName == toolName && part.ToolResult == null)
                {
                    part.ToolResult = toolResult;
                    part.ToolSuccess = toolSuccess;
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Обработка завершения ответа
    /// </summary>
    private void HandleComplete(Dispatcher dispatcher)
    {
        Log($"    Complete");

        // Захватываем ссылку до BeginInvoke, т.к. _currentAssistantMessage может стать null
        var message = _currentAssistantMessage;
        var parts = message?.Parts;
        if (parts == null)
            return;

        dispatcher.BeginInvoke(() =>
        {
            // Удаляем пустые Thinking части (могут появиться из-за фильтрации служебных сообщений)
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (parts[i].Type == ResponsePartType.Thinking &&
                    string.IsNullOrWhiteSpace(parts[i].Text))
                {
                    parts.RemoveAt(i);
                }
            }

            // Находим последний Thinking — он станет ответом (остаётся развёрнутым)
            // Все остальные части сворачиваем
            AssistantResponsePart? lastThinking = null;
            int lastThinkingIndex = -1;

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Type == ResponsePartType.Thinking)
                {
                    lastThinking = part;
                    lastThinkingIndex = i;
                }
            }

            // Сворачиваем все части кроме последнего Thinking (который является ответом)
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i].IsExpanded = (i == lastThinkingIndex);
            }

            // Последний Thinking преобразуем в FinalAnswer для правильного отображения
            if (lastThinking != null && lastThinkingIndex >= 0)
            {
                var finalAnswer = new AssistantResponsePart
                {
                    Type = ResponsePartType.FinalAnswer,
                    Text = lastThinking.Text,
                    IsExpanded = true
                };
                parts[lastThinkingIndex] = finalAnswer;
            }
        });
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
                IsSystem = true,
                SystemText = content
            });
        }
    }
}
