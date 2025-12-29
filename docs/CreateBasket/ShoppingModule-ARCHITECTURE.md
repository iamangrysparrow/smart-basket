# Shopping Module — Архитектура

> Модуль автоматизации еженедельных закупок
> Версия: 3.0
> Дата: Декабрь 2024
> Последнее обновление: 28.12.2024

---

## 1. Обзор

### Цель

Автоматизировать процесс закупок: от анализа чеков до готовой корзины в магазине.

### Три этапа workflow

| # | Этап | Описание | AI операция | Статус |
|---|------|----------|-------------|--------|
| 1 | Составление корзины | Диалог с AI, добавление/удаление товаров | ShoppingChat | ✅ Готово |
| 2 | Поиск в магазинах | Поиск каждой позиции в каждом магазине | — | ✅ Готово |
| 3 | Выбор товаров | AI выбирает товар с обоснованием и альтернативами | ProductMatcher | ✅ Готово |

---

## 2. AI Операции

Три независимые AI операции, каждая со своим провайдером:

```
ShoppingChat       → Диалог, составление корзины (Smart: YandexAgent)
ProductMatcher     → Выбор товара, альтернативы, расчёт количества (YandexAgent с variables ИЛИ Ollama с tool calling)
BasketReview       → Финальный обзор, рекомендации, разбивка (TBD)
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

// Этап 2+3: Поиск и выбор (объединено в BasketBuilder)
IBasketBuilderOperation.BuildBasketAsync(session, stores)
    → IAsyncEnumerable<WorkflowProgress>

// Этап 3: Выбор товара (внутренний, вызывается из BasketBuilder)
IProductMatcherOperation.SelectProductAsync(draftItem, candidates, history)
    → ProductSelectionResult

// Этап 4: Обзор (TBD)
IBasketReviewOperation.ReviewAsync(basket)
    → BasketReviewResult
```

### Конфигурация (appsettings.json)

```json
{
  "AiOperations": {
    "ShoppingChat": "YandexAgent/agent-id-for-chat",
    "ProductMatcher": "YandexAgent/agent-id-for-matcher",
    "Shopping": "YandexAgent/fallback-agent"
  }
}
```

### ProductMatcher — Два режима работы

`ProductMatcherOperation` автоматически выбирает режим в зависимости от провайдера:

#### 1. YandexAgent (prompt.variables) — Рекомендуемый

- Промпт хранится в агенте Yandex AI Studio
- Переменные передаются через `prompt.variables`
- Табличный формат данных (экономия токенов)
- Ответ — plain JSON в `output_text`

```csharp
// Переменные
{
  "DRAFT_ITEM_NAME": "Молоко",
  "DRAFT_ITEM_QUANTITY": "2",
  "DRAFT_ITEM_UNIT": "л",
  "SEARCH_RESULTS": "Id | Name | Price | Unit | Qty | InStock\n..."
}

// Input
"Выполни инструкцию"

// Response (plain JSON)
{
  "selected_product_id": "moloko-dom-25",
  "quantity": 3,
  "reasoning": "...",
  "alternatives": ["id1", "id2"]
}
```

**Документация промпта:** `docs/CreateBasket/ProductMatcher-YandexAgent-PROMPT.md`

#### 2. Tool Calling (Ollama, etc.)

- Полный промпт загружается из `prompt_shopping_select_product.txt`
- Используется tool calling API
- JSON формат данных

```csharp
// Tool: select_product
{
  "selected_product_id": "...",
  "quantity": 1,
  "reasoning": "...",
  "alternatives": [{"product_id": "..."}]
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

### Реализованные

```
src/SmartBasket.Services/Shopping/
├── Operations/
│   ├── IShoppingChatOperation.cs      ✅ Интерфейс чата
│   ├── ShoppingChatOperation.cs       ✅ Реализация (tool calling)
│   ├── IProductMatcherOperation.cs    ✅ Интерфейс выбора товара
│   ├── ProductMatcherOperation.cs     ✅ Реализация (YandexAgent + Ollama)
│   ├── IBasketBuilderOperation.cs     ✅ Интерфейс сборки корзины
│   └── BasketBuilderOperation.cs      ✅ Реализация (поиск + выбор)
├── WorkflowProgress.cs                ✅ Иерархия типов событий
└── (ProductSelectionResult в IProductMatcherOperation.cs)

src/SmartBasket.WPF/Views/Shopping/
├── WorkflowEventTemplates.xaml        ✅ DataTemplates для событий
├── WorkflowEvent.cs                   ✅ ViewModel-обёртка для UI
├── ShoppingView.xaml                  ✅ Обновлён
└── ShoppingViewModel.cs               ✅ Pattern matching handler

src/SmartBasket.Core/Helpers/
└── Json.cs                            ✅ Глобальные настройки JSON (кириллица!)

src/SmartBasket.Services/Llm/
├── YandexAgentLlmProvider.cs          ✅ Добавлен GenerateWithVariablesAsync()
└── LlmJsonOptions.cs                  ✅ Использует Json.DefaultOptions
```

### Документация

```
docs/CreateBasket/
├── ShoppingModule-ARCHITECTURE.md     ← Этот файл
├── ProductMatcher-YandexAgent-PROMPT.md  ✅ Промпт для агента в Yandex AI Studio
├── WeeklyShoppingModule-SPEC.md       Спецификация модуля
└── WeeklyShoppingModule-IMPLEMENTATION.md  План реализации
```

### Промпты

```
src/SmartBasket.WPF/
├── prompt_shopping_select_product.txt  ✅ Промпт для ProductMatcher (tool calling)
├── prompt_chat_priming.txt             Контекст БД для чата
├── prompt_chat_tools.txt               Инструкции по tool calling
└── ...
```

---

## 6. Глобальные настройки

### JSON сериализация (кириллица)

**ВАЖНО:** Всегда использовать `SmartBasket.Core.Helpers.Json` вместо `JsonSerializer`:

```csharp
// ❌ НЕ ДЕЛАТЬ — кириллица как \uXXXX
JsonSerializer.Serialize(obj)

// ✅ ПРАВИЛЬНО — читаемая кириллица
Json.Serialize(obj)        // компактный
Json.SerializePretty(obj)  // для логов
Json.Deserialize<T>(json)  // парсинг
```

### Настройки поиска

```csharp
// ShoppingSettings.cs
public int SearchLimit { get; set; } = 8;  // Было 3, увеличено
```

---

## 7. Не ломать

- Текущая логика `ShoppingSessionService` остаётся
- Правая панель со списком покупок остаётся как есть
- WebView2 для парсеров остаётся
- Существующая система `IAiProviderFactory` и `AiOperations` используется
- `ProductSearchResult` из парсеров используется как есть

---

## 8. TODO / Следующие шаги

### Ближайшие

- [ ] Создать агент ProductMatcher в Yandex AI Studio (промпт в `ProductMatcher-YandexAgent-PROMPT.md`)
- [ ] Настроить провайдер ProductMatcher в UI настроек
- [ ] Протестировать выбор товаров с разными запросами

### Будущие

- [ ] BasketReviewOperation — финальный обзор корзины
- [ ] Сохранение результатов выбора в PlannedBasket
- [ ] UI для просмотра/редактирования выбранных товаров
- [ ] Интеграция с реальным оформлением заказа в магазинах
