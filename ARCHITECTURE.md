# SmartBasket Architecture

> **AI Integration:** См. [ARCHITECTURE-AI.md](ARCHITECTURE-AI.md) для документации по интеграции с Ollama LLM.

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

## Domain Concepts (Доменные понятия)

В системе SmartBasket используются три ключевых понятия, которые важно различать:

### Product (Продукт)
**Суть товара** — абстрактная категория, описывающая что это за товар по существу.

- Примеры: "Сок", "Молоко", "Мясо", "Хлеб", "Огурцы маринованные"
- Может иметь иерархию (ParentId): "Напитки" → "Соки" → "Апельсиновый сок"
- Используется для категоризации и аналитики
- AI помогает определить продукт по названию товара

### Item (Товар)
**Конкретная товарная единица** — то, что лежит на полке магазина.

- Примеры: "Сок J7 апельсиновый 970 мл", "Молоко Простоквашино 3.2% 1л"
- Уникален по полному названию (как в чеке)
- Имеет характеристики: единица измерения (мл, г, кг, л) и количество (970, 1000, 500)
- Связан с Product (категорией)
- Один Product может включать много Items

### ReceiptItem (Товарная позиция)
**Запись в чеке** — описание покупки конкретного товара.

- Содержит: ссылку на Item, количество в чеке, цену за единицу, сумму
- Количество и единица измерения в чеке могут отличаться от характеристик товара

**Пример для понимания:**

```
Чек из Instamart:
┌─────────────────────────────────────────────────────────────────────────┐
│ "Сок J7 апельсиновый 970 мл"  ← Название (идентифицирует Item)          │
│  1 шт × 303,99 ₽              ← Количество в чеке и цена (ReceiptItem)  │
│  970 мл                       ← Характеристика товара (Item)            │
└─────────────────────────────────────────────────────────────────────────┘

Результат парсинга:
- Product: "Сок апельсиновый" (категория)
- Item: Name="Сок J7 апельсиновый 970 мл", UnitOfMeasure="мл", UnitQuantity=970
- ReceiptItem: Quantity=1, Price=303.99, Amount=303.99
```

**Ещё пример (весовой товар):**

```
Чек:
┌─────────────────────────────────────────────────────────────────────────┐
│ "Свекла"                      ← Название (Item)                         │
│  0.524 кг × 30,99 ₽           ← Количество и цена в чеке (ReceiptItem)  │
│  16,24 ₽                      ← Сумма (ReceiptItem)                     │
└─────────────────────────────────────────────────────────────────────────┘

Результат парсинга:
- Product: "Свекла" (категория)
- Item: Name="Свекла", UnitOfMeasure="кг", UnitQuantity=1 (нет отдельной характеристики)
- ReceiptItem: Quantity=0.524, Price=30.99, Amount=16.24
```

---

## System Architecture

### Sources (Источники чеков)

Источник — это канал получения **сырых данных** (текст письма, файл, JSON от API).
Источник НЕ знает о парсере — он только доставляет данные.

```
ReceiptSource
├── Name: string              // Уникальный идентификатор
├── Type: SourceType          // Email, REST, FileSystem
├── Parser: string            // Имя парсера для обработки
├── IsEnabled: bool
└── Config: {}                // Специфичные настройки типа
```

**Типы источников:**

| Type | Description | Config |
|------|-------------|--------|
| **Email** | IMAP почтовый ящик | Server, Port, Credentials, Filters |
| **REST** | API стороннего сервиса | Endpoint, Auth, Headers |
| **FileSystem** | Папка с файлами/фото | Path, FilePattern |

**Пример конфигурации:**
```json
{
  "ReceiptSources": [
    {
      "Name": "Instamart-Main",
      "Type": "Email",
      "Parser": "Instamart",
      "Email": {
        "ImapServer": "imap.yandex.ru",
        "Username": "user@yandex.ru",
        "SenderFilter": "noreply@instamart.ru"
      }
    }
  ]
}
```

---

### Parsers (Парсеры)

Парсер извлекает структурированные данные (Items, ReceiptItems) из сырых данных источника.
Парсер публикует свою **потребность в AI**.

```
ReceiptParser
├── Name: string              // "Instamart", "Auchan", "GenericLLM"
├── Type: ParserType          // Regex, LLM
├── RequiresAI: bool          // Публикует потребность в AI
└── AiProvider: string?       // Ссылка на AI провайдера (если RequiresAI=true)
```

