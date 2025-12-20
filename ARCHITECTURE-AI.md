# SmartBasket AI Architecture

---

## 🔥🔥🔥 ЗОЛОТОЕ ПРАВИЛО: ВОПРОС = ОБСУЖДЕНИЕ, НЕ КОД 🔥🔥🔥

**Если сообщение пользователя заканчивается знаком вопроса (`?`) — ЗАПРЕЩЕНО менять код. ОБЯЗАТЕЛЬНО: обсудить, предложить, объяснить. Это правило БЕЗ ИСКЛЮЧЕНИЙ.**

---

Документация по интеграции с LLM провайдерами (Ollama, YandexGPT, YandexAgent) для обработки чеков, классификации товаров и AI-чата с поддержкой Tool Calling.

## LLM Providers Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           ILlmProvider                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  GenerateAsync(prompt)              - одиночный запрос                  │
│  ChatAsync(messages[], tools[])     - чат с историей и инструментами    │
│  SupportsConversationReset          - поддержка сброса диалога          │
│  SupportsTools                      - поддержка native tool calling     │
│  ResetConversation()                - сброс истории (для stateful API)  │
└─────────────────────────────────────────────────────────────────────────┘
              ▲                    ▲                       ▲
              │                    │                       │
┌─────────────┴─────────┐ ┌───────┴────────┐ ┌────────────┴────────────┐
│  OllamaLlmProvider    │ │ YandexGpt      │ │ YandexAgentLlmProvider  │
│                       │ │ LlmProvider    │ │                         │
│  /api/chat            │ │                │ │ /v1/responses           │
│  Native tools         │ │ /completion    │ │ Native function_call    │
│  + Fallback parsing   │ │ messages[]     │ │ previous_response_id    │
│                       │ │                │ │                         │
│  SupportsTools: true  │ │ SupportsTools: │ │ SupportsTools: true     │
│  (для qwen2.5, etc.)  │ │ false          │ │ (AI Studio agents)      │
│                       │ │ (fallback)     │ │                         │
└───────────────────────┘ └────────────────┘ └─────────────────────────┘
```

## Tool Calling Architecture

### ChatService — центральный сервис чата

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              ChatService                                  │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌────────────────┐    ┌─────────────────────┐    ┌──────────────────┐  │
│  │ ILlmProvider   │◄───│ IAiProviderFactory  │    │ IToolExecutor    │  │
│  └────────────────┘    └─────────────────────┘    └────────┬─────────┘  │
│                                                             │            │
│                                                    ┌────────▼─────────┐  │
│                                                    │  Tool Handlers   │  │
│                                                    │  - describe_data │  │
│                                                    │  - query         │  │
│                                                    └──────────────────┘  │
│                                                                           │
│  SendAsync(userMessage):                                                  │
│    1. Определяем поддержку tools провайдером                             │
│    2. Если SupportsTools=true → native tool calling                      │
│    3. Если SupportsTools=false → prompt injection + text parsing         │
│    4. Tool Loop: выполняем tools → отправляем результаты → повторяем     │
│    5. Возвращаем финальный ответ                                         │
│                                                                           │
└──────────────────────────────────────────────────────────────────────────┘
```

### Два режима работы с Tools

#### 1. Native Tool Calling (для моделей с поддержкой tools)

Модели: `qwen2.5`, `llama3.1`, `llama3.2`, `mistral-nemo`

```json
// Запрос к Ollama /api/chat
{
  "model": "qwen2.5:7b",
  "messages": [...],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_receipts",
        "description": "Получить список чеков...",
        "parameters": { "type": "object", "properties": {...} }
      }
    }
  ]
}

// Ответ модели
{
  "message": {
    "role": "assistant",
    "tool_calls": [
      {
        "id": "call_abc123",
        "function": {
          "name": "get_receipts",
          "arguments": "{\"date_from\":\"2024-10-01\",\"date_to\":\"2024-12-31\"}"
        }
      }
    ]
  }
}
```

#### 2. Prompt Injection + Text Parsing (fallback для моделей без native tools)

