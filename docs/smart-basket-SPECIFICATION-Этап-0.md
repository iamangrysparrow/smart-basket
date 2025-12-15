# Smart Basket Brain: Финальная спецификация

> Детальная спецификация без незакрытых вопросов

---

## 1. Уточнения по структуре БД

### 1.1 UserProfile

```sql
CREATE TABLE UserProfiles (
    Id UUID PRIMARY KEY,
    
    -- Привычки покупок
    ShoppingFrequencyPerWeek INT NOT NULL,           -- 2
    
    -- Предпочтения
    PreferredStores JSONB,                           -- ["samokat", "lavka"]
    OptimizationStrategy VARCHAR(50),                -- "balanced" / "price" / "convenience"
    MaxWeightPerOrder DECIMAL(5, 2) DEFAULT 30.0,    -- Макс вес корзины (кг)
    
    -- Регулярные продукты (строки, не FK!)
    RegularProducts JSONB,                           -- ["Молоко", "Хлеб", "Яйца", ...]
    
    -- Особенности питания
    DietaryRestrictions TEXT,                        -- "Ребёнок - аллергия на орехи"
    Notes TEXT,                                      -- Любые заметки
    
    -- Метаданные
    CreatedAt TIMESTAMP NOT NULL,
    LastUpdatedAt TIMESTAMP NOT NULL,
    OnboardingCompleted BOOLEAN DEFAULT FALSE
);
```

**Почему RegularProducts как строки, а не FK на Products:**
- Можно корректировать независимо
- Не требует существования Product в БД
- AI может предложить новые продукты, которых нет в истории
- Проще для пользователя ("Молоко" понятнее чем guid)

### 1.2 FamilyMember (обновлено!)

```sql
CREATE TABLE FamilyMembers (
    Id UUID PRIMARY KEY,
    ProfileId UUID NOT NULL REFERENCES UserProfiles(Id),
    
    -- Персональные данные
    Name VARCHAR(100),                               -- "Саша", "Маша" (опционально)
    Gender VARCHAR(10) NOT NULL,                     -- "male" / "female"
    DateOfBirth DATE NOT NULL,                       -- 1988-05-15 (не возраст!)
    Category VARCHAR(20) NOT NULL,                   -- "adult" / "child"
    
    -- Особенности
    Notes TEXT,                                      -- "Не ест молочное", "Вегетарианец"
    
    FOREIGN KEY (ProfileId) REFERENCES UserProfiles(Id) ON DELETE CASCADE
);
```

**Изменения:**
- ✅ `DateOfBirth DATE` вместо `Age INT`
- ✅ Возраст вычисляется на лету: `(DateTime.Now - DateOfBirth).TotalDays / 365.25`
- ✅ Имя опционально (для персонализации)

**Вычисляемое свойство:**
```csharp
public class FamilyMember
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string Gender { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Category { get; set; }
    public string? Notes { get; set; }
    
    // Вычисляемые
    [NotMapped]
    public int Age => (int)((DateTime.Now - DateOfBirth).TotalDays / 365.25);
    
    [NotMapped]
    public string DisplayName => Name ?? $"{Gender} {Age}";
}
```

### 1.3 ShoppingLists (без изменений)

```sql
CREATE TABLE ShoppingLists (
    Id UUID PRIMARY KEY,
    CreatedAt TIMESTAMP NOT NULL,
    Status VARCHAR(50) NOT NULL,        -- Draft / Confirmed / Optimizing / Ready / Completed / Cancelled
    ConfirmedAt TIMESTAMP,
    OptimizedAt TIMESTAMP,
    CompletedAt TIMESTAMP,
    Notes TEXT,
    
    ActualReceiptId UUID REFERENCES Receipts(Id)
);
```

### 1.4 ShoppingListItems (без изменений)

```sql
CREATE TABLE ShoppingListItems (
    Id UUID PRIMARY KEY,
    ShoppingListId UUID NOT NULL REFERENCES ShoppingLists(Id),
    
    -- Ссылка на товар (опционально, если AI нашёл соответствие)
    ItemId UUID REFERENCES Items(Id),
    ProductId UUID REFERENCES Products(Id),
    
    -- Что предлагаем купить
    Name VARCHAR(500) NOT NULL,
    Quantity DECIMAL(10, 3) NOT NULL,
    Unit VARCHAR(50),
    
    -- Метаданные
    IsAutoSuggested BOOLEAN NOT NULL DEFAULT TRUE,
    IsConfirmed BOOLEAN NOT NULL DEFAULT TRUE,
    Urgency VARCHAR(50),                             -- Now / Soon / Later
    Reasoning TEXT,                                  -- "Обычно каждые 5 дней"
    
    -- Факт (после покупки)
    ActualQuantity DECIMAL(10, 3),
    ActualReceiptItemId UUID REFERENCES ReceiptItems(Id)
);
```

