# Задача: Упрощённая система Tools для AI Chat

## Контекст

Текущая система с 7 специализированными инструментами не работает — модели не хватает понимания структуры данных, она не может делать гибкие запросы и "рассуждать".

**Новый подход:** 2 универсальных инструмента вместо 7 специализированных.

## Прочитай перед началом

1. `CLAUDE.md`, `WPF_RULES.md`
2. `src/SmartBasket.Data/SmartBasketDbContext.cs` — контекст БД
3. `src/SmartBasket.Core/Entities/` — все сущности
4. `src/SmartBasket.Services/Tools/` — текущая реализация (будет заменена)

## Архитектура

### Два инструмента

| Tool | Описание |
|------|----------|
| `describe_data` | Схема БД + связанные примеры данных |
| `query` | Универсальный SELECT с фильтрами |

### Структура файлов

```
src/SmartBasket.Services/Tools/
├── IToolExecutor.cs          # оставить как есть
├── ToolExecutor.cs           # оставить как есть
├── IToolHandler.cs           # оставить как есть
├── ToolServiceExtensions.cs  # упростить — 2 handler'а
├── Models/
│   ├── ToolDefinition.cs     # оставить
│   ├── ToolResult.cs         # оставить
│   └── QueryArgs.cs          # NEW
└── Handlers/
    ├── DescribeDataHandler.cs   # NEW
    └── QueryHandler.cs          # NEW
```

**Удалить старые handlers:** GetReceiptsHandler, GetReceiptItemsHandler, GetProductsHandler, GetItemsHandler, GetLabelsHandler, GetPurchaseStatisticsHandler, GenerateShoppingListHandler и их Args классы.

---

## Инструмент 1: describe_data

### Назначение

Дать модели полное представление о структуре БД и реальных данных. Вызывается один раз в начале сессии или когда модель "не понимает" данные.

### Параметры

Нет параметров.

### Что возвращает

```json
{
  "schema": {
    "tables": [
      {
        "name": "receipts",
        "description": "Чеки из магазинов",
        "columns": [
          {"name": "id", "type": "uuid", "description": "PK"},
          {"name": "date", "type": "timestamp", "description": "Дата чека"},
          {"name": "shop", "type": "string", "description": "Название магазина"},
          {"name": "total", "type": "decimal", "description": "Сумма чека"},
          {"name": "receipt_number", "type": "string", "description": "Номер чека"},
          {"name": "items_count", "type": "int", "description": "Количество позиций"}
        ]
      },
      {
        "name": "receipt_items",
        "description": "Товарные позиции в чеках",
        "columns": [
          {"name": "id", "type": "uuid", "description": "PK"},
          {"name": "receipt_id", "type": "uuid", "description": "FK → receipts.id"},
          {"name": "item_id", "type": "uuid", "description": "FK → items.id"},
          {"name": "quantity", "type": "decimal", "description": "Количество"},
          {"name": "price", "type": "decimal", "description": "Цена за единицу"},
          {"name": "amount", "type": "decimal", "description": "Сумма позиции"}
        ]
      },
      {
        "name": "items",
        "description": "Справочник уникальных товаров",
        "columns": [
          {"name": "id", "type": "uuid", "description": "PK"},
          {"name": "name", "type": "string", "description": "Название товара"},
          {"name": "product_id", "type": "uuid", "description": "FK → products.id (категория)"}
        ]
      },
      {
        "name": "products",
        "description": "Категории товаров (иерархия)",
        "columns": [
          {"name": "id", "type": "uuid", "description": "PK"},
          {"name": "name", "type": "string", "description": "Название категории"},
          {"name": "parent_id", "type": "uuid?", "description": "FK → products.id (родитель), null = корень"}
        ]
      },
      {
        "name": "labels",
        "description": "Пользовательские метки",
        "columns": [
          {"name": "id", "type": "uuid", "description": "PK"},
          {"name": "name", "type": "string", "description": "Название метки"},
          {"name": "color", "type": "string", "description": "Цвет метки"}
        ]
      },
      {
        "name": "item_labels",
        "description": "Связь товаров с метками (M:N)",
        "columns": [
          {"name": "item_id", "type": "uuid", "description": "FK → items.id"},
          {"name": "label_id", "type": "uuid", "description": "FK → labels.id"}
        ]
      }
    ],
    "relationships": [
      "receipts.id ← receipt_items.receipt_id (1:N)",
      "items.id ← receipt_items.item_id (1:N)",
      "products.id ← items.product_id (1:N)",
      "products.id ← products.parent_id (self-reference, иерархия)",
      "items.id ↔ labels.id через item_labels (M:N)"
    ]
  },
  "examples": [
    {
      "description": "Пример 1: Товар с полной цепочкой связей",
      "item": {
        "id": "...",
        "name": "Молоко 1,5% пастеризованное 930 мл Простоквашино БЗМЖ"
      },
      "product_hierarchy": [
        {"id": "...", "name": "Молоко", "level": 2},
        {"id": "...", "name": "Молочные продукты", "level": 1}
      ],
      "labels": [
        {"id": "...", "name": "Завтрак", "color": "#FFD700"},
        {"id": "...", "name": "Для детей", "color": "#87CEEB"}
      ],
      "receipt_items": [
        {
          "receipt_id": "...",
          "receipt_date": "2025-12-03",
          "receipt_shop": "АШАН",
          "quantity": 4,
          "price": 93.99,
          "amount": 375.96
        },
        {
          "receipt_id": "...",
          "receipt_date": "2025-11-26",
          "receipt_shop": "АШАН",
          "quantity": 2,
          "price": 93.99,
          "amount": 187.98
        }
      ]
    },
    // ... ещё 2 примера
  ],
  "statistics": {
    "total_receipts": 6,
    "total_items": 87,
    "total_products": 15,
    "total_labels": 5,
    "date_range": "2024-09-08 — 2025-12-03"
  }
}
```

