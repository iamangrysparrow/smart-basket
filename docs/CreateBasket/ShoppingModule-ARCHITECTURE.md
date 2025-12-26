# Shopping Module — Архитектура

> Модуль автоматизации еженедельных закупок
> Версия: 2.0
> Дата: Декабрь 2024

---

## 1. Обзор

### Цель

Автоматизировать процесс закупок: от анализа чеков до готовой корзины в магазине.

### Три этапа workflow

| # | Этап | Описание | AI операция |
|---|------|----------|-------------|
| 1 | Составление корзины | Диалог с AI, добавление/удаление товаров | ShoppingChat |
| 2 | Поиск в магазинах | Поиск каждой позиции в каждом магазине | — |
| 3 | Выбор товаров | AI выбирает товар с обоснованием и альтернативами | ProductMatcher |

---

## 2. AI Операции

Три независимые AI операции, каждая со своим провайдером:

```
ShoppingChat       → Диалог, составление корзины (Smart: YandexAgent)
ProductMatcher     → Выбор товара, альтернативы, расчёт массы (Cheap: Ollama)
BasketReview       → Финальный обзор, рекомендации, разбивка (Smart: YandexAgent)
```

### Принцип инкапсуляции

- **ViewModel и сервисы НЕ знают про AI** — только отправляют данные, получают результат
- **Вся механика AI скрыта** в реализации операции (промпты, парсинг, tools)
- **Операция = класс**, реализующий интерфейс
- **Провайдер назначается** через существующую систему AiOperations

### Интерфейсы

```csharp
// Этап 1: Чат
IShoppingChatOperation.ProcessMessageAsync(message, session) 
    → IAsyncEnumerable<WorkflowProgress>

// Этап 3: Выбор товара  
IProductMatcherOperation.SelectProductAsync(draftItem, candidates, history) 
    → ProductSelectionResult

// Этап 3+: Обзор (TBD)
IBasketReviewOperation.ReviewAsync(basket) 
    → BasketReviewResult
```

### Конфигурация

```json
{
  "AiOperations": {
    "ShoppingChat": "YandexAgent/xxx",
    "ProductMatcher": "Ollama/qwen2.5:7b",
    "BasketReview": "YandexAgent/xxx"
  }
}
```

---

## 3. Workflow Timeline

### Концепция

Вместо чата — **единая лента событий**, отображающая весь процесс сборки корзины.

### Иерархия типов событий

```csharp
// ═══════════════════════════════════════════════════════════
// БАЗОВЫЙ ТИП
// ═══════════════════════════════════════════════════════════
public abstract record WorkflowProgress(DateTime Timestamp);

// ═══════════════════════════════════════════════════════════
// ЭТАП 1: Составление корзины
// ═══════════════════════════════════════════════════════════
public record TextDeltaProgress(string Text) 
    : WorkflowProgress(DateTime.Now);

public record ToolCallProgress(string Name, string Args) 
    : WorkflowProgress(DateTime.Now);

public record ToolResultProgress(string Name, string Result, bool Success) 
    : WorkflowProgress(DateTime.Now);

public record ChatCompleteProgress() 
    : WorkflowProgress(DateTime.Now);

// ═══════════════════════════════════════════════════════════
// ЭТАП 2: Поиск в магазинах
// ═══════════════════════════════════════════════════════════
public record SearchStartedProgress(string ProductName, string StoreName) 
    : WorkflowProgress(DateTime.Now);

public record SearchCompletedProgress(string ProductName, string StoreName, int ResultCount) 
    : WorkflowProgress(DateTime.Now);

public record SearchFailedProgress(string ProductName, string StoreName, string Error) 
    : WorkflowProgress(DateTime.Now);

// ═══════════════════════════════════════════════════════════
// ЭТАП 3: Выбор товаров
// ═══════════════════════════════════════════════════════════
public record ProductSelectionStartedProgress(string DraftItemName, string StoreName) 
    : WorkflowProgress(DateTime.Now);

public record ProductSelectionCompletedProgress(
    string DraftItemName,
    string StoreName,
    ProductSearchResult Selected,
    string Reason,
    List<ProductSearchResult> Alternatives   // Из ЭТОГО ЖЕ магазина
) : WorkflowProgress(DateTime.Now);
```

### Модель товара (единая)

Используется существующий тип из парсеров:

```csharp
public record ProductSearchResult(
    string Id,              // ID товара (slug из URL)
    string Name,            // Название товара
    decimal Price,          // Цена
    string? Unit,           // "г", "кг", "шт", "л", "мл"
    decimal Quantity,       // Количество в единице измерения
    bool InStock,           // В наличии
    string? ImageUrl,       // URL изображения
    string ProductUrl       // Полный URL страницы товара
);
```

---

## 4. UI

### Требования

1. **Один DataTemplate на тип события** — визуально различимые блоки
2. **Анимация прогресса** — если событие в процессе (IsCompleted=false), показывать спиннер
3. **Компактность** — технические события (tool calls, search) компактные
4. **Карточки выбора** — ProductSelection отображается как развёрнутая карточка

### Пример ProductSelection в UI

```
┌─────────────────────────────────────────────────────────────┐
│ ✓ Молоко 1.5%                               Samokat         │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ [🥛]  Простоквашино 1л                         89.90 ₽  │ │
│ └─────────────────────────────────────────────────────────┘ │
│ Покупали 3 раза за месяц, лучшая цена среди знакомых марок │
│                                                             │
│ ▶ Альтернативы                                              │
│   ├─ Домик в деревне 1л      95.90 ₽                       │
│   └─ Parmalat 1л            109.00 ₽                       │
└─────────────────────────────────────────────────────────────┘
```

### Handler с pattern matching

```csharp
var progress = new DispatcherProgress<WorkflowProgress>(dispatcher, p =>
{
    switch (p)
    {
        case TextDeltaProgress delta:
            AppendText(delta.Text);
            break;
            
        case ToolCallProgress tool:
            AddToolCallEvent(tool.Name, tool.Args);
            break;
            
        case SearchStartedProgress search:
            AddSearchEvent(search.ProductName, search.StoreName, isCompleted: false);
            break;
            
        case SearchCompletedProgress search:
            CompleteSearchEvent(search.ProductName, search.StoreName, search.ResultCount);
            break;
            
        case ProductSelectionStartedProgress sel:
            AddSelectionEvent(sel.DraftItemName, sel.StoreName, isCompleted: false);
            break;
            
        case ProductSelectionCompletedProgress sel:
            CompleteSelectionEvent(sel);
            break;
    }
});
```

---

## 5. Файлы

### Новые

```
src/SmartBasket.Services/Shopping/
├── Operations/
│   ├── IShoppingChatOperation.cs
│   ├── ShoppingChatOperation.cs
│   ├── IProductMatcherOperation.cs
│   └── ProductMatcherOperation.cs
├── WorkflowProgress.cs              // Иерархия типов событий
└── ProductSelectionResult.cs

src/SmartBasket.WPF/Views/Shopping/
├── WorkflowEventTemplates.xaml      // DataTemplates для каждого типа
└── WorkflowEvent.cs                 // ViewModel-обёртка для UI
```

### Обновить

```
src/SmartBasket.WPF/Views/Shopping/
├── ShoppingView.xaml                // Использовать новые шаблоны
└── ShoppingViewModel.cs             // Pattern matching handler
```

---

## 6. Не ломать

- Текущая логика `ShoppingSessionService` остаётся
- Правая панель со списком покупок остаётся как есть
- WebView2 для парсеров остаётся
- Существующая система `IAiProviderFactory` и `AiOperations` используется
- `ProductSearchResult` из парсеров используется как есть
