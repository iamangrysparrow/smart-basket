# SmartBasket Architecture

---

## 🔥🔥🔥 ЗОЛОТОЕ ПРАВИЛО: ВОПРОС = ОБСУЖДЕНИЕ, НЕ КОД 🔥🔥🔥

**Если сообщение пользователя заканчивается знаком вопроса (`?`) — ЗАПРЕЩЕНО менять код. ОБЯЗАТЕЛЬНО: обсудить, предложить, объяснить. Это правило БЕЗ ИСКЛЮЧЕНИЙ.**

---

> **AI Integration:** См. [ARCHITECTURE-AI.md](ARCHITECTURE-AI.md) для документации по интеграции с LLM провайдерами и Tool Calling.

## Overview

SmartBasket — приложение для автоматического сбора и анализа чеков из различных источников с использованием AI для категоризации.

```
┌─────────────────────────────────────────────────────────────────┐
│                        SmartBasket.WPF                          │
│  ┌─────────────┐    ┌─────────────────┐    ┌────────────────┐  │
│  │ MainWindow  │───▶│ MainViewModel   │───▶│ Services (DI)  │  │
│  │   (XAML)    │    │ (Commands/Props)│    │                │  │
│  └─────────────┘    └─────────────────┘    └────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                                │
         ┌──────────────────────┼──────────────────────┐
         ▼                      ▼                      ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
│ SmartBasket     │   │ SmartBasket     │   │ SmartBasket     │
│   .Services     │   │   .Data         │   │   .Core         │
│                 │   │                 │   │                 │
│ • Sources       │   │ • DbContext     │   │ • Entities      │
│ • Parsers       │   │ • PostgreSQL    │   │ • Configuration │
│ • AI Providers  │   │                 │   │                 │
└─────────────────┘   └─────────────────┘   └─────────────────┘
```

---

## Domain Concepts

### ProductCategory (Категория продуктов)
**Иерархический справочник категорий.**
- Примеры: "Напитки" → "Соки", "Молочные продукты" → "Молоко"
- Иерархия через ParentId
- Категории создаются пользователем вручную

### Product (Продукт)
**Суть товара** — конкретный продукт без вложенности.
- Примеры: "Сок J7", "Молоко Простоквашино", "Хлеб Бородинский"
- Опциональная связь с категорией через CategoryId
- AI классификация: автоматически после загрузки чеков + ручная через кнопку "Реклассификация"

### Item (Товар)
**Конкретная товарная единица** — то, что на полке магазина.
- Примеры: "Сок J7 апельсиновый 970 мл"
- Уникален по полному названию из чека
- Характеристики: UnitOfMeasure (мл, г), UnitQuantity (970)
- Связан с Product (категорией)

### ReceiptItem (Позиция в чеке)
**Запись о покупке** — количество, цена, сумма.
- Ссылается на Item
- Quantity в чеке может отличаться от UnitQuantity товара

---

## System Architecture

### Sources (Источники)

Канал получения сырых данных. Не знает о парсере — только доставляет данные.

```
ReceiptSource
├── Name: string              // Уникальный идентификатор
├── Type: SourceType          // Email, REST, FileSystem
├── Parser: string            // Имя парсера для обработки
└── IsEnabled: bool
```

| Type | Description |
|------|-------------|
| **Email** | IMAP почтовый ящик |
| **REST** | API стороннего сервиса |
| **FileSystem** | Папка с файлами |

---

### Parsers (Парсеры)

Извлекает структурированные данные из сырых данных источника.

```
IReceiptTextParser
├── Name: string              // Уникальный идентификатор парсера для конфигурации
├── SupportedShops: string[]  // Магазины для будущего авто-определения ("*" = универсальный)
├── CanParse(text): bool      // Может ли обработать текст (для auto-detect)
└── Parse(text, date): ParsedReceipt
```

**Зарегистрированные парсеры:**

| Parser | Name | SupportedShops | Description |
|--------|------|----------------|-------------|
| **InstamartParser** | `InstamartParser` | Instamart, СберМаркет, kuper.ru | Regex чеков СберМаркет |
| **LlmUniversalParser** | `LlmUniversalParser` | * | Универсальный AI парсер |

**Фабрика парсеров (`ReceiptTextParserFactory`):**
- `GetParser(name)` — получить парсер по имени
- `GetParserOrDefault(name)` — парсер или LLM fallback
- `TryParseWithRegex(text, date)` — перебор regex-парсеров по CanParse()

**Логика выбора парсера:**
```
Source.Parser = "InstamartParser"  → InstamartParser.Parse()
                                        └── fail? → LlmUniversalParser
Source.Parser = "Auto"             → TryParseWithRegex() (по CanParse())
                                        └── no match? → LlmUniversalParser
```

---

### AI Providers (Поставщики AI)

Конфигурация доступа к AI-модели. Ключ: `Provider/Model` или `Provider/AgentId`.

