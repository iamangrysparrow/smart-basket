# Задача: Реализация Tools для Smart Basket

## Контекст

Система инструментов для доступа к данным о покупках. Используется в Chat, Brain, CLI.

## Прочитай перед началом

1. `CLAUDE.md`, `WPF_RULES.md`
2. `docs/AiTools/TOOLS-SPEC.md` — полная спецификация всех инструментов
3. `docs/AiTools/TOOLS-IMPLEMENTATION.md` — примеры реализации
4. `src/SmartBasket.Data/SmartBasketDbContext.cs` — контекст БД
5. `src/SmartBasket.Core/Entities/` — сущности (Receipt, ReceiptItem, Item, Product, Label, ItemLabel)

## Структура

```
src/SmartBasket.Services/Tools/
├── IToolExecutor.cs
├── ToolExecutor.cs
├── IToolHandler.cs
├── ToolServiceExtensions.cs
├── Models/
│   ├── ToolDefinition.cs
│   ├── ToolResult.cs
│   └── Args/
│       ├── GetReceiptsArgs.cs
│       ├── GetReceiptItemsArgs.cs
│       ├── GetProductsArgs.cs
│       ├── GetItemsArgs.cs
│       ├── GetLabelsArgs.cs
│       ├── GetPurchaseStatisticsArgs.cs
│       └── GenerateShoppingListArgs.cs
└── Handlers/
    ├── GetReceiptsHandler.cs
    ├── GetReceiptItemsHandler.cs
    ├── GetProductsHandler.cs
    ├── GetItemsHandler.cs
    ├── GetLabelsHandler.cs
    ├── GetPurchaseStatisticsHandler.cs
    └── GenerateShoppingListHandler.cs
```

## 7 инструментов

| Tool | Описание | Ключевые параметры |
|------|----------|-------------------|
| `get_receipts` | Чеки + summary | date_from, date_to, shop, limit, include_summary |
| `get_receipt_items` | Позиции чеков | receipt_id, date_from, item_name, group_by_item |
| `get_products` | Иерархия категорий | parent_id, search, include_children |
| `get_items` | Справочник товаров | product_id, search, shop, include_purchase_stats |
| `get_labels` | Метки и связи | label_name, include_items |
| `get_purchase_statistics` | Статистика + prediction | period_days, group_by, include_prediction |
| `generate_shopping_list` | Генерация корзины | days_ahead, include_labels, min_confidence |

Полные JSON Schema — в `docs/AiTools/TOOLS-SPEC.md`.

## Регистрация в DI

**ToolServiceExtensions.cs:**
```csharp
public static IServiceCollection AddTools(this IServiceCollection services)
{
    services.AddTransient<IToolHandler, GetReceiptsHandler>();
    services.AddTransient<IToolHandler, GetReceiptItemsHandler>();
    services.AddTransient<IToolHandler, GetProductsHandler>();
    services.AddTransient<IToolHandler, GetItemsHandler>();
    services.AddTransient<IToolHandler, GetLabelsHandler>();
    services.AddTransient<IToolHandler, GetPurchaseStatisticsHandler>();
    services.AddTransient<IToolHandler, GenerateShoppingListHandler>();
    
    services.AddTransient<IToolExecutor, ToolExecutor>();
    return services;
}
```

**App.xaml.cs:** добавить `services.AddTools();`

## Требования

- Args классы с `[JsonPropertyName("snake_case")]`
- JSON ответы в snake_case
- Использовать существующий `SmartBasketDbContext`
- Обработка ошибок через `ToolResult.Error()`

## Проверка

```bash
dotnet build src/SmartBasket.sln
```

## Критерии готовности

- [ ] 7 handlers реализованы
- [ ] Компилируется без ошибок
- [ ] `GetToolDefinitions()` возвращает 7 определений