### 1.5 StoreBaskets (без изменений)

```sql
CREATE TABLE StoreBaskets (
    Id UUID PRIMARY KEY,
    ShoppingListId UUID NOT NULL REFERENCES ShoppingLists(Id),
    
    StoreName VARCHAR(100) NOT NULL,                 -- samokat, lavka, kuper
    StoreDisplayName VARCHAR(100) NOT NULL,          -- Самокат, Лавка, Kuper
    
    TotalPrice DECIMAL(10, 2) NOT NULL,
    TotalWeight DECIMAL(10, 3),                      -- кг
    ItemsCount INTEGER NOT NULL,
    IsRecommended BOOLEAN NOT NULL DEFAULT FALSE,
    
    CartUrl TEXT,
    CreatedAt TIMESTAMP NOT NULL
);
```

### 1.6 StoreBasketItems (без изменений)

```sql
CREATE TABLE StoreBasketItems (
    Id UUID PRIMARY KEY,
    StoreBasketId UUID NOT NULL REFERENCES StoreBaskets(Id),
    ShoppingListItemId UUID REFERENCES ShoppingListItems(Id),
    
    -- Что нашли в магазине
    ProductId VARCHAR(500) NOT NULL,                 -- slug для парсера
    Name VARCHAR(500) NOT NULL,
    Price DECIMAL(10, 2) NOT NULL,
    Quantity DECIMAL(10, 3) NOT NULL,
    Unit VARCHAR(50),
    Amount DECIMAL(10, 2) NOT NULL,
    
    ItemQuantity DECIMAL(10, 3),                     -- Фасовка
    ItemUnit VARCHAR(50),
    InStock BOOLEAN NOT NULL,
    ImageUrl TEXT,
    ProductUrl TEXT
);
```

---

## 2. Интеграция с существующей системой AI провайдеров

### 2.1 Текущая система (из проекта)

**Есть:**
```csharp
// Интерфейс провайдера
interface ILlmProvider
{
    Task<LlmGenerationResult> GenerateAsync(
        string prompt, 
        int maxTokens, 
        double temperature, 
        IProgress<string>? progress, 
        CancellationToken ct);
}

// Фабрика
class AiProviderFactory : IAiProviderFactory
{
    ILlmProvider GetProvider(string key);
    ILlmProvider GetProviderForOperation(AiOperation operation);
}

// Конфигурация
class AiProviderConfig
{
    string Key { get; set; }              // "Ollama/qwen2.5:1.5b"
    AiProviderType Provider { get; set; } // Ollama, YandexGPT, etc
    string Model { get; set; }
    // ... остальные настройки
}

// Операции (существующие)
enum AiOperation
{
    Parsing,        // Парсинг чеков
    Classification, // Категоризация продуктов
    Labels          // Назначение меток
}
```

**Настройки в appsettings.json:**
```json
{
  "LlmSettings": {
    "AiProviders": [
      {
        "Key": "Ollama/qwen2.5:1.5b",
        "Provider": "Ollama",
        "Model": "qwen2.5:1.5b",
        "BaseUrl": "http://localhost:11434"
      },
      {
        "Key": "Claude/sonnet",
        "Provider": "OpenAI",
        "Model": "claude-sonnet-4-20250514",
        "BaseUrl": "https://api.anthropic.com",
        "ApiKey": "sk-ant-..."
      }
    ],
    "AiOperations": {
      "Parsing": "Ollama/qwen2.5:1.5b",
      "Classification": "Ollama/qwen2.5:1.5b",
      "Labels": "Claude/sonnet"
    }
  }
}
```

### 2.2 Новые операции для Brain

**Добавляем в enum AiOperation:**
```csharp
public enum AiOperation
{
    // Существующие
    Parsing,
    Classification,
    Labels,
    
    // НОВЫЕ для Brain
    Onboarding,           // AI-интервью при первом запуске
    ListRefinement,       // Чат с пользователем для уточнения списка
    ProductMatching,      // Подбор товаров из магазинов
    BasketOptimization    // Оптимизация корзин
}
```

**Настройки в appsettings.json (пример):**
```json
{
  "LlmSettings": {
    "AiOperations": {
      "Parsing": "Ollama/qwen2.5:1.5b",
      "Classification": "Ollama/qwen2.5:1.5b",
      "Labels": "Claude/sonnet",
      
      "Onboarding": "Ollama/qwen2.5:1.5b",
      "ListRefinement": "Claude/sonnet",
      "ProductMatching": "Claude/sonnet",
      "BasketOptimization": "Claude/sonnet"
    }
  }
}
```

