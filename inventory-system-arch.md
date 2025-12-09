# Архитектура системы управления остатками товаров

## Концепция системы

Система предназначена для **отслеживания остатков товаров из чеков** с автоматической категоризацией, пользовательской кураторкой и уведомлениями о пополнении. Система работает с локальными LLM (Ollama) для обработки естественного языка и не требует облачных сервисов.

**Ключевая особенность:** Данные остаются на локальной машине пользователя.

---

## 1. Архитектурные слои

```
┌─────────────────────────────────────────────────────────┐
│                    UI Layer (Web)                        │
│    - Dashboard остатков                                  │
│    - Форма категоризации товаров                         │
│    - Таблица настроек порогов                            │
│    - История и аналитика                                │
└────────┬────────────────────────────────────────────────┘
         │
┌────────▼────────────────────────────────────────────────┐
│              API Layer (REST/GraphQL)                    │
│    - POST /receipts/upload                              │
│    - POST /items/categorize                             │
│    - PUT /products/thresholds                           │
│    - GET /inventory/status                              │
│    - GET /notifications                                 │
└────────┬────────────────────────────────────────────────┘
         │
┌────────▼────────────────────────────────────────────────┐
│         Business Logic Layer (Services)                  │
│    - ReceiptParser                                       │
│    - ItemCategorizer (NLP via Ollama)                    │
│    - InventoryCalculator                                │
│    - NotificationEngine                                 │
│    - EmailPoller                                        │
└────────┬────────────────────────────────────────────────┘
         │
┌────────▼────────────────────────────────────────────────┐
│           Data Access Layer (Repositories)               │
│    - ReceiptRepository                                   │
│    - ProductRepository                                   │
│    - ItemRepository                                      │
│    - ThresholdRepository                                │
│    - ConsumptionHistoryRepository                        │
└────────┬────────────────────────────────────────────────┘
         │
┌────────▼────────────────────────────────────────────────┐
│              Database Layer (SQLite/PostgreSQL)          │
│    - receipts                                            │
│    - products                                            │
│    - items                                               │
│    - thresholds                                          │
│    - consumption_history                                │
│    - categories                                          │
└─────────────────────────────────────────────────────────┘
         │
┌────────▼────────────────────────────────────────────────┐
│         External Services (Async)                        │
│    - Email Poller (IMAP/POP3)                           │
│    - Ollama LLM Service                                 │
│    - Notification Service (Desktop/Telegram)            │
└─────────────────────────────────────────────────────────┘
```

---

## 2. Иерархия данных

### 2.1 Структурная иерархия

```
Продукт (Product)
├── название: "Молоко"
├── пользовательская категория: "Молоко"
├── пороговое значение (unit): 2 литра
├── средний расход в день: 0.5 литра
└── Товарные позиции (Items)
    ├── Item 1: Молоко 3.2% жирности
    │   ├── объем: 1 литр
    │   ├── цена за единицу: 100 руб
    │   └── источник: "ПЯТЁРОЧКА"
    │       └── Товары (Goods)
    │           ├── Good 1: Пакет от 2024-12-08
    │           │   ├── количество: 2 шт
    │           │   ├── дата покупки: 2024-12-08
    │           │   └── чек: receipt_id_1
    │           └── Good 2: Пакет от 2024-12-06
    │               ├── количество: 1 шт
    │               └── дата покупки: 2024-12-06
    │
    └── Item 2: Молоко 1.5% жирности
        ├── объем: 0.5 литра
        ├── цена за единицу: 80 руб
        └── источник: "ПЯТЁРОЧКА"
```

### 2.2 Расчёт кэша остатка

**Формула:**
```
Cache = Σ(количество_товаров) - (дней_прошло × средний_расход_в_день)
```

**Пример:**
```
Продукт: Молоко
- Item 1 (Молоко 3.2%): 2 пакета × 1л = 2л
- Item 2 (Молоко 1.5%): 1 пакет × 0.5л = 0.5л
= ВСЕГО: 2.5л

Средний расход: 0.5л/день
Дней прошло: 2
Прогнозный расход: 2 × 0.5 = 1л

CACHE = 2.5л - 1л = 1.5л

Порог: 2л
Статус: ⚠️ ТРЕБУЕТ ПОПОЛНЕНИЯ (1.5л < 2л)
```

