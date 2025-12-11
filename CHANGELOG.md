# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

### Added
- **Yandex AI Studio Agent Provider**:
  - `YandexAgentLlmProvider` — провайдер для кастомных агентов Yandex AI Studio
  - Использует REST Assistant API (`https://rest-assistant.api.cloud.yandex.net/v1/responses`)
  - **Streaming поддержка** — SSE (Server-Sent Events) для real-time вывода
  - `AiProviderType.YandexAgent` — новый тип провайдера
  - `AiProviderConfig.AgentId` — свойство для ID агента
  - UI настройки: поля Agent ID, Folder ID, API Key
  - Логирование запросов/ответов в формате ARCHITECTURE-AI.md
- **ResponseParser для унифицированного парсинга JSON**:
  - `IResponseParser` / `ResponseParser` — унифицированное извлечение JSON из LLM ответов
  - Поддержка markdown code blocks (`\`\`\`json...`)
  - Поддержка chain-of-thought (bracket matching)
  - 5 fallback стратегий при ошибках парсинга
  - Интеграция в `ProductClassificationService`, `LabelAssignmentService`, `LlmUniversalParser`
- **Автоматическая синхронизация Labels из файла**:
  - `ILabelService.SyncFromFileAsync()` — загружает метки из `user_labels.txt` в БД
  - `ReceiptCollectionService` — при назначении меток автоматически синхронизирует из файла
- **Утилита `HtmlHelper`**: общие методы `CleanHtml()` и `IsHtml()` для работы с HTML

### Changed
- **Рефакторинг интерфейса парсеров (`IReceiptTextParser`)**:
  - `ShopName` → `Name` — уникальный идентификатор для конфигурации
  - Добавлен `SupportedShops: IReadOnlyList<string>` — магазины для будущего авто-определения
  - `ReceiptTextParserFactory.GetParser()` теперь ищет по `Name`
- **Удалены legacy классы конфигурации**:
  - `OllamaSettings`, `YandexGptSettings`, `EmailSettings`, `LlmSettings` — удалены
  - `ConfigurationMigrationService` — удалён (миграция завершена)
  - `IOllamaService`, `OllamaService` — удалены (заменены на `ILlmProvider`)
  - `AppSettings` теперь использует только новую архитектуру (без legacy свойств)
- **Рефакторинг namespace**: классы перемещены из `SmartBasket.Services.Ollama` в `SmartBasket.Services.Llm`
- **Рефакторинг кнопки Collect**: переключена на `CollectReceiptsCommand` (новая архитектура)
- **Удалены #pragma warning disable CS0618**: заменён obsolete `TryParse()` на `TryParseWithRegex()`
- **Удалён легаси код из MainViewModel**: ~450 строк (FetchAndParseEmailsAsync, legacy поля/методы)

### Fixed
- **Исправлена проблема шифрования секретов**: после сохранения настроек API ключи и пароли
  оставались зашифрованными в памяти. Теперь `SettingsService.Save()` расшифровывает их обратно.
- **Исправлен поиск парсера по имени**: конфигурация `Parser: "InstamartParser"` теперь корректно
  находит `InstamartReceiptParser` (раньше искал по `ShopName = "Instamart"`)
- **InstamartParser: исправлен парсинг HTML писем**:
  - Добавлена очистка HTML перед парсингом (использует `HtmlHelper.CleanHtml()`)
  - Добавлено извлечение названия магазина из темы письма (`"Ваш заказ в магазине АШАН"` → `"АШАН"`)
  - Добавлен параметр `subject` в `IReceiptTextParser.Parse()`
- **Labels не назначались**: исправлено — теперь метки автоматически синхронизируются из `user_labels.txt`

---

## [0.4.0] - 2024-12-11

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
  - Новые команды в MainViewModel:
    - `CollectReceiptsCommand` — сбор чеков через ReceiptCollectionService
    - `CleanupOrphanedProductsCommand` — ручная очистка осиротевших Products

### Changed
- `AppSettings` теперь содержит секции: `ReceiptSources`, `Parsers`, `AiProviders`, `AiOperations`
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
