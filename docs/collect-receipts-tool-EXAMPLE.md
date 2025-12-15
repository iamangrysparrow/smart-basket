# Пример: Превращение "Collect Receipts" в AI инструмент

> Пошаговое руководство как сделать команду доступной для AI

---

## Текущее состояние

### Что есть сейчас

**UI (MainWindow.xaml):**
```xml
<Button Style="{StaticResource PrimaryButton}"
        Command="{Binding CollectReceiptsCommand}"
        IsEnabled="{Binding IsNotProcessing}"
        ToolTip="Collect receipts from all sources (Ctrl+F)">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="✓"/>
        <TextBlock Text="Collect"/>
    </StackPanel>
</Button>
```

**ViewModel (MainViewModel.cs):**
```csharp
[RelayCommand]
private async Task CollectReceiptsAsync()
{
    if (IsProcessing) return;
    
    IsProcessing = true;
    StatusText = "Collecting receipts...";
    Log("=== Starting receipt collection ===");
    
    try
    {
        var progress = new ThreadSafeProgress<string>(msg => Log(msg));
        
        var result = await Task.Run(async () =>
            await _receiptCollectionService.CollectAsync(
                sourceNames: null,              // все enabled источники
                progress: progress,
                cancellationToken: _cts.Token));
        
        // Результат
        Log($"Collection complete: {result.ReceiptsSaved} receipts saved");
        StatusText = $"Collected {result.ReceiptsSaved} receipts";
    }
    catch (Exception ex)
    {
        LogException(ex, "CollectReceipts");
        StatusText = "Collection failed";
    }
    finally
    {
        IsProcessing = false;
    }
}
```

**Сервис (IReceiptCollectionService):**
```csharp
public interface IReceiptCollectionService
{
    Task<CollectionResult> CollectAsync(
        IEnumerable<string>? sourceNames = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public class CollectionResult
{
    public int TotalSources { get; set; }
    public int ReceiptsFetched { get; set; }
    public int ReceiptsParsed { get; set; }
    public int ReceiptsSaved { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; } = new();
}
```

---

## Шаг 1: Определить инструмент (ToolDefinition)

```csharp
// Brain/Tools/SystemToolsRegistration.cs
using SmartBasket.Brain.Tools;
using SmartBasket.Services;

namespace SmartBasket.Brain.Tools;

public static class SystemToolsRegistration
{
    public static void RegisterTools(IToolsRegistry registry)
    {
        // collect_receipts
        registry.RegisterTool(
            name: "collect_receipts",
            handler: CollectReceiptsHandler,
            definition: new ToolDefinition
            {
                Name = "collect_receipts",
                Description = "Собрать чеки из настроенных источников (email, файлы). " +
                             "Запускает процесс загрузки, парсинга и сохранения новых чеков.",
                Category = ToolCategory.System,
                Parameters = new JsonSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, JsonProperty>
                    {
                        ["source_names"] = new JsonProperty
                        {
                            Type = "array",
                            Description = "Необязательно: список источников для сбора. " +
                                        "Если не указано - собирает из всех включённых источников.",
                        }
                    },
                    Required = new List<string>() // Все параметры опциональны
                }
            });
    }
    
    // Handler будет ниже
    private static Task<ToolExecutionResult> CollectReceiptsHandler(
        Dictionary<string, object> arguments,
        ToolExecutionContext context)
    {
        // Реализация ниже
        throw new NotImplementedException();
    }
}
```

**Пояснение:**
- `name`: Уникальное имя инструмента (snake_case)
- `Description`: Что делает инструмент (для AI)
- `Category`: Тип инструмента
- `Parameters`: Входные параметры (JSON Schema)
- `Required`: Обязательные параметры (в данном случае - нет)

---

## Шаг 2: Создать Handler

Handler - это функция которая выполняет действие.

