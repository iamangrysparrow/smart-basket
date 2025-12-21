# Weekly Shopping Module — План реализации

> Пошаговая инструкция для Claude Code
> Референс: WeeklyShoppingModule-SPEC.md, мокапы в mockups/

---

## Общие правила

1. **Один шаг = рабочее приложение** — после каждого шага код компилируется и работает
2. **Не ломать существующее** — модуль Shopping независим от остального UI
3. **DynamicResource для цветов** — использовать существующую тему
4. **Логирование везде** — каждый метод логирует вход, параметры, результат

---

## Шаг 0: Меню Smart Basket

### Задача
Преобразовать логотип Smart Basket в выпадающее меню. Освободить тулбар от кнопок модулей.

### Текущее состояние
```
[🛒 Smart Basket]  Чеки  Продукты    [AI Chat] [Collect] [🗑] [📋] [⚙] [☀]
```

### Целевое состояние
```
[🛒 Smart Basket ▾]  Чеки  Продукты              7 чеков  [📋] [⚙] [☀]
        │
        ▼
   ┌────────────────────────┐
   │  🛒 Закупки            │  ← (пока disabled, реализуем в шаге 4)
   │  💬 AI Чат             │
   │  ─────────────────     │
   │  📥 Собрать чеки       │
   │  🗑️ Удалить чек        │
   │  ─────────────────     │
   │  ❓ О программе        │
   └────────────────────────┘
```

### Что остаётся в тулбаре
- **Вкладки:** Чеки | Продукты
- **Статистика:** 7 чеков
- **Быстрые toggle:** 📋 Лог | ⚙️ Настройки | ☀️/🌙 Тема

### Действия

**1. Создать стиль для Menu в SharedStyles.xaml:**

```xml
<!-- Стиль для главного меню приложения -->
<Style x:Key="AppMenuStyle" TargetType="Menu">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Padding" Value="0"/>
</Style>

<Style x:Key="AppMenuItemStyle" TargetType="MenuItem">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Padding" Value="8,6"/>
    <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
</Style>
```

**2. Заменить логотип на Menu в MainWindow.xaml:**

```xml
<!-- Logo как выпадающее меню -->
<Menu Style="{StaticResource AppMenuStyle}" VerticalAlignment="Center">
    <MenuItem Style="{StaticResource AppMenuItemStyle}">
        <MenuItem.Header>
            <StackPanel Orientation="Horizontal">
                <Path Data="{StaticResource IconCart}" 
                      Fill="{DynamicResource AccentBrush}" 
                      Width="16" Height="16" Stretch="Uniform"
                      VerticalAlignment="Center"/>
                <TextBlock Text="Smart Basket" 
                           FontWeight="SemiBold" 
                           FontSize="14"
                           Foreground="{DynamicResource AccentBrush}"
                           Margin="8,0,0,0"
                           VerticalAlignment="Center"/>
                <Path Data="{StaticResource IconArrowDown}" 
                      Fill="{DynamicResource ForegroundSecondaryBrush}"
                      Width="8" Height="8" Stretch="Uniform"
                      Margin="6,0,0,0"
                      VerticalAlignment="Center"/>
            </StackPanel>
        </MenuItem.Header>
        
        <!-- Модули -->
        <MenuItem Header="🛒  Закупки" 
                  Click="OpenShopping_Click"
                  IsEnabled="False"
                  ToolTip="В разработке"/>
        <MenuItem Header="💬  AI Чат" 
                  Click="OpenAiChat_Click"/>
        
        <Separator/>
        
        <!-- Действия с чеками -->
        <MenuItem Header="📥  Собрать чеки" 
                  Click="CollectReceipts_Click"/>
        <MenuItem Header="🗑️  Удалить выбранный чек" 
                  Click="DeleteReceipt_Click"
                  IsEnabled="{Binding HasSelectedReceipt}"/>
        
        <Separator/>
        
        <!-- Справка -->
        <MenuItem Header="❓  О программе" 
                  Click="ShowAbout_Click"/>
    </MenuItem>
</Menu>
```

**3. Убрать из тулбара кнопки модулей:**

Удалить:
- Кнопку "AI Chat"
- Кнопку "Collect" (сбор чеков)
- Кнопку удаления чека (🗑️)
- Панель `AiChatTabPanel` (вкладка AI Chat в тулбаре)

Оставить:
- Вкладки `Чеки` | `Продукты`
- Статистику `7 чеков`
- Кнопку лога 📋
- Кнопку настроек ⚙️
- Кнопку темы ☀️/🌙

**4. Обновить code-behind:**

```csharp
// MainWindow.xaml.cs

private void OpenShopping_Click(object sender, RoutedEventArgs e)
{
    // TODO: Реализовать в шаге 4
    // Пока показываем заглушку
    MessageBox.Show("Модуль закупок в разработке", "Smart Basket", 
        MessageBoxButton.OK, MessageBoxImage.Information);
}

private void ShowAbout_Click(object sender, RoutedEventArgs e)
{
    var version = Assembly.GetExecutingAssembly().GetName().Version;
    MessageBox.Show(
        $"Smart Basket v{version}\n\n" +
        "Автоматизация учёта покупок\n" +
        "и формирования списка закупок",
        "О программе",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
}
```

**5. Добавить иконку IconArrowDown в Icons.xaml (если нет):**

```xml
<Geometry x:Key="IconArrowDown">M7.41 8.59L12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41z</Geometry>
```