### 2.3 Использование в Brain сервисах

**Пример:**
```csharp
public class AiOnboardingService
{
    private readonly IAiProviderFactory _aiFactory;
    
    public async Task<UserProfile> InterviewUserAsync()
    {
        // Получаем провайдер для операции Onboarding
        var provider = _aiFactory.GetProviderForOperation(AiOperation.Onboarding);
        
        var prompt = BuildOnboardingPrompt();
        
        var result = await provider.GenerateAsync(
            prompt,
            maxTokens: 2000,
            temperature: 0.7,
            progress: null,
            cancellationToken: default
        );
        
        // Парсим результат и возвращаем профиль
        return ParseProfileFromResponse(result.Text);
    }
}
```

**Аналогично для остальных:**
```csharp
public class ShoppingListRefinementService
{
    public async Task<string> ChatAsync(string userMessage)
    {
        var provider = _aiFactory.GetProviderForOperation(AiOperation.ListRefinement);
        // ...
    }
}

public class ProductMatchingService
{
    public async Task<List<ProductMatch>> MatchProductsAsync(...)
    {
        var provider = _aiFactory.GetProviderForOperation(AiOperation.ProductMatching);
        // ...
    }
}

public class BasketOptimizerService
{
    public async Task<List<BasketVariant>> OptimizeAsync(...)
    {
        var provider = _aiFactory.GetProviderForOperation(AiOperation.BasketOptimization);
        // ...
    }
}
```

---

## 3. Расчёт расхода (автоматический)

### 3.1 Алгоритм

**Не руками!** Расход вычисляется из чеков:

```csharp
public class ConsumptionCalculator
{
    public decimal CalculateAverageConsumptionPerDay(List<Purchase> purchases)
    {
        if (purchases.Count < 2)
            return 0;
        
        // Сортируем по дате
        var sorted = purchases.OrderBy(p => p.Date).ToList();
        
        // Считаем общее количество купленное
        decimal totalQuantity = purchases.Sum(p => p.Quantity);
        
        // Период между первой и последней покупкой
        var periodDays = (sorted.Last().Date - sorted.First().Date).TotalDays;
        
        if (periodDays <= 0)
            return 0;
        
        // Средний расход = купленное / дни
        return (decimal)(totalQuantity / periodDays);
    }
}
```

**Пример:**
```
Молоко:
01.12: 2л
08.12: 2л  (прошло 7 дней)
15.12: 2л  (прошло 7 дней)
22.12: 2л  (прошло 7 дней)

Всего куплено: 8л
Период: 21 день (с 01.12 по 22.12)
Расход: 8л / 21д = 0.38 л/день

Или: интервал между покупками 7 дней, покупка 2л
Расход: 2л / 7д = 0.29 л/день (примерно то же)
```

### 3.2 Когда расход не работает

**Проблемы:**
- Разовая большая покупка (запас на месяц)
- Смена привычек (раньше 2л, теперь 1л)
- Не все чеки (покупки в других местах)

**Решение: AI компенсирует**
```
AI: Вижу что молоко покупаете нерегулярно.
    Сколько примерно уходит молока в неделю?
    
User: 4-5 литров

AI: Окей, запомнил. Буду предлагать ~4.5л раз в неделю.
```

---

## 4. Оценка достоверности математики

### 4.1 Формула confidence

```csharp
public class ConfidenceAssessment
{
    public double OverallConfidence { get; set; }     // 0.0 - 1.0
    public string Reasoning { get; set; }
    
    public int TotalReceipts { get; set; }
    public int ReceiptsLastMonth { get; set; }
    public bool IsEvenlyDistributed { get; set; }
    public int DaysSinceLastReceipt { get; set; }
}

public ConfidenceAssessment AssessConfidence(List<Receipt> receipts)
{
    var lastMonth = receipts
        .Where(r => r.ReceiptDate > DateTime.Now.AddDays(-30))
        .ToList();
    
    // 1. Базовая оценка по количеству
    // 20+ чеков = 100% базы
    double baseConfidence = Math.Min(receipts.Count / 20.0, 1.0);
    
    // 2. Активность за последний месяц
    // 4+ чека/месяц = хорошо (100%)
    double recentActivity = Math.Min(lastMonth.Count / 4.0, 1.0);
    
    // 3. Равномерность распределения
    bool isEvenlyDistributed = CheckEvenDistribution(receipts);
    double distributionBonus = isEvenlyDistributed ? 0.2 : 0.0;
    
    // Итоговая формула
    double overall = (baseConfidence * 0.5) +      // 50% вес
                     (recentActivity * 0.3) +       // 30% вес
                     distributionBonus;             // +20% бонус
    
    overall = Math.Clamp(overall, 0.0, 1.0);
    
    return new ConfidenceAssessment
    {
        OverallConfidence = overall,
        Reasoning = GenerateReasoning(overall, receipts, lastMonth, isEvenlyDistributed),
        TotalReceipts = receipts.Count,
        ReceiptsLastMonth = lastMonth.Count,
        IsEvenlyDistributed = isEvenlyDistributed,
        DaysSinceLastReceipt = (DateTime.Now - receipts.Max(r => r.ReceiptDate)).Days
    };
}

private bool CheckEvenDistribution(List<Receipt> receipts)
{
    if (receipts.Count < 3) return false;
    
    var dates = receipts.Select(r => r.ReceiptDate).OrderBy(d => d).ToList();
    var intervals = new List<double>();
    
    for (int i = 1; i < dates.Count; i++)
    {
        intervals.Add((dates[i] - dates[i - 1]).TotalDays);
    }
    
    var avgInterval = intervals.Average();
    var stdDev = CalculateStdDev(intervals);
    
    // Равномерно если StdDev < 50% среднего интервала
    return stdDev < (avgInterval * 0.5);
}
```

