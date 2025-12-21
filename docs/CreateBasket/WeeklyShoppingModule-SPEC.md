# Weekly Shopping Module — Спецификация

> Модуль автоматизации еженедельных закупок для Smart Basket
> Версия: 1.0 MVP
> Дата: Декабрь 2024

---

## 1. Обзор

### Цель
Автоматизировать процесс еженедельных закупок: от анализа чеков до готовой корзины в магазине.

### User Flow
```
[Сформировать корзину] → AI анализирует чеки → Список товаров
        ↓
Чат-корректировка ← → Редактирование списка
        ↓
[Собрать корзины] → Поиск в магазинах → Сравнение цен
        ↓
Выбор магазина → [Оформить] → Ссылка на корзину
```

### Этапы
| # | Этап | Участники | UI |
|---|------|-----------|-----|
| 1 | Drafting | AI + User | Чат + Корзина справа |
| 2 | Planning | AI + Parsers | Прогресс + Лог |
| 3 | Analysis | AI + User | Карточки корзин + Сравнение |
| 4 | Complete | — | Success screen + Ссылка |

---

## 2. Модели данных

### 2.1 ShoppingSession

Главная сущность — сессия покупок. Живёт в памяти для MVP, позже в БД.

```csharp
public class ShoppingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ShoppingSessionState State { get; set; } = ShoppingSessionState.Drafting;
    
    // Этап 1: Черновик
    public List<DraftItem> DraftItems { get; set; } = new();
    
    // Этап 2: Планирование
    public Dictionary<string, StoreSearchResult> StoreResults { get; set; } = new();
    
    // Этап 3: Анализ
    public Dictionary<string, PlannedBasket> PlannedBaskets { get; set; } = new();
    public string? SelectedStore { get; set; }
    
    // Этап 4: Финализация
    public string? CheckoutUrl { get; set; }
    
    // История чата
    public List<ChatMessage> ChatHistory { get; set; } = new();
}

public enum ShoppingSessionState
{
    Drafting,    // Формирование списка
    Planning,    // Поиск в магазинах
    Analyzing,   // Сравнение корзин
    Finalizing,  // Оформление заказа
    Completed    // Готово
}
```

### 2.2 DraftItem

Абстрактный товар в списке покупок (до поиска в магазинах).

```csharp
public class DraftItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";           // "Молоко 2.5%"
    public decimal Quantity { get; set; } = 1;       // 2
    public string Unit { get; set; } = "шт";         // "л", "кг", "шт"
    public string? Category { get; set; }            // "Молочные продукты"
    public string? Note { get; set; }                // "обычно берёте каждые 5 дней"
    public DraftItemSource Source { get; set; }      // FromReceipts / Manual
}

public enum DraftItemSource
{
    FromReceipts,  // Автоматически из чеков
    Manual         // Добавлено пользователем
}
```

### 2.3 StoreSearchResult

Результаты поиска товара в одном магазине.

```csharp
public class StoreSearchResult
{
    public string Store { get; set; } = "";          // "samokat"
    public Dictionary<Guid, List<ProductMatch>> ItemMatches { get; set; } = new();
    public bool IsComplete { get; set; }
    public int FoundCount { get; set; }
    public int TotalCount { get; set; }
}

public class ProductMatch
{
    public string ProductId { get; set; } = "";      // slug для AddToCartAsync
    public string ProductName { get; set; } = "";    // Полное название
    public decimal Price { get; set; }               // Цена за единицу
    public decimal? PackageSize { get; set; }        // 930 (мл)
    public string? PackageUnit { get; set; }         // "мл"
    public bool InStock { get; set; } = true;
    public string? ImageUrl { get; set; }
    public float MatchScore { get; set; }            // 0-1, насколько подходит
    public bool IsSelected { get; set; }             // Выбран AI или пользователем
}
```

### 2.4 PlannedBasket

Собранная корзина для одного магазина.