### Критерий готовности
- [ ] Логотип Smart Basket открывает выпадающее меню
- [ ] Меню содержит: Закупки (disabled), AI Чат, Собрать чеки, Удалить чек, О программе
- [ ] AI Чат открывается из меню (как раньше)
- [ ] Сбор чеков работает из меню
- [ ] Тулбар содержит только: вкладки, статистику, лог, настройки, тему
- [ ] Приложение компилируется и работает

---

## Шаг 1: Модели данных

### Задача
Создать модели для сессии покупок.

### Файлы
```
src/SmartBasket.Core/Shopping/
├── ShoppingSession.cs
├── DraftItem.cs
├── StoreSearchResult.cs
├── ProductMatch.cs
├── PlannedBasket.cs
└── PlannedItem.cs
```

### Действия

**1. Создать папку и файлы моделей:**

```csharp
// src/SmartBasket.Core/Shopping/ShoppingSession.cs
namespace SmartBasket.Core.Shopping;

public class ShoppingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ShoppingSessionState State { get; set; } = ShoppingSessionState.Drafting;
    
    public List<DraftItem> DraftItems { get; set; } = new();
    public Dictionary<string, StoreSearchResult> StoreResults { get; set; } = new();
    public Dictionary<string, PlannedBasket> PlannedBaskets { get; set; } = new();
    
    public string? SelectedStore { get; set; }
    public string? CheckoutUrl { get; set; }
}

public enum ShoppingSessionState
{
    Drafting,
    Planning,
    Analyzing,
    Finalizing,
    Completed
}
```

```csharp
// src/SmartBasket.Core/Shopping/DraftItem.cs
namespace SmartBasket.Core.Shopping;

public class DraftItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "шт";
    public string? Category { get; set; }
    public string? Note { get; set; }
    public DraftItemSource Source { get; set; } = DraftItemSource.Manual;
}

public enum DraftItemSource
{
    FromReceipts,
    Manual
}
```

```csharp
// src/SmartBasket.Core/Shopping/ProductMatch.cs
namespace SmartBasket.Core.Shopping;

public class ProductMatch
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? PackageSize { get; set; }
    public string? PackageUnit { get; set; }
    public bool InStock { get; set; } = true;
    public string? ImageUrl { get; set; }
    public float MatchScore { get; set; }
    public bool IsSelected { get; set; }
}
```

```csharp
// src/SmartBasket.Core/Shopping/StoreSearchResult.cs
namespace SmartBasket.Core.Shopping;

public class StoreSearchResult
{
    public string Store { get; set; } = "";
    public string StoreName { get; set; } = "";
    public Dictionary<Guid, List<ProductMatch>> ItemMatches { get; set; } = new();
    public bool IsComplete { get; set; }
    public int FoundCount { get; set; }
    public int TotalCount { get; set; }
}
```

```csharp
// src/SmartBasket.Core/Shopping/PlannedBasket.cs
namespace SmartBasket.Core.Shopping;

public class PlannedBasket
{
    public string Store { get; set; } = "";
    public string StoreName { get; set; } = "";
    public List<PlannedItem> Items { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public int ItemsFound { get; set; }
    public int ItemsTotal { get; set; }
    public bool IsComplete => ItemsFound == ItemsTotal;
    public decimal? EstimatedWeight { get; set; }
    public string? DeliveryTime { get; set; }
    public string? DeliveryPrice { get; set; }
}

public class PlannedItem
{
    public Guid DraftItemId { get; set; }
    public string DraftItemName { get; set; } = "";
    public ProductMatch? Match { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal LineTotal { get; set; }
}
```

### Критерий готовности
- [ ] Все файлы созданы в SmartBasket.Core/Shopping/
- [ ] Проект компилируется без ошибок
- [ ] Модели доступны из других проектов

---

## Шаг 2: Tool update_basket

### Задача
Создать инструмент для AI, чтобы изменять список покупок.

### Файлы
```
src/SmartBasket.Services/Tools/
├── Args/UpdateBasketArgs.cs
└── Handlers/UpdateBasketHandler.cs
```

### Действия

**1. Создать модель аргументов:**

```csharp
// src/SmartBasket.Services/Tools/Args/UpdateBasketArgs.cs
namespace SmartBasket.Services.Tools.Args;

public class UpdateBasketArgs
{
    public List<BasketOperation> Operations { get; set; } = new();
}

public class BasketOperation
{
    public string Action { get; set; } = "";  // "add", "remove", "update"
    public string Name { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? Category { get; set; }
}
```

**2. Создать Handler:**

```csharp
// src/SmartBasket.Services/Tools/Handlers/UpdateBasketHandler.cs
namespace SmartBasket.Services.Tools.Handlers;

public class UpdateBasketHandler : IToolHandler
{
    public string Name => "update_basket";
    
    public ToolDefinition GetDefinition() => new()
    {
        Name = Name,
        Description = "Добавить, удалить или изменить товары в текущем списке покупок",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                operations = new
                {
                    type = "array",
                    description = "Список операций над корзиной",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            action = new { type = "string", @enum = new[] { "add", "remove", "update" } },
                            name = new { type = "string", description = "Название товара" },
                            quantity = new { type = "number", description = "Количество" },
                            unit = new { type = "string", description = "Единица: шт, кг, л, г, мл" },
                            category = new { type = "string", description = "Категория товара" }
                        },
                        required = new[] { "action", "name" }
                    }
                }
            },
            required = new[] { "operations" }
        }
    };
    
    // ExecuteAsync будет использовать ShoppingSessionService
    // Пока заглушка — реализуем в шаге 3
    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        throw new NotImplementedException("Will be implemented with ShoppingSessionService");
    }
}
```

**3. Зарегистрировать в DI:**

В `ToolServiceExtensions.cs` добавить:
```csharp
services.AddScoped<IToolHandler, UpdateBasketHandler>();
```

