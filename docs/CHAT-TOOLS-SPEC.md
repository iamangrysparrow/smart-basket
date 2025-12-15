# Smart Basket Chat Tools Specification

> Спецификация системы AI-инструментов для интерактивного чата с данными о покупках.

## Цель

Реализовать набор инструментов, которые AI может вызывать для ответа на вопросы пользователя:
- Какая дата у моего последнего чека?
- Сколько денег я потратил за последний месяц?
- Какие продукты я чаще всего покупаю?
- Предскажи мои следующие покупки
- Сформируй корзину для следующей закупки

---

## Архитектура

```
┌─────────────────────────────────────────────────────────────────┐
│                        ChatView (UI)                            │
│  TextBox + Send Button + Messages ListView                      │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      ChatViewModel                              │
│  - Messages: ObservableCollection<ChatMessage>                  │
│  - SendMessageCommand → ChatService.SendAsync()                 │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                       IChatService                              │
│  - SendAsync(userMessage) → assistantResponse                   │
│  - Использует ILlmProvider + IChatToolExecutor                  │
│  - Управляет tool-use loop (вызов → результат → продолжение)    │
└─────────────────────────────────────────────────────────────────┘
                               │
              ┌────────────────┴────────────────┐
              ▼                                 ▼
┌─────────────────────────┐       ┌─────────────────────────────┐
│     ILlmProvider        │       │    IChatToolExecutor        │
│  (Ollama/Yandex/...)    │       │  - GetToolDefinitions()     │
│  - GenerateAsync()      │       │  - ExecuteAsync(name, args) │
└─────────────────────────┘       └─────────────────────────────┘
                                               │
                    ┌──────────────────────────┼──────────────────────────┐
                    ▼                          ▼                          ▼
           ┌──────────────┐          ┌──────────────┐          ┌──────────────┐
           │ ReceiptTools │          │  ItemTools   │          │ StatsTools   │
           └──────────────┘          └──────────────┘          └──────────────┘
```

---

## Сущности (напоминание)

| Entity | Описание | Ключевые поля |
|--------|----------|---------------|
| **Receipt** | Чек | Shop, ReceiptDate, Total, ReceiptNumber |
| **ReceiptItem** | Позиция в чеке | ReceiptId, ItemId, Quantity, Price, Amount |
| **Item** | Товар (справочник) | Name, ProductId, UnitOfMeasure, Shop |
| **Product** | Категория (иерархия) | Name, ParentId |
| **Label** | Метка | Name |
| **ItemLabel** | Связь Item↔Label | ItemId, LabelId |

---

## Инструменты

### 1. get_receipts

**Описание:** Получить список чеков с фильтрацией и агрегацией.

**Когда использовать:**
- "Покажи мои чеки за последнюю неделю"
- "Какая дата последнего чека?"
- "Сколько я потратил в декабре?"
- "В каких магазинах я покупал?"

**Schema:**
```json
{
  "name": "get_receipts",
  "description": "Получить список чеков. Используй для вопросов о датах чеков, суммах покупок, списке магазинов.",
  "parameters": {
    "type": "object",
    "properties": {
      "date_from": {
        "type": "string",
        "format": "date",
        "description": "Начало периода (YYYY-MM-DD). Если не указано - без ограничения."
      },
      "date_to": {
        "type": "string",
        "format": "date",
        "description": "Конец периода (YYYY-MM-DD). Если не указано - до сегодня."
      },
      "shop": {
        "type": "string",
        "description": "Фильтр по названию магазина (частичное совпадение)."
      },
      "limit": {
        "type": "integer",
        "default": 10,
        "description": "Максимальное количество чеков в ответе."
      },
      "order_by": {
        "type": "string",
        "enum": ["date_desc", "date_asc", "total_desc", "total_asc"],
        "default": "date_desc",
        "description": "Сортировка результатов."
      },
      "include_summary": {
        "type": "boolean",
        "default": true,
        "description": "Включить сводку: общее количество, сумма, средний чек."
      }
    },
    "required": []
  }
}
```

**Пример вызова:**
```json
{
  "name": "get_receipts",
  "arguments": {
    "date_from": "2024-12-01",
    "date_to": "2024-12-31",
    "limit": 5,
    "include_summary": true
  }
}
```

