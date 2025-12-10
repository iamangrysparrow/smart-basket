# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

### Added
- **Фаза 1: Классы конфигурации (Core)** — новая модульная архитектура конфигурации
  - `SourceType` — enum типов источников (Email, REST, FileSystem)
  - `ParserType` — enum типов парсеров (Regex, LLM)
  - `AiProviderType` — enum типов AI провайдеров (Ollama, YandexGPT, OpenAI)
  - `EmailSourceConfig` — конфигурация email-источника
  - `ReceiptSourceConfig` — конфигурация источника чеков
  - `ParserConfig` — конфигурация парсера
  - `AiProviderConfig` — конфигурация AI провайдера
  - `AiOperationsConfig` — связка AI операций с провайдерами
- **Фаза 2: Сервисы источников и фабрики (Services)**
  - `RawReceipt` — record для сырых данных чека (до парсинга)
  - `IReceiptSource` — интерфейс источника чеков
  - `EmailReceiptSource` — реализация email-источника через IMAP
  - `IReceiptSourceFactory` / `ReceiptSourceFactory` — фабрика источников
  - `IAiProviderFactory` / `AiProviderFactory` — новая фабрика AI провайдеров по ключам
- **Фаза 3: UI настроек с древовидной навигацией (WPF)**
  - `SettingsWindow` — новое окно настроек с древовидной навигацией
  - `SettingsViewModel` — главная ViewModel для настроек
  - `ReceiptSourceViewModel` — ViewModel для редактирования источников
  - `ParserViewModel` — ViewModel для редактирования парсеров
  - `AiProviderViewModel` — ViewModel для редактирования AI провайдеров
  - `AiOperationsViewModel` — ViewModel для связки операций с провайдерами
  - `SourcesSettingsView.xaml` — UI редактора источников
  - `ParsersSettingsView.xaml` — UI редактора парсеров
  - `AiProvidersSettingsView.xaml` — UI редактора AI провайдеров
  - `AiOperationsSettingsView.xaml` — UI редактора AI операций
  - Кнопка открытия расширенных настроек в toolbar
- **Фаза 4: Интеграция**
  - `IReceiptCollectionService` / `ReceiptCollectionService` — оркестратор сбора чеков из источников
    - Автоматический выбор парсера (regex → LLM fallback)
    - Дедупликация по ExternalId
    - Классификация товаров через LLM
    - Автоматическое назначение меток
  - `IProductCleanupService` / `ProductCleanupService` — очистка осиротевших Products
    - Удаление Products без связанных Items
    - Удаление Products без дочерних Products
  - `ConfigurationMigrationService` — автоматическая миграция legacy конфигурации
    - Email → ReceiptSources
    - Ollama → AiProviders
    - YandexGpt → AiProviders
    - Llm → AiOperations
  - Новые команды в MainViewModel:
    - `CollectReceiptsCommand` — сбор чеков через ReceiptCollectionService
    - `CleanupOrphanedProductsCommand` — ручная очистка осиротевших Products

### Changed
- `AppSettings` теперь содержит новые секции: `ReceiptSources`, `Parsers`, `AiProviders`, `AiOperations`
- Legacy свойства (`Email`, `Ollama`, `YandexGpt`, `Llm`) помечены как `[Obsolete]` для обратной совместимости
- DI регистрации обновлены для новых сервисов (Scoped lifecycle)

### Planned
- UI для управления метками (Labels)
- Аналитика по меткам и категориям
- REST и FileSystem источники чеков

---

## [0.3.0] - 2024-12-10

### Added
- **Regex-парсер для Instamart** — парсинг чеков без использования AI
  - `IReceiptTextParser` — интерфейс для парсеров конкретных магазинов
  - `InstamartReceiptParser` — извлечение товаров из чеков Instamart/СберМаркет
  - `ReceiptTextParserFactory` — фабрика для выбора подходящего парсера
- **Документация доменных понятий** в ARCHITECTURE.md:
  - Product (Продукт) — суть товара, абстрактная категория
  - Item (Товар) — конкретная товарная единица на полке магазина
  - ReceiptItem (Товарная позиция) — запись в чеке

### Changed
- `ReceiptParsingService` теперь сначала пробует regex-парсеры, затем fallback на LLM
- Обновлена архитектура системы в ARCHITECTURE.md:
  - Sources (Источники) — каналы получения сырых данных
  - Parsers (Парсеры) — извлечение структурированных данных
  - AI Providers (Поставщики AI) — конфигурации доступа к AI
  - AI Operations (Операции AI) — связка пост-обработки с провайдерами

### Performance
- Парсинг чеков Instamart теперь мгновенный (без вызова LLM API)
- Работает offline для поддерживаемых форматов

---

## [0.2.0] - 2024-12-XX

### Added
- **AI категоризация товаров** — автоматическое определение Product по названию Item
  - `ProductClassificationService` — сервис категоризации через LLM
  - Иерархия продуктов через `Product.ParentId`
- **AI назначение меток** — автоматическое присвоение Labels товарам
  - `LabelAssignmentService` — сервис назначения меток через LLM
- **Поддержка нескольких LLM провайдеров**
  - `ILlmProvider` — унифицированный интерфейс
  - `OllamaLlmProvider` — локальная Ollama (streaming)
  - `YandexGptLlmProvider` — Yandex GPT
  - `LlmProviderFactory` — фабрика провайдеров с выбором по типу операции
- **Конфигурация LLM по операциям** в `LlmSettings`
  - Parsing, Classification, Labels — независимый выбор провайдера

### Changed
- Рефакторинг `OllamaService` → разделение на провайдеры и сервисы

---

## [0.1.0] - 2024-11-XX

### Added
- **Базовая функциональность парсинга чеков из email**
  - `EmailService` — получение писем через IMAP
  - `OllamaService` — парсинг через локальную LLM
  - Поддержка streaming ответов
- **Data Model**
  - `Product` — группа товаров с иерархией
  - `Item` — справочник уникальных товаров
  - `Receipt` — чек из магазина
  - `ReceiptItem` — позиция в чеке
  - `Label` — пользовательская метка
  - `EmailHistory` — дедупликация писем
- **WPF приложение**
  - MVVM архитектура (CommunityToolkit.Mvvm)
  - Master-Detail интерфейс для чеков
  - Фильтры по дате и магазину
  - Настройки подключения (Email, Ollama)
- **Database**
  - PostgreSQL (основной)
  - SQLite (для тестов)
  - Entity Framework Core
- **CLI утилиты**
  - `test-ollama` — тест производительности
  - `email` — скачать письма
  - `parse` — скачать и распарсить