```csharp
public class PlannedBasket
{
    public string Store { get; set; } = "";
    public string StoreName { get; set; } = "";      // "Самокат"
    public List<PlannedItem> Items { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public int ItemsFound { get; set; }
    public int ItemsTotal { get; set; }
    public bool IsComplete => ItemsFound == ItemsTotal;
    public decimal? EstimatedWeight { get; set; }    // кг
    public string? DeliveryTime { get; set; }        // "15-30 мин"
    public string? DeliveryPrice { get; set; }       // "Бесплатно"
}

public class PlannedItem
{
    public Guid DraftItemId { get; set; }            // Ссылка на DraftItem
    public string DraftItemName { get; set; } = "";  // Для отображения
    public ProductMatch? Match { get; set; }         // Найденный товар (null = не найден)
    public int Quantity { get; set; } = 1;           // Сколько добавить в корзину
    public decimal LineTotal { get; set; }           // Price × Quantity
}
```

---

## 3. Tool: update_basket

### 3.1 JSON Schema

```json
{
  "name": "update_basket",
  "description": "Добавить, удалить или изменить товары в текущем списке покупок. Вызывай после любого изменения списка.",
  "parameters": {
    "type": "object",
    "properties": {
      "operations": {
        "type": "array",
        "description": "Список операций над корзиной",
        "items": {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": ["add", "remove", "update"],
              "description": "Тип операции"
            },
            "name": {
              "type": "string",
              "description": "Название товара"
            },
            "quantity": {
              "type": "number",
              "description": "Количество (для add/update)"
            },
            "unit": {
              "type": "string",
              "description": "Единица измерения: шт, кг, л, г, мл"
            },
            "category": {
              "type": "string",
              "description": "Категория товара"
            }
          },
          "required": ["action", "name"]
        }
      }
    },
    "required": ["operations"]
  }
}
```

### 3.2 Примеры вызовов

**Добавить товары:**
```json
{
  "operations": [
    {"action": "add", "name": "Огурцы", "quantity": 0.5, "unit": "кг", "category": "Овощи"},
    {"action": "add", "name": "Сметана 20%", "quantity": 1, "unit": "шт", "category": "Молочные продукты"}
  ]
}
```

**Удалить товар:**
```json
{
  "operations": [
    {"action": "remove", "name": "Чипсы"}
  ]
}
```

**Изменить количество:**
```json
{
  "operations": [
    {"action": "update", "name": "Молоко 2.5%", "quantity": 3, "unit": "л"}
  ]
}
```

### 3.3 Реализация Handler

```csharp
public class UpdateBasketHandler : IToolHandler
{
    public string Name => "update_basket";
    
    private readonly ShoppingSessionService _sessionService;
    
    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var operations = args.GetProperty("operations").EnumerateArray();
        var results = new List<string>();
        
        foreach (var op in operations)
        {
            var action = op.GetProperty("action").GetString();
            var name = op.GetProperty("name").GetString();
            
            switch (action)
            {
                case "add":
                    var quantity = op.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 1;
                    var unit = op.TryGetProperty("unit", out var u) ? u.GetString() : "шт";
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
                        results.Add($"✓ Изменено: {name} → {newQty} {newUnit}");
                    else
                        results.Add($"✗ Не найдено: {name}");
                    break;
            }
        }
        
        var currentItems = _sessionService.GetCurrentItems();
        return ToolResult.Success(new
        {
            results = results,
            currentItemCount = currentItems.Count,
            items = currentItems.Select(i => $"{i.Name} ({i.Quantity} {i.Unit})").ToList()
        });
    }
}
```

---

## 4. Промпты

### 4.1 Этап 1: Формирование списка

**Системный промпт:**
```
Ты — AI-помощник для формирования списка покупок в приложении Smart Basket.

КОНТЕКСТ:
- Сегодняшняя дата: {{TODAY}}
- У тебя есть доступ к истории покупок пользователя через инструменты query и describe_data
- Ты можешь изменять список покупок через инструмент update_basket

ЗАДАЧА:
1. Проанализируй 2-3 последних чека пользователя
2. Сформируй список товаров, которые пора купить
3. Учитывай частоту покупок и время с последней покупки
4. Группируй товары по категориям

ПРАВИЛА:
- После ЛЮБОГО изменения списка — вызывай update_basket
- Отвечай кратко и по делу
- Если пользователь называет блюдо — предложи ингредиенты
- Если пользователь просит убрать/добавить — сразу делай через update_basket

ТЕКУЩИЙ СПИСОК:
{{CURRENT_ITEMS}}

Начни с анализа чеков, если список пуст.
```