### 4.2 Примеры оценки

**Сценарий A: Мало чеков**
```
Входные данные:
- Всего чеков: 6
- За последний месяц: 1
- Равномерность: нет (промежутки 15, 45, 20, 60, 30 дней)

Расчёт:
- baseConfidence = 6 / 20 = 0.30
- recentActivity = 1 / 4 = 0.25
- distributionBonus = 0.0 (не равномерно)
- overall = (0.30 × 0.5) + (0.25 × 0.3) + 0.0 = 0.225

Результат: 0.23 (низкая)
Reasoning: "Мало данных (6 чеков), последняя покупка давно. Буду использовать профиль."
```

**Сценарий B: Средне**
```
Входные данные:
- Всего чеков: 15
- За последний месяц: 4
- Равномерность: да (каждые 7-8 дней)

Расчёт:
- baseConfidence = 15 / 20 = 0.75
- recentActivity = 4 / 4 = 1.0
- distributionBonus = 0.2
- overall = (0.75 × 0.5) + (1.0 × 0.3) + 0.2 = 0.875

Результат: 0.88 (высокая)
Reasoning: "Стабильная история покупок, регулярные заказы."
```

**Сценарий C: Отлично**
```
Входные данные:
- Всего чеков: 25
- За последний месяц: 8
- Равномерность: да (каждые 3-4 дня)

Расчёт:
- baseConfidence = 25 / 20 = 1.0 (макс)
- recentActivity = 8 / 4 = 1.0 (макс)
- distributionBonus = 0.2
- overall = (1.0 × 0.5) + (1.0 × 0.3) + 0.2 = 1.0

Результат: 1.0 (отличная)
Reasoning: "Отличная история покупок, можно полностью доверять."
```

### 4.3 Использование оценки

**Передача AI:**
```csharp
var mathResult = await _analyzer.CreateDraftListAsync();
var confidence = mathResult.Confidence;

// AI получает контекст
var aiPrompt = $@"
ОЦЕНКА ДОСТОВЕРНОСТИ МАТЕМАТИКИ:
{confidence.Reasoning}
Confidence: {confidence.OverallConfidence:F2}

Всего чеков: {confidence.TotalReceipts}
За месяц: {confidence.ReceiptsLastMonth}
Равномерность: {(confidence.IsEvenlyDistributed ? "да" : "нет")}

{(confidence.OverallConfidence < 0.5 
    ? "ВАЖНО: Мало данных, опирайся на профиль пользователя!" 
    : "Данные надёжные, можно доверять математике.")}

ПРЕДЛОЖЕННЫЙ СПИСОК:
{FormatItems(mathResult.Items)}

ПРОФИЛЬ ПОЛЬЗОВАТЕЛЯ:
{FormatProfile(profile)}

ЗАДАЧА: Скорректируй список с учётом оценки.
";
```

---

## 5. Детальный flow каждого этапа

### Этап 1: Онбординг

**Триггер автоматический:**
```csharp
public class BrainModuleInitializer
{
    public async Task InitializeAsync()
    {
        var profile = await _context.UserProfiles.FirstOrDefaultAsync();
        
        if (profile == null || !profile.OnboardingCompleted)
        {
            // Показать онбординг
            var dialog = new OnboardingDialog();
            var result = await dialog.ShowAsync();
            
            if (result.IsCompleted)
            {
                await _context.UserProfiles.AddAsync(result.Profile);
                await _context.SaveChangesAsync();
            }
        }
    }
}
```

**Триггер ручной:**
```
Настройки → Профиль семьи → [Пройти онбординг заново]
```