**Зарегистрированные парсеры:**

| Parser | Type | RequiresAI | Description |
|--------|------|------------|-------------|
| **Instamart** | Regex | false | Чеки Instamart/СберМаркет |
| **GenericLLM** | LLM | true | Универсальный парсер через AI |

**Конфигурация:**
```json
{
  "Parsers": [
    { "Name": "Instamart", "Type": "Regex", "RequiresAI": false },
    { "Name": "GenericLLM", "Type": "LLM", "RequiresAI": true, "AiProvider": "Ollama/qwen2.5:1.5b" }
  ]
}
```

---

### AI Providers (Поставщики AI)

AI Provider — конкретная конфигурация доступа к AI-модели.
Ключ: `Provider/Model` (например `Ollama/qwen2.5:1.5b`).

```
AiProviderConfig
├── Key: string               // "Ollama/qwen2.5:1.5b"
├── Provider: ProviderType    // Ollama, YandexGPT, OpenAI
├── Model: string
├── Temperature: decimal
├── Timeout: int
└── ... специфичные настройки
```

**Конфигурация:**
```json
{
  "AiProviders": [
    {
      "Key": "Ollama/qwen2.5:1.5b",
      "Provider": "Ollama",
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen2.5-coder:1.5b",
      "Temperature": 0.1
    },
    {
      "Key": "Ollama/llama3.2:3b",
      "Provider": "Ollama",
      "BaseUrl": "http://localhost:11434",
      "Model": "llama3.2:3b",
      "Temperature": 0.2
    },
    {
      "Key": "YandexGPT/lite",
      "Provider": "YandexGPT",
      "Model": "yandexgpt-lite",
      "FolderId": "...",
      "ApiKey": "..."
    }
  ]
}
```

---

### AI Operations (Операции AI)

Связка системных операций пост-обработки с AI провайдерами.
Парсинг управляется через `Parser.AiProvider`, не здесь.

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
| **Classification** | Связывание Item → Product |
| **Labels** | Присвоение меток Items |

---

## Workflow (Процесс обработки)

```
┌─────────────────────────────────────────────────────────────────┐
│                    1. СБОР СЫРЫХ ДАННЫХ                         │
│                                                                 │
│  Source #1 (Email)     Source #2 (Email)     Source #3 (REST)  │
│        │                     │                     │            │
│        ▼                     ▼                     ▼            │
│   RawReceipt            RawReceipt            RawReceipt       │
│   (text/html)           (text/html)           (json)           │
└────────┬─────────────────────┬─────────────────────┬────────────┘
         │                     │                     │
         ▼                     ▼                     ▼
┌─────────────────────────────────────────────────────────────────┐
│                    2. ПАРСИНГ                                   │
│                                                                 │
│  Parser: Instamart     Parser: GenericLLM    Parser: RestJson  │
│  (RequiresAI: false)   (RequiresAI: true)    (RequiresAI: false)│
│        │                     │                     │            │
│        │               ┌─────┴─────┐               │            │
│        │               │ AI Provider│               │            │
│        │               └─────┬─────┘               │            │
│        ▼                     ▼                     ▼            │
│   ParsedReceipt         ParsedReceipt         ParsedReceipt    │
└────────┬─────────────────────┬─────────────────────┬────────────┘
         │                     │                     │
         └──────────────────── ┼ ────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│              3. СОХРАНЕНИЕ В БД                                 │
│         Receipt + Items + ReceiptItems → DB                     │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│              4. КАТЕГОРИЗАЦИЯ (AI)                              │
│         Item → Product (AiOperations.Classification)           │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│              5. ПОМЕТКИ (AI)                                    │
│         Item → Labels (AiOperations.Labels)                    │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│              6. ОЧИСТКА                                         │
│         DELETE Products WHERE нет Items и нет Children         │
└─────────────────────────────────────────────────────────────────┘
```

---

## Data Model

