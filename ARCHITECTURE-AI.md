# SmartBasket AI Architecture

Документация по интеграции с Ollama LLM для обработки чеков и классификации товаров.

## AI Services Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         SmartBasket.Services.Ollama                      │
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
└─────────────────────────────────────────────────────────────────────────┘
```

## Data Processing Pipeline

```
Email Body (HTML)
      │
      ▼
┌─────────────────┐
│ OllamaService   │  ← prompt_template.txt
│ ParseReceiptAsync│
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
│ ClassifyAsync           │
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
│ AssignLabelsAsync       │
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

## Configuration

**appsettings.json:**
```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "MFDoom/deepseek-r1-tool-calling:8b",
    "Temperature": 0.1,
    "TimeoutSeconds": 300
  }
}
```

**Параметры:**
- `BaseUrl` - URL сервера Ollama
- `Model` - модель LLM (рекомендуется модели с поддержкой JSON)
- `Temperature` - 0.1 для детерминированных ответов
- `TimeoutSeconds` - таймаут для streaming-запросов (300 для классификации)

## Streaming Response Pattern

Все сервисы используют streaming для получения ответов от Ollama:

```csharp
var request = new OllamaGenerateRequest
{
    Model = settings.Model,
    Prompt = prompt,
    Stream = true,  // Включаем streaming
    Options = new OllamaOptions
    {
        Temperature = 0.1,
        NumPredict = 4096  // Максимум токенов
    }
};

// Streaming читаем построчно
using var stream = await response.Content.ReadAsStreamAsync(token);
using var reader = new StreamReader(stream);

var fullResponse = new StringBuilder();
var lineBuffer = new StringBuilder();

while (!reader.EndOfStream)
{
    var line = await reader.ReadLineAsync(token);
    var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);

    if (chunk?.Response != null)
    {
        fullResponse.Append(chunk.Response);

        // Построчный вывод в лог
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

    if (chunk?.Done == true) break;
}
```

## Logging Convention

Все AI-сервисы используют единый формат логирования:

```
[Service] === PROMPT START ===
<полный текст промпта>
[Service] === PROMPT END ===
[Service] Sending STREAMING request to http://localhost:11434/api/generate (model: ..., timeout: ...s)...
[Service] === STREAMING RESPONSE ===
<построчный вывод ответа модели в реальном времени>
[Service] === END STREAMING (12.3s) ===
[Service] Total response: 1234 chars
```

**Prefixes:**
- `[Ollama]` - OllamaService (парсинг чеков)
- `[Classify]` - ProductClassificationService
- `[Labels]` - LabelAssignmentService

## Error Handling

### NullableDecimalConverter

Ollama может возвращать `""` (пустую строку) вместо числа или `null` для полей типа `decimal?`. Конвертер обрабатывает это gracefully:

```csharp
/// <summary>
/// Конвертер для decimal? который обрабатывает пустые строки как null.
/// Нужен потому что Ollama иногда возвращает "" вместо числа или null
/// когда не может определить значение (например unit_quantity для товара без веса в названии).
/// </summary>
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

**Применение:**
```csharp
[JsonConverter(typeof(NullableDecimalConverter))]
public decimal? Amount { get; set; }

[JsonConverter(typeof(NullableDecimalConverter))]
public decimal? UnitQuantity { get; set; }
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
    result.Message = "Request timed out";
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
Используй `Temperature: 0.1` для детерминированных JSON-ответов.

### 5. Batch Size
Для классификации используй батчи по 5 товаров - баланс между скоростью и качеством.

## Files Structure

```
src/SmartBasket.WPF/
├── prompt_template.txt          # Парсинг чеков
├── prompt_classify_products.txt # Классификация товаров
├── prompt_assign_labels.txt     # Назначение меток
└── user_labels.txt              # Доступные метки пользователя

src/SmartBasket.Services/Ollama/
├── OllamaService.cs                    # Парсинг чеков
├── IOllamaService.cs
├── ParsedReceiptItem.cs                # DTO + NullableDecimalConverter
├── ProductClassificationService.cs     # Классификация
├── IProductClassificationService.cs
├── ProductClassificationResult.cs      # DTO
├── LabelAssignmentService.cs           # Метки
├── ILabelAssignmentService.cs
├── CategoryService.cs                  # (legacy)
└── ICategoryService.cs
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
