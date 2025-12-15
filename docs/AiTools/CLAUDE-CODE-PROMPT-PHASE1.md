# Задача: Реализация системы инструментов (Tools) для Smart Basket

## Контекст

Smart Basket — WPF приложение для анализа чеков из магазинов. Нужно реализовать систему инструментов для доступа к данным, которая будет использоваться в:
- AI Chat (диалог с пользователем)
- Brain Module (автоматизация закупок)
- CLI утилиты

## Обязательно прочитай перед началом

1. `CLAUDE.md` — правила проекта
2. `WPF_RULES.md` — правила WPF разработки
3. `docs/TOOLS-SPEC.md` — спецификация инструментов (JSON Schema, примеры)
4. `docs/TOOLS-IMPLEMENTATION.md` — пример реализации get_receipts

## Задача: Phase 1 — Core + MVP

Реализовать базовую инфраструктуру и первые два инструмента.

### Шаг 1: Создать структуру папок

```
src/SmartBasket.Services/
└── Tools/
    ├── IToolExecutor.cs
    ├── ToolExecutor.cs
    ├── IToolHandler.cs
    ├── ToolServiceExtensions.cs
    ├── Models/
    │   ├── ToolDefinition.cs
    │   ├── ToolResult.cs
    │   └── Args/
    │       ├── GetReceiptsArgs.cs
    │       └── GetReceiptItemsArgs.cs
    └── Handlers/
        ├── GetReceiptsHandler.cs
        └── GetReceiptItemsHandler.cs
```

### Шаг 2: Реализовать модели

**ToolDefinition.cs:**
```csharp
namespace SmartBasket.Services.Tools.Models;

public record ToolDefinition(
    string Name,
    string Description,
    object ParametersSchema
);
```

**ToolResult.cs:**
```csharp
namespace SmartBasket.Services.Tools.Models;

public record ToolResult(
    bool Success,
    string JsonData,
    string? ErrorMessage = null
)
{
    public static ToolResult Ok(object data) { ... }
    public static ToolResult Error(string message) { ... }
}
```

### Шаг 3: Реализовать интерфейсы

**IToolHandler.cs** — интерфейс одного инструмента:
- `Name` — имя инструмента
- `GetDefinition()` — JSON Schema для LLM
- `ExecuteAsync(argumentsJson)` — выполнение

**IToolExecutor.cs** — роутер:
- `GetToolDefinitions()` — все определения
- `ExecuteAsync(toolName, argumentsJson)` — вызов по имени
- `HasTool(toolName)` — проверка существования

### Шаг 4: Реализовать ToolExecutor

Собирает все `IToolHandler` через DI и роутит вызовы.

### Шаг 5: Реализовать GetReceiptsHandler

См. спецификацию в `docs/TOOLS-SPEC.md`:
- Фильтры: date_from, date_to, shop
- Сортировка: order_by
- Агрегация: include_summary
- Лимит: limit

### Шаг 6: Реализовать GetReceiptItemsHandler

См. спецификацию в `docs/TOOLS-SPEC.md`:
- Фильтры: receipt_id, date_from, date_to, item_name, product_name, shop
- Группировка: group_by_item
- Лимит: limit

### Шаг 7: Регистрация в DI

**ToolServiceExtensions.cs:**
```csharp
public static class ToolServiceExtensions
{
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        services.AddTransient<IToolHandler, GetReceiptsHandler>();
        services.AddTransient<IToolHandler, GetReceiptItemsHandler>();
        services.AddTransient<IToolExecutor, ToolExecutor>();
        return services;
    }
}
```

**App.xaml.cs** — добавить:
```csharp
services.AddTools();
```

### Шаг 8: Проверить сборку

```bash
dotnet build D:\AI\smart-basket\src\SmartBasket.sln
```

## Критерии готовности

- [ ] Все файлы созданы в правильных папках
- [ ] Проект компилируется без ошибок
- [ ] `IToolExecutor` можно получить через DI
- [ ] `GetToolDefinitions()` возвращает 2 инструмента
- [ ] `ExecuteAsync("get_receipts", "{}")` возвращает данные из БД

## Важно

- Используй существующий `SmartBasketDbContext`
- Используй существующие entities: `Receipt`, `ReceiptItem`, `Item`, `Product`
- Не создавай заглушки — полная реализация
- JSON ответы в snake_case (для совместимости с LLM)

## После завершения Phase 1

Сообщи о готовности. Следующие фазы:
- Phase 2: GetProductsHandler, GetItemsHandler, GetLabelsHandler
- Phase 3: GetPurchaseStatisticsHandler, GenerateShoppingListHandler
