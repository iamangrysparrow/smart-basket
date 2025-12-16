# SmartBasket Architecture

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

### Product (Продукт)
**Суть товара** — абстрактная категория.
- Примеры: "Сок", "Молоко", "Хлеб"
- Иерархия через ParentId: "Напитки" → "Соки"
- AI помогает определить продукт по названию

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
    "Classification": "Ollama/llama3.2:3b",
    "Labels": "YandexGPT/lite"
  }
}
```

| Operation | Description |
|-----------|-------------|
| **Classification** | Item → Product |
| **Labels** | Item → Labels |

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
┌─────────────────┐    ┌─────────────────┐
│    Products     │    │     Items       │
│ (иерархия)      │◄───│ ProductId (FK)  │  1 ──── *
└─────────────────┘    └────────┬────────┘
                                │ 1
                                ▼
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
| **Product** | Категория товара. Иерархия через ParentId. |
| **Item** | Конкретный товар. UnitOfMeasure, UnitQuantity. |
| **ReceiptItem** | Позиция в чеке: Quantity, Price, Amount. |
| **Receipt** | Чек: Shop, Date, Total, Status. |
| **Label** | Пользовательская метка. |
| **EmailHistory** | Дедупликация обработанных писем. |

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
- `ReceiptCollectionService` — оркестрация сбора
- `ProductClassificationService` — категоризация
- `LabelAssignmentService` — назначение меток
- `ProductCleanupService` — удаление осиротевших Products

### SmartBasket.WPF
MVVM приложение (CommunityToolkit.Mvvm).

### SmartBasket.CLI
Консольные утилиты для тестирования.
