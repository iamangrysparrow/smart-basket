# SmartBasket Architecture

> **AI Integration:** См. [ARCHITECTURE-AI.md](ARCHITECTURE-AI.md) для документации по интеграции с Ollama LLM.

## Overview

SmartBasket - приложение для автоматического парсинга чеков из email с использованием локального LLM (Ollama).

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
│ • EmailService  │   │ • DbContext     │   │ • Entities      │
│ • OllamaService │   │ • PostgreSQL    │   │ • Configuration │
└─────────────────┘   └─────────────────┘   └─────────────────┘
```

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
| **Product** | Группа товаров с иерархией (ParentId). AI генерирует, пользователь переименовывает. |
| **Item** | Уникальный товар из чека. Справочник всех названий с единицами измерения. |
| **ReceiptItem** | Позиция в конкретном чеке: ссылка на Item + количество + цена. |
| **Receipt** | Чек (магазин, дата, статус обработки). |
| **Label** | Пользовательская метка для группировки ("Сытая семья", "Чистый дом"). |
| **ProductLabel** | Связь Product ↔ Label (many-to-many). |
| **ItemLabel** | Связь Item ↔ Label (many-to-many). |
| **EmailHistory** | История обработки писем для дедупликации. |

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
- `EmailSettings` - IMAP сервер, фильтры
- `OllamaSettings` - URL, модель, температура
- `DatabaseSettings` - провайдер, connection string

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
Бизнес-логика. HTTP клиенты, IMAP.

**EmailService** (`IEmailService`):
```csharp
Task<(bool Success, string Message)> TestConnectionAsync(EmailSettings settings);
Task<IReadOnlyList<EmailMessage>> FetchEmailsAsync(EmailSettings settings, IProgress<string>? progress);
```

**OllamaService** (`IOllamaService`):
```csharp
void SetPromptTemplatePath(string path);  // Внешний шаблон prompt
Task<(bool Success, string Message)> TestConnectionAsync(OllamaSettings settings);
Task<ParsedReceipt> ParseReceiptAsync(OllamaSettings settings, string emailBody, DateTime emailDate, IProgress<string>? progress);
```
- Поддержка streaming (построчный вывод ответа модели)
- Внешний шаблон `prompt_template.txt` с плейсхолдерами `{{YEAR}}`, `{{RECEIPT_TEXT}}`

### SmartBasket.WPF
WPF приложение с MVVM (CommunityToolkit.Mvvm).

**App.xaml.cs** - DI контейнер, exception handlers
**MainWindow.xaml** - UI layout
**MainViewModel** - команды, состояние

**UI Features:**
- Современный card-based дизайн с тенями и градиентами
- Master-Detail: список чеков → детали с позициями
- Фильтры: дата, магазин (с поиском через ComboBox)
- Поиск по позициям в выбранном чеке
- Управление категориями (Products)
- Назначение меток (Labels)

**Key Commands:**
- `TestEmailConnectionCommand` - тест IMAP
- `TestOllamaConnectionCommand` - тест Ollama
- `FetchAndParseEmailsCommand` - полный пайплайн
- `LoadReceiptsCommand` - загрузка из БД с фильтрами
- `ApplyFiltersCommand` / `ClearFiltersCommand` - фильтрация
- `SaveSettingsCommand` - сохранение настроек
- `SaveLogCommand` - сохранение лога в файл
- `LoadCategoryTreeCommand` - загрузка дерева категорий

### SmartBasket.CLI
Консольные утилиты для тестирования.

```bash
dotnet run -- test-ollama [count]  # Тест производительности Ollama
dotnet run -- email                # Скачать письма
dotnet run -- parse                # Скачать и распарсить
```

## Data Flow

```
1. Email (IMAP)
   │
   ▼
2. EmailService.FetchEmailsAsync()
   │  - Connect to IMAP server
   │  - Search by filters (sender, subject, date)
   │  - Download message bodies
   │
   ▼
3. OllamaService.ParseReceiptAsync()
   │  - Clean HTML to text
   │  - Build prompt with JSON schema
   │  - Call Ollama API
   │  - Extract JSON from response
   │
   ▼
4. Receipt + Items + ReceiptItems
   │  - Find or create Product (via AI)
   │  - Find or create Item (unique name)
   │  - Create ReceiptItem
   │  - Save to PostgreSQL
   │  - Mark email as processed
   │
   ▼
5. UI (MainViewModel)
   - Отображение в Master-Detail с фильтрами и поиском
   - Управление категориями и метками
```

## Configuration

**appsettings.json:**
```json
{
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionString": "Host=localhost;Database=smart_basket;..."
  },
  "Email": {
    "ImapServer": "imap.yandex.com",
    "ImapPort": 993,
    "UseSsl": true,
    "Username": "user@yandex.ru",
    "Password": "app-password",
    "SenderFilter": "info@shop.ru",
    "SubjectFilter": "заказ",
    "SearchDaysBack": 30
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen2.5-coder:1.5b",
    "Temperature": 0.1,
    "TimeoutSeconds": 30
  }
}
```

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

## Threading Model

```
┌─────────────────────┐     ┌─────────────────────┐
│     UI Thread       │     │    ThreadPool       │
│                     │     │                     │
│  MainWindow         │     │  Task.Run(() =>     │
│  MainViewModel      │     │    EmailService     │
│  ObservableCollect  │◀───│    OllamaService    │
│  (with lock)        │     │    DbContext)       │
│                     │     │                     │
└─────────────────────┘     └─────────────────────┘

BindingOperations.EnableCollectionSynchronization
позволяет безопасно модифицировать коллекции из ThreadPool
```

## TODO (Refactoring in Progress)

- [x] Реализовать создание Items и ReceiptItems при парсинге чеков
- [x] AI категоризация товаров → Product (ProductClassificationService)
- [x] Иерархия продуктов (Product.ParentId)
- [x] AI назначение меток (LabelAssignmentService)
- [ ] UI для управления метками (Labels)
- [ ] Аналитика по меткам и категориям