### Критерий готовности
- [ ] Handler создан и зарегистрирован
- [ ] ToolDefinition возвращает корректную JSON schema
- [ ] Проект компилируется

---

## Шаг 3: ShoppingSessionService

### Задача
Создать сервис для управления сессией покупок.

### Файлы
```
src/SmartBasket.Services/Shopping/
├── IShoppingSessionService.cs
├── ShoppingSessionService.cs
└── ShoppingConfiguration.cs
```

### Действия

**1. Интерфейс:**

```csharp
// src/SmartBasket.Services/Shopping/IShoppingSessionService.cs
namespace SmartBasket.Services.Shopping;

public interface IShoppingSessionService
{
    ShoppingSession? CurrentSession { get; }
    event EventHandler<ShoppingSession>? SessionChanged;
    event EventHandler<DraftItem>? ItemAdded;
    event EventHandler<DraftItem>? ItemRemoved;
    event EventHandler<DraftItem>? ItemUpdated;
    
    // Этап 1
    Task<ShoppingSession> StartNewSessionAsync(CancellationToken ct = default);
    void AddItem(string name, decimal quantity, string unit, string? category = null);
    bool RemoveItem(string name);
    bool UpdateItem(string name, decimal quantity, string? unit = null);
    List<DraftItem> GetCurrentItems();
    
    // Этап 2
    Task StartPlanningAsync(IProgress<PlanningProgress>? progress = null, CancellationToken ct = default);
    
    // Этап 3
    PlannedBasket? GetBasket(string store);
    Dictionary<string, PlannedBasket> GetAllBaskets();
    
    // Этап 4
    Task<string?> CreateCartAsync(string store, CancellationToken ct = default);
}

public class PlanningProgress
{
    public string Store { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int CurrentItem { get; set; }
    public int TotalItems { get; set; }
    public int CurrentStore { get; set; }
    public int TotalStores { get; set; }
    public bool ItemFound { get; set; }
    public string? MatchedProduct { get; set; }
    public decimal? Price { get; set; }
}
```

**2. Реализация (этап 1 только):**

```csharp
// src/SmartBasket.Services/Shopping/ShoppingSessionService.cs
namespace SmartBasket.Services.Shopping;

public class ShoppingSessionService : IShoppingSessionService
{
    private readonly ILogger<ShoppingSessionService> _logger;
    private ShoppingSession? _currentSession;
    
    public ShoppingSession? CurrentSession => _currentSession;
    
    public event EventHandler<ShoppingSession>? SessionChanged;
    public event EventHandler<DraftItem>? ItemAdded;
    public event EventHandler<DraftItem>? ItemRemoved;
    public event EventHandler<DraftItem>? ItemUpdated;
    
    public ShoppingSessionService(ILogger<ShoppingSessionService> logger)
    {
        _logger = logger;
    }
    
    public Task<ShoppingSession> StartNewSessionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting new shopping session");
        
        _currentSession = new ShoppingSession();
        SessionChanged?.Invoke(this, _currentSession);
        
        return Task.FromResult(_currentSession);
    }
    
    public void AddItem(string name, decimal quantity, string unit, string? category = null)
    {
        if (_currentSession == null)
            throw new InvalidOperationException("No active session");
        
        var item = new DraftItem
        {
            Name = name,
            Quantity = quantity,
            Unit = unit,
            Category = category,
            Source = DraftItemSource.Manual
        };
        
        _currentSession.DraftItems.Add(item);
        _logger.LogDebug("Added item: {Name} {Quantity} {Unit}", name, quantity, unit);
        
        ItemAdded?.Invoke(this, item);
    }
    
    public bool RemoveItem(string name)
    {
        if (_currentSession == null) return false;
        
        var item = _currentSession.DraftItems
            .FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        
        if (item == null) return false;
        
        _currentSession.DraftItems.Remove(item);
        _logger.LogDebug("Removed item: {Name}", name);
        
        ItemRemoved?.Invoke(this, item);
        return true;
    }
    
    public bool UpdateItem(string name, decimal quantity, string? unit = null)
    {
        if (_currentSession == null) return false;
        
        var item = _currentSession.DraftItems
            .FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        
        if (item == null) return false;
        
        item.Quantity = quantity;
        if (unit != null) item.Unit = unit;
        
        _logger.LogDebug("Updated item: {Name} → {Quantity} {Unit}", name, quantity, unit ?? item.Unit);
        
        ItemUpdated?.Invoke(this, item);
        return true;
    }
    
    public List<DraftItem> GetCurrentItems()
    {
        return _currentSession?.DraftItems.ToList() ?? new List<DraftItem>();
    }
    
    // Этапы 2-4 — заглушки, реализуем позже
    public Task StartPlanningAsync(IProgress<PlanningProgress>? progress = null, CancellationToken ct = default)
    {
        throw new NotImplementedException("Step 5");
    }
    
    public PlannedBasket? GetBasket(string store) => _currentSession?.PlannedBaskets.GetValueOrDefault(store);
    
    public Dictionary<string, PlannedBasket> GetAllBaskets() 
        => _currentSession?.PlannedBaskets ?? new();
    
    public Task<string?> CreateCartAsync(string store, CancellationToken ct = default)
    {
        throw new NotImplementedException("Step 6");
    }
}
```

**3. Зарегистрировать в DI:**

```csharp
services.AddSingleton<IShoppingSessionService, ShoppingSessionService>();
```

**4. Доделать UpdateBasketHandler:**