**Пример ответа:**
```json
{
  "receipts": [
    {
      "id": "guid-1",
      "date": "2024-12-10",
      "shop": "Kuper",
      "total": 3450.50,
      "items_count": 12
    },
    {
      "id": "guid-2", 
      "date": "2024-12-05",
      "shop": "Kuper",
      "total": 2100.00,
      "items_count": 8
    }
  ],
  "summary": {
    "total_receipts": 15,
    "total_amount": 45230.50,
    "average_receipt": 3015.37,
    "period": "2024-12-01 - 2024-12-31"
  }
}
```

---

### 2. get_receipt_items

**Описание:** Получить товарные позиции из чеков.

**Когда использовать:**
- "Что я купил в последнем чеке?"
- "Сколько молока я купил за месяц?"
- "Покажи все покупки категории 'Молочные продукты'"

**Schema:**
```json
{
  "name": "get_receipt_items",
  "description": "Получить товарные позиции из чеков. Используй для вопросов о конкретных покупках, количествах, ценах.",
  "parameters": {
    "type": "object",
    "properties": {
      "receipt_id": {
        "type": "string",
        "format": "uuid",
        "description": "ID конкретного чека. Если указан - только позиции этого чека."
      },
      "date_from": {
        "type": "string",
        "format": "date",
        "description": "Начало периода."
      },
      "date_to": {
        "type": "string",
        "format": "date",
        "description": "Конец периода."
      },
      "item_name": {
        "type": "string",
        "description": "Поиск по названию товара (частичное совпадение)."
      },
      "product_name": {
        "type": "string",
        "description": "Фильтр по категории продукта."
      },
      "shop": {
        "type": "string",
        "description": "Фильтр по магазину."
      },
      "limit": {
        "type": "integer",
        "default": 50,
        "description": "Максимальное количество позиций."
      },
      "group_by_item": {
        "type": "boolean",
        "default": false,
        "description": "Группировать по товару с суммированием количества и суммы."
      }
    },
    "required": []
  }
}
```

**Пример вызова (позиции последнего чека):**
```json
{
  "name": "get_receipt_items",
  "arguments": {
    "receipt_id": "guid-of-last-receipt",
    "limit": 100
  }
}
```

**Пример ответа:**
```json
{
  "items": [
    {
      "item_name": "Молоко Parmalat 3.5% 1л",
      "product_name": "Молоко",
      "quantity": 2,
      "price": 89.90,
      "amount": 179.80,
      "receipt_date": "2024-12-10",
      "shop": "Kuper"
    },
    {
      "item_name": "Хлеб Бородинский 400г",
      "product_name": "Хлеб",
      "quantity": 1,
      "price": 65.00,
      "amount": 65.00,
      "receipt_date": "2024-12-10",
      "shop": "Kuper"
    }
  ],
  "total_items": 2,
  "total_amount": 244.80
}
```

**Пример вызова (сгруппировано по товару за месяц):**
```json
{
  "name": "get_receipt_items",
  "arguments": {
    "date_from": "2024-12-01",
    "item_name": "молоко",
    "group_by_item": true
  }
}
```

**Пример ответа:**
```json
{
  "items": [
    {
      "item_name": "Молоко Parmalat 3.5% 1л",
      "product_name": "Молоко",
      "total_quantity": 8,
      "total_amount": 719.20,
      "purchase_count": 4
    },
    {
      "item_name": "Молоко Простоквашино 2.5% 930мл",
      "product_name": "Молоко", 
      "total_quantity": 3,
      "total_amount": 254.70,
      "purchase_count": 2
    }
  ],
  "total_items": 2,
  "grand_total_quantity": 11,
  "grand_total_amount": 973.90
}
```

---

### 3. get_products

**Описание:** Получить иерархию продуктов (категорий).

**Когда использовать:**
- "Какие категории продуктов у меня есть?"
- "Покажи подкатегории 'Молочные продукты'"
- "Сколько товаров в каждой категории?"