---

## 3. Сценарии использования (User Flows)

### 3.1 Flow 1: Импорт и парсинг чеков

```
┌─────────────────────────────────────────┐
│ 1. Email Poller (асинхронный сервис)   │
│    - Проверяет почту каждый час        │
│    - Ищет письма от Яндекс.Маркета     │
│    - Экстрактит HTML чеки               │
└────────────┬────────────────────────────┘
             │
┌────────────▼────────────────────────────┐
│ 2. Receipt Parser                       │
│    - HTML → JSON парсинг                │
│    - Экстрактит:                        │
│      • магазин                          │
│      • дату                             │
│      • товары (название, объём, цену)   │
│    - Сохраняет в БД                     │
└────────────┬────────────────────────────┘
             │
┌────────────▼────────────────────────────┐
│ 3. User Notification                    │
│    - "Новые чеки загружены (3 товара)"  │
│    - Пользователь переходит в UI        │
└────────────┬────────────────────────────┘
             │
┌────────────▼────────────────────────────┐
│ 4. Dashboard отображает                 │
│    - Распознанные товары                │
│    - Статус категоризации               │
└─────────────────────────────────────────┘
```

### 3.2 Flow 2: Категоризация товаров

```
┌──────────────────────────────────────────┐
│ User видит в UI новые товары:            │
│ - "Молочный коктейль Чудо шоколад 3%"  │
│ - "Чай черный Ahmad Tea Earl Grey"       │
│ - "Мешки для мусора Econta 60л"          │
└─────────────────┬──────────────────────┘
                  │
          ┌───────┴──────────┬────────────┐
          │                  │            │
    ┌─────▼───┐         ┌────▼─────┐  ┌──▼──────┐
    │ Вариант │         │ Вариант  │  │ Вариант │
    │    1    │         │    2     │  │    3    │
    └─────────┘         └──────────┘  └─────────┘
    Отмечить как          Добавить      Пропустить
    Молоко для            новый продукт
    кофе
          │                  │            │
          └──────────┬───────┴────────────┘
                     │
        ┌────────────▼───────────────┐
        │ System: Запомни связь      │
        │ "Молочный коктейль..." →   │
        │ "Молоко для кофе"          │
        └────────────────────────────┘
```

### 3.3 Flow 3: Мониторинг и уведомления

```
┌──────────────────────────────────────────┐
│ Background Job (каждый час)              │
│ - Рассчитывает Cache для каждого Product│
│ - Сравнивает с Threshold                 │
└────────────┬─────────────────────────────┘
             │
    ┌────────┴──────────┐
    │ Cache > Threshold?│
    └────────┬──────────┘
             │
        ┌────┴─────┐
        │ ДА       │ НЕТ
        │          │
    ┌───▼────┐  ┌──▼──────┐
    │ OK ✓   │  │ ALERT ⚠️ │
    └────────┘  │          │
               │ Отправить│
               │ уведомле-│
               │ ние:     │
               │ "Молоко  │
               │ закончи- │
               │ тся за   │
               │ 2 дня"   │
               └──────────┘
```

---

## 4. Ключевые модули и сервисы

### 4.1 Email Poller Service

**Назначение:** Автоматический мониторинг почты и загрузка чеков

**Конфигурация:**
```yaml
email_poller:
  enabled: true
  interval_minutes: 60
  email_provider: "imap"  # IMAP/POP3
  sender_filter: "noreply@mail.yandex.ru"
  ssl: true
  folders:
    - inbox
    - promotions
```

**Обработка:**
1. Подключение к почте пользователя
2. Поиск писем с чеками (по отправителю и содержимому)
3. Экстракция HTML-содержимого письма
4. Передача на парсинг
5. Сохранение в БД с статусом "новый"

---

### 4.2 Receipt Parser Service

**Назначение:** Структурирование неструктурированных HTML чеков

**Логика парсинга:**