```
AiProviderConfig
├── Key: string               // "Ollama/qwen2.5:1.5b" или "YandexAgent/fvtp..."
├── Provider: ProviderType    // Ollama, YandexGPT, YandexAgent, OpenAI
├── Model: string             // (не для YandexAgent)
├── AgentId: string           // (только для YandexAgent)
├── FolderId: string          // (для Yandex*)
└── Temperature, Timeout, ...
```

**Реализации:**
| Provider | API | Streaming |
|----------|-----|-----------|
| `OllamaLlmProvider` | Ollama `/api/generate` | ✓ NDJSON |
| `YandexGptLlmProvider` | Foundation Models API | ✓ NDJSON |
| `YandexAgentLlmProvider` | REST Assistant API | ✓ SSE |

**Парсинг ответов:**
- `IResponseParser` — унифицированное извлечение JSON из LLM ответов
- Поддержка: markdown code blocks, chain-of-thought, bracket matching
- 5 fallback стратегий для надёжного парсинга

---

### AI Operations (Операции AI)

Связка системных операций с AI провайдерами.

```json
{
  "AiOperations": {
    "ProductExtraction": "Ollama/llama3.2:3b",
    "Classification": "Ollama/llama3.2:3b",
    "Labels": "YandexGPT/lite",
    "Chat": "Ollama/qwen2.5:7b"
  }
}
```

| Operation | Description |
|-----------|-------------|
| **ProductExtraction** | Item name → Product name (нормализация: убирает бренды, объёмы) |
| **Classification** | Product → Category hierarchy |
| **Labels** | Item → Labels |
| **Chat** | AI чат с пользователем |

---

### AI Prompts (Шаблоны промптов)

Промпты для AI имеют **три уровня приоритета**:

1. **Кастомный промпт** (из настроек UI) — приоритет максимальный
2. **Файл шаблона** (`prompt_*.txt`) — если кастомный не задан
3. **Hardcoded fallback** — если файл не найден

**Кастомные промпты:**
- Редактируются через UI (Settings → AI Operations → "Редактировать промпт")
- Сохраняются в `AiOperations.Prompts` по ключу `"Operation/ProviderKey"`
- Кнопка "Умный промпт" — вставляет минимальный промпт для GPT-4/Claude

| File | Service | Placeholders |
|------|---------|--------------|
| `prompt_template.txt` | LlmUniversalParser | `{{YEAR}}`, `{{RECEIPT_TEXT}}` |
| `prompt_classify_products.txt` | ProductClassificationService | `{{EXISTING_HIERARCHY}}`, `{{EXISTING_HIERARCHY_JSON}}`, `{{ITEMS}}` |
| `prompt_assign_labels.txt` | LabelAssignmentService | `{{LABELS}}`, `{{ITEMS}}` |

**Расположение:** `SmartBasket.WPF/` (рядом с exe)

**JSON-формат иерархии** (`{{EXISTING_HIERARCHY_JSON}}`):
```json
[{"id": 1, "name": "Молочные продукты", "parent_id": null}, ...]
```
Используется для умных моделей вместо текстового `{{EXISTING_HIERARCHY}}`.

---

## Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│                    1. СБОР СЫРЫХ ДАННЫХ                         │
│  Source (Email/REST/FS) → RawReceipt (text/html/json)          │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    2. ПАРСИНГ                                   │
│  ReceiptTextParserFactory.GetParser(source.Parser)             │
│       → Regex parser / LlmUniversalParser                      │
│       → ParsedReceipt (Shop, Date, Items[])                    │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│              3. КАТЕГОРИЗАЦИЯ (AI)                              │
│  ProductClassificationService: Item → Product                  │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│              4. МЕТКИ (AI)                                      │
│  LabelAssignmentService: Item → Labels                         │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│              5. СОХРАНЕНИЕ                                      │
│  Receipt + Items + ReceiptItems → DB                           │
└─────────────────────────────────────────────────────────────────┘
```

---

## Data Model

```
┌─────────────────┐
│     Labels      │  ← Метки ("Молоко для кофе", "Папа доволен")
└────────┬────────┘
         │
         ├──────────────────────┐
         ▼                      ▼
┌─────────────────┐    ┌─────────────────┐
│  ProductLabels  │    │   ItemLabels    │  ← many-to-many
└────────┬────────┘    └────────┬────────┘
         │                      │
         ▼                      ▼
┌─────────────────┐    ┌─────────────────┐    ┌───────────────────┐
│    Products     │    │     Items       │    │ ProductCategories │
│ CategoryId (FK) │◄───│ ProductId (FK)  │    │ (иерархия)        │
└────────┬────────┘    └────────┬────────┘    └───────────────────┘
         │                      │ 1                    ▲
         └──────────────────────┼──────────────────────┘
                                ▼                 CategoryId (FK)
                       ┌─────────────────┐
                       │  ReceiptItems   │  ← Позиция в чеке
                       │ Quantity, Price │
                       └────────┬────────┘
                                │
                                ▼
                       ┌─────────────────┐
                       │    Receipts     │  ← Чек
                       │ Shop, Date      │
                       └─────────────────┘