**AI-промпт:**
```
Ты - помощник по настройке системы автоматических покупок.

Твоя задача: провести короткое интервью (5-7 вопросов) чтобы понять семью пользователя.

Спрашивай по одному вопросу. Тон дружелюбный, не формальный.

Вопросы:
1. Сколько человек в семье?
2. Расскажи подробнее о составе (пол, возраст)
3. Как часто обычно закупаетесь продуктами?
4. Какие продукты покупаете регулярно?
5. Есть особенности питания? (аллергии, диеты)

После ответов верни JSON:
{
  "family_members": [
    {"gender": "male", "age": 35},
    ...
  ],
  "shopping_frequency": 2,
  "regular_products": ["Молоко", "Хлеб", ...],
  "dietary_restrictions": "..."
}
```

**Парсинг ответа:**
```csharp
public class OnboardingResponseParser
{
    public UserProfile ParseResponse(string aiResponse)
    {
        // Извлечь JSON из ответа
        var json = ExtractJson(aiResponse);
        var data = JsonSerializer.Deserialize<OnboardingData>(json);
        
        var profile = new UserProfile
        {
            Id = Guid.NewGuid(),
            ShoppingFrequencyPerWeek = data.ShoppingFrequency,
            RegularProducts = data.RegularProducts,
            DietaryRestrictions = data.DietaryRestrictions,
            CreatedAt = DateTime.Now,
            LastUpdatedAt = DateTime.Now,
            OnboardingCompleted = true
        };
        
        // Создать FamilyMembers
        profile.FamilyMembers = data.FamilyMembers
            .Select(m => new FamilyMember
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Gender = m.Gender,
                DateOfBirth = CalculateDateOfBirth(m.Age),
                Category = m.Age < 18 ? "child" : "adult"
            })
            .ToList();
        
        return profile;
    }
    
    private DateTime CalculateDateOfBirth(int age)
    {
        return DateTime.Now.AddYears(-age).Date;
    }
}
```

---

### Этап 2: Математика + Оценка

**Входные данные:**
- История покупок (Receipt → ReceiptItem → Item → Product)
- UserProfile (если есть)

**Выходные данные:**
- Список предложений (List<SuggestedItem>)
- Оценка достоверности (ConfidenceAssessment)

**Алгоритм:**
```csharp
public async Task<ListCreationResult> CreateDraftListAsync()
{
    // 1. Получить историю
    var receipts = await _context.Receipts
        .Include(r => r.Items).ThenInclude(ri => ri.Item).ThenInclude(i => i.Product)
        .Where(r => r.ReceiptDate > DateTime.Now.AddDays(-90))
        .ToListAsync();
    
    // 2. Оценить достоверность
    var confidence = AssessConfidence(receipts);
    
    // 3. Группировать по Product
    var grouped = receipts
        .SelectMany(r => r.Items)
        .GroupBy(ri => ri.Item.Product)
        .ToList();
    
    var suggestions = new List<SuggestedItem>();
    
    foreach (var group in grouped)
    {
        var product = group.Key;
        var purchases = group.Select(ri => new Purchase
        {
            Date = ri.Receipt.ReceiptDate,
            Quantity = ri.Quantity,
            Unit = ri.Item.UnitOfMeasure
        }).OrderBy(p => p.Date).ToList();
        
        // 4. Вычислить паттерн
        var pattern = AnalyzePattern(product, purchases);
        
        // 5. Предсказать потребность
        var prediction = PredictReplenishment(pattern, confidence);
        
        if (prediction.Urgency != ReplenishmentUrgency.Skip)
        {
            suggestions.Add(new SuggestedItem
            {
                ProductName = product.Name,
                Quantity = prediction.EstimatedQuantity,
                Unit = pattern.CommonUnit,
                Reasoning = prediction.Reasoning,
                ItemConfidence = pattern.Confidence * confidence.OverallConfidence
            });
        }
    }
    
    // 6. Если мало данных - дополнить из профиля
    if (confidence.OverallConfidence < 0.5)
    {
        suggestions = await AugmentWithProfile(suggestions);
    }
    
    // 7. Сортировка по срочности
    suggestions = suggestions
        .OrderByDescending(s => s.ItemConfidence)
        .ToList();
    
    return new ListCreationResult
    {
        Items = suggestions,
        Confidence = confidence
    };
}
```