```python
def parse_receipt(html_content):
    """
    Входные данные: HTML из письма о чеке
    Выходные данные: JSON со структурой товаров
    """
    
    # Шаг 1: Санитизация HTML
    soup = BeautifulSoup(html_content)
    
    # Шаг 2: Экстракция метаданных
    shop = extract_shop()        # "ПЯТЁРОЧКА"
    date = extract_date()        # "2024-12-08"
    order_id = extract_order_id()# "H7257375483"
    
    # Шаг 3: Экстракция товаров (регулярные выражения + CSS селекторы)
    items = []
    for item_block in soup.select('.item, [data-item]'):
        item = {
            'raw_name': extract_text(item_block),          # "Молочный коктейль Чудо..."
            'raw_volume': extract_volume(item_block),      # "200 мл"
            'raw_price': extract_price(item_block),        # "53.99"
            'quantity': extract_quantity(item_block),      # "1 шт"
            'shop': shop,
            'date': date
        }
        items.append(item)
    
    return {
        'receipt_id': order_id,
        'shop': shop,
        'date': date,
        'items': items,
        'status': 'parsed'
    }
```

**Регулярные выражения для экстракции:**

```regex
# Объём товара
(\d+(?:\.\d+)?)\s*(мл|л|шт|г|кг|см)

# Цена в рублях
(^\d+\.\d{2}|^[₽₴$€]?\d+\.?\d*)

# Дата
(\d{1,2}\s+\w+\s+\d{4}|^\d{4}-\d{2}-\d{2})
```

**Вызовы парсинга:**
- HTML может быть не-стандартизирован между письмами
- Нужна система fallback для ручного ввода
- Хранить исходный HTML для переиспользования

---

### 4.3 Item Categorizer Service (NLP)

**Назначение:** Связать распознанные товары с пользовательскими категориями

**Архитектура:**

```
┌────────────────────────────────────────┐
│ Распознанный товар:                    │
│ "Молочный коктейль Чудо шоколад 3%"   │
└──────────┬─────────────────────────────┘
           │
┌──────────▼──────────────────────────────┐
│ Стратегия 1: Точное совпадение (fast)   │
│ Поиск в истории прошлых категоризаций  │
│ (кэш в памяти)                         │
└──────────┬─────────────────────────────┘
           │ НЕ НАЙДЕНО
┌──────────▼──────────────────────────────┐
│ Стратегия 2: Fuzzy matching             │
│ Похожие товары из истории               │
│ (сходство > 80%)                        │
└──────────┬─────────────────────────────┘
           │ НЕ НАЙДЕНО
┌──────────▼──────────────────────────────┐
│ Стратегия 3: NLP (Ollama)               │
│ Отправить в локальную LLM               │
│ Запрос: "Классифицируй товар..."        │
└──────────┬─────────────────────────────┘
           │
┌──────────▼──────────────────────────────┐
│ Результаты:                             │
│ - Предложенная категория: "Молоко"      │
│ - Уверенность: 95%                      │
│ - Варианты: [Молоко, Молоко для кофе]  │
└──────────┬─────────────────────────────┘
           │
┌──────────▼──────────────────────────────┐
│ UI: Пользователь подтверждает            │
│ "✓ Молоко для кофе"                     │
└──────────────────────────────────────────┘
```

**Prompt для Ollama:**

```
Ты помощник по категоризации товаров.

Пользовательские категории:
- Молоко
- Молоко для кофе
- Мясо
- Мясо на суп
- Кофе нормальный
- Кофе для гостей

Товар: "Молочный коктейль Чудо шоколад 3% 200 мл"

Задача:
1. Определи, к какой категории относится товар
2. Дай уверенность (%)
3. Предложи еще 2 возможных варианта

Ответ в JSON:
{
  "primary_category": "Молоко для кофе",
  "confidence": 92,
  "alternatives": ["Молоко", "Кофе нормальный"],
  "reasoning": "Молочный коктейль это молочный продукт..."
}
```

**Параметры Ollama:**
```yaml
ollama:
  model: "mistral:latest"  # или "neural-chat", "orca-mini"
  temperature: 0.3         # Низкая температура - более предсказуемо
  top_p: 0.9
  context_window: 2048
  timeout: 30s
```

---

### 4.4 Inventory Calculator Service

**Назначение:** Расчёт текущих остатков и прогнозирование

**Логика:**

