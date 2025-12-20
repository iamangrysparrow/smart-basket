# Задача: Рефакторинг AI Chat UI

## Цель

Переделать UI чата с AI для наглядного отображения процесса "думания" модели.

## Текущая проблема

`IProgress<string>` используется как универсальный канал — строки парсятся по префиксам (`"  "`, `"🔧"`, `"[DEBUG]"`). Хрупко, сложно поддерживать.

## Прочитай перед началом

1. `CLAUDE.md`, `WPF_RULES.md`
2. `src/SmartBasket.Services/Chat/ChatService.cs` — текущая реализация
3. `src/SmartBasket.WPF/ViewModels/AiChatViewModel.cs` — текущий ViewModel
4. `src/SmartBasket.WPF/Views/AiChatView.xaml` — текущий UI

---

## Архитектура

### Типизированный Progress вместо строк

Заменить `IProgress<string>` на `IProgress<ChatProgress>`:

```csharp
public enum ChatProgressType { TextDelta, ToolCall, ToolResult, Complete }

public record ChatProgress(
    ChatProgressType Type,
    string? Text = null,
    string? ToolName = null,
    string? ToolArgs = null,
    string? ToolResult = null,
    bool? ToolSuccess = null
);
```

### Структура ответа AI

Один ответ AI состоит из частей (Parts):

```csharp
public enum ResponsePartType { Thinking, ToolCall, FinalAnswer }

public class AssistantResponsePart : ObservableObject
{
    public ResponsePartType Type { get; init; }
    
    // Текст (для Thinking и FinalAnswer) — streaming обновление
    public string Text { get; set; }
    
    // Tool call данные
    public string? ToolName { get; init; }
    public string? ToolArgs { get; init; }
    public string? ToolResult { get; set; }  // заполняется после выполнения
    public bool? ToolSuccess { get; set; }
    
    // UI состояние
    public bool IsExpanded { get; set; }  // для Thinking/ToolCall — collapsed по умолчанию
}
```

### Сообщение чата

```csharp
public class ChatMessage : ObservableObject
{
    public bool IsUser { get; init; }
    
    // Для пользователя
    public string? UserText { get; init; }
    
    // Для AI — коллекция частей
    public ObservableCollection<AssistantResponsePart>? Parts { get; init; }
}
```

---

## Логика ViewModel

### Обработка Progress событий

```
TextDelta:
  - Если нет текущего Thinking → создать Thinking, добавить в Parts
  - Накапливать текст в текущем Thinking

ToolCall:
  - Текущий Thinking (если есть) остаётся как есть
  - Создать ToolCall part, добавить в Parts
  - Текущий Thinking = null (следующий текст будет новым)

ToolResult:
  - Найти последний ToolCall с таким ToolName
  - Заполнить ToolResult и ToolSuccess

Complete:
  - Последний Thinking переименовать в FinalAnswer
  - Свернуть все части кроме FinalAnswer (IsExpanded = false)
```

### Throttling

Оставить throttling для TextDelta (~50-100ms). Остальные события — сразу.

---

## Описание UI

### Общий layout

```
┌─────────────────────────────────────────────────────────┐
│ [ComboBox: Provider ▼]  [🔧 Prompt]  [🗑 Clear]         │  ← Header
├─────────────────────────────────────────────────────────┤
│                                                         │
│  Сообщения чата (ScrollViewer)                         │
│                                                         │
├─────────────────────────────────────────────────────────┤
│ [TextBox: ввод сообщения              ] [Отправить]    │  ← Input
├─────────────────────────────────────────────────────────┤
│ Статус: Готов / Думаю... / Выполняю query...           │  ← StatusBar
└─────────────────────────────────────────────────────────┘
```

### Сообщение пользователя

```
┌─────────────────────────────────────────────────────────┐
│                                    👤 Покажи чеки      │
│                                       за декабрь       │
└─────────────────────────────────────────────────────────┘
```

- Выравнивание: справа
- Фон: акцентный цвет (из темы)
- Иконка: 👤 или без

### Ответ AI (несколько частей)