### Алгоритм выбора примеров

**Цель:** найти 3 товара с максимально богатыми связями.

**Критерии (в порядке приоритета):**
1. Товар ДОЛЖЕН иметь хотя бы одну метку (через item_labels)
2. Товар ДОЛЖЕН иметь категорию (product_id не null)
3. Категория должна иметь максимальную глубину иерархии
4. Товар должен быть в нескольких чеках (больше receipt_items)

**SQL-логика:**
```sql
-- Найти товары с метками и глубокой иерархией
WITH product_depth AS (
  -- Рекурсивно вычислить глубину каждой категории
  WITH RECURSIVE tree AS (
    SELECT id, name, parent_id, 1 as depth
    FROM products WHERE parent_id IS NULL
    UNION ALL
    SELECT p.id, p.name, p.parent_id, t.depth + 1
    FROM products p JOIN tree t ON p.parent_id = t.id
  )
  SELECT id, depth FROM tree
),
item_stats AS (
  SELECT 
    i.id,
    i.name,
    i.product_id,
    pd.depth as product_depth,
    COUNT(DISTINCT il.label_id) as labels_count,
    COUNT(DISTINCT ri.receipt_id) as receipts_count
  FROM items i
  LEFT JOIN product_depth pd ON i.product_id = pd.id
  LEFT JOIN item_labels il ON i.id = il.item_id
  LEFT JOIN receipt_items ri ON i.id = ri.item_id
  GROUP BY i.id, i.name, i.product_id, pd.depth
)
SELECT * FROM item_stats
WHERE labels_count > 0           -- ОБЯЗАТЕЛЬНО с метками
  AND product_id IS NOT NULL     -- ОБЯЗАТЕЛЬНО с категорией
ORDER BY 
  product_depth DESC NULLS LAST, -- Максимальная глубина
  receipts_count DESC,           -- Больше покупок
  labels_count DESC              -- Больше меток
LIMIT 3;
```

**Для каждого найденного товара загрузить:**
1. Полную цепочку категорий (от товара до корня)
2. Все связанные метки
3. Все receipt_items с информацией о чеке (date, shop)

---

## Инструмент 2: query

### Назначение

Универсальный SELECT к любой таблице с безопасными фильтрами.