```python
def calculate_inventory(product_id, date=None):
    """
    Рассчитать кэш остатка для продукта
    """
    date = date or datetime.now()
    
    # Шаг 1: Получить все товары продукта
    items = db.items.filter(product_id=product_id)
    
    # Шаг 2: Получить все товары (goods) из этих позиций
    total_quantity = 0
    for item in items:
        goods = db.goods.filter(item_id=item.id)
        for good in goods:
            # Нормализовать к единицам продукта
            normalized_qty = good.quantity * item.unit_ratio
            total_quantity += normalized_qty
    
    # Шаг 3: Рассчитать прогнозный расход
    product = db.products.get(product_id)
    consumption_history = db.consumption_history.filter(
        product_id=product_id,
        date__gte=date - timedelta(days=30)
    )
    
    # Средний расход в день
    days_count = (date - consumption_history[0].date).days
    avg_daily_consumption = sum(h.quantity for h in consumption_history) / days_count
    
    # Дни прошло с последней покупки
    last_purchase_date = max(g.date for g in all_goods)
    days_passed = (date - last_purchase_date).days
    
    # Шаг 4: Рассчитать кэш
    projected_consumption = avg_daily_consumption * days_passed
    cache = total_quantity - projected_consumption
    
    # Шаг 5: Определить статус
    threshold = product.threshold
    status = "OK" if cache > threshold else "ALERT"
    
    return {
        'product_id': product_id,
        'total_quantity': total_quantity,
        'avg_daily_consumption': avg_daily_consumption,
        'projected_consumption': projected_consumption,
        'cache': cache,
        'threshold': threshold,
        'status': status,
        'days_until_shortage': (cache / avg_daily_consumption) if avg_daily_consumption > 0 else 0
    }
```

---

### 4.5 Notification Engine

**Назначение:** Отправка уведомлений пользователю

**Каналы уведомлений:**
1. **Desktop Push** (системное уведомление)
2. **Telegram** (асинхронное)
3. **Email** (избыточное, для резервной копии)
4. **Web Dashboard** (встроенные уведомления)

**Логика:**

```python
def check_and_notify():
    """Фоновая задача каждый час"""
    products = db.products.all()
    
    for product in products:
        inventory = calculate_inventory(product.id)
        
        # Проверить, был ли уже отправлен алерт
        recent_alert = db.alerts.filter(
            product_id=product.id,
            status='ALERT',
            created_at__gte=now() - timedelta(hours=24)
        ).first()
        
        if inventory['status'] == 'ALERT' and not recent_alert:
            message = f"""
            ⚠️ {product.name}
            Остаток: {inventory['cache']:.2f} {product.unit}
            Истощится через: {inventory['days_until_shortage']:.1f} дня
            """
            
            # Отправить уведомление
            notify_desktop(message)
            if product.telegram_enabled:
                notify_telegram(product.telegram_chat_id, message)
            
            # Логировать
            db.alerts.create(
                product_id=product.id,
                status='ALERT',
                message=message
            )
```

---

## 5. База данных - схема