Модели: `deepseek-r1`, `llama3.2:3b`, `YandexGPT`

ChatService инжектирует описание инструментов в системный промпт:

```
=== ДОСТУПНЫЕ ИНСТРУМЕНТЫ ===

get_receipts - Получить список чеков
Параметры: {"date_from": "string", "date_to": "string", ...}

=== КАК ИСПОЛЬЗОВАТЬ ИНСТРУМЕНТЫ ===
Чтобы вызвать инструмент, верни JSON:
{"name": "имя_инструмента", "arguments": {...}}
Или используй формат: имя_инструмента({"arg": "value"})
```

ChatService парсит текстовый ответ, ища:
1. `[TOOL_CALL_START]` формат (YandexGPT fallback): `[TOOL_CALL_START]query\n{...}`
2. JSON в code block: `\`\`\`json {...} \`\`\``
3. Голый JSON: `{"name": "...", "arguments": {...}}`
4. Function call формат: `get_receipts({"date_from": "2024-10-01"})`
5. Теги `<tool_request>` / `<tool_response>` (qwen)
6. Прямой JSON аргументов query (если есть `"table":`)

### Формат [TOOL_CALL_START] (YandexGPT fallback)

YandexGPT (без native tool calling) может выводить инструменты в текстовом формате:

```
Для получения информации мне нужно запросить данные.

[TOOL_CALL_START]query
{"columns":["Items.Name","Products.Name"],"joins":[{"on":["Receipts.Id","ReceiptItems.ReceiptId"],"table":"ReceiptItems"}],"limit":100,"table":"Receipts"}
```

`ChatService.TryParseToolCallStartFormat()`:
1. Ищет `[TOOL_CALL_START]` в тексте ответа
2. Извлекает имя инструмента (до `\n` или `{`)
3. Парсит JSON с балансировкой скобок `{` / `}`
4. Возвращает `LlmToolCall` если JSON валидный

Текст **до** `[TOOL_CALL_START]` — это рассуждения модели. Они возвращаются в `result.Response` и отображаются пользователю.

### Обработка DeepSeek-R1 "thinking"

Модель `MFDoom/deepseek-r1-tool-calling:8b` возвращает ответы с thinking блоками:

```
<think>
Пользователь спрашивает о чеках за последние 3 месяца.
Мне нужно вызвать get_receipts с датами от 2024-09-15 до 2024-12-15.
</think>

get_receipts({"date_from": "2024-09-15", "date_to": "2024-12-15"})
```

`OllamaLlmProvider` и `ChatService` автоматически:
1. Удаляют `<think>...</think>` блоки
2. Парсят function call формат
3. Возвращают структурированный tool call

## Tool Definitions

### Архитектурное решение: 2 универсальных инструмента

**Ключевой инсайт эксперимента:** Вместо создания множества специализированных инструментов (get_receipts, get_items, get_products, search_items, etc.) достаточно **2 универсальных инструмента**:

1. **`describe_data`** — LLM получает схему БД один раз в начале диалога
2. **`query`** — LLM формирует любые SELECT-запросы сам, используя знание схемы

**Преимущества:**
- Минимум кода — 2 handler'а вместо 10+
- Гибкость — LLM сам решает как объединять данные
- Масштабируемость — новая таблица = строка в whitelist
- Безопасность — whitelist таблиц/колонок + SqlKata для экранирования

### Файлы инструментов

```
src/SmartBasket.Services/Tools/
├── IToolExecutor.cs           # Интерфейс исполнителя
├── ToolExecutor.cs            # Роутинг к обработчикам
├── IToolHandler.cs            # Интерфейс обработчика
├── ToolServiceExtensions.cs   # DI регистрация
├── Models/
│   ├── ToolDefinition.cs      # Определение инструмента
│   ├── ToolResult.cs          # Результат выполнения
│   └── QueryArgs.cs           # DTO для аргументов query
└── Handlers/
    ├── DescribeDataHandler.cs # Схема БД + примеры данных
    └── QueryHandler.cs        # SqlKata-based универсальный SELECT
```

### describe_data — Контекст для LLM