```csharp
public class UpdateBasketHandler : IToolHandler
{
    private readonly IShoppingSessionService _sessionService;
    
    public UpdateBasketHandler(IShoppingSessionService sessionService)
    {
        _sessionService = sessionService;
    }
    
    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var operations = args.GetProperty("operations").EnumerateArray();
        var results = new List<string>();
        
        foreach (var op in operations)
        {
            var action = op.GetProperty("action").GetString()!;
            var name = op.GetProperty("name").GetString()!;
            
            switch (action)
            {
                case "add":
                    var quantity = op.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 1;
                    var unit = op.TryGetProperty("unit", out var u) ? u.GetString() ?? "шт" : "шт";
                    var category = op.TryGetProperty("category", out var c) ? c.GetString() : null;
                    
                    _sessionService.AddItem(name, quantity, unit, category);
                    results.Add($"✓ Добавлено: {name} {quantity} {unit}");
                    break;
                    
                case "remove":
                    if (_sessionService.RemoveItem(name))
                        results.Add($"✓ Удалено: {name}");
                    else
                        results.Add($"✗ Не найдено: {name}");
                    break;
                    
                case "update":
                    var newQty = op.GetProperty("quantity").GetDecimal();
                    var newUnit = op.TryGetProperty("unit", out var nu) ? nu.GetString() : null;
                    
                    if (_sessionService.UpdateItem(name, newQty, newUnit))
                        results.Add($"✓ Изменено: {name} → {newQty} {newUnit ?? ""}".Trim());
                    else
                        results.Add($"✗ Не найдено: {name}");
                    break;
            }
        }
        
        var items = _sessionService.GetCurrentItems();
        
        return ToolResult.Success(new
        {
            results,
            itemCount = items.Count,
            items = items.Select(i => new { i.Name, i.Quantity, i.Unit, i.Category }).ToList()
        });
    }
}
```

### Критерий готовности
- [ ] Сервис создан и зарегистрирован
- [ ] UpdateBasketHandler работает с сервисом
- [ ] События ItemAdded/Removed/Updated срабатывают
- [ ] Проект компилируется

---

## Шаг 4: UI — ShoppingView (этап 1)

### Задача
Создать UI для этапа формирования списка: чат слева, корзина справа.

### Файлы
```
src/SmartBasket.WPF/Views/Shopping/
├── ShoppingView.xaml
├── ShoppingView.xaml.cs
└── ShoppingViewModel.cs
```

### Действия

**1. Создать ViewModel:**

```csharp
// src/SmartBasket.WPF/Views/Shopping/ShoppingViewModel.cs
namespace SmartBasket.WPF.Views.Shopping;

public partial class ShoppingViewModel : ObservableObject
{
    private readonly IShoppingSessionService _sessionService;
    private readonly IChatService _chatService;
    private readonly ILogger<ShoppingViewModel> _logger;
    
    [ObservableProperty]
    private ShoppingSessionState _state = ShoppingSessionState.Drafting;
    
    [ObservableProperty]
    private ObservableCollection<DraftItem> _draftItems = new();
    
    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messages = new();
    
    [ObservableProperty]
    private string _userInput = "";
    
    [ObservableProperty]
    private bool _isProcessing;
    
    [ObservableProperty]
    private bool _canProceed;
    
    public ShoppingViewModel(
        IShoppingSessionService sessionService,
        IChatService chatService,
        ILogger<ShoppingViewModel> logger)
    {
        _sessionService = sessionService;
        _chatService = chatService;
        _logger = logger;
        
        // Подписка на события
        _sessionService.ItemAdded += OnItemAdded;
        _sessionService.ItemRemoved += OnItemRemoved;
        _sessionService.ItemUpdated += OnItemUpdated;
    }
    
    [RelayCommand]
    private async Task StartSessionAsync()
    {
        _logger.LogInformation("Starting shopping session");
        
        await _sessionService.StartNewSessionAsync();
        State = ShoppingSessionState.Drafting;
        
        // Отправить инициализирующий промпт
        await SendInitialPromptAsync();
    }
    
    private async Task SendInitialPromptAsync()
    {
        IsProcessing = true;
        
        try
        {
            // TODO: Отправить промпт для анализа чеков
            // Пока заглушка — добавим тестовые данные
            _sessionService.AddItem("Молоко 2.5%", 2, "л", "Молочные продукты");
            _sessionService.AddItem("Яйца С1", 10, "шт", "Яйца");
            _sessionService.AddItem("Батон нарезной", 1, "шт", "Хлеб");
            
            Messages.Add(new ChatMessageViewModel
            {
                Role = "assistant",
                Content = "Проанализировал последние чеки. Сформировал список из 3 товаров.\n\nЧто-то добавить или убрать?",
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            IsProcessing = false;
        }
    }
    
    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;
        
        var message = UserInput.Trim();
        UserInput = "";
        
        Messages.Add(new ChatMessageViewModel
        {
            Role = "user",
            Content = message,
            Timestamp = DateTime.Now
        });
        
        IsProcessing = true;
        
        try
        {
            // TODO: Отправить в ChatService с update_basket tool
            // Пока заглушка
            await Task.Delay(500);
            
            Messages.Add(new ChatMessageViewModel
            {
                Role = "assistant",
                Content = "Понял, обновляю список...",
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            IsProcessing = false;
        }
    }
    
    private bool CanSendMessage() => !IsProcessing && !string.IsNullOrWhiteSpace(UserInput);
    
    [RelayCommand(CanExecute = nameof(CanStartPlanning))]
    private async Task StartPlanningAsync()
    {
        _logger.LogInformation("Starting planning phase");
        State = ShoppingSessionState.Planning;
        // TODO: Реализовать в шаге 5
    }
    
    private bool CanStartPlanning() => DraftItems.Count > 0 && State == ShoppingSessionState.Drafting;
    
    // Event handlers
    private void OnItemAdded(object? sender, DraftItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            DraftItems.Add(item);
            UpdateCanProceed();
        });
    }
    
    private void OnItemRemoved(object? sender, DraftItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = DraftItems.FirstOrDefault(i => i.Id == item.Id);
            if (existing != null) DraftItems.Remove(existing);
            UpdateCanProceed();
        });
    }
    
    private void OnItemUpdated(object? sender, DraftItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = DraftItems.FirstOrDefault(i => i.Id == item.Id);
            if (existing != null)
            {
                var index = DraftItems.IndexOf(existing);
                DraftItems[index] = item;
            }
        });
    }
    
    private void UpdateCanProceed()
    {
        CanProceed = DraftItems.Count > 0;
        StartPlanningCommand.NotifyCanExecuteChanged();
    }
}

public class ChatMessageViewModel
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? ToolCall { get; set; }
}
```