```sql
-- Продукты (категории пользователя)
CREATE TABLE products (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    name VARCHAR(255) NOT NULL,          -- "Молоко"
    unit VARCHAR(50) NOT NULL,           -- "л", "шт", "г"
    threshold DECIMAL(10,3) NOT NULL,    -- 2.0 (пороговое значение)
    avg_daily_consumption DECIMAL(10,3), -- рассчитывается автоматически
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

-- Товарные позиции (детализация продукта)
CREATE TABLE items (
    id UUID PRIMARY KEY,
    product_id UUID NOT NULL REFERENCES products(id),
    name VARCHAR(255) NOT NULL,          -- "Молоко 3.2%"
    unit_ratio DECIMAL(10,3) NOT NULL,   -- 1.0 если совпадает с product.unit
    shop VARCHAR(255),                   -- "ПЯТЁРОЧКА"
    created_at TIMESTAMP DEFAULT NOW()
);

-- Товары (конкретные единицы)
CREATE TABLE goods (
    id UUID PRIMARY KEY,
    item_id UUID NOT NULL REFERENCES items(id),
    receipt_id UUID NOT NULL REFERENCES receipts(id),
    quantity DECIMAL(10,3) NOT NULL,     -- 2 (пакета)
    price DECIMAL(10,2),
    purchase_date DATE NOT NULL,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Чеки
CREATE TABLE receipts (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    receipt_number VARCHAR(255),         -- "H7257375483"
    shop VARCHAR(255) NOT NULL,          -- "ПЯТЁРОЧКА"
    receipt_date DATE NOT NULL,
    email_id VARCHAR(255) UNIQUE,        -- для дедупликации
    raw_html LONGTEXT,                   -- исходный HTML для переиспользования
    status VARCHAR(50),                  -- "parsed", "categorized", "archived"
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

-- История потребления (для расчёта среднего расхода)
CREATE TABLE consumption_history (
    id UUID PRIMARY KEY,
    product_id UUID NOT NULL REFERENCES products(id),
    date DATE NOT NULL,
    quantity_consumed DECIMAL(10,3) NOT NULL,
    source VARCHAR(50),                  -- "calculated", "manual"
    created_at TIMESTAMP DEFAULT NOW()
);

-- Пороговые значения и настройки
CREATE TABLE thresholds (
    id UUID PRIMARY KEY,
    product_id UUID NOT NULL REFERENCES products(id),
    threshold_value DECIMAL(10,3) NOT NULL,
    updated_by VARCHAR(255),
    updated_at TIMESTAMP DEFAULT NOW()
);

-- Уведомления
CREATE TABLE alerts (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    product_id UUID NOT NULL REFERENCES products(id),
    status VARCHAR(50),                  -- "ALERT", "ACKNOWLEDGED", "RESOLVED"
    message TEXT,
    sent_at TIMESTAMP,
    acknowledged_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT NOW()
);

-- История категоризации для кэширования
CREATE TABLE categorization_cache (
    id UUID PRIMARY KEY,
    raw_item_name VARCHAR(255) NOT NULL UNIQUE,
    product_id UUID NOT NULL REFERENCES products(id),
    confidence DECIMAL(5,2),
    categorized_by VARCHAR(50),          -- "fuzzy", "ollama", "manual"
    created_at TIMESTAMP DEFAULT NOW()
);

-- История писем (для дедупликации)
CREATE TABLE email_history (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    email_id VARCHAR(255) UNIQUE NOT NULL,
    sender VARCHAR(255),
    subject VARCHAR(255),
    received_at TIMESTAMP,
    processed_at TIMESTAMP,
    status VARCHAR(50),                  -- "processed", "failed", "skipped"
    created_at TIMESTAMP DEFAULT NOW()
);
```

---

## 6. UI/UX компоненты

### 6.1 Dashboard (главная страница)

```
┌─────────────────────────────────────────────────────────┐
│                    📊 ОСТАТКИ ТОВАРОВ                    │
│                                                           │
│  ⭐ Отслеживается: 6 продуктов                            │
│  🔔 Требуют внимания: 2 продукта                         │
│                                                           │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  ⚠️  МОЛОКО              1.5л / 2л (75%)                 │
│      Истощится через: 2 дня                             │
│      Посл. покупка: 2024-12-08                          │
│      [Добавить] [Настроить] [История]                   │
│                                                           │
│  ✓  МОЛОКО ДЛЯ КОФЕ     0.8л / 0.5л (160%)              │
│      Всё хорошо                                         │
│                                                           │
│  ⚠️  МЯС НА СУП         0.3кг / 1кг (30%)                │
│      Истощится через: 0.6 дней (СРОЧНО!)               │
│      [Добавить] [Настроить] [История]                   │
│                                                           │
│  ✓  КОФЕ НОРМАЛЬНЫЙ     300г / 200г (150%)              │
│  ✓  КОФЕ ДЛЯ ГОСТЕЙ     400г / 300г (133%)              │
│  ✓  МЯСО                2.2кг / 2кг (110%)              │
│                                                           │
├─────────────────────────────────────────────────────────┤
│  Последний импорт чеков: 2024-12-09 14:05 (6 товаров)   │
│  [↻ Обновить] [⚙ Настройки] [📈 Аналитика]             │
└─────────────────────────────────────────────────────────┘
```

### 6.2 Форма категоризации новых товаров

```
┌─────────────────────────────────────────────────────────┐
│            📥 НОВЫЕ ТОВАРЫ ИЗ ЧЕКОВ (3)                  │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  Товар: Молочный коктейль Чудо шоколад 3% 200 мл        │
│  Объём: 200 мл                                          │
│  Магазин: ПЯТЁРОЧКА                                     │
│  Дата: 2024-12-08                                       │
│                                                           │
│  🤖 Предложено (Ollama, 92%): Молоко для кофе           │
│                                                           │
│  Категория: [▼ Молоко для кофе]                         │
│             Альтернативы: Молоко, Кофе нормальный       │
│                                                           │
│  [ ] Или создать новый продукт:                         │
│  Название: ____________________________________         │
│  Единица: [▼ мл]                                        │
│                                                           │
│  [✓ Подтвердить] [✗ Пропустить] [➜ Следующий]         │
│                                                           │
├─────────────────────────────────────────────────────────┤
│ 2 из 3                                                   │
└─────────────────────────────────────────────────────────┘
```