```

| Entity | Description |
|--------|-------------|
| **ProductCategory** | Иерархический справочник категорий. ParentId для вложенности. |
| **Product** | Плоский справочник продуктов. Опциональная связь с категорией через CategoryId. |
| **Item** | Конкретный товар. UnitOfMeasure, UnitQuantity. |
| **ReceiptItem** | Позиция в чеке: Quantity, Price, Amount. |
| **Receipt** | Чек: Shop, Date, Total, Status. |
| **Label** | Пользовательская метка. |
| **EmailHistory** | Дедупликация обработанных писем. |

---

## Logging Architecture

Логирование построено на **Serilog** с интеграцией в UI через custom sink.

```
ILogger<T>.LogXxx(...)
        │
        ▼
    Serilog
        ├─→ Debug Sink ────────→ Visual Studio Output
        ├─→ File Sink ─────────→ logs/smartbasket-YYYY-MM-DD.log
        └─→ LogViewerSink ─────→ SmartBasket Logs UI (ObservableCollection)
```

### Компоненты

| Component | Description |
|-----------|-------------|
| **LogViewerSink** | Custom Serilog sink → `ObservableCollection<LogEntry>` для UI |
| **LogEntry** | Модель записи лога: Timestamp, Level, Message |
| **FilteredLogEntries** | `ICollectionView` с фильтрацией по уровню (Debug/Info/Warning/Error) |

### Конфигурация (App.xaml.cs)

```csharp
// Отдельный файл на каждый запуск + ротация при работе > 24ч
var sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
var logsPath = Path.Combine(logsDir, $"smartbasket_{sessionTimestamp}.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}")
    .WriteTo.File(logsPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .WriteTo.LogViewer(LogEventLevel.Debug)
    .CreateLogger();
```

### Использование

```csharp
// В любом сервисе через DI
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService> logger) => _logger = logger;

    public void DoWork()
    {
        _logger.LogDebug("Starting work...");
        _logger.LogInformation("Processed {Count} items", 42);
        _logger.LogError(ex, "Failed to process");
    }
}
```

### Особенности

- **Thread-safe**: `LogViewerSink` использует `BindingOperations.EnableCollectionSynchronization`
- **UI limit**: 10000 записей в UI (настраивается), полный лог хранится отдельно для экспорта
- **Per-session files**: Отдельный лог-файл на каждый запуск (`smartbasket_2025-12-16_14-30-45.log`)
- **Daily rotation**: Ротация при работе > 24ч, 30 файлов хранения
- **Structured logging**: Параметры через `{Placeholder}`, не string interpolation
- **Full AI logging**: Все LLM запросы/ответы логируются на уровне Debug

### Настройка лимита UI лога

```json
// appsettings.json
{
  "MaxUiLogEntries": 10000  // Default. Уменьшить если UI тормозит
}
```

---

## Projects

### SmartBasket.Core
Entities и Configuration. Без зависимостей.

### SmartBasket.Data
EF Core DbContext. PostgreSQL / SQLite.

### SmartBasket.Services

**Sources:**
- `IReceiptSource`, `EmailReceiptSource`
- `IReceiptSourceFactory`

**Parsers:**
- `IReceiptTextParser`
- `InstamartReceiptParser` (regex)
- `LlmUniversalParser` (AI)
- `ReceiptTextParserFactory`

**AI/LLM:**
- `ILlmProvider`, `OllamaLlmProvider`, `YandexGptLlmProvider`, `YandexAgentLlmProvider`
- `IAiProviderFactory`
- `IResponseParser` — унифицированный парсинг JSON из LLM ответов

**Chat:**
- `IChatService`, `ChatService` — чат с поддержкой Tool Calling
- Tool Loop: выполняет инструменты и возвращает результаты в LLM
- Поддержка native tools и fallback (prompt injection)

**Tools:**
- `IToolExecutor`, `ToolExecutor` — исполнитель инструментов
- `IToolHandler` — интерфейс обработчика инструмента
- **2 универсальных инструмента** (вместо множества специализированных):
  - `describe_data` — схема БД + примеры данных для LLM контекста
  - `query` — SqlKata-based универсальный SELECT (JOIN, агрегаты, GROUP BY, HAVING)

**Services:**
- `ReceiptCollectionService` — оркестрация сбора (классификация временно отключена)
- `ProductClassificationService` — категоризация (для пакетной обработки)
- `LabelAssignmentService` — назначение меток
- `ProductCleanupService` — удаление осиротевших Products
- `ProductCategoryService` — CRUD для справочника категорий

### SmartBasket.WPF
MVVM приложение (CommunityToolkit.Mvvm).

**Logging:**
- `Logging/LogViewerSink.cs` — custom Serilog sink для UI
- `Models/LogEntry.cs` — модель записи лога с уровнем

**Themes:**
- `ThemeManager.cs` — переключение Light/Dark тем

### SmartBasket.CLI
Консольные утилиты для тестирования.
