# SmartBasket AI Architecture

Документация по интеграции с LLM провайдерами (Ollama, YandexGPT, YandexAgent) для обработки чеков, классификации товаров и AI-чата.

## LLM Providers Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           ILlmProvider                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  GenerateAsync(prompt)              - одиночный запрос                  │
│  ChatAsync(messages[])              - чат с историей сообщений          │
│  SupportsConversationReset          - поддержка сброса диалога          │
│  ResetConversation()                - сброс истории (для stateful API)  │
└─────────────────────────────────────────────────────────────────────────┘
              ▲                    ▲                       ▲
              │                    │                       │
┌─────────────┴─────────┐ ┌───────┴────────┐ ┌────────────┴────────────┐
│  OllamaLlmProvider    │ │ YandexGpt      │ │ YandexAgentLlmProvider  │
│                       │ │ LlmProvider    │ │                         │
│  /api/generate        │ │                │ │ /v1/responses           │
│  /api/chat            │ │ /completion    │ │                         │
│                       │ │ messages[]     │ │ previous_response_id    │
│  Stateless            │ │ Stateless      │ │ Stateful                │
│                       │ │                │ │                         │
│  SupportsReset: false │ │ SupportsReset: │ │ SupportsReset: true     │
│                       │ │ false          │ │ _lastResponseId хранит  │
│                       │ │                │ │ ID для продолжения      │
└───────────────────────┘ └────────────────┘ └─────────────────────────┘
```

### Типы провайдеров

| Провайдер | API Endpoint | Метод передачи истории | Stateful |
|-----------|--------------|------------------------|----------|
| Ollama | `/api/chat` | `messages[]` в каждом запросе | Нет |
| YandexGPT | `/completion` | `messages[]` в каждом запросе | Нет |
| YandexAgent | `/v1/responses` | `previous_response_id` | Да |

### ILlmProvider Interface

```csharp
public interface ILlmProvider
{
    string Name { get; }

    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default);

    // Одиночный запрос (для парсинга чеков, классификации)
    Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        int maxTokens = 2000,
        double temperature = 0.1,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    // Чат с историей (для AI Chat UI)
    Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        int maxTokens = 2000,
        double temperature = 0.7,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    bool SupportsConversationReset { get; }
    void ResetConversation();
}

public class LlmChatMessage
{
    public string Role { get; set; } = "user";  // user, assistant, system
    public string Content { get; set; } = string.Empty;
}