### 6.3 Таблица настроек порогов

```
┌──────────────────────────────────────────────────────────┐
│              ⚙️  НАСТРОЙКИ ПОРОГОВ ОСТАТКОВ              │
├──────────────────────────────────────────────────────────┤
│                                                            │
│  Продукт           Текущий Порог  Расход/день Действие   │
│  ─────────────────────────────────────────────────────   │
│  Молоко            2.0 л          0.5 л/день [Изменить] │
│  Молоко для кофе   0.5 л          0.2 л/день [Изменить] │
│  Мясо              2.0 кг         0.3 кг/день [Изменить] │
│  Мясо на суп       1.0 кг         0.15 кг/день [Изменить]│
│  Кофе нормальный   200 г          10 г/день  [Изменить] │
│  Кофе для гостей   300 г          5 г/день   [Изменить] │
│                                                            │
│  [Добавить продукт] [Сохранить] [Отмена]                │
│                                                            │
└──────────────────────────────────────────────────────────┘
```

### 6.4 История и аналитика

```
┌─────────────────────────────────────────────────────────┐
│           📈 ИСТОРИЯ ОСТАТКОВ И АНАЛИТИКА                │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  Продукт: [▼ МОЛОКО]                                     │
│  Период: [▼ Последние 30 дней]                           │
│                                                           │
│  График остатков:                                       │
│  3л │     ╱╲                                            │
│  2л │    ╱  ╲    ╱─────                                 │
│  1l │   ╱    ╲  ╱                                       │
│  0л └──────────────────────────────                      │
│     1  5  10  15  20  25  30                            │
│                                                           │
│  Статистика:                                            │
│  - Средний расход: 0.5 л/день                           │
│  - Макс остаток: 3.5л (дата: 2024-11-15)               │
│  - Мин остаток: 0.8л (дата: 2024-12-08)                │
│  - Пик расхода: 1.2л/день (выходные)                    │
│                                                           │
│  [Экспорт CSV] [Печать] [Поделиться]                    │
│                                                           │
└─────────────────────────────────────────────────────────┘
```

---

## 7. Ключевые технические решения

### 7.1 Парсинг HTML чеков

**Стратегия:**

1. **Regex + CSS селекторы** для известных форматов (Яндекс.Маркет имеет стандартный шаблон)
2. **BeautifulSoup** для надёжного парсинга DOM
3. **Fallback к ручному вводу** если парсинг не удался

**Примеры шаблонов:**

```python
# Яндекс.Маркет
YANDEX_MARKET_PATTERNS = {
    'shop': r'<h1[^>]*>(?P<shop>[\w\s]+)</h1>',
    'date': r'(?P<date>\d{1,2}\s+\w+\s+\d{4})',
    'item': r'<div class="item">.*?<span[^>]*>(?P<name>.*?)</span>.*?<span[^>]*>(?P<price>[\d.]+)</span>',
    'volume': r'(\d+(?:\.\d+)?)\s*(мл|л|шт|г|кг)',
}

# BeautifulSoup селекторы
YANDEX_SELECTORS = {
    'items': '.purchase-item, [data-item]',
    'item_name': '.item-title, .name',
    'item_volume': '.item-specs, [data-specs]',
    'item_price': '.item-price, .price',
}
```

**Обработка ошибок:**

```
Если парсинг провалился:
1. Логировать исходный HTML
2. Отправить пользователю форму ручного ввода
3. Дать возможность исправить и переиспользовать
```

---

### 7.2 Использование Ollama для NLP

**Преимущества:**
- ✅ Полностью локальная обработка (приватность)
- ✅ Нет зависимости от облачных сервисов
- ✅ Работает оффлайн
- ✅ Контроль над моделью и промптом

**Интеграция:**