**2. Создать View (см. мокап 01-drafting.html):**

XAML будет большой — основные элементы:
- Grid с двумя колонками (чат | корзина)
- ItemsControl для сообщений чата
- TextBox + Button для ввода
- ItemsControl для DraftItems с группировкой по Category
- Кнопка "Собрать корзины"

**3. Добавить навигацию в MainWindow:**

Добавить пункт меню или кнопку "Еженедельные закупки" → открывает ShoppingView.

### Критерий готовности
- [ ] ShoppingView отображается
- [ ] Кнопка "Сформировать корзину" запускает сессию
- [ ] Список товаров отображается справа
- [ ] Товары группируются по категориям
- [ ] Кнопки +/- изменяют количество
- [ ] Чат принимает сообщения (пока без AI)

---

## Шаг 5: Интеграция с парсерами (этап 2)

### Задача
Реализовать поиск товаров в магазинах с видимым WebView.

### UI Layout (этап Planning)

```
┌─────────────────────────────────────────────────────────────────┐
│  🔍 Поиск товаров в магазинах              [Этап 2 из 3]        │
├─────────────────────────────────┬───────────────────────────────┤
│                                 │  📋 Прогресс                  │
│                                 │  ───────────────────────────  │
│        [ WebView2 ]             │  Карточки магазинов с         │
│                                 │  прогрессом поиска            │
│    (показывает сайт магазина    │  ───────────────────────────  │
│     где идёт поиск)             │  📜 Лог операций              │
│                                 │                               │
├─────────────────────────────────┴───────────────────────────────┤
│  Progress bar                                                   │
└─────────────────────────────────────────────────────────────────┘
```

**Зачем видимый WebView:**
- Визуальный контроль работы парсера
- Легче отлаживать проблемы
- Видно если нужна капча или авторизация
- Скроем когда всё стабильно заработает

### Действия

**1. Добавить WebView2 в ShoppingView:**

```xml
<!-- ShoppingView.xaml — состояние Planning -->
<Grid x:Name="PlanningPanel" Visibility="{Binding IsPlanningState}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="380"/>
    </Grid.ColumnDefinitions>
    
    <!-- WebView слева -->
    <Border Grid.Column="0" 
            BorderBrush="{DynamicResource BorderDefaultBrush}" 
            BorderThickness="1"
            Margin="16">
        <wv2:WebView2 x:Name="ParserWebView"/>
    </Border>
    
    <!-- Прогресс справа -->
    <Grid Grid.Column="1">
        <!-- Карточки магазинов + лог -->
    </Grid>
</Grid>
```

**2. Создать WebViewContext и передать в парсеры:**

```csharp
// В ShoppingViewModel
private WebView2 _webView;
private IWebViewContext _webViewContext;

public void SetWebView(WebView2 webView)
{
    _webView = webView;
    _webViewContext = new WebViewContext(webView);
}

// В code-behind ShoppingView.xaml.cs
public ShoppingView()
{
    InitializeComponent();
    Loaded += async (s, e) =>
    {
        await ParserWebView.EnsureCoreWebView2Async();
        if (DataContext is ShoppingViewModel vm)
        {
            vm.SetWebView(ParserWebView);
        }
    };
}
```

**3. Инициализация парсеров из их конфигурации:**

```csharp
// В ShoppingSessionService
private readonly Dictionary<string, IStoreParser> _parsers = new();
private readonly Dictionary<string, StoreRuntimeConfig> _storeConfigs = new();

public ShoppingSessionService(
    IEnumerable<IStoreParser> parsers,
    IOptions<ShoppingSettings> settings,
    ILogger<ShoppingSessionService> logger)
{
    _logger = logger;
    _settings = settings.Value;
    
    // Инициализируем парсеры и вытягиваем их конфигурацию
    foreach (var parser in parsers)
    {
        var storeId = parser.StoreId;
        
        // Проверяем включён ли магазин в настройках
        if (!_settings.Stores.TryGetValue(storeId, out var storeSettings) || !storeSettings.Enabled)
            continue;
        
        // Kuper требует Initialize
        if (parser is KuperParser kuper)
        {
            var slug = storeSettings.StoreSlug ?? "auchan";
            kuper.Initialize($"https://kuper.ru/{slug}");
        }
        
        _parsers[storeId] = parser;
        
        // Сохраняем runtime конфигурацию
        _storeConfigs[storeId] = new StoreRuntimeConfig
        {
            StoreId = storeId,
            StoreName = parser.StoreName,
            BaseUrl = parser is KuperParser k ? k.StoreBaseUrl : parser.BaseUrl,
            SearchLimit = storeSettings.SearchLimit,
            Color = GetStoreColor(storeId),
            DeliveryTime = GetDeliveryTime(storeId)
        };
    }
}
```