Возвращает:
- **Схема БД** — все таблицы, колонки, типы, связи
- **Статистика** — количество записей, диапазон дат
- **3 примера товаров** — с полными связями (категории, метки, покупки)

LLM вызывает этот инструмент ОДИН РАЗ в начале диалога, получая полную картину данных.

### query — Универсальный SELECT (SqlKata)

Полнофункциональный SELECT с поддержкой:

| Возможность | Пример |
|-------------|--------|
| JOIN | `"joins": [{"table": "Items", "on": ["Items.Id", "ReceiptItems.ItemId"]}]` |
| Агрегаты | `"aggregates": [{"function": "SUM", "column": "Amount", "alias": "total"}]` |
| GROUP BY | `"group_by": ["Shop"]` |
| HAVING | `"having": [{"function": "SUM", "column": "Amount", "op": ">", "value": 1000}]` |
| WHERE операторы | `=, !=, >, <, >=, <=, ILIKE, IN, NOT IN, IS NULL, BETWEEN` |
| ORDER BY | `"order_by": [{"column": "total", "direction": "DESC"}]` |

**Технические детали реализации:**

```csharp
// PostgreSQL с PascalCase именами требует кавычек
// НО: SqlKata двойно экранирует уже закавыченные строки!

// НЕПРАВИЛЬНО: query.From("\"Receipts\"") → "\"\"Receipts\"\""
// ПРАВИЛЬНО: query.FromRaw("public.\"Receipts\"")

// JOIN-ы строим как raw SQL и вставляем в FromRaw:
var fromWithJoins = $"public.\"{tableName}\" {string.Join(" ", joinClauses)}";
query = new Query().FromRaw(fromWithJoins);

// Для дат добавляем ::timestamp cast (PostgreSQL timestamptz)
if (IsDateString(value))
    query.WhereRaw($"{column} >= ?::timestamp", value);
```

**Безопасность:**
- Whitelist таблиц: Receipts, ReceiptItems, Items, Products, Labels, ItemLabels, ProductLabels
- Whitelist колонок для каждой таблицы
- Whitelist агрегатных функций: COUNT, SUM, AVG, MIN, MAX
- Whitelist операторов: =, !=, ILIKE, IN, BETWEEN и др.
- Нормализация input: `snake_case` → `PascalCase` (LLM может использовать любой формат)

### Примеры запросов LLM

**Простой подсчёт:**
```json
{
  "table": "Receipts",
  "aggregates": [
    {"function": "COUNT", "column": "*", "alias": "total_receipts"},
    {"function": "SUM", "column": "Total", "alias": "total_amount"}
  ]
}
```

**JOIN с фильтрацией:**
```json
{
  "table": "ReceiptItems",
  "columns": ["Items.Name", "Amount"],
  "joins": [{"table": "Items", "on": ["Items.Id", "ReceiptItems.ItemId"]}],
  "where": [{"column": "Items.Name", "op": "ILIKE", "value": "%молоко%"}],
  "order_by": [{"column": "Amount", "direction": "DESC"}],
  "limit": 10
}
```

**Агрегация с GROUP BY и HAVING:**
```json
{
  "table": "Receipts",
  "columns": ["Shop"],
  "aggregates": [{"function": "SUM", "column": "Total", "alias": "shop_total"}],
  "group_by": ["Shop"],
  "having": [{"function": "SUM", "column": "Total", "op": ">", "value": 1000}],
  "order_by": [{"column": "shop_total", "direction": "DESC"}]
}
```

**BETWEEN для дат:**
```json
{
  "table": "Receipts",
  "where": [{"column": "ReceiptDate", "op": "BETWEEN", "value": ["2024-10-01", "2024-12-31"]}]
}
```

### Доступные таблицы и колонки

