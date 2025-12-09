# SmartBasket Architecture

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

## Projects

### SmartBasket.Core
Базовые сущности и конфигурация. Без зависимостей.

**Entities:**
- `Receipt` - чек (магазин, дата, статус)
- `RawReceiptItem` - сырая позиция из чека (имя, цена, объем)
- `Good` - категоризированный товар
- `Product` - эталонный продукт из справочника
- `EmailHistory` - история обработки писем (для дедупликации)
- `Alert` - уведомления о заканчивающихся продуктах

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
    public DbSet<Receipt> Receipts { get; set; }
    public DbSet<RawReceiptItem> RawItems { get; set; }
    public DbSet<Good> Goods { get; set; }
    public DbSet<Product> Products { get; set; }
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

**Key Commands:**
- `TestEmailConnectionCommand` - тест IMAP
- `TestOllamaConnectionCommand` - тест Ollama
- `FetchAndParseEmailsCommand` - полный пайплайн
- `LoadReceiptsCommand` - загрузка из БД с фильтрами
- `ApplyFiltersCommand` / `ClearFiltersCommand` - фильтрация
- `SaveSettingsCommand` - сохранение настроек
- `SaveLogCommand` - сохранение лога в файл

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
4. Receipt + RawReceiptItems
   │  - Save to PostgreSQL
   │  - Mark email as processed
   │
   ▼
5. UI (MainViewModel)
   - Отображение в Master-Detail с фильтрами и поиском
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

## Completed Features

- [x] Email fetching (IMAP) + Ollama parsing
- [x] Receipt + RawReceiptItems сохраняются в БД
- [x] Modern UI: Master-Detail, фильтры, поиск по позициям

## Future Plans (Phase 2+)

- [ ] Добавление продуктов и товарных позиций
- [ ] Категоризация товаров через LLM
- [ ] Справочник продуктов с нормализованными названиями
- [ ] Отслеживание расхода продуктов
- [ ] Алерты о заканчивающихся продуктах
- [ ] Аналитика покупок (графики, статистика)
- [ ] Web UI (Blazor/React)