**4. Реализовать StartPlanningAsync:**

```csharp
public async Task StartPlanningAsync(
    IWebViewContext webViewContext,
    IProgress<PlanningProgress>? progress = null, 
    CancellationToken ct = default)
{
    if (_currentSession == null) throw new InvalidOperationException("No active session");
    
    _currentSession.State = ShoppingSessionState.Planning;
    SessionChanged?.Invoke(this, _currentSession);
    
    var items = _currentSession.DraftItems;
    var stores = _storeConfigs.Keys.ToList();
    
    var storeIndex = 0;
    foreach (var storeId in stores)
    {
        storeIndex++;
        var config = _storeConfigs[storeId];
        var parser = _parsers[storeId];
        
        _logger.LogInformation("Starting search in {Store} ({StoreName})", storeId, config.StoreName);
        
        var searchResult = new StoreSearchResult
        {
            Store = storeId,
            StoreName = config.StoreName,
            TotalCount = items.Count
        };
        
        var itemIndex = 0;
        foreach (var item in items)
        {
            itemIndex++;
            ct.ThrowIfCancellationRequested();
            
            progress?.Report(new PlanningProgress
            {
                Store = storeId,
                StoreName = config.StoreName,
                ItemName = item.Name,
                CurrentItem = itemIndex,
                TotalItems = items.Count,
                CurrentStore = storeIndex,
                TotalStores = stores.Count,
                Status = PlanningStatus.Searching
            });
            
            try
            {
                var results = await parser.SearchAsync(webViewContext, item.Name, config.SearchLimit, ct);
                
                var matches = results.Select((r, i) => new ProductMatch
                {
                    ProductId = r.Id,
                    ProductName = r.Name,
                    Price = r.Price,
                    PackageSize = r.Quantity,
                    PackageUnit = r.Unit,
                    InStock = r.InStock,
                    ImageUrl = r.ImageUrl,
                    MatchScore = 1.0f - (i * 0.1f),
                    IsSelected = i == 0
                }).ToList();
                
                searchResult.ItemMatches[item.Id] = matches;
                if (matches.Any(m => m.InStock)) searchResult.FoundCount++;
                
                progress?.Report(new PlanningProgress
                {
                    Store = storeId,
                    StoreName = config.StoreName,
                    ItemName = item.Name,
                    CurrentItem = itemIndex,
                    TotalItems = items.Count,
                    CurrentStore = storeIndex,
                    TotalStores = stores.Count,
                    Status = matches.Any() ? PlanningStatus.Found : PlanningStatus.NotFound,
                    MatchedProduct = matches.FirstOrDefault()?.ProductName,
                    Price = matches.FirstOrDefault()?.Price
                });
                
                _logger.LogDebug("Found {Count} matches for '{Item}' in {Store}", 
                    matches.Count, item.Name, storeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search '{Item}' in {Store}", item.Name, storeId);
                searchResult.ItemMatches[item.Id] = new List<ProductMatch>();
                
                progress?.Report(new PlanningProgress
                {
                    Store = storeId,
                    StoreName = config.StoreName,
                    ItemName = item.Name,
                    CurrentItem = itemIndex,
                    TotalItems = items.Count,
                    CurrentStore = storeIndex,
                    TotalStores = stores.Count,
                    Status = PlanningStatus.Error,
                    ErrorMessage = ex.Message
                });
            }
        }
        
        searchResult.IsComplete = true;
        _currentSession.StoreResults[storeId] = searchResult;
        
        // Формируем PlannedBasket
        var basket = BuildPlannedBasket(storeId, config, searchResult);
        _currentSession.PlannedBaskets[storeId] = basket;
        
        _logger.LogInformation("Completed {Store}: {Found}/{Total} items found", 
            storeId, searchResult.FoundCount, searchResult.TotalCount);
    }
    
    _currentSession.State = ShoppingSessionState.Analyzing;
    SessionChanged?.Invoke(this, _currentSession);
}

private PlannedBasket BuildPlannedBasket(string storeId, StoreRuntimeConfig config, StoreSearchResult searchResult)
{
    var items = _currentSession!.DraftItems;
    var plannedItems = new List<PlannedItem>();
    decimal total = 0;
    
    foreach (var item in items)
    {
        var matches = searchResult.ItemMatches.GetValueOrDefault(item.Id) ?? new();
        var selected = matches.FirstOrDefault(m => m.IsSelected && m.InStock);
        
        var lineTotal = selected != null ? selected.Price * (int)item.Quantity : 0;
        total += lineTotal;
        
        plannedItems.Add(new PlannedItem
        {
            DraftItemId = item.Id,
            DraftItemName = item.Name,
            Match = selected,
            Quantity = (int)item.Quantity,
            LineTotal = lineTotal
        });
    }
    
    return new PlannedBasket
    {
        Store = storeId,
        StoreName = config.StoreName,
        Items = plannedItems,
        TotalPrice = total,
        ItemsFound = plannedItems.Count(i => i.Match != null),
        ItemsTotal = plannedItems.Count,
        DeliveryTime = config.DeliveryTime,
        DeliveryPrice = "Бесплатно"  // TODO: определять по сумме
    };
}
```

**5. Добавить enum для статуса:**