**Инициализирующий промпт (первое сообщение):**
```
Проанализируй последние 2-3 чека и предложи список покупок на неделю.
Учти частоту покупок каждого товара.
```

### 4.2 Этап 2: Планирование (выбор товаров)

**Промпт для AI-выбора товара из результатов поиска:**
```
Выбери наиболее подходящий товар из результатов поиска.

ЗАПРОС ПОЛЬЗОВАТЕЛЯ:
{{DRAFT_ITEM_NAME}} — {{QUANTITY}} {{UNIT}}

РЕЗУЛЬТАТЫ ПОИСКА В {{STORE_NAME}}:
{{SEARCH_RESULTS_JSON}}

КРИТЕРИИ ВЫБОРА (по приоритету):
1. Соответствие названию и типу товара
2. Подходящая фасовка (не слишком много/мало)
3. Наличие на складе
4. Цена (при прочих равных — дешевле)

ИСТОРИЯ ПОКУПОК (если есть):
{{PURCHASE_HISTORY}}

Верни JSON:
{
  "selected_index": 0,
  "quantity_to_add": 2,
  "reasoning": "Краткое объяснение выбора"
}

Если ничего не подходит, верни:
{
  "selected_index": -1,
  "reasoning": "Причина"
}
```

### 4.3 Этап 3: Анализ корзин

**Системный промпт:**
```
Ты — AI-помощник для выбора оптимальной корзины в Smart Basket.

СОБРАННЫЕ КОРЗИНЫ:
{{BASKETS_JSON}}

ИСХОДНЫЙ СПИСОК:
{{DRAFT_ITEMS}}

ЗАДАЧА:
Проанализируй корзины и дай рекомендацию.

УЧИТЫВАЙ:
1. Полнота корзины (все ли товары найдены)
2. Общая стоимость
3. Время доставки
4. Качество подобранных товаров

ФОРМАТ ОТВЕТА:
Кратко (2-3 предложения) объясни какую корзину рекомендуешь и почему.
Если в лучшей по цене корзине не хватает товаров — отметь это.
```

---

## 5. AI Provider

### Выбор провайдера

**Решение:** YandexAgent с Responses API

**Обоснование:**
1. **Единая история** — весь процесс заказа (от анализа чеков до финальной корзины) = одна conversation
2. **Stateful API** — не нужно передавать историю сообщений в каждом запросе
3. **Простота** — не усложняем поддержкой разных провайдеров в Shopping модуле
4. **Tool calling** — YandexAgent поддерживает native function calling

### Архитектура

```
ShoppingChatService (новый, специализированный)
        │
        ▼
   YandexAgentProvider
        │
        ▼
   Responses API (stateful)
        │
   conversation_id хранится в ShoppingSession
```

### Отличия от общего ChatService

| Аспект | ChatService (AI Chat) | ShoppingChatService |
|--------|----------------------|---------------------|
| Провайдер | Любой (выбирает пользователь) | Только YandexAgent |
| История | В памяти ChatService | На стороне Yandex (conversation_id) |
| Tools | query, describe_data, etc. | update_basket + query |
| Жизненный цикл | Пока открыт чат | Пока активна ShoppingSession |

### ShoppingChatService

