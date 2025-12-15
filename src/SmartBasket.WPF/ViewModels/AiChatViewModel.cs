using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartBasket.Services.Llm;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// Сообщение в чате
/// </summary>
public class ChatMessage
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";
}

/// <summary>
/// ViewModel для AI чата
/// </summary>
public partial class AiChatViewModel : ObservableObject
{
    private readonly IAiProviderFactory _aiProviderFactory;
    private readonly Action<string>? _log;
    private readonly object _messagesLock = new();
    private CancellationTokenSource? _cts;

    public AiChatViewModel(IAiProviderFactory aiProviderFactory, Action<string>? log = null)
    {
        _aiProviderFactory = aiProviderFactory;
        _log = log;

        Log("=== AI Chat ViewModel создан ===");

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
            SelectedProvider = AvailableProviders[0];
            Log($"Выбран провайдер по умолчанию: {SelectedProvider}");
        }
    }

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

        // Сбрасываем диалог у старого провайдера (если был)
        if (!string.IsNullOrEmpty(oldValue))
        {
            var oldProvider = _aiProviderFactory.GetProvider(oldValue);
            if (oldProvider?.SupportsConversationReset == true)
            {
                oldProvider.ResetConversation();
                Log($"    ResetConversation() вызван для старого провайдера: {oldValue}");
            }
        }

        if (!string.IsNullOrEmpty(newValue))
        {
            Log($">>> Провайдер изменён на: {newValue}");

            // Очищаем UI историю при смене провайдера
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
    /// Отправить сообщение
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || string.IsNullOrEmpty(SelectedProvider))
            return;

        var userMessage = UserInput.Trim();
        UserInput = string.Empty;

        Log("========================================");
        Log($">>> ОТПРАВКА СООБЩЕНИЯ");
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

        try
        {
            Log($"    Запрашиваю провайдер из фабрики по ключу: '{SelectedProvider}'");
            var provider = _aiProviderFactory.GetProvider(SelectedProvider);
            if (provider == null)
            {
                Log($"    ОШИБКА: провайдер не найден!");
                AddSystemMessage($"Ошибка: провайдер {SelectedProvider} не найден");
                return;
            }

            Log($"    Провайдер получен: {provider.GetType().Name}");
            Log($"    Provider.Name: {provider.Name}");

            // Формируем массив сообщений для ChatAsync
            var chatMessages = BuildChatMessages();
            Log($"    Отправляю ChatAsync с {chatMessages.Count} сообщениями...");

            // Progress для логирования из провайдера
            var progressReporter = new Progress<string>(msg => Log($"    {msg}"));

            var result = await provider.ChatAsync(
                chatMessages,
                maxTokens: 2000,
                temperature: 0.7,
                progress: progressReporter,
                cancellationToken: _cts.Token);

            Log($"    Ответ получен:");
            Log($"    IsSuccess: {result.IsSuccess}");
            Log($"    ErrorMessage: {result.ErrorMessage ?? "(null)"}");
            Log($"    ResponseId: {result.ResponseId ?? "(null)"}");
            if (!string.IsNullOrEmpty(result.Response))
            {
                var preview = result.Response.Length > 500
                    ? result.Response.Substring(0, 500) + "..."
                    : result.Response;
                Log($"    Response: {preview}");
            }

            if (result.IsSuccess && !string.IsNullOrEmpty(result.Response))
            {
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage
                    {
                        Role = "assistant",
                        Content = result.Response
                    });
                }
                ConnectionStatus = "Готов";
                Log($"    Сообщение добавлено в чат");
            }
            else
            {
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
        // Сбрасываем диалог у текущего провайдера
        if (!string.IsNullOrEmpty(SelectedProvider))
        {
            var provider = _aiProviderFactory.GetProvider(SelectedProvider);
            if (provider?.SupportsConversationReset == true)
            {
                provider.ResetConversation();
                Log($"ResetConversation() вызван для провайдера: {SelectedProvider}");
            }
        }

        lock (_messagesLock)
        {
            Messages.Clear();
        }
        ConnectionStatus = "Чат очищен";
    }

    /// <summary>
    /// Построить массив сообщений для ChatAsync
    /// </summary>
    private List<LlmChatMessage> BuildChatMessages()
    {
        // Берём последние N сообщений для контекста
        const int maxHistoryMessages = 20;

        return Messages
            .Where(m => m.Role == "user" || m.Role == "assistant")
            .TakeLast(maxHistoryMessages)
            .Select(m => new LlmChatMessage
            {
                Role = m.Role,
                Content = m.Content
            })
            .ToList();
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
