# Roadmap: Рефакторинг конфигурации и UI настроек

## Цель

Переход от монолитной конфигурации к модульной архитектуре:
- **Sources** — независимые источники данных
- **Parsers** — отделены от источников, публикуют потребность в AI
- **AI Providers** — переиспользуемые конфигурации AI
- **AI Operations** — связка пост-обработки с провайдерами

---

## Фаза 1: Конфигурация (Core)

### 1.1 Новые классы конфигурации

**Файл:** `SmartBasket.Core/Configuration/`

```
ReceiptSourceConfig.cs
├── Name: string
├── Type: SourceType (enum: Email, REST, FileSystem)
├── Parser: string
├── IsEnabled: bool
└── Email: EmailSourceConfig? (вложенный)

ParserConfig.cs
├── Name: string
├── Type: ParserType (enum: Regex, LLM)
├── RequiresAI: bool
└── AiProvider: string?

AiProviderConfig.cs
├── Key: string
├── Provider: AiProviderType (enum: Ollama, YandexGPT, OpenAI)
├── Model: string
├── BaseUrl: string?
├── Temperature: decimal
├── TimeoutSeconds: int
└── ... специфичные поля

AiOperationsConfig.cs
├── Classification: string?
└── Labels: string?
```

### 1.2 Обновление AppSettings

```csharp
public class AppSettings
{
    public DatabaseSettings Database { get; set; }
    public List<ReceiptSourceConfig> ReceiptSources { get; set; }
    public List<ParserConfig> Parsers { get; set; }
    public List<AiProviderConfig> AiProviders { get; set; }
    public AiOperationsConfig AiOperations { get; set; }

    // Legacy (для обратной совместимости, deprecated)
    public EmailSettings? Email { get; set; }
    public OllamaSettings? Ollama { get; set; }
}
```

### 1.3 Миграция существующих настроек

- При загрузке: если есть `Email` + `Ollama` → автоконвертация в новый формат
- Предупреждение в логе о deprecated формате

---

## Фаза 2: Сервисы (Services)

### 2.1 Интерфейс источника

**Файл:** `SmartBasket.Services/Sources/IReceiptSource.cs`

```csharp
public interface IReceiptSource
{
    string Name { get; }
    SourceType Type { get; }
    Task<IReadOnlyList<RawReceipt>> FetchAsync(IProgress<string>? progress, CancellationToken ct);
}

public record RawReceipt(
    string Content,          // Сырые данные (HTML, JSON, текст)
    string ContentType,      // "text/html", "application/json"
    DateTime Date,
    string? ExternalId       // Для дедупликации (email message-id)
);
```

### 2.2 Email источник

**Файл:** `SmartBasket.Services/Sources/EmailReceiptSource.cs`

- Рефакторинг из существующего `EmailService`
- Возвращает `RawReceipt` вместо `EmailMessage`

### 2.3 Фабрика источников

**Файл:** `SmartBasket.Services/Sources/ReceiptSourceFactory.cs`

```csharp
public class ReceiptSourceFactory
{
    IReceiptSource Create(ReceiptSourceConfig config);
    IReadOnlyList<IReceiptSource> CreateAll(IEnumerable<ReceiptSourceConfig> configs);
}
```

### 2.4 Рефакторинг парсеров

**Файл:** `SmartBasket.Services/Parsing/IReceiptTextParser.cs`

```csharp
public interface IReceiptTextParser
{
    string Name { get; }
    ParserType Type { get; }
    bool RequiresAI { get; }

    bool CanParse(string content, string contentType);
    Task<ParsedReceipt> ParseAsync(RawReceipt raw, ILlmProvider? aiProvider, ...);
}
```

### 2.5 LLM парсер

**Файл:** `SmartBasket.Services/Parsing/LlmReceiptParser.cs`

- Рефакторинг из текущей LLM-логики в `ReceiptParsingService`
- Реализует `IReceiptTextParser` с `RequiresAI = true`

### 2.6 Фабрика AI провайдеров

**Файл:** `SmartBasket.Services/Llm/AiProviderFactory.cs`

```csharp
public class AiProviderFactory
{
    ILlmProvider? GetProvider(string key);  // "Ollama/qwen2.5:1.5b"
    ILlmProvider? GetProviderForOperation(AiOperation operation);
    IReadOnlyList<string> GetAvailableProviders();
}
```

### 2.7 Сервис сбора чеков (оркестратор)

**Файл:** `SmartBasket.Services/ReceiptCollectionService.cs`

```csharp
public class ReceiptCollectionService
{
    Task<CollectionResult> CollectAsync(
        IEnumerable<string>? sourceNames,  // null = все enabled
        IProgress<string>? progress,
        CancellationToken ct);
}
```

Workflow:
1. Получить источники (по именам или все enabled)
2. Для каждого источника:
   - Fetch сырых данных
   - Найти парсер по имени
   - Если парсер требует AI → получить провайдер
   - Парсинг → ParsedReceipt
   - Сохранение в БД
3. Вернуть статистику

### 2.8 Сервис очистки Products

**Файл:** `SmartBasket.Services/Products/ProductCleanupService.cs`

```csharp
public class ProductCleanupService
{
    Task<int> CleanupOrphanedProductsAsync();
}
```

SQL:
```sql
DELETE FROM Products
WHERE Id NOT IN (SELECT DISTINCT ProductId FROM Items WHERE ProductId IS NOT NULL)
  AND Id NOT IN (SELECT DISTINCT ParentId FROM Products WHERE ParentId IS NOT NULL)
```