```csharp
// Brain/Tools/SystemToolsRegistration.cs
private static async Task<ToolExecutionResult> CollectReceiptsHandler(
    Dictionary<string, object> arguments,
    ToolExecutionContext context)
{
    try
    {
        // 1. Получить сервис из DI
        var collectionService = context.Services
            .GetRequiredService<IReceiptCollectionService>();
        
        // 2. Извлечь параметры (если есть)
        List<string>? sourceNames = null;
        if (arguments.ContainsKey("source_names"))
        {
            // Парсим массив источников
            var sourcesJson = arguments["source_names"].ToString();
            sourceNames = System.Text.Json.JsonSerializer
                .Deserialize<List<string>>(sourcesJson);
        }
        
        // 3. Создать progress reporter для AI
        var progressMessages = new List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));
        
        // 4. Вызвать существующий сервис
        var result = await collectionService.CollectAsync(
            sourceNames: sourceNames,
            progress: progress,
            cancellationToken: CancellationToken.None);
        
        // 5. Сформировать результат для AI
        var summary = $"Сбор завершён:\n" +
                     $"- Обработано источников: {result.SourcesProcessed}\n" +
                     $"- Найдено чеков: {result.ReceiptsFetched}\n" +
                     $"- Распознано: {result.ReceiptsParsed}\n" +
                     $"- Сохранено: {result.ReceiptsSaved}\n" +
                     $"- Пропущено: {result.ReceiptsSkipped}\n" +
                     $"- Ошибок: {result.Errors}";
        
        if (result.Errors > 0)
        {
            summary += $"\n\nОшибки:\n" + 
                      string.Join("\n", result.ErrorMessages.Take(3));
        }
        
        return new ToolExecutionResult
        {
            Success = true,
            Result = summary,  // Это увидит AI
            Data = result      // Это может использовать UI
        };
    }
    catch (Exception ex)
    {
        return new ToolExecutionResult
        {
            Success = false,
            ErrorMessage = $"Ошибка при сборе чеков: {ex.Message}",
            Result = $"Не удалось собрать чеки: {ex.Message}"
        };
    }
}
```

**Пояснение:**
1. Получаем нужный сервис из DI контейнера
2. Извлекаем параметры из `arguments`
3. Вызываем существующую бизнес-логику
4. Форматируем результат для AI
5. Возвращаем `ToolExecutionResult`

---

## Шаг 3: Зарегистрировать при старте

```csharp
// Brain/BrainServiceCollectionExtensions.cs
public static class BrainServiceCollectionExtensions
{
    public static IServiceCollection AddSmartBasketBrain(
        this IServiceCollection services)
    {
        // Tools Registry
        services.AddSingleton<IToolsRegistry, ToolsRegistry>();
        services.AddSingleton<IToolsProvider, ToolsProvider>();
        
        // Регистрируем инструменты при старте
        services.AddHostedService<ToolsRegistrationService>();
        
        return services;
    }
}

// Brain/ToolsRegistrationService.cs
public class ToolsRegistrationService : IHostedService
{
    private readonly IToolsRegistry _registry;
    
    public ToolsRegistrationService(IToolsRegistry registry)
    {
        _registry = registry;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Регистрируем все инструменты
        SystemToolsRegistration.RegisterTools(_registry);
        // ShoppingListToolsRegistration.RegisterTools(_registry);
        // ... другие
        
        return Task.CompletedTask;
    }
    
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

**Пояснение:**
- `IHostedService` запускается при старте приложения
- Вызывает регистрацию всех инструментов
- После этого инструменты доступны для AI

---

## Шаг 4: Использование в чате с AI

### 4.1 Создать чат-сервис

```csharp
// Brain/Services/BrainChatService.cs
public class BrainChatService
{
    private readonly ILlmProvider _provider;
    private readonly IToolsRegistry _registry;
    private readonly IToolsProvider _toolsProvider;
    private readonly IServiceProvider _services;
    private readonly ILogger<BrainChatService> _logger;
    
