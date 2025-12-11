Продолжаем работу над SmartBasket - WPF приложением для сбора и категоризации чеков с использованием AI.

### Контекст предыдущей сессии

Успешно реализован новый AI провайдер `YandexAgentLlmProvider` для Yandex AI Studio агентов:
- Использует REST Assistant API (`https://rest-assistant.api.cloud.yandex.net/v1/responses`)
- Формат запроса: `{ "prompt": { "id": "agent_id" }, "input": "text" }`
- Формат ответа: `{ "output_text": "..." }` или `{ "output": [{ "type": "text", "text": "..." }] }`
- Тест подключения работает успешно
- Добавлено логирование в формате ARCHITECTURE-AI.md

### Текущая проблема

Модель YandexAgent отвечает успешно, но парсинг ответа не работает корректно — товары получают статус "Не категоризировано". Нужно проанализировать формат ответа агента и улучшить извлечение JSON.

### Приоритетные задачи (TODO)

**#7. Анализ ответа AI моделей** (HIGH PRIORITY)
- Создать `IResponseParser` для унифицированного парсинга ответов
- Поддержка markdown code blocks (```json...```)
- Поддержка chain-of-thought (извлечение JSON из reasoning)
- Fallback стратегии при ошибках парсинга
- Логирование и отладка проблем парсинга

**#8. Потоковое чтение ответов Yandex Agent**
- Изучить документацию REST Assistant API на предмет streaming
- Если streaming поддерживается — реализовать
- Если нет — добавить индикатор прогресса
- Унифицировать логирование streaming/non-streaming провайдеров

**#3. Улучшение Application Log**
- Сделать лог копируемым
- Кнопка "Pause auto-scroll"
- Возможно: уровни логирования с фильтрацией

**#6. UI для настройки AI Prompts**
- Секция "AI Prompts" в Settings
- Редактирование промптов через UI
- Кнопка "Reset to default"

### Ключевые файлы

- `src/SmartBasket.Services/Llm/YandexAgentLlmProvider.cs` — провайдер агента
- `src/SmartBasket.Services/Llm/LabelAssignmentService.cs` — назначение меток (использует LLM)
- `src/SmartBasket.Services/Llm/ProductClassificationService.cs` — категоризация (использует LLM)
- `src/TODO_20251211.md` — актуальный список задач
- `ARCHITECTURE.md`, `ARCHITECTURE-AI.md` — документация архитектуры

### Команды

```bash
# Сборка
dotnet build src/SmartBasket.WPF

# Запуск
dotnet run --project src/SmartBasket.WPF

# CLI тесты
dotnet run --project src/SmartBasket.CLI -- parse