---

## Фаза 3: UI Настроек (WPF)

### 3.1 Структура ViewModel

```
SettingsViewModel
├── SelectedCategory: SettingsCategory (enum)
├── Categories: ObservableCollection<SettingsCategoryItem>
│
├── SourcesViewModel
│   ├── Sources: ObservableCollection<ReceiptSourceViewModel>
│   ├── SelectedSource: ReceiptSourceViewModel?
│   ├── AddSourceCommand
│   ├── DeleteSourceCommand
│   └── TestSourceCommand
│
├── ParsersViewModel
│   ├── Parsers: ObservableCollection<ParserViewModel>
│   ├── SelectedParser: ParserViewModel?
│   └── AvailableAiProviders: List<string>
│
├── AiProvidersViewModel
│   ├── Providers: ObservableCollection<AiProviderViewModel>
│   ├── SelectedProvider: AiProviderViewModel?
│   ├── AddProviderCommand
│   ├── DeleteProviderCommand
│   └── TestProviderCommand
│
└── AiOperationsViewModel
    ├── ClassificationProvider: string?
    ├── LabelsProvider: string?
    └── AvailableProviders: List<string>
```

### 3.2 UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│ Настройки                                              [X]  │
├─────────────┬───────────────────────────────────────────────┤
│             │                                               │
│ ▼ Источники │  [Название источника]                         │
│   Instamart │  ┌─────────────────────────────────────────┐  │
│             │  │ Тип: [Email ▼]                          │  │
│ ▼ Парсеры   │  │ Парсер: [Instamart ▼]                   │  │
│   Instamart │  │ ☑ Включен                               │  │
│   GenericLLM│  │                                         │  │
│             │  │ === Email настройки ===                 │  │
│ ▼ AI        │  │ IMAP: [imap.yandex.ru    ]              │  │
│   Ollama/.. │  │ Port: [993] ☑ SSL                       │  │
│   YandexGPT │  │ User: [user@yandex.ru    ]              │  │
│             │  │ Pass: [••••••••          ]              │  │
│ ▼ Операции  │  │ Filter: [noreply@instamart.ru]          │  │
│             │  │                                         │  │
│             │  │ [Тест]  [Удалить]  [Сохранить]         │  │
│             │  └─────────────────────────────────────────┘  │
│             │                                               │
│ [+Источник] │                                               │
│ [+Провайдер]│                                               │
└─────────────┴───────────────────────────────────────────────┘
```

### 3.3 XAML компоненты

```
Views/Settings/
├── SettingsWindow.xaml           // Главное окно настроек
├── SourcesSettingsView.xaml      // Список и редактор источников
├── ParsersSettingsView.xaml      // Список парсеров
├── AiProvidersSettingsView.xaml  // Список AI провайдеров
└── AiOperationsSettingsView.xaml // Выбор провайдеров для операций
```

---

## Фаза 4: Интеграция

### 4.1 Обновление MainViewModel

- Заменить прямые вызовы `EmailService` + `ReceiptParsingService`
- Использовать `ReceiptCollectionService`
- Добавить вызов `ProductCleanupService` после обработки

### 4.2 Обновление DI

```csharp
// Sources
services.AddSingleton<ReceiptSourceFactory>();

// Parsers
services.AddSingleton<IReceiptTextParser, InstamartReceiptParser>();
services.AddSingleton<IReceiptTextParser, LlmReceiptParser>();
services.AddSingleton<ReceiptTextParserFactory>();

// AI
services.AddSingleton<AiProviderFactory>();

// Orchestration
services.AddScoped<ReceiptCollectionService>();
services.AddScoped<ProductCleanupService>();
```

### 4.3 Миграция appsettings.json

Автоматическая при первом запуске:
```
Email: {...}           →  ReceiptSources: [{...}]
Ollama: {...}          →  AiProviders: [{...}]
LlmSettings: {...}     →  AiOperations: {...}
```

---

## Порядок выполнения

| # | Задача | Зависит от | Оценка |
|---|--------|------------|--------|
| 1 | Классы конфигурации (Core) | - | S |
| 2 | Обновление AppSettings | 1 | S |
| 3 | IReceiptSource + EmailReceiptSource | - | M |
| 4 | ReceiptSourceFactory | 1, 3 | S |
| 5 | Рефакторинг IReceiptTextParser | - | S |
| 6 | LlmReceiptParser | 5 | M |
| 7 | AiProviderFactory | 1 | M |
| 8 | ReceiptCollectionService | 4, 5, 7 | L |
| 9 | ProductCleanupService | - | S |
| 10 | SettingsViewModel | 1 | M |
| 11 | Settings UI (XAML) | 10 | L |
| 12 | Интеграция в MainViewModel | 8, 9 | M |
| 13 | Миграция конфигурации | 2 | S |
| 14 | Тестирование | all | M |

**S** = Small, **M** = Medium, **L** = Large

---

## Риски и митигация

| Риск | Митигация |
|------|-----------|
| Breaking changes в конфигурации | Автомиграция + обратная совместимость |
| Сложность UI настроек | Итеративная разработка, начать с базового |
| Регрессии в парсинге | Сохранить текущую логику как fallback |

---

## Definition of Done

- [ ] Все тесты проходят
- [ ] Существующая функциональность работает
- [ ] Новый формат конфигурации документирован
- [ ] UI настроек позволяет CRUD для всех сущностей
- [ ] Очистка orphaned Products работает