public class LlmGenerationResult
{
    public bool IsSuccess { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseId { get; set; }  // Для YandexAgent
}
```

## ChatAsync Implementation Details

### Ollama (`/api/chat`)

```json
// Запрос
{
  "model": "llama3.2:3b",
  "messages": [
    {"role": "user", "content": "Привет!"},
    {"role": "assistant", "content": "Привет! Чем могу помочь?"},
    {"role": "user", "content": "Расскажи о погоде"}
  ],
  "stream": true,
  "options": {
    "temperature": 0.7,
    "num_predict": 2000
  }
}

// Streaming response
{"message":{"role":"assistant","content":"Сегодня"},"done":false}
{"message":{"role":"assistant","content":" солнечно"},"done":false}
{"message":{"role":"assistant","content":"!"},"done":true}
```

### YandexGPT (`/completion`)

```json
// Запрос
{
  "modelUri": "gpt://folder_id/yandexgpt-lite/latest",
  "completionOptions": {
    "stream": true,
    "temperature": 0.7,
    "maxTokens": "2000"
  },
  "messages": [
    {"role": "user", "text": "Привет!"},
    {"role": "assistant", "text": "Привет! Чем могу помочь?"},
    {"role": "user", "text": "Расскажи о погоде"}
  ]
}

// Streaming response (NDJSON)
{"result":{"alternatives":[{"message":{"role":"assistant","text":"Сегодня солнечно!"},"status":"ALTERNATIVE_STATUS_FINAL"}]}}
```

### YandexAgent (`/v1/responses` с `previous_response_id`)

```json
// Первый запрос (новый диалог)
{
  "prompt": {"id": "agent_id"},
  "input": "Привет!",
  "stream": true
}

// SSE Response включает response.id
data:{"response":{"id":"resp_abc123","output_text":"Привет!"}}
event:response.completed

// Следующий запрос (продолжение диалога)
{
  "prompt": {"id": "agent_id"},
  "input": "Расскажи о погоде",
  "stream": true,
  "previous_response_id": "resp_abc123"  // ← ID предыдущего ответа
}
```

**Особенность YandexAgent:**
- Сервер хранит историю диалога
- Клиент передаёт только `previous_response_id` и текущее сообщение
- При вызове `ResetConversation()` очищается `_lastResponseId`

## AI Services Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         SmartBasket.Services                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────────┐    ┌────────────────────────┐    ┌─────────────┐  │
│  │  OllamaService   │    │ ProductClassification  │    │   Label     │  │
│  │                  │    │      Service           │    │ Assignment  │  │
│  │  Парсинг чеков   │    │                        │    │   Service   │  │
│  │  из email        │    │  Иерархическая         │    │             │  │
│  │                  │    │  классификация         │    │  Назначение │  │
│  │  prompt_template │    │  товаров → продукты    │    │  меток      │  │
│  │       .txt       │    │                        │    │             │  │
│  │                  │    │  prompt_classify_      │    │  prompt_    │  │
│  │                  │    │  products.txt          │    │  assign_    │  │
│  │                  │    │                        │    │  labels.txt │  │
│  └──────────────────┘    └────────────────────────┘    └─────────────┘  │
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                        SmartBasket.Services.Llm                   │   │
│  │  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────────┐ │   │
│  │  │OllamaLlmProvider│ │YandexGptProvider│ │YandexAgentProvider  │ │   │
│  │  └─────────────────┘ └─────────────────┘ └─────────────────────┘ │   │
│  │  ┌─────────────────┐ ┌─────────────────────────────────────────┐ │   │
│  │  │ ILlmProvider    │ │ IAiProviderFactory                      │ │   │
│  │  └─────────────────┘ └─────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

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
   - Shop, Date, Total
   - Items[] (name, quantity, unit, price, amount, unit_of_measure, unit_quantity)
         │
         ▼
┌─────────────────────────┐
│ProductClassificationSvc │  ← prompt_classify_products.txt
│ ClassifyAsync           │     (использует GenerateAsync)
│ (batch processing)      │
└────────┬────────────────┘
         │
         ▼
   ClassificationResult
   - Products[] (name, parent)
   - Items[] (name → product)
         │
         ▼
┌─────────────────────────┐
│ LabelAssignmentService  │  ← prompt_assign_labels.txt
│ AssignLabelsAsync       │     (использует GenerateAsync)
│ (per new item)          │
└────────┬────────────────┘
         │
         ▼
   LabelAssignmentResult
   - AssignedLabels[]
         │
         ▼
   Database (PostgreSQL)
   - Receipt, ReceiptItem
   - Item, Product
   - ItemLabel, ProductLabel
```

## AI Chat UI

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         AiChatViewModel                                  │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  AvailableProviders[]  ← IAiProviderFactory.GetAvailableProviders()     │
│  SelectedProvider      ← ComboBox выбор                                  │
│  Messages[]            ← История UI (ChatMessage)                        │
│                                                                          │
│  SendMessageAsync():                                                     │
│    1. Messages.Add(userMessage)                                          │
│    2. chatMessages = BuildChatMessages()  // → LlmChatMessage[]         │
│    3. provider.ChatAsync(chatMessages)                                   │
│    4. Messages.Add(assistantMessage)                                     │
│                                                                          │
│  OnSelectedProviderChanged():                                            │
│    - oldProvider.ResetConversation() если SupportsConversationReset     │
│    - Messages.Clear()                                                    │
│                                                                          │
│  ClearChat():                                                            │
│    - provider.ResetConversation() если SupportsConversationReset        │
│    - Messages.Clear()                                                    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

## Configuration

### appsettings.json

```json
{
  "AiProviders": [
    {
      "Key": "ollama-llama",
      "Type": "Ollama",
      "Model": "llama3.2:3b",
      "BaseUrl": "http://localhost:11434",
      "TimeoutSeconds": 120,
      "Temperature": 0.7,
      "MaxTokens": 2000
    },
    {
      "Key": "yandex-gpt-lite",
      "Type": "YandexGPT",
      "Model": "yandexgpt-lite",
      "FolderId": "b1g...",
      "ApiKey": "AQVN...",
      "TimeoutSeconds": 60,
      "Temperature": 0.7,
      "MaxTokens": 2000
    },
    {
      "Key": "yandex-agent-receipts",
      "Type": "YandexAgent",
      "AgentId": "fvt...",
      "FolderId": "b1g...",
      "ApiKey": "y0_...",
      "TimeoutSeconds": 120
    }
  ],

  "AiOperations": {
    "ReceiptParsing": {
      "Provider": "ollama-llama",
      "PromptFile": "prompt_template.txt"
    },
    "ProductClassification": {
      "Provider": "ollama-llama",
      "PromptFile": "prompt_classify_products.txt"
    },
    "LabelAssignment": {
      "Provider": "ollama-llama",
      "PromptFile": "prompt_assign_labels.txt"
    }
  }
}
```

### Параметры провайдеров

| Параметр | Ollama | YandexGPT | YandexAgent |
|----------|--------|-----------|-------------|
| Key | ✓ | ✓ | ✓ |
| Type | "Ollama" | "YandexGPT" | "YandexAgent" |
| Model | ✓ | ✓ | - |
| BaseUrl | ✓ (default: localhost:11434) | - | - |
| FolderId | - | ✓ | ✓ |
| ApiKey | - | ✓ | ✓ |
| AgentId | - | - | ✓ |
| TimeoutSeconds | ✓ | ✓ | ✓ |
| Temperature | ✓ | ✓ | - (в настройках агента) |
| MaxTokens | ✓ | ✓ | - (в настройках агента) |

## Service Descriptions

### 1. OllamaService (Receipt Parsing)

Парсинг HTML-письма с чеком в структурированный JSON.

**Interface:** `IOllamaService`
```csharp
void SetPromptTemplatePath(string path);
Task<(bool Success, string Message)> TestConnectionAsync(OllamaSettings settings);
Task<ParsedReceipt> ParseReceiptAsync(OllamaSettings settings, string emailBody, DateTime emailDate, IProgress<string>? progress);
```

**Prompt Template:** `prompt_template.txt`

**Placeholders:**
- `{{YEAR}}` - текущий год (для дат без года)
- `{{RECEIPT_TEXT}}` - очищенный текст письма

**Output JSON:**
```json
{
  "shop": "АШАН",
  "order_datetime": "2024-12-03:15:55",
  "order_number": "H03255764114",
  "total": 1234.56,
  "items": [
    {
      "name": "Молоко Домик в деревне 2.5% 930 г",
      "quantity": 2,
      "unit": "шт",
      "price": 89.99,
      "amount": 179.98,
      "unit_of_measure": "г",
      "unit_quantity": 930
    }
  ]
}
```

### 2. ProductClassificationService (Hierarchical Classification)

Группировка товаров по продуктам с иерархией.

**Interface:** `IProductClassificationService`
```csharp
void SetPromptTemplatePath(string path);
Task<ProductClassificationResult> ClassifyAsync(
    OllamaSettings settings,
    IReadOnlyList<string> itemNames,
    IReadOnlyList<ExistingProduct> existingProducts,
    IProgress<string>? progress,
    CancellationToken cancellationToken);
```

**Prompt Template:** `prompt_classify_products.txt`

**Placeholders:**
- `{{EXISTING_HIERARCHY}}` - текущая иерархия продуктов из БД
- `{{ITEMS}}` - список товаров для классификации

**Output JSON:**
```json
{
  "products": [
    {"name": "Молоко", "parent": null},
    {"name": "Томаты", "parent": "Овощи"}
  ],
  "items": [
    {"name": "Молоко Домик в деревне 2.5% 930 г", "product": "Молоко"}
  ]
}
```

**Batch Processing:**
- Товары обрабатываются батчами по 5 штук
- Уменьшает нагрузку на модель
- Позволяет частичное восстановление при ошибках

### 3. LabelAssignmentService (Label Assignment)

Автоматическое назначение пользовательских меток новым товарам.

**Interface:** `ILabelAssignmentService`
```csharp
void SetPromptTemplatePath(string path);
Task<LabelAssignmentResult> AssignLabelsAsync(
    OllamaSettings settings,
    string itemName,
    string productName,
    IReadOnlyList<string> availableLabels,
    IProgress<string>? progress,
    CancellationToken cancellationToken);
```

**Prompt Template:** `prompt_assign_labels.txt`

**Placeholders:**
- `{{LABELS}}` - список доступных меток из файла `user_labels.txt`
- `{{ITEM_NAME}}` - название товара
- `{{PRODUCT_NAME}}` - название продукта (категория)

**Output JSON:**
```json
["Молочные продукты", "Для завтрака"]
```

## Streaming Response Pattern

Все провайдеры используют streaming для получения ответов:

```csharp
// ResponseHeadersRead - не ждём полного ответа
response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);

// Streaming читаем построчно
using var stream = await response.Content.ReadAsStreamAsync(token);
using var reader = new StreamReader(stream);

var fullResponse = new StringBuilder();
var lineBuffer = new StringBuilder();

while (!reader.EndOfStream)
{
    linkedCts.Token.ThrowIfCancellationRequested();

    var line = await reader.ReadLineAsync(token);
    // Parse JSON chunk and append to fullResponse

    // Построчный вывод в progress
    foreach (var ch in chunk.Response)
    {
        if (ch == '\n')
        {
            progress?.Report($"  {lineBuffer}");
            lineBuffer.Clear();
        }
        else
        {
            lineBuffer.Append(ch);
        }
    }
}
```

## Logging Convention

Все LLM провайдеры используют единый формат логирования:

```
[Provider] ========================================
[Provider] >>> ЗАПРОС К API
[Provider] Config.Model: llama3.2:3b
[Provider] Messages count: 3
[Provider] URL: http://localhost:11434/api/chat
[Provider] === STREAMING RESPONSE ===
  <построчный вывод ответа модели в реальном времени>
[Provider] <<< ОТВЕТ ПОЛУЧЕН
[Provider] Response length: 1234 chars
[Provider] ========================================
```

**Prefixes:**
- `[Ollama]` / `[Ollama Chat]` - OllamaLlmProvider
- `[YandexGPT]` / `[YandexGPT Chat]` - YandexGptLlmProvider
- `[YandexAgent]` / `[YandexAgent Chat]` - YandexAgentLlmProvider
- `[Classify]` - ProductClassificationService
- `[Labels]` - LabelAssignmentService
- `[AI Chat]` - AiChatViewModel

## Error Handling

### NullableDecimalConverter

Ollama может возвращать `""` (пустую строку) вместо числа или `null`:

```csharp
public class NullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str)) return null;
            if (decimal.TryParse(str, out var result)) return result;
            return null;
        }
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDecimal();
        return null;
    }
}
```

### Cancellation Handling

Различаем внутренний таймаут и отмену пользователем:

```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

try
{
    // ... work with linkedCts.Token ...
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // Пользователь отменил - пробрасываем исключение
    throw;
}
catch (OperationCanceledException)
{
    // Внутренний таймаут - возвращаем ошибку, не бросаем
    result.IsSuccess = false;
    result.ErrorMessage = "Request timed out";
}
```

### Conversation Reset

При смене провайдера или очистке чата:

```csharp
// AiChatViewModel
partial void OnSelectedProviderChanged(string? oldValue, string? newValue)
{
    // Сбрасываем диалог у старого провайдера (если поддерживает)
    if (!string.IsNullOrEmpty(oldValue))
    {
        var oldProvider = _aiProviderFactory.GetProvider(oldValue);
        if (oldProvider?.SupportsConversationReset == true)
        {
            oldProvider.ResetConversation();
        }
    }

    // Очищаем UI историю
    Messages.Clear();
}
```

## Prompt Engineering Tips

### 1. Explicit JSON Schema
Всегда указывай точный формат JSON в промпте:
```
ФОРМАТ ОТВЕТА (строго JSON):
{"shop":"","order_datetime":"","total":0,"items":[...]}
```

### 2. Negative Examples
Явно указывай что НЕ является товаром:
```
ИГНОРИРУЙ строки: "Собрано", "Оплата", "Доставка", "Сервисный сбор"
```

### 3. Default Values
Для опциональных полей указывай что делать если значение отсутствует:
```
- unit_quantity: (Может отсутствовать. Тогда равна 0)
- unit_of_measure: (Может отсутствовать. Тогда равна unit)
```

### 4. Low Temperature
Используй `Temperature: 0.1` для детерминированных JSON-ответов (парсинг, классификация).
Используй `Temperature: 0.7` для чата (более творческие ответы).

### 5. Batch Size
Для классификации используй батчи по 5 товаров - баланс между скоростью и качеством.

## Files Structure

```
src/SmartBasket.WPF/
├── prompt_template.txt          # Парсинг чеков
├── prompt_classify_products.txt # Классификация товаров
├── prompt_assign_labels.txt     # Назначение меток
├── user_labels.txt              # Доступные метки пользователя
└── ViewModels/
    └── AiChatViewModel.cs       # AI Chat UI

src/SmartBasket.Services/
├── Llm/
│   ├── ILlmProvider.cs              # Интерфейс + LlmChatMessage + LlmGenerationResult
│   ├── OllamaLlmProvider.cs         # Ollama провайдер
│   ├── YandexGptLlmProvider.cs      # YandexGPT провайдер
│   ├── YandexAgentLlmProvider.cs    # YandexAgent провайдер (stateful)
│   └── IAiProviderFactory.cs        # Фабрика провайдеров
├── Ollama/
│   ├── OllamaService.cs                    # Парсинг чеков
│   ├── IOllamaService.cs
│   ├── ParsedReceiptItem.cs                # DTO + NullableDecimalConverter
│   ├── ProductClassificationService.cs     # Классификация
│   ├── IProductClassificationService.cs
│   ├── ProductClassificationResult.cs      # DTO
│   ├── LabelAssignmentService.cs           # Метки
│   └── ILabelAssignmentService.cs
└── AI/
    └── ... (дополнительные AI сервисы)

src/SmartBasket.Core/Configuration/
└── AiProviderConfig.cs          # Конфигурация провайдеров
```

## Testing via CLI

```bash
# Тест парсинга чеков
dotnet run --project src/SmartBasket.CLI -- parse

# Тест классификации (с батчами)
dotnet run --project src/SmartBasket.CLI -- classify

# Просмотр логов в реальном времени
# - Промпты отображаются между === PROMPT START === и === PROMPT END ===
# - Ответы модели стримятся построчно между === STREAMING RESPONSE === и === END STREAMING ===
```

---

*Последнее обновление: 15.12.2025*