**Schema:**
```json
{
  "name": "get_products",
  "description": "Получить иерархию категорий продуктов. Используй для навигации по категориям.",
  "parameters": {
    "type": "object",
    "properties": {
      "parent_id": {
        "type": "string",
        "format": "uuid",
        "description": "ID родительской категории. null = корневые категории."
      },
      "search": {
        "type": "string",
        "description": "Поиск по названию категории."
      },
      "include_children": {
        "type": "boolean",
        "default": false,
        "description": "Включить дочерние категории (1 уровень)."
      },
      "include_items_count": {
        "type": "boolean",
        "default": true,
        "description": "Включить количество товаров в категории."
      }
    },
    "required": []
  }
}
```

**Пример ответа:**
```json
{
  "products": [
    {
      "id": "guid-1",
      "name": "Молочные продукты",
      "items_count": 45,
      "children": [
        {"id": "guid-1a", "name": "Молоко", "items_count": 12},
        {"id": "guid-1b", "name": "Сыр", "items_count": 18},
        {"id": "guid-1c", "name": "Йогурт", "items_count": 15}
      ]
    },
    {
      "id": "guid-2",
      "name": "Хлебобулочные",
      "items_count": 23,
      "children": []
    }
  ],
  "total_products": 2
}
```

---

### 4. get_items

**Описание:** Получить товары из справочника.

**Когда использовать:**
- "Какие товары я покупаю в категории 'Молоко'?"
- "Найди все товары со словом 'сыр'"
- "Какие товары есть в магазине Kuper?"

**Schema:**
```json
{
  "name": "get_items",
  "description": "Получить товары из справочника. Используй для поиска конкретных товаров.",
  "parameters": {
    "type": "object",
    "properties": {
      "product_id": {
        "type": "string",
        "format": "uuid",
        "description": "Фильтр по категории продукта."
      },
      "product_name": {
        "type": "string",
        "description": "Фильтр по названию категории (частичное совпадение)."
      },
      "search": {
        "type": "string",
        "description": "Поиск по названию товара."
      },
      "shop": {
        "type": "string",
        "description": "Фильтр по магазину."
      },
      "limit": {
        "type": "integer",
        "default": 50
      },
      "include_purchase_stats": {
        "type": "boolean",
        "default": false,
        "description": "Включить статистику покупок (сколько раз покупали, последняя покупка)."
      }
    },
    "required": []
  }
}
```

**Пример ответа с статистикой:**
```json
{
  "items": [
    {
      "id": "guid-1",
      "name": "Молоко Parmalat 3.5% 1л",
      "product_name": "Молоко",
      "unit_of_measure": "шт",
      "shop": "Kuper",
      "purchase_stats": {
        "total_purchases": 24,
        "total_quantity": 48,
        "last_purchase": "2024-12-10",
        "avg_price": 89.50
      }
    }
  ],
  "total_items": 1
}
```

---

### 5. get_labels

**Описание:** Получить метки и связанные товары.

**Когда использовать:**
- "Какие метки у меня есть?"
- "Какие товары помечены как 'Для завтрака'?"
- "Покажи все товары с меткой 'Любимое'"

**Schema:**
```json
{
  "name": "get_labels",
  "description": "Получить метки и связанные с ними товары. Используй для работы с пользовательскими метками.",
  "parameters": {
    "type": "object",
    "properties": {
      "label_name": {
        "type": "string",
        "description": "Фильтр по названию метки (частичное совпадение)."
      },
      "include_items": {
        "type": "boolean",
        "default": true,
        "description": "Включить список товаров для каждой метки."
      },
      "items_limit": {
        "type": "integer",
        "default": 10,
        "description": "Максимум товаров на метку."
      }
    },
    "required": []
  }
}
```

**Пример ответа:**
```json
{
  "labels": [
    {
      "id": "guid-1",
      "name": "Для завтрака",
      "items_count": 8,
      "items": [
        {"name": "Молоко Parmalat 3.5% 1л", "product": "Молоко"},
        {"name": "Хлопья Nestle Fitness 300г", "product": "Хлопья"},
        {"name": "Йогурт Danone натуральный 350г", "product": "Йогурт"}
      ]
    },
    {
      "id": "guid-2",
      "name": "Любимое",
      "items_count": 15,
      "items": [
        {"name": "Сыр Hochland плавленый 400г", "product": "Сыр"}
      ]
    }
  ],
  "total_labels": 2
}
```