```
┌─────────────────────────────────────────────────────────┐
│ 🤖                                                      │
│                                                         │
│  💭 Рассуждение                          [▼ развернуть] │
│  ┌ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┐ │
│  │ Мне нужно найти чеки за декабрь.                  │ │  ← collapsed
│  │ Использую инструмент query...                     │ │
│  └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┘ │
│                                                         │
│  🔧 query                                [▼ развернуть] │
│  ┌ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┐ │
│  │ SELECT * FROM Receipts WHERE...                   │ │  ← collapsed
│  │ ────────────────────────────                      │ │
│  │ ✓ 5 rows returned                                 │ │
│  └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┘ │
│                                                         │
│  ───────────────────────────────────────────────────── │
│  Найдено 5 чеков за декабрь на сумму 12,450₽:         │  ← FinalAnswer
│  • Kuper 10.12 — 2,450₽                               │     VISIBLE
│  • Samokat 08.12 — 1,200₽                             │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### Иконки частей

| Тип | Иконка | Состояние |
|-----|--------|-----------|
| Thinking | 💭 | Collapsed после завершения |
| ToolCall | 🔧 | Collapsed после завершения |
| FinalAnswer | (нет иконки) | Всегда visible |

### Цвета (из текущей темы)

- Фон сообщения пользователя: `{DynamicResource AccentBrush}` или аналог
- Фон ответа AI: `{DynamicResource CardBackgroundBrush}` или `SurfaceBrush`
- Фон collapsed части: чуть темнее/светлее основного
- Текст: `{DynamicResource ForegroundBrush}`

### Expander для частей

Использовать простой `ToggleButton` + `Visibility` binding, не нужен полноценный `Expander`:

```xaml
<StackPanel>
    <ToggleButton IsChecked="{Binding IsExpanded}" Content="💭 Рассуждение ▼"/>
    <Border Visibility="{Binding IsExpanded, Converter={StaticResource BoolToVisibility}}">
        <TextBox Text="{Binding Text}" IsReadOnly="True" TextWrapping="Wrap"/>
    </Border>
</StackPanel>
```

### Копирование текста

Все текстовые поля — `TextBox` с `IsReadOnly="True"` (позволяет выделять и копировать).

---

## Изменения в ChatService

### Убрать

- Все `progress?.Report($"  {delta}")` — форматирование с пробелами
- Все `progress?.Report($"🔧 Tool call: ...")` — emoji маркеры
- Все `progress?.Report($"[DEBUG] ...")` — debug в progress
- Все `progress?.Report($"[Iteration ...")` — iterations в progress

Debug информация должна идти через `ILogger`, не через progress.

### Заменить на

```csharp
// Текст от модели
progress?.Report(new ChatProgress(ChatProgressType.TextDelta, Text: delta));

// Вызов инструмента
progress?.Report(new ChatProgress(ChatProgressType.ToolCall, ToolName: name, ToolArgs: args));

// Результат инструмента
progress?.Report(new ChatProgress(ChatProgressType.ToolResult, ToolName: name, ToolResult: result, ToolSuccess: success));

// Завершение (финальный ответ готов)
progress?.Report(new ChatProgress(ChatProgressType.Complete));
```

### Сигнатура SendAsync

```csharp
Task<ChatResponse> SendAsync(
    string userMessage,
    IProgress<ChatProgress>? progress = null,  // ИЗМЕНИТЬ ТИП
    CancellationToken cancellationToken = default);
```

---

## Порядок выполнения

### Шаг 1: Модели данных

Создать в `SmartBasket.Services/Chat/`:
- `ChatProgress.cs` — record для progress событий
- `ChatProgressType.cs` — enum (или в том же файле)

Создать в `SmartBasket.WPF/ViewModels/`:
- `AssistantResponsePart.cs` — часть ответа AI
- `ResponsePartType.cs` — enum
- Обновить `ChatMessage.cs` — добавить `Parts`

### Шаг 2: ChatService

- Изменить сигнатуру `SendAsync` — `IProgress<ChatProgress>`
- Заменить все `progress?.Report(string)` на типизированные
- Debug логи перенести в `_logger.LogDebug()`

### Шаг 3: AiChatViewModel

- Изменить обработчик progress — `switch` по `Type`
- Логика создания/обновления `AssistantResponsePart`
- Throttling для `TextDelta`
- Сворачивание частей при `Complete`

### Шаг 4: AiChatView.xaml

- DataTemplate для `ChatMessage` с `IsUser=true`
- DataTemplate для `ChatMessage` с `IsUser=false` (Parts)
- ItemTemplate для `AssistantResponsePart` с DataTrigger по `Type`
- Стили для collapsed/expanded

### Шаг 5: Проверка

```
Тест 1: Отправить "Привет" → должен быть только FinalAnswer (без tool calls)
Тест 2: Отправить "Покажи чеки" → Thinking + ToolCall + FinalAnswer
Тест 3: Проверить streaming — текст появляется постепенно
Тест 4: Проверить expand/collapse — клик раскрывает/сворачивает
Тест 5: Проверить копирование — можно выделить и скопировать текст
```

---

## Критерии готовности

- [ ] Типизированный `ChatProgress` вместо строк
- [ ] `ChatMessage.Parts` — коллекция частей ответа
- [ ] Streaming текста работает с throttling
- [ ] Tool calls отображаются отдельными панелями
- [ ] Панели сворачиваются после завершения ответа
- [ ] FinalAnswer всегда виден
- [ ] Текст можно копировать
- [ ] Компилируется без ошибок
- [ ] Существующий функционал сохранён

---

## Чего НЕ делать

- Не создавать отдельные интерфейсы `IChatEvents` — достаточно `IProgress<T>`
- Не создавать иерархию классов событий — один record хватит
- Не трогать `ILlmProvider`, `IToolExecutor`, tool handlers
- Не менять логику tool-use loop — только reporting
