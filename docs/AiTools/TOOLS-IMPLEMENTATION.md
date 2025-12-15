# Tools Implementation Example

> Пример реализации инструмента `get_receipts` для Claude Code.

## Файловая структура

```
src/SmartBasket.Services/
└── Tools/
    ├── IToolExecutor.cs
    ├── ToolExecutor.cs
    ├── IToolHandler.cs
    ├── ToolServiceExtensions.cs
    ├── Models/
    │   ├── ToolDefinition.cs
    │   ├── ToolResult.cs
    │   └── Args/
    │       └── GetReceiptsArgs.cs
    └── Handlers/
        └── GetReceiptsHandler.cs
```

---

## 1. Модели

### Models/ToolDefinition.cs

```csharp
namespace SmartBasket.Services.Tools.Models;

/// <summary>
/// Определение инструмента для LLM
/// </summary>
public record ToolDefinition(
    string Name,
    string Description,
    object ParametersSchema
);
```

### Models/ToolResult.cs

```csharp
using System.Text.Json;

namespace SmartBasket.Services.Tools.Models;

/// <summary>
/// Результат выполнения инструмента
/// </summary>
public record ToolResult(
    bool Success,
    string JsonData,
    string? ErrorMessage = null
)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public static ToolResult Ok(object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        return new ToolResult(true, json);
    }

    public static ToolResult Error(string message)
        => new(false, $"{{\"error\": \"{message}\"}}", message);
}
```

### Models/Args/GetReceiptsArgs.cs

```csharp
using System.Text.Json.Serialization;

namespace SmartBasket.Services.Tools.Models.Args;

/// <summary>
/// Аргументы для инструмента get_receipts
/// </summary>
public class GetReceiptsArgs
{
    [JsonPropertyName("date_from")]
    public string? DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public string? DateTo { get; set; }

    [JsonPropertyName("shop")]
    public string? Shop { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 10;

    [JsonPropertyName("order_by")]
    public string OrderBy { get; set; } = "date_desc";

    [JsonPropertyName("include_summary")]
    public bool IncludeSummary { get; set; } = true;
}
```

---

## 2. Интерфейсы

### IToolHandler.cs

```csharp
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools;

/// <summary>
/// Интерфейс обработчика инструмента
/// </summary>
public interface IToolHandler
{
    /// <summary>
    /// Имя инструмента
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Определение инструмента для LLM (JSON Schema)
    /// </summary>
    ToolDefinition GetDefinition();

    /// <summary>
    /// Выполнить инструмент
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        string argumentsJson, 
        CancellationToken cancellationToken = default);
}
```

### IToolExecutor.cs

```csharp
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools;

/// <summary>
/// Исполнитель инструментов
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Получить определения всех доступных инструментов
    /// </summary>
    IReadOnlyList<ToolDefinition> GetToolDefinitions();

    /// <summary>
    /// Выполнить инструмент по имени
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        string toolName, 
        string argumentsJson, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверить существование инструмента
    /// </summary>
    bool HasTool(string toolName);
}
```

---

## 3. ToolExecutor (роутер)

### ToolExecutor.cs

```csharp
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools;

/// <summary>
/// Исполнитель инструментов - роутит вызовы к конкретным обработчикам
/// </summary>
public class ToolExecutor : IToolExecutor
{
    private readonly Dictionary<string, IToolHandler> _handlers;

    public ToolExecutor(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(
            h => h.Name, 
            h => h, 
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return _handlers.Values
            .Select(h => h.GetDefinition())
            .ToList();
    }

    public async Task<ToolResult> ExecuteAsync(
        string toolName, 
        string argumentsJson, 
        CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            return ToolResult.Error($"Unknown tool: {toolName}");
        }

        return await handler.ExecuteAsync(argumentsJson, cancellationToken);
    }

    public bool HasTool(string toolName)
    {
        return _handlers.ContainsKey(toolName);
    }
}
```

---

## 4. GetReceiptsHandler

### Handlers/GetReceiptsHandler.cs

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartBasket.Data;
using SmartBasket.Services.Tools.Models;
using SmartBasket.Services.Tools.Models.Args;

namespace SmartBasket.Services.Tools.Handlers;

/// <summary>
/// Обработчик инструмента get_receipts
/// </summary>
public class GetReceiptsHandler : IToolHandler
{
    private readonly SmartBasketDbContext _dbContext;