**Дополнение из профиля:**
```csharp
private async Task<List<SuggestedItem>> AugmentWithProfile(List<SuggestedItem> suggestions)
{
    var profile = await _context.UserProfiles.FirstAsync();
    
    // Добавить регулярные продукты из профиля, которых нет в suggestions
    foreach (var productName in profile.RegularProducts)
    {
        if (!suggestions.Any(s => s.ProductName.Contains(productName, StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add(new SuggestedItem
            {
                ProductName = productName,
                Quantity = 1, // Дефолт
                Unit = "шт",
                Reasoning = "Из профиля: регулярный товар",
                ItemConfidence = 0.3 // Низкая (из профиля)
            });
        }
    }
    
    return suggestions;
}
```

---

### Этап 3: AI уточняет через чат

**UI компоненты:**
```
ShoppingListChatView.xaml:
├── Grid
│   ├── Column 0: ChatPanel (60%)
│   │   ├── ScrollViewer (история сообщений)
│   │   └── TextBox + Button (ввод)
│   └── Column 1: ListPanel (40%)
│       ├── ListView (товары)
│       └── Button "Готово, искать в магазинах"
```

**Function calling tools:**
```typescript
const tools = [
    {
        name: "add_item_to_list",
        description: "Добавить товар в список покупок",
        parameters: {
            type: "object",
            properties: {
                product_name: {
                    type: "string",
                    description: "Название товара"
                },
                quantity: {
                    type: "number",
                    description: "Количество"
                },
                unit: {
                    type: "string",
                    description: "Единица измерения (шт, кг, л)"
                }
            },
            required: ["product_name", "quantity", "unit"]
        }
    },
    {
        name: "remove_item_from_list",
        description: "Удалить товар из списка",
        parameters: {
            type: "object",
            properties: {
                product_name: {
                    type: "string",
                    description: "Название товара для удаления"
                }
            },
            required: ["product_name"]
        }
    },
    {
        name: "update_item_quantity",
        description: "Изменить количество товара",
        parameters: {
            type: "object",
            properties: {
                product_name: {
                    type: "string"
                },
                new_quantity: {
                    type: "number"
                }
            },
            required: ["product_name", "new_quantity"]
        }
    },
    {
        name: "get_current_list",
        description: "Получить текущий список покупок",
        parameters: {
            type: "object",
            properties: {}
        }
    }
];
```

**Реализация function calling:**
```csharp
public class ShoppingListFunctionHandler
{
    private ShoppingList _currentList;
    
    public async Task<string> HandleFunctionCallAsync(string functionName, JsonElement arguments)
    {
        return functionName switch
        {
            "add_item_to_list" => await AddItemAsync(arguments),
            "remove_item_from_list" => await RemoveItemAsync(arguments),
            "update_item_quantity" => await UpdateQuantityAsync(arguments),
            "get_current_list" => GetCurrentList(),
            _ => "Unknown function"
        };
    }
    
    private async Task<string> AddItemAsync(JsonElement args)
    {
        var productName = args.GetProperty("product_name").GetString();
        var quantity = args.GetProperty("quantity").GetDecimal();
        var unit = args.GetProperty("unit").GetString();
        
        var item = new ShoppingListItem
        {
            Id = Guid.NewGuid(),
            ShoppingListId = _currentList.Id,
            Name = productName,
            Quantity = quantity,
            Unit = unit,
            IsAutoSuggested = false, // Пользователь добавил
            IsConfirmed = true
        };
        
        _currentList.Items.Add(item);
        
        // Уведомить UI
        OnListChanged?.Invoke(_currentList);
        
        return $"Добавлен: {productName} {quantity} {unit}";
    }
    
    // Аналогично для других функций
}
```

**AI система промпт:**
```
Ты - умный помощник по составлению списков покупок.

У пользователя есть предварительный список. Твоя задача - помочь его уточнить.

ТЕКУЩИЙ СПИСОК:
{FormatCurrentList()}

ПРОФИЛЬ СЕМЬИ:
{FormatFamilyProfile()}

Пользователь может:
- Добавить товар: "Добавь хлеб бородинский"
- Убрать товар: "Убери сметану"
- Изменить количество: "Молока хватит 3 литра"
- Спросить совет: "Что ещё может понадобиться?"

Используй доступные функции для изменения списка.
Отвечай кратко и по делу. Тон дружелюбный.

Когда пользователь говорит "Всё", "Готово", "Дальше" - спроси подтверждение и закончи диалог.
```

**Пример диалога с function calls:**
```
User: "Добавь хлеб бородинский"

AI internal reasoning:
{
  "thought": "Пользователь хочет добавить хлеб бородинский. Нужно вызвать add_item_to_list",
  "function_call": {
    "name": "add_item_to_list",
    "arguments": {
      "product_name": "Хлеб бородинский",
      "quantity": 1,
      "unit": "шт"
    }
  }
}

→ Function executed → list updated

AI response: "Добавил хлеб бородинский 1шт"

---

User: "Молока хватит 3 литра"

AI internal:
{
  "thought": "Пользователь хочет изменить количество молока с 4л на 3л",
  "function_call": {
    "name": "update_item_quantity",
    "arguments": {
      "product_name": "Молоко",
      "new_quantity": 3
    }
  }
}

→ Function executed

AI response: "Изменил молоко на 3л. Запомню для следующих раз."
```