```
┌─────────────────┐
│     Labels      │  ← Метки пользователя ("Молоко для кофе", "Папа доволен")
├─────────────────┤
│ Id              │
│ Name            │
│ Color           │
└────────┬────────┘
         │
         ├──────────────────────┐
         ▼                      ▼
┌─────────────────┐    ┌─────────────────┐
│  ProductLabels  │    │   ItemLabels    │  ← Связующие таблицы (many-to-many)
└────────┬────────┘    └────────┬────────┘
         │                      │
         ▼                      ▼
┌─────────────────┐    ┌─────────────────┐
│    Products     │    │     Items       │  ← Справочник товаров
├─────────────────┤    ├─────────────────┤
│ Id              │    │ Id              │
│ ParentId (self) │◄───│ ProductId (FK)  │  1 ──── *
│ Name            │    │ UnitOfMeasure   │
└─────────────────┘    │ UnitQuantity    │
   (иерархия)          │ Name            │
                       └────────┬────────┘
                                │
                                │ 1
                                ▼
                       ┌─────────────────┐
                       │  ReceiptItems   │  ← Позиция в чеке
                       ├─────────────────┤
                       │ Id              │
                       │ ItemId (FK)     │
                       │ ReceiptId (FK)  │
                       │ Quantity        │
                       │ Price           │
                       │ Amount          │
                       └────────┬────────┘
                                │
                                ▼
                       ┌─────────────────┐
                       │    Receipts     │  ← Чек из магазина
                       ├─────────────────┤
                       │ Id              │
                       │ Shop            │
                       │ ReceiptDate     │
                       │ Total           │
                       │ Status          │
                       └─────────────────┘
```

### Entity Descriptions

| Entity | Description |
|--------|-------------|
| **Product** | Суть товара — абстрактная категория (сок, молоко, хлеб). Иерархия через ParentId. |
| **Item** | Конкретный товар на полке магазина. Уникален по названию из чека. Характеристики: UnitOfMeasure, UnitQuantity. |
| **ReceiptItem** | Товарная позиция в чеке: ссылка на Item + количество + цена + сумма. |
| **Receipt** | Чек (магазин, дата, номер заказа, итого, статус обработки). |
| **Label** | Пользовательская метка для группировки ("Сытая семья", "Чистый дом"). |
| **ProductLabel** | Связь Product ↔ Label (many-to-many). |
| **ItemLabel** | Связь Item ↔ Label (many-to-many). |
| **EmailHistory** | История обработки писем для дедупликации. |

---

## Projects

### SmartBasket.Core
Базовые сущности и конфигурация. Без зависимостей.

**Entities:**
- `Product` - группа товаров с иерархией
- `Item` - справочник уникальных товаров
- `Receipt` - чек из магазина
- `ReceiptItem` - позиция в чеке
- `Label` - пользовательская метка
- `ProductLabel`, `ItemLabel` - связи many-to-many
- `EmailHistory` - история обработки писем

**Configuration:**
- `AppSettings` - корневой класс настроек
- `ReceiptSourceConfig` - конфигурация источников
- `ParserConfig` - конфигурация парсеров
- `AiProviderConfig` - конфигурация AI провайдеров
- `AiOperationsConfig` - связка операций с провайдерами

### SmartBasket.Data
Entity Framework Core DbContext.

```csharp
public class SmartBasketDbContext : DbContext
{
    public DbSet<Product> Products { get; set; }
    public DbSet<Item> Items { get; set; }
    public DbSet<Receipt> Receipts { get; set; }
    public DbSet<ReceiptItem> ReceiptItems { get; set; }
    public DbSet<Label> Labels { get; set; }
    public DbSet<ProductLabel> ProductLabels { get; set; }
    public DbSet<ItemLabel> ItemLabels { get; set; }
    public DbSet<EmailHistory> EmailHistory { get; set; }
}
```

**Providers:**
- PostgreSQL (default)
- SQLite (for testing)

### SmartBasket.Services
Бизнес-логика.

**Sources:**
- `IReceiptSource` - интерфейс источника данных
- `EmailReceiptSource` - получение чеков из IMAP

**Parsers:**
- `IReceiptTextParser` - интерфейс парсера
- `InstamartReceiptParser` - regex-парсер для Instamart
- `LlmReceiptParser` - универсальный парсер через AI

**AI Providers:**
- `ILlmProvider` - интерфейс AI провайдера
- `OllamaLlmProvider` - локальная Ollama
- `YandexGptLlmProvider` - Yandex GPT

**Services:**
- `ReceiptCollectionService` - оркестрация сбора чеков
- `ProductClassificationService` - категоризация через AI
- `LabelAssignmentService` - назначение меток через AI
- `ProductCleanupService` - удаление осиротевших Products

