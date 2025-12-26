# Workflow Timeline для модуля закупок

> Спецификация переработки чата в единую ленту событий
> Дата: Декабрь 2024

---

## Цель

Переработать чат в ShoppingView в **Workflow Timeline** — единую ленту событий, отображающую весь процесс сборки корзины.

---

## Три этапа workflow

1. **Составление корзины** — диалог с AI, добавление/удаление товаров
2. **Поиск в магазинах** — поиск каждой позиции в каждом магазине
3. **Выбор товаров** — AI выбирает конкретный товар с обоснованием и альтернативами

---

## Архитектура

Заменить текущие `ShoppingChatMessage` на систему событий:

```
WorkflowEvent (abstract)
├── Этап 1: UserMessageEvent, AiMessageEvent, ToolCallEvent, BasketUpdateEvent
├── Этап 2: SearchStartedEvent, SearchCompletedEvent, SearchFailedEvent  
└── Этап 3: ProductSelectionEvent (с Selected, Reason, Alternatives)
```

Каждый тип события = свой DataTemplate в XAML.

---

## Требования к UI

1. **Один шаблон на тип события** — визуально различимые блоки
2. **Анимация прогресса** — если событие в процессе (IsCompleted = false), показывать вращающийся спиннер
3. **Компактность** — технические события (tool calls, search) должны быть компактными
4. **Карточки выбора** — ProductSelectionEvent отображается как развёрнутая карточка с альтернативами

---

## Файлы

- `WorkflowEvent.cs` — базовый класс и все типы событий
- `WorkflowEventTemplates.xaml` — DataTemplates для каждого типа
- Обновить `ShoppingViewModel` — заменить `Messages` на `WorkflowEvents`
- Обновить `ShoppingView.xaml` — использовать новые шаблоны

---

## AI Операции

Три независимые AI операции, каждая со своим провайдером:

```
ShoppingChat       → Диалог, составление корзины (Smart: YandexAgent)
ProductMatcher     → Выбор товара, альтернативы, расчёт массы (Cheap: Ollama)
BasketReview       → Финальный обзор, рекомендации, разбивка (Smart: YandexAgent)
```

### Принцип инкапсуляции

- **ViewModel и сервисы НЕ знают про AI** — только отправляют данные, получают результат
- **Вся механика AI скрыта** в реализации операции (промпты, парсинг, tools)
- **Операция = класс**, реализующий интерфейс (IShoppingChatOperation, etc.)
- **Провайдер назначается** через существующую систему AiOperations в конфиге

### Интерфейсы

```csharp
// Этап 1: Чат
IShoppingChatOperation.ProcessMessageAsync(message, session) → IAsyncEnumerable<WorkflowEvent>

// Этап 2: Выбор товара  
IProductMatcherOperation.SelectProductAsync(draftItem, candidates, history) → ProductSelectionResult

// Этап 3: Обзор
IBasketReviewOperation.ReviewAsync(basket) → BasketReviewResult
```

### Конфиг

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

## Не ломать

- Текущая логика `ShoppingSessionService` остаётся
- Правая панель со списком покупок остаётся как есть
- WebView2 для парсеров остаётся
- Существующая система `IAiProviderFactory` и `AiOperations` используется как есть