    public async Task<ChatResponse> ProcessMessageAsync(
        string userMessage,
        List<ChatMessage> history)
    {
        // 1. Добавить сообщение пользователя
        history.Add(new ChatMessage
        {
            Role = "user",
            Content = userMessage
        });
        
        // 2. Получить все доступные инструменты
        var tools = _toolsProvider.GetOpenAiTools();
        
        // 3. Вызвать AI с инструментами
        var result = await _provider.GenerateWithToolsAsync(
            messages: history,
            tools: tools,
            temperature: 0.7);
        
        if (!result.IsSuccess)
        {
            return new ChatResponse
            {
                Success = false,
                ErrorMessage = result.ErrorMessage
            };
        }
        
        // 4. Добавить ответ AI в историю
        history.Add(result.Message);
        
        // 5. Если есть tool calls - выполнить
        if (result.HasToolCalls)
        {
            foreach (var toolCall in result.Message.ToolCalls)
            {
                _logger.LogInformation(
                    "AI вызывает инструмент: {ToolName} с параметрами: {Args}",
                    toolCall.Function.Name,
                    System.Text.Json.JsonSerializer.Serialize(toolCall.Function.Arguments));
                
                // Выполнить инструмент
                var toolResult = await _registry.ExecuteToolAsync(
                    toolCall.Function.Name,
                    toolCall.Function.Arguments,
                    new ToolExecutionContext
                    {
                        Services = _services
                    });
                
                // Добавить результат в историю (для AI)
                history.Add(new ChatMessage
                {
                    Role = "tool",
                    Content = toolResult.Success 
                        ? toolResult.Result 
                        : $"Ошибка: {toolResult.ErrorMessage}"
                });
            }
            
            // 6. Продолжить диалог (AI ответит с учётом результатов)
            var finalResult = await _provider.GenerateWithToolsAsync(
                messages: history,
                tools: tools,
                temperature: 0.7);
            
            history.Add(finalResult.Message);
            
            return new ChatResponse
            {
                Success = true,
                Message = finalResult.TextContent,
                History = history
            };
        }
        else
        {
            // Просто текстовый ответ
            return new ChatResponse
            {
                Success = true,
                Message = result.TextContent,
                History = history
            };
        }
    }
}
```

### 4.2 UI для чата

```xaml
<!-- Views/BrainChatView.xaml -->
<UserControl>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>      <!-- История чата -->
            <RowDefinition Height="Auto"/>   <!-- Ввод -->
        </Grid.RowDefinitions>
        
        <!-- История сообщений -->
        <ScrollViewer Grid.Row="0" VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding ChatHistory}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Padding="8" 
                                Background="{Binding Background}"
                                Margin="4">
                            <StackPanel>
                                <TextBlock Text="{Binding Sender}" 
                                          FontWeight="Bold"/>
                                <TextBlock Text="{Binding Message}" 
                                          TextWrapping="Wrap"
                                          Margin="0,4,0,0"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
        
        <!-- Ввод сообщения -->
        <Grid Grid.Row="1" Margin="8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            
            <TextBox Grid.Column="0"
                     Text="{Binding UserInput, UpdateSourceTrigger=PropertyChanged}"
                     PlaceholderText="Напишите команду..."
                     KeyDown="TextBox_KeyDown"/>
            
            <Button Grid.Column="1"
                    Content="Отправить"
                    Command="{Binding SendMessageCommand}"
                    Margin="8,0,0,0"/>
        </Grid>
    </Grid>
</UserControl>
```

### 4.3 ViewModel для чата

```csharp
public partial class BrainChatViewModel : ObservableObject
{
    private readonly BrainChatService _chatService;
    private readonly List<ChatMessage> _history = new();
    
    [ObservableProperty]
    private string _userInput = "";
    
    public ObservableCollection<ChatMessageViewModel> ChatHistory { get; } = new();
    
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;
        
        var message = UserInput;
        UserInput = ""; // Очистить поле
        
        // Добавить сообщение пользователя в UI
        ChatHistory.Add(new ChatMessageViewModel
        {
            Sender = "Вы",
            Message = message,
            Background = Brushes.LightBlue
        });
        
        try
        {
            // Отправить в AI
            var response = await _chatService.ProcessMessageAsync(message, _history);
            
            if (response.Success)
            {
                // Добавить ответ AI в UI
                ChatHistory.Add(new ChatMessageViewModel
                {
                    Sender = "AI",
                    Message = response.Message,
                    Background = Brushes.LightGreen
                });
            }
            else
            {
                ChatHistory.Add(new ChatMessageViewModel
                {
                    Sender = "Система",
                    Message = $"Ошибка: {response.ErrorMessage}",
                    Background = Brushes.LightCoral
                });
            }
        }
        catch (Exception ex)
        {
            ChatHistory.Add(new ChatMessageViewModel
            {
                Sender = "Система",
                Message = $"Ошибка: {ex.Message}",
                Background = Brushes.LightCoral
            });
        }
    }
}

public class ChatMessageViewModel
{
    public string Sender { get; set; }
    public string Message { get; set; }
    public Brush Background { get; set; }
}
```

---

## Шаг 5: Пример работы

### Диалог с AI

```
Вы: AI, запусти сбор чеков

[AI решает вызвать инструмент collect_receipts]

AI (tool_call): {
  "name": "collect_receipts",
  "arguments": {}
}

[Система выполняет CollectReceiptsHandler]

Tool Result: 
Сбор завершён:
- Обработано источников: 2
- Найдено чеков: 5
- Распознано: 5
- Сохранено: 3
- Пропущено: 2
- Ошибок: 0