```python
import requests
import json

class OllamaCategor
izer:
    def __init__(self, model='mistral:latest', base_url='http://localhost:11434'):
        self.model = model
        self.base_url = base_url
    
    def categorize(self, raw_item_name, user_categories):
        """Категоризировать товар через Ollama"""
        
        prompt = f"""Ты помощник по классификации товаров в магазине.

Пользовательские категории товаров:
{chr(10).join(f'- {cat}' for cat in user_categories)}

Товар: {raw_item_name}

Определи к какой категории относится товар.
Ответь в JSON формате:
{{"primary": "категория", "confidence": 95, "alternatives": ["кат1", "кат2"]}}
"""
        
        try:
            response = requests.post(
                f'{self.base_url}/api/generate',
                json={
                    'model': self.model,
                    'prompt': prompt,
                    'stream': False,
                    'temperature': 0.3,
                }
            )
            
            result = response.json()['response']
            # Парсить JSON из ответа
            return json.loads(result)
            
        except Exception as e:
            return {
                'primary': None,
                'confidence': 0,
                'alternatives': [],
                'error': str(e)
            }
```

**Требования к системе:**
- RAM: минимум 8GB (для моделей ~4-7B параметров)
- Рекомендуется: 16GB+ (для более крупных моделей)
- Процесс Ollama работает на `localhost:11434`

---

### 7.3 Кэширование категоризации

**Проблема:** Ollama медленнее, чем простой поиск

**Решение:** Трёхуровневый кэш

```python
class CategorizationCache:
    def __init__(self, db):
        self.db = db
        self.memory_cache = {}  # В памяти процесса
    
    def get_category(self, raw_item_name, user_categories):
        # Уровень 1: Память (быстро)
        if raw_item_name in self.memory_cache:
            return self.memory_cache[raw_item_name]
        
        # Уровень 2: БД (кэш истории)
        cached = self.db.categorization_cache.get(raw_item_name)
        if cached and cached.confidence > 80:
            self.memory_cache[raw_item_name] = cached
            return cached
        
        # Уровень 3: Ollama (медленно, но точно)
        result = ollama_categorizer.categorize(raw_item_name, user_categories)
        
        # Сохранить в БД кэш
        self.db.categorization_cache.create(
            raw_item_name=raw_item_name,
            product_id=result['primary'],
            confidence=result['confidence'],
            categorized_by='ollama'
        )
        
        self.memory_cache[raw_item_name] = result
        return result
```

---

## 8. Workflow интеграции компонентов

```
┌───────────────────────────────────────────────────────────────┐
│                    ПОЛНЫЙ WORKFLOW                            │
└───────────────────────────────────────────────────────────────┘

Время: 8:00 - Email Poller просыпается

[1] EMAIL POLLER
    └─→ Подключиться к IMAP
    └─→ Получить новые письма от Яндекс.Маркета
    └─→ Экстрактить HTML чеков (3 письма)
    └─→ Сохранить в БД (статус: raw)
    └─→ Уведомить пользователя: "3 новых чека"

Время: 8:05 - Пользователь открывает Dashboard

[2] RECEIPT PARSER (асинхронно в фоне)
    └─→ Получить все чеки со статусом "raw"
    └─→ Парсить каждый чек (HTML → JSON)
    └─→ Экстрактить товары с метаданными
    └─→ Обновить статус на "parsed"

Время: 8:10 - UI показывает новые товары

[3] ITEM CATEGORIZER (интерактивно)
    └─→ Получить необкатегоризированные товары
    └─→ Для каждого товара:
        └─→ Проверить кэш (точное совпадение)
        └─→ Если не найдено → fuzzy matching в БД
        └─→ Если не найдено → Ollama (NLP)
    └─→ Показать UI с предложенной категорией
    └─→ Пользователь подтверждает/корректирует
    └─→ Сохранить в БД + кэш

Время: 8:20 - Товары категоризованы

[4] INVENTORY CALCULATOR (фоновая задача каждый час)
    ├─→ Получить все продукты
    ├─→ Для каждого:
    │   └─→ Просуммировать количество всех товаров
    │   └─→ Рассчитать средний расход (из истории)
    │   └─→ Рассчитать Cache = текущий - прогноз
    │   └─→ Сравнить с Threshold
    │   └─→ Сохранить статус
    └─→ Сигнал для Notification Engine

[5] NOTIFICATION ENGINE (в случае ALERT)
    └─→ Пользователь видит уведомление:
        "⚠️ Молоко закончится через 2 дня"
    └─→ Может нажать "Я заказал" → обновить статус
    └─→ Или "Напомнить позже" → сдвинуть на 3 дня

Время: 8:50 - Dashboard обновляется в реальном времени
```