---

### Этап 4: AI подбирает товары

**Входные данные:**
- Финальный список (ShoppingList с items)
- Настройки магазинов (какие включены)

**Процесс:**
```
Для каждого ShoppingListItem:
    Для каждого магазина:
        1. Парсер ищет товары (SearchAsync)
        2. AI анализирует результаты
        3. AI выбирает лучший товар
        4. Сохраняем ProductMatch
```

**1. Поиск через парсеры:**
```csharp
public async Task<Dictionary<string, List<ProductSearchResult>>> SearchInAllStoresAsync(
    string productName)
{
    var results = new Dictionary<string, List<ProductSearchResult>>();
    
    // Самокат
    var samokat = await _samokatParser.SearchAsync(_webView, productName, limit: 5);
    results["samokat"] = samokat;
    
    // Лавка
    var lavka = await _lavkaParser.SearchAsync(_webView, productName, limit: 5);
    results["lavka"] = lavka;
    
    // Kuper
    var kuper = await _kuperParser.SearchAsync(_webView, productName, limit: 5);
    results["kuper"] = kuper;
    
    return results;
}
```

**2. AI анализирует:**

**Промпт:**
```
Пользователь хочет купить: {item.Name} — {item.Quantity} {item.Unit}

Профиль семьи:
{FormatFamily()}

Найденные товары в магазинах:
{FormatSearchResults(searchResults)}

ЗАДАЧА:
Для каждого магазина выбери ОДИН наиболее подходящий товар.

Критерии выбора:
1. Соответствие названию
2. Соответствие объёму/весу (фасовка)
3. Качество (известный бренд vs noname)
4. Цена (учитывай, но не главное)
5. Подходит ли для семьи (детское питание для детей и т.п.)

Верни JSON:
{
  "samokat": {
    "selected_index": 0,
    "product_id": "moloko-domik-2-5-1l",
    "quantity_needed": 3,
    "reasoning": "Популярный бренд, хорошая цена",
    "is_good_match": true
  },
  "lavka": { ... },
  "kuper": { ... }
}
```

**3. Парсинг ответа:**
```csharp
public class ProductMatchingResult
{
    public Dictionary<string, StoreMatch> Matches { get; set; }
}

public class StoreMatch
{
    public string Store { get; set; }
    public string ProductId { get; set; }
    public string ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitQuantity { get; set; }
    public int QuantityNeeded { get; set; }
    public decimal TotalPrice { get; set; }
    public bool IsGoodMatch { get; set; }
    public string Reasoning { get; set; }
}
```

**4. Сохранение:**
```csharp
foreach (var (store, match) in result.Matches)
{
    var basketItem = new StoreBasketItem
    {
        Id = Guid.NewGuid(),
        ShoppingListItemId = item.Id,
        ProductId = match.ProductId,
        Name = match.ProductName,
        Price = match.UnitPrice,
        Quantity = match.QuantityNeeded,
        Amount = match.TotalPrice,
        InStock = match.IsGoodMatch
    };
    
    // Сохраним пока временно (до оптимизации)
    _tempMatches[store].Add(basketItem);
}
```

---

### Этап 5: AI оптимизирует корзины

**Входные данные:**
- Таблица сопоставлений (товары по магазинам)
- Настройки пользователя

**Промпт:**
```
У меня есть список товаров и их цены в разных магазинах.

ТАБЛИЦА ТОВАРОВ:
{FormatMatchingTable()}

ОГРАНИЧЕНИЯ:
- Максимальный вес корзины: {profile.MaxWeightPerOrder} кг
- Все товары должны быть куплены

НАСТРОЙКИ ОПТИМИЗАЦИИ:
- Стратегия: {profile.OptimizationStrategy}
  - "convenience" = всё в одном месте
  - "balanced" = экономия vs удобство (если экономия > 15%)
  - "price" = максимальная экономия
- Предпочтительный магазин: {profile.PreferredStores[0]}

ЗАДАЧА:
Сформируй 2-3 варианта корзин:

Вариант 1: "Всё в одном месте" (в предпочтительном магазине)
Вариант 2: "Оптимально" (если экономия > порога)
Вариант 3: "Максимальная экономия" (если есть смысл)

Для каждого варианта:
- Список товаров по магазинам
- Общая цена
- Вес корзин
- Экономия vs базовый вариант

Верни JSON:
{
  "variants": [
    {
      "name": "Всё в Самокате",
      "strategy": "convenience",
      "baskets": [
        {
          "store": "samokat",
          "items": [...],
          "total_price": 1247,
          "weight_kg": 12.5
        }
      ],
      "total_price": 1247,
      "savings": 0,
      "is_recommended": false
    },
    {
      "name": "Оптимально (Самокат + Kuper)",
      "strategy": "balanced",
      "baskets": [
        {"store": "samokat", "items": [...], "total_price": 847, "weight_kg": 8.3},
        {"store": "kuper", "items": [...], "total_price": 289, "weight_kg": 4.2}
      ],
      "total_price": 1136,
      "savings": 111,
      "is_recommended": true,
      "reasoning": "Экономия 111₽ (9%) при 2 заказах"
    }
  ]
}
```