    public GetReceiptsHandler(SmartBasketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_receipts";

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition(
            Name: Name,
            Description: "Получить список чеков. Используй для вопросов о датах чеков, суммах покупок, списке магазинов.",
            ParametersSchema: new
            {
                type = "object",
                properties = new
                {
                    date_from = new
                    {
                        type = "string",
                        format = "date",
                        description = "Начало периода (YYYY-MM-DD)"
                    },
                    date_to = new
                    {
                        type = "string",
                        format = "date",
                        description = "Конец периода (YYYY-MM-DD)"
                    },
                    shop = new
                    {
                        type = "string",
                        description = "Фильтр по названию магазина"
                    },
                    limit = new
                    {
                        type = "integer",
                        @default = 10,
                        description = "Максимальное количество чеков"
                    },
                    order_by = new
                    {
                        type = "string",
                        @enum = new[] { "date_desc", "date_asc", "total_desc", "total_asc" },
                        @default = "date_desc"
                    },
                    include_summary = new
                    {
                        type = "boolean",
                        @default = true,
                        description = "Включить сводку"
                    }
                },
                required = Array.Empty<string>()
            }
        );
    }

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ParseArguments(argumentsJson);
            var query = BuildQuery(args);
            
            // Summary (до применения limit)
            object? summary = null;
            if (args.IncludeSummary)
            {
                summary = await BuildSummaryAsync(query, cancellationToken);
            }

            // Сортировка и выборка
            var receipts = await ApplyOrderingAndSelect(query, args, cancellationToken);

            // Результат
            var result = new Dictionary<string, object> { ["receipts"] = receipts };
            if (summary != null)
            {
                result["summary"] = summary;
            }

            return ToolResult.Ok(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Ошибка получения чеков: {ex.Message}");
        }
    }

    private GetReceiptsArgs ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new GetReceiptsArgs();

        return JsonSerializer.Deserialize<GetReceiptsArgs>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new GetReceiptsArgs();
    }

    private IQueryable<Core.Entities.Receipt> BuildQuery(GetReceiptsArgs args)
    {
        var query = _dbContext.Receipts.AsQueryable();

        // Фильтр по дате
        if (!string.IsNullOrEmpty(args.DateFrom) && DateTime.TryParse(args.DateFrom, out var dateFrom))
        {
            query = query.Where(r => r.ReceiptDate >= dateFrom);
        }

        if (!string.IsNullOrEmpty(args.DateTo) && DateTime.TryParse(args.DateTo, out var dateTo))
        {
            query = query.Where(r => r.ReceiptDate < dateTo.AddDays(1));
        }

        // Фильтр по магазину
        if (!string.IsNullOrEmpty(args.Shop))
        {
            var shopLower = args.Shop.ToLower();
            query = query.Where(r => r.Shop.ToLower().Contains(shopLower));
        }

        return query;
    }

    private async Task<object> BuildSummaryAsync(
        IQueryable<Core.Entities.Receipt> query, 
        CancellationToken ct)
    {
        var totalReceipts = await query.CountAsync(ct);
        
        if (totalReceipts == 0)
        {
            return new
            {
                total_receipts = 0,
                total_amount = 0m,
                average_receipt = 0m,
                period = "нет данных"
            };
        }

        var totalAmount = await query.SumAsync(r => r.Total ?? 0, ct);
        var avgReceipt = totalAmount / totalReceipts;
        var minDate = await query.MinAsync(r => r.ReceiptDate, ct);
        var maxDate = await query.MaxAsync(r => r.ReceiptDate, ct);

        return new
        {
            total_receipts = totalReceipts,
            total_amount = Math.Round(totalAmount, 2),
            average_receipt = Math.Round(avgReceipt, 2),
            period = $"{minDate:yyyy-MM-dd} - {maxDate:yyyy-MM-dd}"
        };
    }

    private async Task<List<object>> ApplyOrderingAndSelect(
        IQueryable<Core.Entities.Receipt> query,
        GetReceiptsArgs args,
        CancellationToken ct)
    {
        // Сортировка
        query = args.OrderBy switch
        {
            "date_asc" => query.OrderBy(r => r.ReceiptDate),
            "total_desc" => query.OrderByDescending(r => r.Total),
            "total_asc" => query.OrderBy(r => r.Total),
            _ => query.OrderByDescending(r => r.ReceiptDate)
        };

        // Выборка
        return await query
            .Take(args.Limit)
            .Select(r => new
            {
                id = r.Id,
                date = r.ReceiptDate.ToString("yyyy-MM-dd"),
                shop = r.Shop,
                total = r.Total ?? 0,
                receipt_number = r.ReceiptNumber,
                items_count = r.Items.Count
            })
            .ToListAsync<object>(ct);
    }
}
```

---

## 5. Регистрация в DI

### ToolServiceExtensions.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using SmartBasket.Services.Tools.Handlers;

namespace SmartBasket.Services.Tools;

public static class ToolServiceExtensions
{
    /// <summary>
    /// Регистрация всех инструментов
    /// </summary>
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        // === Tool Handlers ===
        // Phase 1: MVP
        services.AddTransient<IToolHandler, GetReceiptsHandler>();
        // services.AddTransient<IToolHandler, GetReceiptItemsHandler>();
        
        // Phase 2: Справочники
        // services.AddTransient<IToolHandler, GetProductsHandler>();
        // services.AddTransient<IToolHandler, GetItemsHandler>();
        // services.AddTransient<IToolHandler, GetLabelsHandler>();
        
        // Phase 3: Аналитика
        // services.AddTransient<IToolHandler, GetPurchaseStatisticsHandler>();
        // services.AddTransient<IToolHandler, GenerateShoppingListHandler>();

        // === Executor ===
        services.AddTransient<IToolExecutor, ToolExecutor>();

        return services;
    }
}
```