### SmartBasket.WPF
WPF приложение с MVVM (CommunityToolkit.Mvvm).

**UI Features:**
- Современный card-based дизайн с тенями и градиентами
- Master-Detail: список чеков → детали с позициями
- Настройки с древовидной навигацией:
  - Источники чеков
  - Парсеры
  - Поставщики AI
  - Операции AI

### SmartBasket.CLI
Консольные утилиты для тестирования.

```bash
dotnet run -- test-ollama [count]  # Тест производительности Ollama
dotnet run -- email                # Скачать письма
dotnet run -- parse                # Скачать и распарсить
```

---

## Settings UI Structure

```
Настройки
│
├── Источники чеков
│   ├── [+] Добавить
│   └── MyEmail-Instamart
│         Type: Email
│         Parser: Instamart
│         IMAP: imap.yandex.ru
│         ...
│
├── Парсеры
│   ├── Instamart (Regex) — AI не требуется
│   └── GenericLLM (LLM)
│         AI Provider: [выбор ▼]
│
├── Поставщики AI
│   ├── [+] Добавить
│   ├── Ollama/qwen2.5:1.5b
│   └── YandexGPT/lite
│
└── Операции AI
    ├── Категоризация: [выбор провайдера ▼]
    └── Пометки: [выбор провайдера ▼]
```

---

## Configuration Example

**appsettings.json:**
```json
{
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionString": "Host=localhost;Database=smart_basket;..."
  },

  "ReceiptSources": [
    {
      "Name": "Instamart-Main",
      "Type": "Email",
      "Parser": "Instamart",
      "IsEnabled": true,
      "Email": {
        "ImapServer": "imap.yandex.com",
        "ImapPort": 993,
        "UseSsl": true,
        "Username": "user@yandex.ru",
        "Password": "app-password",
        "SenderFilter": "noreply@instamart.ru",
        "SubjectFilter": "заказ",
        "SearchDaysBack": 30
      }
    }
  ],

  "Parsers": [
    { "Name": "Instamart", "Type": "Regex", "RequiresAI": false },
    { "Name": "GenericLLM", "Type": "LLM", "RequiresAI": true, "AiProvider": "Ollama/qwen2.5:1.5b" }
  ],

  "AiProviders": [
    {
      "Key": "Ollama/qwen2.5:1.5b",
      "Provider": "Ollama",
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen2.5-coder:1.5b",
      "Temperature": 0.1,
      "TimeoutSeconds": 30
    }
  ],

  "AiOperations": {
    "Classification": "Ollama/qwen2.5:1.5b",
    "Labels": "Ollama/qwen2.5:1.5b"
  }
}
```

---

## Dependencies

| Package | Purpose |
|---------|---------|
| CommunityToolkit.Mvvm | MVVM (ObservableProperty, RelayCommand) |
| MailKit | IMAP email client |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL provider |
| Microsoft.EntityFrameworkCore.Sqlite | SQLite provider |
| Microsoft.Extensions.DependencyInjection | DI container |
| Microsoft.Extensions.Configuration.Json | JSON config |
| Microsoft.Extensions.Logging | Logging abstractions |

---

## Threading Model

```
┌─────────────────────┐     ┌─────────────────────┐
│     UI Thread       │     │    ThreadPool       │
│                     │     │                     │
│  MainWindow         │     │  Task.Run(() =>     │
│  MainViewModel      │     │    ReceiptSource    │
│  ObservableCollect  │◀───│    Parser           │
│  (with lock)        │     │    AI Provider      │
│                     │     │    DbContext)       │
└─────────────────────┘     └─────────────────────┘

BindingOperations.EnableCollectionSynchronization
позволяет безопасно модифицировать коллекции из ThreadPool
```

---

## TODO

- [x] Реализовать создание Items и ReceiptItems при парсинге чеков
- [x] AI категоризация товаров → Product (ProductClassificationService)
- [x] Иерархия продуктов (Product.ParentId)
- [x] AI назначение меток (LabelAssignmentService)
- [x] Regex-парсер для Instamart (без AI)
- [ ] Рефакторинг конфигурации (Sources, Parsers, AI Providers)
- [ ] Новый UI настроек с древовидной навигацией
- [ ] Очистка осиротевших Products
- [ ] UI для управления метками (Labels)
- [ ] Аналитика по меткам и категориям
