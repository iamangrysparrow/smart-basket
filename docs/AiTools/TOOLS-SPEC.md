# Smart Basket Tools Specification

> Спецификация системы инструментов для работы с данными о покупках.
> Используется в Chat, Brain, CLI и других модулях.

## Цель

Набор инструментов для доступа к данным и аналитике покупок:
- Получение чеков и позиций
- Иерархия продуктов и товаров
- Метки и связи
- Статистика и предсказания
- Генерация списка покупок

---

## Архитектура

```
┌─────────────────────────────────────────────────────────────────┐
│                      Consumers (кто использует)                 │
├─────────────────┬─────────────────┬─────────────────────────────┤
│   ChatService   │   BrainService  │   CLI / Reports / Jobs      │
└────────┬────────┴────────┬────────┴──────────────┬──────────────┘
         │                 │                       │
         ▼                 ▼                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                    IToolExecutor                                │
│  - GetToolDefinitions() → для AI (schema)                       │
│  - ExecuteAsync(name, args) → универсальный вызов               │
└─────────────────────────────────────────────────────────────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         ▼                     ▼                     ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
│  IToolHandler   │   │  IToolHandler   │   │  IToolHandler   │
│  GetReceipts    │   │  GetStatistics  │   │  GenerateList   │
└─────────────────┘   └─────────────────┘   └─────────────────┘
```

---

## Файловая структура

```
src/SmartBasket.Services/
└── Tools/
    ├── IToolExecutor.cs           # Интерфейс исполнителя
    ├── ToolExecutor.cs            # Реализация (роутер)
    ├── IToolHandler.cs            # Интерфейс одного инструмента
    ├── ToolServiceExtensions.cs   # Регистрация в DI
    ├── Models/
    │   ├── ToolDefinition.cs      # Определение tool для AI
    │   ├── ToolResult.cs          # Результат выполнения
    │   └── Args/                  # Аргументы каждого tool
    │       ├── GetReceiptsArgs.cs
    │       ├── GetReceiptItemsArgs.cs
    │       └── ...
    └── Handlers/
        ├── GetReceiptsHandler.cs
        ├── GetReceiptItemsHandler.cs
        ├── GetProductsHandler.cs
        ├── GetItemsHandler.cs
        ├── GetLabelsHandler.cs
        ├── GetPurchaseStatisticsHandler.cs
        └── GenerateShoppingListHandler.cs
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

**Пример ответа (обычный):**
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
    }
  ],
  "total_items": 1,
  "total_amount": 179.80
}
```

**Пример ответа (group_by_item=true):**
```json
{
  "items": [
    {
      "item_name": "Молоко Parmalat 3.5% 1л",
      "product_name": "Молоко",
      "total_quantity": 8,
      "total_amount": 719.20,
      "purchase_count": 4
    }
  ],
  "total_items": 1,
  "grand_total_amount": 719.20
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
        {"id": "guid-1b", "name": "Сыр", "items_count": 18}
      ]
    }
  ],
  "total_products": 1
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
        "description": "Включить статистику покупок."
      }
    },
    "required": []
  }
}
```

**Пример ответа:**
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

**Schema:**
```json
{
  "name": "get_labels",
  "description": "Получить метки и связанные с ними товары.",
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
        {"name": "Хлопья Nestle Fitness 300г", "product": "Хлопья"}
      ]
    }
  ],
  "total_labels": 1
}
```

---

### 6. get_purchase_statistics

**Описание:** Статистика покупок для анализа и предсказаний.

**Когда использовать:**
- "Какие продукты я чаще всего покупаю?"
- "Предскажи мои следующие покупки"
- "Сколько я трачу в месяц на молочные продукты?"

**Schema:**
```json
{
  "name": "get_purchase_statistics",
  "description": "Статистика покупок для анализа паттернов и предсказаний.",
  "parameters": {
    "type": "object",
    "properties": {
      "period_days": {
        "type": "integer",
        "default": 90,
        "description": "Период анализа в днях."
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
        "description": "Минимальное количество покупок для включения."
      },
      "limit": {
        "type": "integer",
        "default": 20,
        "description": "Топ N позиций."
      },
      "include_prediction": {
        "type": "boolean",
        "default": false,
        "description": "Включить предсказание следующей покупки."
      }
    },
    "required": []
  }
}
```

**Пример ответа:**
```json
{
  "period": {
    "from": "2024-09-15",
    "to": "2024-12-15",
    "days": 90
  },
  "statistics": [
    {
      "item_name": "Молоко Parmalat 3.5% 1л",
      "product_name": "Молоко",
      "purchase_count": 24,
      "total_quantity": 48,
      "total_amount": 4312.00,
      "avg_days_between": 3.75,
      "last_purchase": "2024-12-10",
      "prediction": {
        "next_purchase_date": "2024-12-14",
        "confidence": "high",
        "suggested_quantity": 2
      }
    }
  ],
  "summary": {
    "total_unique_items": 45,
    "total_spent": 45230.50
  }
}
```

---

### 7. generate_shopping_list

**Описание:** Сформировать список покупок на основе истории.

**Когда использовать:**
- "Сформируй корзину для следующей закупки"
- "Что мне нужно купить на этой неделе?"

**Schema:**
```json
{
  "name": "generate_shopping_list",
  "description": "Сформировать список покупок на основе истории и паттернов.",
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
        "description": "Минимальный уровень уверенности."
      },
      "budget_limit": {
        "type": "number",
        "description": "Ограничение бюджета."
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

## План реализации

### Phase 1: Core + MVP
1. `IToolExecutor`, `ToolExecutor`, `IToolHandler`
2. `ToolDefinition`, `ToolResult`
3. `GetReceiptsHandler`
4. `GetReceiptItemsHandler`
5. DI регистрация

### Phase 2: Справочники
6. `GetProductsHandler`
7. `GetItemsHandler`
8. `GetLabelsHandler`

### Phase 3: Аналитика
9. `GetPurchaseStatisticsHandler`
10. `GenerateShoppingListHandler`

### Phase 4: Интеграция
11. `ChatService` с tool-use loop
12. Адаптеры под разные LLM провайдеры