```csharp
public enum PlanningStatus
{
    Pending,
    Searching,
    Found,
    NotFound,
    Error
}

public class PlanningProgress
{
    public string Store { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int CurrentItem { get; set; }
    public int TotalItems { get; set; }
    public int CurrentStore { get; set; }
    public int TotalStores { get; set; }
    public PlanningStatus Status { get; set; }
    public string? MatchedProduct { get; set; }
    public decimal? Price { get; set; }
    public string? ErrorMessage { get; set; }
    
    public int TotalProgress => (CurrentStore - 1) * TotalItems + CurrentItem;
    public int TotalOperations => TotalStores * TotalItems;
    public double ProgressPercent => TotalOperations > 0 ? (double)TotalProgress / TotalOperations * 100 : 0;
}
```

**6. Модель StoreRuntimeConfig:**

```csharp
public class StoreRuntimeConfig
{
    public string StoreId { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public int SearchLimit { get; set; } = 10;
    public string? Color { get; set; }
    public string? DeliveryTime { get; set; }
    public bool IsAuthorized { get; set; }
}
```

### Критерий готовности
- [ ] WebView2 отображается в левой части экрана
- [ ] Парсеры инициализируются из своей конфигурации
- [ ] Kuper получает правильный StoreSlug из настроек
- [ ] Поиск запускается последовательно по всем магазинам
- [ ] Прогресс отображается в UI (карточки + лог)
- [ ] Видно какой магазин сейчас обрабатывается в WebView
- [ ] Результаты сохраняются в StoreResults
- [ ] PlannedBaskets формируются с ценами
- [ ] Progress bar показывает общий прогресс

---

## Шаг 6: Анализ и оформление (этапы 3-4)

### Задача
Показать сравнение корзин и оформить заказ.

### Действия

**1. UI для анализа (см. мокап 03-analysis.html):**
- Карточки корзин с ценами
- Сравнительная таблица
- AI-рекомендация
- Кнопка "Оформить заказ"

**2. Реализовать CreateCartAsync:**

```csharp
public async Task<string?> CreateCartAsync(string store, CancellationToken ct = default)
{
    if (_currentSession == null) throw new InvalidOperationException("No active session");
    
    var basket = _currentSession.PlannedBaskets.GetValueOrDefault(store);
    if (basket == null) throw new ArgumentException($"No basket for store {store}");
    
    var parser = _parserFactory.GetParser(store);
    
    // Очистить корзину
    await parser.ClearCartAsync(_webViewContext, ct);
    
    // Добавить товары
    foreach (var item in basket.Items.Where(i => i.Match != null))
    {
        await parser.AddToCartAsync(_webViewContext, item.Match!.ProductId, item.Quantity, ct);
    }
    
    // Получить URL
    var url = await parser.GetCartUrlAsync(_webViewContext, ct);
    
    _currentSession.CheckoutUrl = url;
    _currentSession.SelectedStore = store;
    _currentSession.State = ShoppingSessionState.Completed;
    
    SessionChanged?.Invoke(this, _currentSession);
    
    return url;
}
```

**3. UI для завершения (см. мокап 04-complete.html):**
- Success screen
- Ссылка на корзину магазина
- Timeline статусов
- Кнопка "Новая корзина"

### Критерий готовности
- [ ] Карточки корзин отображаются
- [ ] Сравнение цен работает
- [ ] Оформление добавляет товары в корзину магазина
- [ ] URL корзины открывается в браузере

---

## Шаг 7: ShoppingChatService (YandexAgent)

### Задача
Создать специализированный сервис чата для Shopping модуля с YandexAgent.

### Почему отдельный сервис?

| Аспект | Общий ChatService | ShoppingChatService |
|--------|------------------|---------------------|
| Провайдер | Любой | Только YandexAgent |
| История | В памяти | Responses API (stateful) |
| Жизненный цикл | Пока открыт чат | Привязан к ShoppingSession |

### Файлы

```
src/SmartBasket.Services/Shopping/
├── IShoppingChatService.cs
└── ShoppingChatService.cs
```

### Действия

**1. Создать интерфейс:**

```csharp
// src/SmartBasket.Services/Shopping/IShoppingChatService.cs
namespace SmartBasket.Services.Shopping;

public interface IShoppingChatService
{
    /// <summary>
    /// Начать новую conversation для сессии покупок
    /// </summary>
    Task<string> StartConversationAsync(ShoppingSession session, CancellationToken ct = default);
    
    /// <summary>
    /// Отправить сообщение (streaming + tool calling)
    /// </summary>
    Task<ChatResponse> SendAsync(
        string message, 
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Отправить инициализирующий промпт (анализ чеков)
    /// </summary>
    Task<ChatResponse> SendInitialPromptAsync(
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default);
    
    string? ConversationId { get; }
}
```

**2. Реализация:**