| Таблица | Колонки |
|---------|---------|
| **Receipts** | Id, ReceiptDate, Shop, Total, ReceiptNumber, EmailId, Status, CreatedAt, UpdatedAt |
| **ReceiptItems** | Id, ReceiptId, ItemId, Quantity, Price, Amount, CreatedAt, UpdatedAt |
| **Items** | Id, Name, ProductId, UnitOfMeasure, UnitQuantity, Shop, CreatedAt, UpdatedAt |
| **Products** | Id, Name, ParentId, CreatedAt, UpdatedAt |
| **Labels** | Id, Name, Color, CreatedAt, UpdatedAt |
| **ItemLabels** | ItemId, LabelId |
| **ProductLabels** | ProductId, LabelId |

## ChatService Loop

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         ChatService.SendAsync                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  [User Message]                                                          │
│       │                                                                  │
│       ▼                                                                  │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │ 1. Добавить сообщение в историю                                   │   │
│  │ 2. Если !SupportsTools → InjectToolsIntoSystemPrompt()           │   │
│  │ 3. provider.ChatAsync(history, tools)                            │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│       │                                                                  │
│       ▼                                                                  │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │ 4. Проверить: есть tool calls?                                    │   │
│  │    - Native: result.ToolCalls                                     │   │
│  │    - Fallback: TryParseToolCallsFromText(result.Response)        │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│       │                                                                  │
│       ▼  (если есть tool calls)                                         │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │ TOOL LOOP (до MAX_TOOL_ITERATIONS=5):                             │   │
│  │                                                                    │   │
│  │   foreach (toolCall in toolCalls):                                │   │
│  │     - toolExecutor.ExecuteAsync(name, args)                       │   │
│  │     - history.Add(assistant: tool_call)                           │   │
│  │     - history.Add(tool: result)                                   │   │
│  │                                                                    │   │
│  │   provider.ChatAsync(history, tools)                              │   │
│  │   → снова проверяем tool calls                                    │   │
│  │                                                                    │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│       │                                                                  │
│       ▼  (нет больше tool calls)                                        │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │ 5. Возвращаем финальный ответ                                     │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

## Типы провайдеров и поддержка Tools

| Провайдер | API Endpoint | SupportsTools | Метод tool calling |
|-----------|--------------|---------------|-------------------|
| Ollama | `/api/chat` | `true` (для совместимых моделей) | Native + Fallback parsing |
| YandexGPT | `/completion` | `false` | Prompt injection + Fallback parsing + Message conversion |
| YandexAgent | `/v1/responses` | `true` | Native function calling API |

### YandexGPT Message Conversion

YandexGPT API поддерживает только роли `system`, `user`, `assistant`.

При отправке истории с tool calls, `YandexGptLlmProvider.ConvertMessages()` выполняет конвертацию:

```csharp
// role: "tool" → role: "user" с результатом инструмента
if (m.Role == "tool")
{
    result.Add(new YandexMessage
    {
        Role = "user",
        Text = $"[Результат инструмента {m.ToolCallId}]:\n{m.Content}"
    });
}

// role: "assistant" с tool calls → добавляем информацию о вызове
else if (m.Role == "assistant" && m.ToolCalls?.Count > 0)
{
    var toolCallsInfo = string.Join("\n", m.ToolCalls.Select(tc =>
        $"[Вызов инструмента {tc.Name}]: {tc.Arguments}"));
    result.Add(new YandexMessage { Role = "assistant", Text = toolCallsInfo });
}
```

### YandexAgent Native Function Calling

YandexAgent (Yandex AI Studio Agents) поддерживает native function calling через REST Assistant API.

**API Reference:** https://yandex.cloud/ru/docs/ai-studio/operations/agents/create-function-text-agent

**Формат запроса:**
```json
{
  "prompt": { "id": "agent-id" },
  "input": [
    { "type": "message", "role": "user", "content": "Покажи чеки" }
  ],
  "tools": [
    {
      "type": "function",
      "name": "get_receipts",
      "description": "Получить список чеков",
      "parameters": { "type": "object", "properties": {...} }
    }
  ],
  "stream": true
}
```

**SSE события при function call:**
```
data:{"item":{"type":"function_call","call_id":"abc123","name":"get_receipts","arguments":"{...}"}}
event:response.output_item.done
```

**Отправка результата выполнения:**
```json
{
  "input": [
    { "type": "function_call_output", "call_id": "abc123", "output": "{...}" }
  ]
}
```