---

### 6. get_purchase_statistics

**Описание:** Получить статистику покупок для анализа и предсказаний.

**Когда использовать:**
- "Какие продукты я чаще всего покупаю?"
- "Предскажи мои следующие покупки"
- "Сформируй корзину для закупки"
- "Сколько я трачу в месяц на молочные продукты?"

**Schema:**
```json
{
  "name": "get_purchase_statistics",
  "description": "Получить статистику покупок для анализа паттернов и предсказаний. Используй для частотного анализа, предсказаний, формирования корзины.",
  "parameters": {
    "type": "object",
    "properties": {
      "period_days": {
        "type": "integer",
        "default": 90,
        "description": "Период анализа в днях (от сегодня назад)."
      },
      "group_by": {
        "type": "string",
        "enum": ["item", "product", "shop", "day_of_week", "week"],
        "default": "item",
        "description": "Группировка статистики."
      },
      "product_name": {
        "type": "string",
        "description": "Фильтр по категории продукта."
      },
      "min_purchase_count": {
        "type": "integer",
        "default": 2,
        "description": "Минимальное количество покупок для включения в статистику."
      },
      "limit": {
        "type": "integer",
        "default": 20,
        "description": "Топ N позиций."
      },
      "include_prediction": {
        "type": "boolean",
        "default": false,
        "description": "Включить предсказание следующей покупки (на основе средней частоты)."
      }
    },
    "required": []
  }
}
```

**Пример вызова (топ товаров):**
```json
{
  "name": "get_purchase_statistics",
  "arguments": {
    "period_days": 30,
    "group_by": "item",
    "limit": 10,
    "include_prediction": true
  }
}
```

**Пример ответа:**
```json
{
  "period": {
    "from": "2024-11-15",
    "to": "2024-12-15",
    "days": 30
  },
  "statistics": [
    {
      "item_name": "Молоко Parmalat 3.5% 1л",
      "product_name": "Молоко",
      "purchase_count": 8,
      "total_quantity": 16,
      "total_amount": 1438.40,
      "avg_price": 89.90,
      "avg_days_between": 3.75,
      "last_purchase": "2024-12-10",
      "prediction": {
        "next_purchase_date": "2024-12-14",
        "confidence": "high",
        "suggested_quantity": 2
      }
    },
    {
      "item_name": "Хлеб Бородинский 400г",
      "product_name": "Хлеб",
      "purchase_count": 6,
      "total_quantity": 6,
      "total_amount": 390.00,
      "avg_price": 65.00,
      "avg_days_between": 5.0,
      "last_purchase": "2024-12-12",
      "prediction": {
        "next_purchase_date": "2024-12-17",
        "confidence": "medium",
        "suggested_quantity": 1
      }
    }
  ],
  "summary": {
    "total_unique_items": 45,
    "total_purchases": 156,
    "total_spent": 45230.50
  }
}
```

**Пример вызова (по категориям):**
```json
{
  "name": "get_purchase_statistics",
  "arguments": {
    "period_days": 90,
    "group_by": "product",
    "limit": 10
  }
}
```

**Пример ответа:**
```json
{
  "statistics": [
    {
      "product_name": "Молочные продукты",
      "purchase_count": 45,
      "total_amount": 12500.00,
      "percent_of_total": 28.5
    },
    {
      "product_name": "Хлебобулочные",
      "purchase_count": 32,
      "total_amount": 4200.00,
      "percent_of_total": 9.6
    }
  ]
}
```

---

### 7. generate_shopping_list

**Описание:** Сформировать список покупок на основе истории.

**Когда использовать:**
- "Сформируй корзину для следующей закупки"
- "Что мне нужно купить на этой неделе?"
- "Составь список покупок на неделю"