```csharp
public interface IShoppingChatService
{
    /// <summary>
    /// Начать новую conversation для сессии покупок
    /// </summary>
    Task<string> StartConversationAsync(ShoppingSession session, CancellationToken ct = default);
    
    /// <summary>
    /// Отправить сообщение в текущую conversation
    /// </summary>
    Task<ChatResponse> SendAsync(
        string message, 
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Текущий conversation_id (хранится в ShoppingSession)
    /// </summary>
    string? ConversationId { get; }
}

public class ShoppingChatService : IShoppingChatService
{
    private readonly YandexAgentProvider _provider;
    private readonly IToolExecutor _tools;
    private ShoppingSession? _session;
    
    public async Task<string> StartConversationAsync(ShoppingSession session, CancellationToken ct)
    {
        _session = session;
        
        // Создаём conversation с системным промптом
        var conversationId = await _provider.CreateConversationAsync(
            ShoppingPrompts.GetDraftingSystemPrompt(session.DraftItems),
            ct);
        
        _session.ConversationId = conversationId;
        return conversationId;
    }
    
    public async Task<ChatResponse> SendAsync(
        string message,
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_session?.ConversationId == null)
            throw new InvalidOperationException("Conversation not started");
        
        // Tool-use loop с YandexAgent
        return await _provider.SendWithToolsAsync(
            _session.ConversationId,
            message,
            _tools,
            progress,
            ct);
    }
}
```

### Изменения в ShoppingSession

```csharp
public class ShoppingSession
{
    // ... existing fields ...
    
    /// <summary>
    /// ID conversation в YandexAgent (Responses API)
    /// </summary>
    public string? ConversationId { get; set; }
}
```

### Конфигурация

```json
{
  "Shopping": {
    "AiProvider": "yandex-agent",
    "YandexAgent": {
      "ModelUri": "gpt://...",
      "MaxTokens": 2000
    }
  }
}
```

### Fallback (если YandexAgent недоступен)

Для MVP — просто показываем ошибку:
```
"Для работы модуля закупок требуется настроить YandexAgent в настройках"
```

В будущем можно добавить fallback на Ollama с ручным управлением историей.

---

## 6. Сервисы

### 5.1 ShoppingSessionService

Главный сервис, оркестрирует весь процесс.

```csharp
public interface IShoppingSessionService
{
    // Состояние
    ShoppingSession? CurrentSession { get; }
    event EventHandler<ShoppingSession>? SessionChanged;
    
    // Этап 1: Drafting
    Task<ShoppingSession> StartNewSessionAsync(CancellationToken ct = default);
    void AddItem(string name, decimal quantity, string unit, string? category = null);
    bool RemoveItem(string name);
    bool UpdateItem(string name, decimal quantity, string? unit = null);
    List<DraftItem> GetCurrentItems();
    
    // Этап 2: Planning
    Task StartPlanningAsync(IProgress<PlanningProgress>? progress = null, CancellationToken ct = default);
    
    // Этап 3: Analysis
    PlannedBasket? GetBasket(string store);
    Dictionary<string, PlannedBasket> GetAllBaskets();
    
    // Этап 4: Finalization
    Task<string?> CreateCartAsync(string store, CancellationToken ct = default);
}
```

### 5.2 Интеграция с парсерами

```csharp
public class StoreParserService
{
    private readonly Dictionary<string, IStoreParser> _parsers;
    private readonly IWebViewContext _webViewContext;
    
    public async Task<List<ProductSearchResult>> SearchAsync(
        string store, 
        string query, 
        int limit = 10,
        CancellationToken ct = default)
    {
        var parser = _parsers[store];
        return await parser.SearchAsync(_webViewContext, query, limit, ct);
    }
    
    public async Task<bool> AddToCartAsync(
        string store,
        string productId,
        int quantity,
        CancellationToken ct = default)
    {
        var parser = _parsers[store];
        return await parser.AddToCartAsync(_webViewContext, productId, quantity, ct);
    }
    
    public async Task<string?> GetCartUrlAsync(string store, CancellationToken ct = default)
    {
        var parser = _parsers[store];
        return await parser.GetCartUrlAsync(_webViewContext, ct);
    }
}
```

---

## 7. UI Components

### 6.1 Структура файлов