`YandexAgentLlmProvider` автоматически:
1. Конвертирует `LlmChatMessage[]` в формат `input[]` с правильными типами
2. Конвертирует `ToolDefinition[]` в формат `tools[]` для Yandex API
3. Парсит `function_call` события из SSE streaming
4. **Парсит текстовые tool calls** в формате `[TOOL_CALL_START]...[TOOL_CALL_END]`
5. Возвращает `LlmToolCall[]` в `result.ToolCalls`

### YandexAgent Text Tool Call Parsing

YandexAgent модель иногда выводит tool calls как текст вместо native function_call. Формат:

```
Чтобы рассчитать частоту покупок молока...
1. Сначала выясним даты покупок.
...

[TOOL_CALL_START]query
{"columns":["Receipts.ReceiptDate"],"joins":[...],"table":"ReceiptItems","where":[...]}
[TOOL_CALL_END]
```

**Теги:**
- `[TOOL_CALL_START]` — начало вызова, после тега идёт имя инструмента
- `[TOOL_CALL_END]` — конец вызова (опционально)
- JSON аргументы между тегами

`YandexAgentLlmProvider.TryParseTextToolCalls()`:
1. Ищет `[TOOL_CALL_START]` в тексте ответа
2. Извлекает имя инструмента (до `\n` или `{`)
3. Парсит JSON с балансировкой скобок
4. Возвращает `LlmToolCall` если JSON валидный

**Текст до `[TOOL_CALL_START]`** — это рассуждения модели (reasoning). Они возвращаются в `result.Response` и отображаются пользователю в UI.

### Модели Ollama с native tool support

- `qwen2.5` (все размеры)
- `llama3.1` (все размеры)
- `llama3.2` (3b поддерживает частично)
- `mistral-nemo`
- `nemotron-mini`

### Модели требующие fallback

- `MFDoom/deepseek-r1-tool-calling:8b` — использует `<think>` блоки и function call формат
- `llama3.2:3b` — иногда игнорирует native tools
- `phi3`, `gemma2` — не поддерживают tools

## ILlmProvider Interface

```csharp
public interface ILlmProvider
{
    string Name { get; }
    bool SupportsConversationReset { get; }
    bool SupportsTools { get; }

    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct);

    // Одиночный запрос (для парсинга чеков, классификации)
    Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        int maxTokens = 2000,
        double temperature = 0.1,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    // Чат с историей и инструментами
    Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        int maxTokens = 2000,
        double temperature = 0.7,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    void ResetConversation();
}

public class LlmGenerationResult
{
    public bool IsSuccess { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseId { get; set; }
    public List<LlmToolCall>? ToolCalls { get; set; }  // Native tool calls
}

public class LlmToolCall
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Arguments { get; set; }  // JSON string
}
```

## AI Chat UI (AiChatViewModel)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         AiChatViewModel                                  │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  AvailableProviders[]  ← IAiProviderFactory.GetAvailableProviders()     │
│  SelectedProvider      ← ComboBox выбор                                  │
│  Messages[]            ← История UI (ChatMessage)                        │
│  SystemPrompt          ← Редактируемый системный промпт                 │
│                                                                          │
│  SendMessageAsync():                                                     │
│    1. Messages.Add(userMessage)                                          │
│    2. chatService.SendAsync(userMessage)  // Tool calling внутри         │
│    3. Messages.Add(assistantMessage)                                     │
│                                                                          │
│  OnSelectedProviderChanged():                                            │
│    - chatService.SetProvider(newProvider)                                │
│    - chatService.ClearHistory()                                          │
│    - Messages.Clear()                                                    │
│                                                                          │
│  ApplySystemPrompt():                                                    │
│    - chatService.SetSystemPrompt(prompt)                                 │
│    - Сохранение в appsettings.json                                       │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Streaming UI и показ рассуждений модели

`AiChatViewModel` поддерживает streaming отображение ответа модели:

**ThreadSafeProgress** — для предотвращения зависания UI при частых обновлениях:
```csharp
// НЕ использовать Progress<T> — он захватывает SynchronizationContext
var progress = new Progress<string>(msg => UpdateUI(msg));  // ПЛОХО!

// Использовать ThreadSafeProgress + Dispatcher.BeginInvoke
var progress = new ThreadSafeProgress<string>(msg =>
{
    dispatcher.BeginInvoke(() => UpdateUI(msg));  // ХОРОШО!
});
```

**Отображение в UI:**
1. **Рассуждения модели** — дельты текста (строки начинающиеся с `"  "`) показываются в streaming
2. **Вызовы инструментов** — форматы `"Выполняю query..."` или `"🔧 Tool call: query"` показываются как `🔧 Вызываю инструмент: query`
3. **Статус** — обновляется в хедере (`Думаю...`, `Выполняю query...`, `Готов`)

**Пример streaming сообщения:**
```
Чтобы найти информацию о покупках молока, мне нужно
запросить данные из базы.

🔧 Вызываю инструмент: query

На основе полученных данных могу сказать, что...
```

## Configuration

### appsettings.json

```json
{
  "AiProviders": [
    {
      "Key": "ollama-qwen",
      "Type": "Ollama",
      "Model": "qwen2.5:7b",
      "BaseUrl": "http://localhost:11434",
      "TimeoutSeconds": 120,
      "Temperature": 0.7,
      "MaxTokens": 4000
    },
    {
      "Key": "ollama-deepseek",
      "Type": "Ollama",
      "Model": "MFDoom/deepseek-r1-tool-calling:8b",
      "BaseUrl": "http://localhost:11434",
      "TimeoutSeconds": 180
    },
    {
      "Key": "yandex-gpt-lite",
      "Type": "YandexGPT",
      "Model": "yandexgpt-lite",
      "FolderId": "b1g...",
      "ApiKey": "AQVN...",
      "TimeoutSeconds": 60
    }
  ],

  "AiOperations": {
    "ReceiptParsing": {
      "Provider": "ollama-qwen"
    },
    "ProductClassification": {
      "Provider": "ollama-qwen"
    },
    "Prompts": {
      "Chat": "Ты — помощник для учёта расходов...\n{{TODAY}}"
    }
  }
}
```

### Системный промпт чата

```
Ты — умный помощник приложения Smart Basket для учёта домашних расходов.

СЕГОДНЯШНЯЯ ДАТА: {{TODAY}}

У тебя есть доступ к инструментам для работы с базой данных чеков пользователя.

ПРАВИЛА:
1. Когда спрашивают про "последние N месяцев" — считай от сегодняшней даты назад
2. При запросе чеков ВСЕГДА используй инструмент get_receipts
3. НЕ ПОВТОРЯЙ вызов инструмента с теми же параметрами
4. После получения данных — ответь пользователю на основе этих данных
```

`{{TODAY}}` автоматически заменяется на текущую дату (YYYY-MM-DD).

## Data Processing Pipeline

```
Email Body (HTML)
      │
      ▼
┌─────────────────┐
│ OllamaService   │  ← prompt_template.txt
│ ParseReceiptAsync│     (использует GenerateAsync)
└────────┬────────┘
         │
         ▼
   ParsedReceipt
         │
         ▼
┌─────────────────────────┐
│ProductClassificationSvc │  ← prompt_classify_products.txt
│ ClassifyAsync           │     (batch по 5 товаров)
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│ LabelAssignmentService  │  ← prompt_assign_labels.txt
│ AssignLabelsAsync       │
└────────┬────────────────┘
         │
         ▼
   Database (PostgreSQL)
```

## Files Structure