**Schema:**
```json
{
  "name": "generate_shopping_list",
  "description": "Сформировать список покупок на основе истории и паттернов потребления.",
  "parameters": {
    "type": "object",
    "properties": {
      "days_ahead": {
        "type": "integer",
        "default": 7,
        "description": "На сколько дней вперед планировать."
      },
      "include_labels": {
        "type": "array",
        "items": {"type": "string"},
        "description": "Включить товары с этими метками."
      },
      "exclude_labels": {
        "type": "array", 
        "items": {"type": "string"},
        "description": "Исключить товары с этими метками."
      },
      "min_confidence": {
        "type": "string",
        "enum": ["low", "medium", "high"],
        "default": "medium",
        "description": "Минимальный уровень уверенности в предсказании."
      },
      "budget_limit": {
        "type": "number",
        "description": "Ограничение бюджета (опционально)."
      }
    },
    "required": []
  }
}
```

**Пример ответа:**
```json
{
  "shopping_list": [
    {
      "item_name": "Молоко Parmalat 3.5% 1л",
      "product_name": "Молоко",
      "suggested_quantity": 2,
      "estimated_price": 179.80,
      "confidence": "high",
      "reason": "Покупаете каждые 3-4 дня, последняя покупка 5 дней назад",
      "labels": ["Для завтрака"]
    },
    {
      "item_name": "Хлеб Бородинский 400г",
      "product_name": "Хлеб",
      "suggested_quantity": 1,
      "estimated_price": 65.00,
      "confidence": "high",
      "reason": "Покупаете каждые 5 дней, последняя покупка 4 дня назад",
      "labels": []
    },
    {
      "item_name": "Яйца С1 10шт",
      "product_name": "Яйца",
      "suggested_quantity": 1,
      "estimated_price": 120.00,
      "confidence": "medium",
      "reason": "Покупаете каждые 10 дней, последняя покупка 8 дней назад",
      "labels": ["Для завтрака"]
    }
  ],
  "summary": {
    "total_items": 12,
    "estimated_total": 2450.00,
    "planning_period": "2024-12-15 - 2024-12-22"
  }
}
```

---

## Реализация

### Файловая структура

```
src/SmartBasket.Services/
├── Chat/
│   ├── IChatService.cs              # Интерфейс сервиса чата
│   ├── ChatService.cs               # Реализация с tool-use loop
│   ├── IChatToolExecutor.cs         # Интерфейс исполнителя инструментов
│   ├── ChatToolExecutor.cs          # Роутинг вызовов к конкретным tools
│   ├── ChatToolDefinitions.cs       # JSON Schema определения всех tools
│   └── Tools/
│       ├── ReceiptTools.cs          # get_receipts, get_receipt_items
│       ├── ProductTools.cs          # get_products, get_items
│       ├── LabelTools.cs            # get_labels
│       ├── StatisticsTools.cs       # get_purchase_statistics
│       └── ShoppingListTools.cs     # generate_shopping_list

src/SmartBasket.WPF/
├── Views/
│   └── ChatView.xaml                # UI чата
└── ViewModels/
    └── ChatViewModel.cs             # ViewModel для чата
```

### IChatService

```csharp
public interface IChatService
{
    /// <summary>
    /// Отправить сообщение и получить ответ (с автоматическим выполнением tools)
    /// </summary>
    Task<ChatResponse> SendAsync(
        string userMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// История сообщений текущей сессии
    /// </summary>
    IReadOnlyList<ChatMessage> History { get; }
    
    /// <summary>
    /// Очистить историю
    /// </summary>
    void ClearHistory();
}

public record ChatMessage(
    string Role,           // "user", "assistant", "tool"
    string Content,
    DateTime Timestamp,
    string? ToolName = null,
    string? ToolResult = null
);

public record ChatResponse(
    string Content,
    bool Success,
    List<ToolCall>? ToolCalls = null
);

public record ToolCall(
    string Name,
    string Arguments,
    string Result
);
```

### IChatToolExecutor

```csharp
public interface IChatToolExecutor
{
    /// <summary>
    /// Получить определения всех доступных инструментов
    /// </summary>
    IReadOnlyList<ToolDefinition> GetToolDefinitions();
    
    /// <summary>
    /// Выполнить инструмент
    /// </summary>
    Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default);
}

public record ToolDefinition(
    string Name,
    string Description,
    object ParametersSchema  // JSON Schema object
);
```

### Tool-Use Loop в ChatService