```
SmartBasket.WPF/
└── Views/
    └── Shopping/
        ├── ShoppingView.xaml              # Главный контейнер
        ├── ShoppingView.xaml.cs
        ├── Components/
        │   ├── DraftBasketPanel.xaml      # Панель корзины (этап 1)
        │   ├── PlanningProgressPanel.xaml # Прогресс поиска (этап 2)
        │   ├── BasketCardsPanel.xaml      # Карточки корзин (этап 3)
        │   ├── ComparisonPanel.xaml       # Сравнение цен (этап 3)
        │   └── CompletionPanel.xaml       # Success screen (этап 4)
        └── ShoppingViewModel.cs
```

### 6.2 ShoppingViewModel

```csharp
public partial class ShoppingViewModel : ObservableObject
{
    private readonly IShoppingSessionService _sessionService;
    private readonly IChatService _chatService;
    
    [ObservableProperty]
    private ShoppingSessionState _state = ShoppingSessionState.Drafting;
    
    [ObservableProperty]
    private ObservableCollection<DraftItem> _draftItems = new();
    
    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();
    
    [ObservableProperty]
    private Dictionary<string, PlannedBasket> _baskets = new();
    
    [ObservableProperty]
    private int _planningProgress;
    
    [ObservableProperty]
    private string _planningStatus = "";
    
    // Commands
    [RelayCommand]
    private async Task StartSessionAsync();
    
    [RelayCommand]
    private async Task SendMessageAsync(string message);
    
    [RelayCommand]
    private async Task StartPlanningAsync();
    
    [RelayCommand]
    private async Task SelectBasketAsync(string store);
    
    [RelayCommand]
    private async Task CreateCartAsync();
}
```

---

## 8. Конфигурация

### 7.1 appsettings.json

```json
{
  "Shopping": {
    "Stores": {
      "samokat": {
        "Enabled": true,
        "SearchLimit": 10
      },
      "lavka": {
        "Enabled": true,
        "SearchLimit": 10
      },
      "kuper": {
        "Enabled": true,
        "SearchLimit": 10,
        "StoreSlug": "auchan"
      }
    },
    "DefaultReceiptsCount": 3,
    "MaxWeightKg": 27
  }
}
```

### 7.2 Конфигурация из парсеров

Парсеры содержат встроенную конфигурацию (`StoreId`, `StoreName`, `BaseUrl`).
При инициализации `ShoppingSessionService`:

```csharp
// Вытягиваем конфигурацию из парсеров
foreach (var parser in _parsers.Values)
{
    var storeConfig = new StoreRuntimeConfig
    {
        StoreId = parser.StoreId,        // "samokat"
        StoreName = parser.StoreName,    // "Самокат"  
        BaseUrl = parser.BaseUrl,        // "https://samokat.ru"
        Color = GetStoreColor(parser.StoreId),
        DeliveryTime = GetDeliveryTime(parser.StoreId)
    };
    
    // Kuper требует Initialize с URL магазина
    if (parser is KuperParser kuper)
    {
        var slug = _settings.Stores["kuper"].StoreSlug ?? "auchan";
        kuper.Initialize($"https://kuper.ru/{slug}");
    }
}
```

### 7.3 Цвета и время доставки (hardcoded)

```csharp
private static readonly Dictionary<string, string> StoreColors = new()
{
    ["samokat"] = "#ff6b35",
    ["lavka"] = "#ffcc00", 
    ["kuper"] = "#00b956"
};

private static readonly Dictionary<string, string> DeliveryTimes = new()
{
    ["samokat"] = "15-30 мин",
    ["lavka"] = "10-20 мин",
    ["kuper"] = "60-90 мин"
};

---

## 9. TODO после MVP

- [ ] Сохранение сессий в БД
- [ ] История сессий
- [ ] Шаблоны списков ("Повторить прошлый")
- [ ] Разбиение корзины при весе > 27 кг
- [ ] Комбинирование магазинов (часть из одного, часть из другого)
- [ ] Push-уведомления "Пора закупиться"
- [ ] Интеграция с календарём (планирование дня доставки)
- [ ] Учёт акций и скидок
- [ ] Feedback loop (анализ что реально купили vs план)