**Парсинг и сохранение:**
```csharp
public async Task<List<StoreBasket>> OptimizeAndSaveAsync(
    ShoppingList list,
    Dictionary<string, List<StoreBasketItem>> tempMatches)
{
    var aiResponse = await _ai.OptimizeAsync(tempMatches, _profile);
    var variants = ParseVariants(aiResponse);
    
    var savedBaskets = new List<StoreBasket>();
    
    // Сохраняем все варианты
    foreach (var variant in variants)
    {
        foreach (var basket in variant.Baskets)
        {
            var storeBasket = new StoreBasket
            {
                Id = Guid.NewGuid(),
                ShoppingListId = list.Id,
                StoreName = basket.Store,
                StoreDisplayName = GetDisplayName(basket.Store),
                TotalPrice = basket.TotalPrice,
                TotalWeight = basket.WeightKg,
                ItemsCount = basket.Items.Count,
                IsRecommended = variant.IsRecommended,
                CreatedAt = DateTime.Now
            };
            
            await _context.StoreBaskets.AddAsync(storeBasket);
            
            // Сохраняем items
            foreach (var item in basket.Items)
            {
                item.StoreBasketId = storeBasket.Id;
                await _context.StoreBasketItems.AddAsync(item);
            }
            
            savedBaskets.Add(storeBasket);
        }
    }
    
    await _context.SaveChangesAsync();
    
    return savedBaskets;
}
```

---

## 6. Незакрытые вопросы (контрольный список)

### ✅ Закрыто

- [x] Структура БД (UserProfile, FamilyMembers, Lists, Baskets)
- [x] Дата рождения вместо возраста
- [x] RegularProducts как строки
- [x] Интеграция с AI провайдерами (через существующую фабрику)
- [x] Расчёт расхода (автоматически из чеков)
- [x] Оценка достоверности математики (формула confidence)
- [x] Работа с малым и большим количеством чеков
- [x] AI на каждом этапе (онбординг, уточнение, подбор, оптимизация)
- [x] Function calling для чата
- [x] Детальный flow всех этапов

### ❓ Требуют уточнения

**1. Имена в FamilyMembers:**
- Хранить имена членов семьи?
- Использовать для персонализации? ("Саше нужны детские йогурты")
- Или только пол/возраст?

**Предложение:** Хранить опционально. Если есть - круто, если нет - не критично.

**2. Веса товаров:**
- Откуда брать вес для проверки ограничения "< 30кг"?
- Парсеры не всегда возвращают вес
- Вычислять примерный вес по категории?

**Предложение:** Пока игнорировать ограничение по весу в MVP. Добавить позже.

**3. Function calling format:**
- Какой формат использует твоя текущая система?
- OpenAI-style function calling?
- Anthropic tools?
- Custom?

**Нужно посмотреть:** Как сейчас работает в YandexAgent или других провайдерах.

**4. Хранение истории чата:**
- Нужно ли сохранять историю диалога в БД?
- Или только финальный результат (скорректированный список)?

**Предложение:** В MVP не сохранять историю. Только результат.

**5. WebView2 для всех магазинов одновременно:**
- Можно ли использовать один WebView2 для поиска в 3 магазинах?
- Или нужно 3 инстанса WebView2?
- Как это влияет на производительность?

**Предложение:** Один WebView2, последовательный поиск. Параллельно - позже.

---

## 7. Следующий шаг

**После закрытия всех вопросов:**

1. Утвердить структуру БД (миграции)
2. Добавить новые AiOperation в enum
3. Начать реализацию с Этапа 1 или Этапа 2

**С чего начать?**

**Вариант A:** Этап 2 (математика + оценка) - независимо от AI
**Вариант B:** Этап 1 (подключение AiWebSniffer) - проверить парсеры
**Вариант C:** Этап 2.5 (онбординг) - настроить AI провайдеры

**Твоё мнение?**