```csharp
// src/SmartBasket.Services/Shopping/ShoppingChatService.cs
namespace SmartBasket.Services.Shopping;

public class ShoppingChatService : IShoppingChatService
{
    private readonly ILlmProvider _provider;
    private readonly IToolExecutor _tools;
    private readonly IShoppingSessionService _sessionService;
    private readonly ILogger<ShoppingChatService> _logger;
    
    private ShoppingSession? _session;
    
    public string? ConversationId => _session?.ConversationId;
    
    public ShoppingChatService(
        IAiProviderFactory providerFactory,
        IToolExecutor tools,
        IShoppingSessionService sessionService,
        ILogger<ShoppingChatService> logger)
    {
        // Используем только YandexAgent
        _provider = providerFactory.GetProvider("yandex-agent");
        _tools = tools;
        _sessionService = sessionService;
        _logger = logger;
    }
    
    public async Task<string> StartConversationAsync(ShoppingSession session, CancellationToken ct)
    {
        _session = session;
        _logger.LogInformation("Starting conversation for session {SessionId}", session.Id);
        
        // Создаём conversation с системным промптом
        var systemPrompt = ShoppingPrompts.GetDraftingSystemPrompt(session.DraftItems);
        
        // YandexAgent Responses API — создаёт stateful conversation
        var conversationId = await _provider.CreateConversationAsync(systemPrompt, ct);
        
        _session.ConversationId = conversationId;
        _logger.LogInformation("Created conversation {ConversationId}", conversationId);
        
        return conversationId;
    }
    
    public async Task<ChatResponse> SendInitialPromptAsync(
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default)
    {
        const string initialPrompt = """
            Проанализируй последние 2-3 чека и предложи список покупок на неделю.
            Учти частоту покупок каждого товара.
            После анализа сразу вызови update_basket с предложенными товарами.
            """;
        
        return await SendAsync(initialPrompt, progress, ct);
    }
    
    public async Task<ChatResponse> SendAsync(
        string message,
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_session?.ConversationId == null)
            throw new InvalidOperationException("Conversation not started. Call StartConversationAsync first.");
        
        _logger.LogInformation("Sending message to conversation {ConversationId}: {Message}", 
            _session.ConversationId, message.Length > 100 ? message[..100] + "..." : message);
        
        // Tool-use loop
        var tools = _tools.GetToolDefinitions()
            .Where(t => t.Name is "update_basket" or "query" or "describe_data")
            .ToList();
        
        const int maxIterations = 5;
        
        for (int i = 0; i < maxIterations; i++)
        {
            var result = await _provider.SendToConversationAsync(
                _session.ConversationId,
                message,
                tools,
                progress,
                ct);
            
            if (!result.IsSuccess)
            {
                _logger.LogError("Provider returned error: {Error}", result.ErrorMessage);
                return new ChatResponse("", false, result.ErrorMessage);
            }
            
            // Если нет tool calls — это финальный ответ
            if (!result.HasToolCalls)
            {
                progress?.Report(new ChatProgress(ChatProgressType.Complete));
                return new ChatResponse(result.Response ?? "", true);
            }
            
            // Обрабатываем tool calls
            foreach (var call in result.ToolCalls!)
            {
                progress?.Report(new ChatProgress(
                    ChatProgressType.ToolCall,
                    ToolName: call.Name,
                    ToolArgs: call.Arguments));
                
                var toolResult = await _tools.ExecuteAsync(call.Name, call.Arguments, ct);
                
                progress?.Report(new ChatProgress(
                    ChatProgressType.ToolResult,
                    ToolName: call.Name,
                    ToolResult: toolResult.JsonData,
                    ToolSuccess: toolResult.Success));
                
                // Отправляем результат обратно в conversation
                await _provider.SendToolResultAsync(
                    _session.ConversationId,
                    call.Id,
                    toolResult.JsonData,
                    ct);
            }
            
            // После tool calls делаем ещё один запрос без сообщения
            // чтобы получить ответ модели
            message = "";
        }
        
        _logger.LogWarning("Max iterations reached");
        return new ChatResponse("", false, "Превышено количество итераций");
    }
}
```

**3. Проверка доступности YandexAgent:**

```csharp
// В ShoppingViewModel или при старте модуля
private async Task<bool> CheckYandexAgentAvailableAsync()
{
    try
    {
        var provider = _providerFactory.GetProvider("yandex-agent");
        return provider != null && await provider.IsAvailableAsync();
    }
    catch
    {
        return false;
    }
}

// Если недоступен — показываем сообщение
if (!await CheckYandexAgentAvailableAsync())
{
    ShowError("Для работы модуля закупок требуется настроить YandexAgent в настройках");
    return;
}
```

**4. Зарегистрировать в DI:**

```csharp
services.AddScoped<IShoppingChatService, ShoppingChatService>();
```

**5. Подключить к ViewModel:**

```csharp
// ShoppingViewModel
private readonly IShoppingChatService _chatService;

[RelayCommand]
private async Task StartSessionAsync()
{
    var session = await _sessionService.StartNewSessionAsync();
    
    // Начинаем conversation
    await _chatService.StartConversationAsync(session);
    
    // Отправляем инициализирующий промпт
    IsProcessing = true;
    try
    {
        var response = await _chatService.SendInitialPromptAsync(_progressReporter);
        // AI проанализирует чеки и вызовет update_basket
    }
    finally
    {
        IsProcessing = false;
    }
}

[RelayCommand]
private async Task SendMessageAsync()
{
    if (string.IsNullOrWhiteSpace(UserInput)) return;
    
    var message = UserInput;
    UserInput = "";
    
    AddUserMessage(message);
    
    IsProcessing = true;
    try
    {
        var response = await _chatService.SendAsync(message, _progressReporter);
        // Ответ добавится через progress events
    }
    finally
    {
        IsProcessing = false;
    }
}
```

### Критерий готовности
- [ ] ShoppingChatService создан и использует YandexAgent
- [ ] Conversation создаётся при старте сессии
- [ ] Tool calling работает (update_basket вызывается)
- [ ] Streaming ответов отображается в UI
- [ ] При отсутствии YandexAgent показывается понятная ошибка
- [ ] Проект компилируется

---

## Финальная проверка

После всех шагов проверить полный цикл:

1. [ ] Нажать "Сформировать корзину"
2. [ ] AI предлагает список из чеков
3. [ ] Написать "добавь огурцы" — товар добавляется
4. [ ] Написать "убери чипсы" — товар удаляется
5. [ ] Нажать "Собрать корзины"
6. [ ] Видеть прогресс поиска
7. [ ] Видеть карточки корзин с ценами
8. [ ] Выбрать магазин → нажать "Оформить"
9. [ ] Открыть ссылку → увидеть товары в корзине магазина