```csharp
public async Task<ChatResponse> SendAsync(string userMessage, ...)
{
    _history.Add(new ChatMessage("user", userMessage, DateTime.Now));
    
    var systemPrompt = BuildSystemPrompt();
    var tools = _toolExecutor.GetToolDefinitions();
    
    while (true)
    {
        // 1. Вызвать LLM
        var llmResponse = await _llmProvider.GenerateWithToolsAsync(
            systemPrompt,
            _history,
            tools,
            cancellationToken);
        
        // 2. Если нет tool_use — вернуть ответ
        if (llmResponse.ToolCalls == null || llmResponse.ToolCalls.Count == 0)
        {
            _history.Add(new ChatMessage("assistant", llmResponse.Content, DateTime.Now));
            return new ChatResponse(llmResponse.Content, true);
        }
        
        // 3. Выполнить каждый tool
        var toolResults = new List<ToolCall>();
        foreach (var call in llmResponse.ToolCalls)
        {
            progress?.Report($"Выполняю {call.Name}...");
            
            var result = await _toolExecutor.ExecuteAsync(
                call.Name, 
                call.Arguments,
                cancellationToken);
            
            toolResults.Add(new ToolCall(call.Name, call.Arguments, result));
            
            // Добавить в историю как tool message
            _history.Add(new ChatMessage(
                "tool", 
                result, 
                DateTime.Now, 
                call.Name, 
                result));
        }
        
        // 4. Продолжить цикл — LLM увидит результаты и сформирует ответ
    }
}
```

### Адаптация под разные провайдеры

#### Ollama (JSON в промпте)

```csharp
// Добавить в system prompt:
var toolsJson = JsonSerializer.Serialize(tools);
systemPrompt += $@"

Доступные инструменты:
{toolsJson}

Если нужно использовать инструмент, ответь ТОЛЬКО JSON:
{{""tool"": ""имя_инструмента"", ""arguments"": {{...}}}}

Если инструмент не нужен, отвечай обычным текстом.
";
```

#### Claude API (native tools)

```csharp
// Использовать tools parameter в API
var request = new
{
    model = "claude-3-sonnet",
    messages = history,
    tools = tools.Select(t => new
    {
        name = t.Name,
        description = t.Description,
        input_schema = t.ParametersSchema
    })
};
```

#### OpenAI (function calling)

```csharp
// Использовать functions parameter
var request = new
{
    model = "gpt-4",
    messages = history,
    functions = tools.Select(t => new
    {
        name = t.Name,
        description = t.Description,
        parameters = t.ParametersSchema
    })
};
```

---

## System Prompt

```
Ты — умный ассистент для анализа покупок в приложении Smart Basket.

У тебя есть доступ к базе данных чеков пользователя. Используй инструменты для получения данных.

Правила:
1. Всегда используй инструменты для получения актуальных данных
2. Не выдумывай данные — только то, что вернули инструменты
3. Отвечай кратко и по делу
4. Суммы указывай в рублях
5. Даты форматируй как "10 декабря 2024"

Примеры:
- "Последний чек?" → get_receipts с limit=1
- "Траты за месяц?" → get_receipts с date_from и include_summary=true
- "Что покупаю чаще всего?" → get_purchase_statistics с group_by=item
- "Сформируй корзину" → generate_shopping_list
```

---

## План реализации

### Phase 1: Базовые инструменты (MVP)
1. `IChatToolExecutor` + `ChatToolExecutor`
2. `get_receipts` — базовый запрос чеков
3. `get_receipt_items` — позиции чеков
4. `ChatService` с tool-use loop для Ollama
5. `ChatView` + `ChatViewModel` (простой UI)

### Phase 2: Расширенные инструменты
6. `get_products` — иерархия категорий
7. `get_items` — справочник товаров
8. `get_labels` — метки

### Phase 3: Аналитика и предсказания
9. `get_purchase_statistics` — статистика
10. `generate_shopping_list` — генерация корзины

### Phase 4: Улучшения
11. Поддержка Claude API tools
12. Поддержка OpenAI function calling
13. Streaming ответов в UI
14. Сохранение истории чата

---

## Примеры диалогов

### Пример 1: Простой запрос
```
User: Какая дата у моего последнего чека?

[Tool: get_receipts(limit=1)]
[Result: {"receipts": [{"date": "2024-12-10", "shop": "Kuper", "total": 3450.50}]}]