```
src/SmartBasket.Services/
├── Llm/
│   ├── ILlmProvider.cs              # Интерфейс провайдера
│   ├── OllamaLlmProvider.cs         # Ollama + native tools + fallback
│   ├── YandexGptLlmProvider.cs      # YandexGPT (fallback only)
│   ├── YandexAgentLlmProvider.cs    # YandexAgent stateful
│   └── AiProviderFactory.cs         # Фабрика провайдеров
├── Chat/
│   ├── IChatService.cs              # Интерфейс чат-сервиса
│   └── ChatService.cs               # Tool calling orchestration
├── Tools/
│   ├── IToolExecutor.cs
│   ├── ToolExecutor.cs
│   ├── IToolHandler.cs
│   ├── Models/
│   │   └── ToolDefinition.cs
│   └── Handlers/
│       └── Get*Handler.cs           # Обработчики инструментов
└── Ollama/
    ├── OllamaService.cs             # Парсинг чеков
    ├── ProductClassificationService.cs
    └── LabelAssignmentService.cs

src/SmartBasket.WPF/
├── ViewModels/
│   └── AiChatViewModel.cs           # AI Chat UI
├── Views/
│   └── AiChatView.xaml              # Chat UI
└── appsettings.json                 # Конфигурация провайдеров
```

## Error Handling

### Tool Call Parsing Fallback

```csharp
// OllamaLlmProvider.TryParseToolCallsFromText()
// ChatService.TryParseToolCallsFromText()

// 1. Удаляем <think>...</think> блоки
text = RemoveThinkBlocks(text);

// 2. Ищем function call формат: tool_name({"arg": "value"})
var funcCall = TryParseFunctionCallFormat(text);

// 3. Ищем JSON в code block: ```json {...} ```
var codeBlockPattern = @"```(?:json)?\s*(\{[\s\S]*?\})\s*```";

// 4. Ищем голый JSON: {"name": "...", "arguments": {...}}
if (trimmed.StartsWith("{") && trimmed.EndsWith("}")) { ... }
```

### Cancellation Handling

```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

try { ... }
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    throw;  // Пользователь отменил
}
catch (OperationCanceledException)
{
    return Error("Request timed out");  // Внутренний таймаут
}
```

## Logging Convention

```
[ChatService] ========================================
[ChatService] >>> ОТПРАВКА СООБЩЕНИЯ
[ChatService] Provider: ollama-qwen (SupportsTools: True)
[ChatService] Messages: 5, Tools: 9
[Ollama Chat] >>> ЗАПРОС К OLLAMA
[Ollama Chat] Model: qwen2.5:7b
[Ollama Chat] === STREAMING RESPONSE ===
  <ответ модели>
[Ollama Chat] <<< TOOL CALLS: 1
[ChatService] Выполняю инструмент: get_receipts
[ChatService] Tool result: 5 receipts found
[ChatService] <<< ФИНАЛЬНЫЙ ОТВЕТ
[ChatService] ========================================
```

---

## Тестирование Query Handler

### CLI команда test-query

Для верификации QueryHandler создана команда в CLI:

```bash
dotnet run --project SmartBasket.CLI -- test-query
```

**26 тестов покрывают:**
- Все 7 таблиц (Receipts, ReceiptItems, Items, Products, Labels, ItemLabels, ProductLabels)
- Агрегаты: COUNT, SUM, AVG, MIN, MAX
- Простые SELECT с лимитом
- JOIN между таблицами
- Фильтрация: ILIKE, IN, =, BETWEEN
- GROUP BY с агрегатами
- Нормализация snake_case → PascalCase
- Работа с датами (timestamptz cast)

### Пример вывода тестов

```
=== Testing QueryHandler (26 tests) ===

[1/26] Receipts: COUNT(*), SUM(Total)
       SQL: SELECT COUNT(*) as "total_receipts", SUM(public."Receipts"."Total") as "total_sum" FROM public."Receipts"
       ✓ PASS (3 ms, 1 row)

[2/26] Receipts: Simple SELECT with limit
       SQL: SELECT public."Receipts"."Id", ... FROM public."Receipts" LIMIT 3
       ✓ PASS (2 ms, 3 rows)

...

[26/26] Receipts: Date filtering with BETWEEN
       SQL: SELECT ... WHERE public."Receipts"."ReceiptDate" BETWEEN ?::timestamp AND ?::timestamp
       ✓ PASS (4 ms, 2 rows)

=== Results: 26/26 passed ===
```

---

*Последнее обновление: 21.12.2025*