### Параметры (QueryArgs)

```json
{
  "name": "query",
  "description": "Выполнить SELECT запрос к таблице",
  "parameters": {
    "type": "object",
    "properties": {
      "table": {
        "type": "string",
        "enum": ["receipts", "receipt_items", "items", "products", "labels", "item_labels"],
        "description": "Таблица для запроса"
      },
      "columns": {
        "type": "array",
        "items": {"type": "string"},
        "description": "Список колонок (пусто = все колонки)"
      },
      "where": {
        "type": "array",
        "description": "Условия фильтрации (AND между условиями)",
        "items": {
          "type": "object",
          "properties": {
            "column": {"type": "string"},
            "op": {
              "type": "string",
              "enum": ["=", "!=", ">", "<", ">=", "<=", "ILIKE", "IN", "IS NULL", "IS NOT NULL"]
            },
            "value": {"type": ["string", "number", "boolean", "array", "null"]}
          },
          "required": ["column", "op"]
        }
      },
      "order_by": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "column": {"type": "string"},
            "direction": {"type": "string", "enum": ["ASC", "DESC"]}
          }
        }
      },
      "limit": {
        "type": "integer",
        "default": 20,
        "maximum": 100
      }
    },
    "required": ["table"]
  }
}
```

### Примеры вызовов

**Найти товары с "кур" в названии:**
```json
{
  "table": "items",
  "where": [{"column": "name", "op": "ILIKE", "value": "%кур%"}],
  "limit": 10
}
```

**Последние 5 чеков:**
```json
{
  "table": "receipts",
  "order_by": [{"column": "date", "direction": "DESC"}],
  "limit": 5
}
```

**Позиции конкретного чека:**
```json
{
  "table": "receipt_items",
  "where": [{"column": "receipt_id", "op": "=", "value": "ca7a740e-3f99-456c-95ea-55a93cef042c"}]
}
```

**Товары в категории "Молочные продукты":**
```json
{
  "table": "items",
  "where": [{"column": "product_id", "op": "=", "value": "6e421b7f-440b-411a-bf7a-17e8491d7fa7"}]
}
```

**Чеки из АШАН за ноябрь:**
```json
{
  "table": "receipts",
  "where": [
    {"column": "shop", "op": "ILIKE", "value": "%ашан%"},
    {"column": "date", "op": ">=", "value": "2025-11-01"},
    {"column": "date", "op": "<", "value": "2025-12-01"}
  ]
}
```

### Формат ответа

```json
{
  "table": "items",
  "columns": ["id", "name", "product_id"],
  "rows": [
    {"id": "...", "name": "Филе куриное охл.", "product_id": "..."},
    {"id": "...", "name": "Бедро куриное", "product_id": "..."}
  ],
  "total_rows": 2,
  "truncated": false
}
```

### Реализация QueryHandler

```csharp
public class QueryHandler : IToolHandler
{
    private readonly SmartBasketDbContext _db;
    
    // Whitelist таблиц и колонок для безопасности
    private static readonly Dictionary<string, HashSet<string>> AllowedColumns = new()
    {
        ["receipts"] = new() { "id", "date", "shop", "total", "receipt_number", "items_count" },
        ["receipt_items"] = new() { "id", "receipt_id", "item_id", "quantity", "price", "amount" },
        ["items"] = new() { "id", "name", "product_id" },
        ["products"] = new() { "id", "name", "parent_id" },
        ["labels"] = new() { "id", "name", "color" },
        ["item_labels"] = new() { "item_id", "label_id" }
    };
    
    public async Task<ToolResult> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<QueryArgs>(argsJson);
        
        // Валидация таблицы
        if (!AllowedColumns.ContainsKey(args.Table))
            return ToolResult.Error($"Неизвестная таблица: {args.Table}");
        
        // Валидация колонок
        var allowedCols = AllowedColumns[args.Table];
        var columns = args.Columns?.Count > 0 
            ? args.Columns.Where(c => allowedCols.Contains(c)).ToList()
            : allowedCols.ToList();
        
        // Построить запрос через EF Core
        // НЕ использовать raw SQL — строить через LINQ/Expression
        
        var result = await ExecuteQueryAsync(args.Table, columns, args.Where, args.OrderBy, args.Limit, ct);
        return ToolResult.Ok(result);
    }
}
```