AI: Готово! Запустил сбор чеков. 
    Обработал 2 источника, нашёл 5 чеков, 
    из них 3 новых сохранил в базу данных. 
    2 чека были пропущены (уже есть в системе).
```

### Диалог с параметрами

```
Вы: Собери чеки только из Email

AI (tool_call): {
  "name": "collect_receipts",
  "arguments": {
    "source_names": ["Email"]
  }
}

Tool Result:
Сбор завершён:
- Обработано источников: 1
- Найдено чеков: 3
- ...

AI: Собрал чеки из Email. Нашёл 3 новых чека.
```

---

## Что происходит под капотом

### Последовательность вызовов

```
1. User → BrainChatViewModel.SendMessageAsync()
   ↓
2. BrainChatService.ProcessMessageAsync(message, history)
   ↓
3. ILlmProvider.GenerateWithToolsAsync(messages, tools)
   │
   └─→ AI анализирует сообщение
       AI видит доступные инструменты:
       - collect_receipts
       - add_item_to_list
       - remove_item_from_list
       - ...
       
       AI решает: нужно вызвать collect_receipts
       
   ↓
4. AI возвращает tool_call:
   {
     "name": "collect_receipts",
     "arguments": {}
   }
   ↓
5. BrainChatService вызывает:
   ToolsRegistry.ExecuteToolAsync("collect_receipts", {})
   ↓
6. ToolsRegistry находит Handler:
   CollectReceiptsHandler(arguments, context)
   ↓
7. Handler получает сервис из DI:
   IReceiptCollectionService collectionService = context.Services.GetRequiredService<>()
   ↓
8. Handler вызывает существующую логику:
   collectionService.CollectAsync(...)
   ↓
9. CollectionResult возвращается в Handler
   ↓
10. Handler форматирует результат для AI:
    "Сбор завершён: 3 чека сохранено..."
    ↓
11. Результат добавляется в историю (role: "tool")
    ↓
12. BrainChatService снова вызывает AI:
    GenerateWithToolsAsync(messages + tool_result, tools)
    ↓
13. AI видит результат выполнения инструмента
    AI формирует ответ пользователю:
    "Готово! Запустил сбор чеков. Обработал..."
    ↓
14. Ответ возвращается в UI
```

---

## Преимущества такого подхода

### 1. Переиспользование логики
```csharp
// Одна и та же логика используется:
// - Кнопкой "Collect" в UI
// - Командой AI "запусти сбор"
// - Планировщиком (в будущем)
```

### 2. Централизованная регистрация
```csharp
// Все инструменты в одном месте
SystemToolsRegistration.RegisterTools(registry);
ShoppingListToolsRegistration.RegisterTools(registry);
```

### 3. Расширяемость
```csharp
// Добавить новый инструмент = 3 шага:
1. Определить ToolDefinition
2. Написать Handler
3. Зарегистрировать при старте
```

### 4. Тестируемость
```csharp
// Handler можно тестировать независимо
[Test]
public async Task CollectReceiptsHandler_Should_Return_Success()
{
    var mockService = new Mock<IReceiptCollectionService>();
    mockService.Setup(s => s.CollectAsync(...))
        .ReturnsAsync(new CollectionResult { ReceiptsSaved = 3 });
    
    var context = new ToolExecutionContext
    {
        Services = CreateServiceProvider(mockService.Object)
    };
    
    var result = await CollectReceiptsHandler(new(), context);
    
    Assert.True(result.Success);
    Assert.Contains("3 чека", result.Result);
}
```

---

## Резюме

### Что нужно сделать

1. **Создать структуру Brain в проекте:**
   ```
   SmartBasket.Brain/
   ├── Tools/
   │   ├── ToolsRegistry.cs
   │   ├── ToolsProvider.cs
   │   ├── SystemToolsRegistration.cs
   │   └── ShoppingListToolsRegistration.cs
   ├── Services/
   │   └── BrainChatService.cs
   └── BrainServiceCollectionExtensions.cs
   ```

2. **Обновить ILlmProvider:**
   - Добавить `GenerateWithToolsAsync`
   - Реализовать в `OllamaLlmProvider`

3. **Зарегистрировать в DI:**
   ```csharp
   services.AddSmartBasketBrain();
   ```

4. **Создать UI для чата:**
   - BrainChatView.xaml
   - BrainChatViewModel.cs

5. **Зарегистрировать инструменты:**
   ```csharp
   SystemToolsRegistration.RegisterTools(registry);
   ```

---

**Готов начинать реализацию? С какого шага начнём?** 🚀