---

## 9. Обработка исключений и граничные случаи

### 9.1 Сценарии отказа

| Сценарий | Действие |
|----------|----------|
| Email сервер недоступен | Повтор через 10 мин, макс 3 раза |
| Ollama offline | Использовать fuzzy matching, пометить для ручного ввода |
| Парсинг чека провалился | Сохранить исходный HTML, предложить ручной ввод |
| Дублирование чеков | Дедупликация по email_id + receipt_number |
| Пользователь удалил товар | Мягкое удаление (soft delete, сохранить историю) |
| Нет данных о расходе (новый товар) | Использовать ручное значение или параметр по умолчанию |

### 9.2 Валидация данных

```python
# Валидация товара при парсинге
def validate_item(item):
    errors = []
    
    if not item.get('raw_name'):
        errors.append("Название товара пусто")
    
    if item.get('raw_volume'):
        volume_match = re.search(r'(\d+(?:\.\d+)?)\s*(мл|л|шт|г|кг)', item['raw_volume'])
        if not volume_match:
            errors.append(f"Неверный формат объёма: {item['raw_volume']}")
    
    if item.get('raw_price'):
        try:
            float(item['raw_price'])
        except:
            errors.append(f"Неверная цена: {item['raw_price']}")
    
    return {
        'valid': len(errors) == 0,
        'errors': errors
    }
```

---

## 10. Развёртывание и конфигурация

### 10.1 Требования

```
Python 3.9+
- FastAPI / Flask для API
- Ollama (локально, на localhost:11434)
- SQLite (по умолчанию) или PostgreSQL
- IMAP/POP3 доступ к почте пользователя
- 8GB+ RAM

Опционально:
- Docker (для контейнеризации)
- Redis (для кэширования)
- Celery (для асинхронных задач)
```

### 10.2 Конфигурационный файл

```yaml
# config.yaml
app:
  name: "Inventory Tracker"
  version: "0.1.0"
  debug: false
  host: "localhost"
  port: 8000

database:
  engine: "sqlite"  # или "postgresql"
  path: "./data/inventory.db"
  
email:
  provider: "imap"
  imap_server: "imap.yandex.ru"
  imap_port: 993
  ssl: true
  check_interval_minutes: 60
  
ollama:
  enabled: true
  base_url: "http://localhost:11434"
  model: "mistral:latest"
  temperature: 0.3
  timeout: 30
  
notifications:
  desktop: true
  telegram: false
  telegram_token: ""
  email: false
  
logging:
  level: "INFO"
  file: "./logs/inventory.log"
```

---

## 11. Roadmap развития

### Phase 1: MVP (текущая сессия - архитектура)
- ✅ Архитектура системы
- ✅ Модель данных
- ✅ UI/UX макеты

### Phase 2: Backend (2-3 недели)
- Email Poller + парсинг HTML чеков
- REST API для управления товарами
- Интеграция с Ollama
- БД + миграции

### Phase 3: Frontend (2-3 недели)
- React/Vue приложение
- Dashboard с графиками
- Интерактивная категоризация
- Настройки порогов

### Phase 4: Deployment (1 неделя)
- Docker контейнеризация
- Инструкции установки
- Документация для пользователя

### Phase 5: Advanced (future)
- Интеграция с календарём (вечеринки = больше расход)
- ML модель для предсказания расхода
- Интеграция с онлайн-магазинами (автозаказ)
- Многопользовательская версия (семья)
- Экспорт отчётов

---

## Заключение

Система **Inventory Tracker** решает проблему ручного отслеживания остатков товаров путём:

1. **Автоматизации** сбора данных из чеков
2. **Интеллектуализации** через локальный NLP (Ollama)
3. **Прозрачности** с понятным dashboard
4. **Предсказания** нехватки товаров через расчёт Cache

Архитектура спроектирована для **масштабируемости** (многопользовательские версии, интеграции), **приватности** (всё локально) и **простоты использования** (интуитивный UI).