**ВАЖНО:** Не использовать `FromSqlRaw` — строить запросы через EF Core для безопасности.

---

## System Prompt для Chat

```
Ты — ассистент для анализа покупок. У тебя есть доступ к базе данных чеков.

ДОСТУПНЫЕ ИНСТРУМЕНТЫ:

1. describe_data — получить схему БД и примеры данных. 
   Вызови ОДИН РАЗ в начале, чтобы понять структуру.

2. query — выполнить SELECT запрос к таблице.
   Параметры:
   - table: receipts | receipt_items | items | products | labels | item_labels
   - where: [{column, op, value}] — фильтры (AND)
   - order_by: [{column, direction}] — сортировка
   - limit: число (макс 100)
   
   Операторы: =, !=, >, <, >=, <=, ILIKE (поиск подстроки), IN, IS NULL, IS NOT NULL

ПРАВИЛА:

1. Если не знаешь структуру данных — сначала вызови describe_data
2. Для поиска по части слова используй ILIKE с %: {"op": "ILIKE", "value": "%кур%"}
3. Для связей между таблицами — делай последовательные запросы:
   - Сначала найди ID в одной таблице
   - Потом используй эти ID для фильтра в другой
4. Даты в формате YYYY-MM-DD
5. После получения данных — СРАЗУ отвечай пользователю, не повторяй запросы

ПРИМЕР:
Пользователь: "Сколько я потратил на молочные продукты?"
1. query(products, where: [{column: "name", op: "ILIKE", value: "%молоч%"}]) → получил product_id
2. query(items, where: [{column: "product_id", op: "=", value: "..."}]) → получил item_id'ы
3. query(receipt_items, where: [{column: "item_id", op: "IN", value: [...]}]) → суммирую amount
4. Отвечаю: "На молочные продукты вы потратили X рублей"
```

---

## Регистрация в DI

**ToolServiceExtensions.cs:**
```csharp
public static IServiceCollection AddTools(this IServiceCollection services)
{
    // Только 2 handler'а
    services.AddTransient<IToolHandler, DescribeDataHandler>();
    services.AddTransient<IToolHandler, QueryHandler>();
    
    services.AddTransient<IToolExecutor, ToolExecutor>();
    return services;
}
```

---

## Порядок выполнения

1. Удалить старые handlers (7 штук) и их Args классы
2. Создать `QueryArgs.cs`
3. Создать `DescribeDataHandler.cs` с алгоритмом выбора примеров
4. Создать `QueryHandler.cs` с безопасным построением запросов
5. Обновить `ToolServiceExtensions.cs`
6. Обновить system prompt в `appsettings.json`

## Проверка

```bash
dotnet build src/SmartBasket.sln
```

## Критерии готовности

- [ ] describe_data возвращает схему + 3 связанных примера
- [ ] query работает для всех 6 таблиц
- [ ] query поддерживает все операторы (=, ILIKE, IN, etc.)
- [ ] Нет SQL injection (всё через EF Core)
- [ ] Компилируется без ошибок

## Тестовые сценарии

**Тест 1:** "Какие товары я покупал с курицей?"
```
1. describe_data() — модель видит структуру
2. query(items, where: name ILIKE %кур%) — находит "Филе куриное"
3. query(receipt_items, where: item_id = ...) — находит покупки
4. Ответ с датами и суммами
```

**Тест 2:** "Сколько чеков за последний месяц?"
```
1. query(receipts, where: date >= 2025-11-15, order_by: date DESC)
2. Ответ: "N чеков на сумму X рублей"
```

**Тест 3:** "Что я обычно покупаю в АШАН?"
```
1. query(receipts, where: shop ILIKE %ашан%) — получить receipt_id
2. query(receipt_items, where: receipt_id IN [...]) — получить item_id
3. query(items, where: id IN [...]) — получить названия
4. Сгруппировать и ответить
```