### Использование в App.xaml.cs

```csharp
private void ConfigureServices(IServiceCollection services)
{
    // ... существующие регистрации ...

    // Tools
    services.AddTools();
    
    // Chat (использует IToolExecutor)
    // services.AddTransient<IChatService, ChatService>();
}
```

---

## 6. Примеры использования

### В ChatService

```csharp
public class ChatService : IChatService
{
    private readonly IToolExecutor _tools;
    private readonly IAiProviderFactory _aiFactory;

    public ChatService(IToolExecutor tools, IAiProviderFactory aiFactory)
    {
        _tools = tools;
        _aiFactory = aiFactory;
    }

    // Tool-use loop...
}
```

### В BrainService (будущее)

```csharp
public class BrainService
{
    private readonly IToolExecutor _tools;

    public async Task<ShoppingList> PlanShoppingAsync()
    {
        // Используем те же инструменты
        var statsResult = await _tools.ExecuteAsync(
            "get_purchase_statistics",
            """{"period_days": 30, "include_prediction": true}""");
        
        var listResult = await _tools.ExecuteAsync(
            "generate_shopping_list", 
            """{"days_ahead": 7}""");
        
        // ...
    }
}
```

### В CLI

```csharp
// Program.cs
var executor = serviceProvider.GetRequiredService<IToolExecutor>();

// Вывести доступные инструменты
foreach (var tool in executor.GetToolDefinitions())
{
    Console.WriteLine($"  {tool.Name}: {tool.Description}");
}

// Выполнить запрос
var result = await executor.ExecuteAsync(
    "get_receipts",
    """{"limit": 5, "include_summary": true}""");

Console.WriteLine(result.JsonData);
```

### Прямой вызов для тестов

```csharp
[Test]
public async Task GetReceipts_ReturnsLastReceipt()
{
    var executor = _serviceProvider.GetRequiredService<IToolExecutor>();
    
    var result = await executor.ExecuteAsync(
        "get_receipts", 
        """{"limit": 1}""");
    
    Assert.True(result.Success);
    
    var data = JsonDocument.Parse(result.JsonData);
    var receipts = data.RootElement.GetProperty("receipts");
    Assert.Equal(1, receipts.GetArrayLength());
}
```

---

## Добавление нового инструмента

### Шаги:

1. **Создать Args класс** (если нужны параметры):
```csharp
// Models/Args/GetReceiptItemsArgs.cs
public class GetReceiptItemsArgs
{
    [JsonPropertyName("receipt_id")]
    public string? ReceiptId { get; set; }
    // ...
}
```

2. **Создать Handler**:
```csharp
// Handlers/GetReceiptItemsHandler.cs
public class GetReceiptItemsHandler : IToolHandler
{
    public string Name => "get_receipt_items";
    
    public ToolDefinition GetDefinition() => new ToolDefinition(...);
    
    public async Task<ToolResult> ExecuteAsync(...) { ... }
}
```

3. **Зарегистрировать в DI**:
```csharp
// ToolServiceExtensions.cs
services.AddTransient<IToolHandler, GetReceiptItemsHandler>();
```

4. **Готово!** ToolExecutor подхватит автоматически.
