# Задача: Переписать QueryHandler на SqlKata с поддержкой агрегатов и JOIN

## Контекст

Текущий `QueryHandler` работает, но модель хочет использовать `COUNT(*)`, `SUM()`, `JOIN` — и это логично. Нужно дать ей полноценный SQL через безопасный query builder.

## Прочитай перед началом

1. `src/SmartBasket.Services/Tools/Handlers/QueryHandler.cs` — текущая реализация
2. `src/SmartBasket.Services/Tools/Models/QueryArgs.cs` — текущие аргументы
3. Документация SqlKata: https://sqlkata.com/docs

## Установка пакетов

```bash
cd src/SmartBasket.Services
dotnet add package SqlKata --version 2.4.0
dotnet add package SqlKata.Execution --version 2.4.0
dotnet add package Npgsql --version 8.0.0  # если ещё нет
```

## Новая схема QueryArgs

```csharp
using System.Text.Json.Serialization;

namespace SmartBasket.Services.Tools.Models;

public class QueryArgs
{
    /// <summary>
    /// Основная таблица запроса
    /// </summary>
    [JsonPropertyName("table")]
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// Колонки для SELECT (пусто = все колонки основной таблицы)
    /// Формат: "column" или "table.column"
    /// </summary>
    [JsonPropertyName("columns")]
    public List<string>? Columns { get; set; }

    /// <summary>
    /// Агрегатные функции
    /// </summary>
    [JsonPropertyName("aggregates")]
    public List<AggregateColumn>? Aggregates { get; set; }

    /// <summary>
    /// JOIN с другими таблицами
    /// </summary>
    [JsonPropertyName("joins")]
    public List<JoinClause>? Joins { get; set; }

    /// <summary>
    /// Условия WHERE (AND между условиями)
    /// </summary>
    [JsonPropertyName("where")]
    public List<WhereCondition>? Where { get; set; }

    /// <summary>
    /// GROUP BY колонки
    /// </summary>
    [JsonPropertyName("group_by")]
    public List<string>? GroupBy { get; set; }

    /// <summary>
    /// HAVING условия (для агрегатов)
    /// </summary>
    [JsonPropertyName("having")]
    public List<HavingCondition>? Having { get; set; }

    /// <summary>
    /// ORDER BY
    /// </summary>
    [JsonPropertyName("order_by")]
    public List<OrderByClause>? OrderBy { get; set; }

    /// <summary>
    /// LIMIT (максимум 100)
    /// </summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; } = 20;
}

public class AggregateColumn
{
    /// <summary>
    /// Функция: COUNT, SUM, AVG, MIN, MAX
    /// </summary>
    [JsonPropertyName("function")]
    public string Function { get; set; } = string.Empty;

    /// <summary>
    /// Колонка (или * для COUNT)
    /// </summary>
    [JsonPropertyName("column")]
    public string Column { get; set; } = string.Empty;

    /// <summary>
    /// Алиас в результате
    /// </summary>
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }
}

public class JoinClause
{
    /// <summary>
    /// Таблица для JOIN
    /// </summary>
    [JsonPropertyName("table")]
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// Условие: [левая_колонка, правая_колонка]
    /// Пример: ["items.id", "receipt_items.item_id"]
    /// </summary>
    [JsonPropertyName("on")]
    public List<string> On { get; set; } = new();

    /// <summary>
    /// Тип JOIN: inner (default), left, right
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; } = "inner";
}

public class WhereCondition
{
    [JsonPropertyName("column")]
    public string Column { get; set; } = string.Empty;

    /// <summary>
    /// Оператор: =, !=, >, <, >=, <=, ILIKE, LIKE, IN, NOT IN, IS NULL, IS NOT NULL, BETWEEN
    /// </summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = "=";

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class HavingCondition
{
    /// <summary>
    /// Агрегатная функция: COUNT, SUM, AVG, MIN, MAX
    /// </summary>
    [JsonPropertyName("function")]
    public string Function { get; set; } = string.Empty;

    [JsonPropertyName("column")]
    public string Column { get; set; } = string.Empty;

    /// <summary>
    /// Оператор: =, !=, >, <, >=, <=
    /// </summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = "=";

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class OrderByClause
{
    [JsonPropertyName("column")]
    public string Column { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "ASC";
}
```

## Новый QueryHandler

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using SqlKata;
using SqlKata.Compilers;
using SqlKata.Execution;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools.Handlers;

public class QueryHandler : IToolHandler
{
    private readonly AppSettings _settings;
    private readonly ILogger<QueryHandler> _logger;

    public string Name => "query";

    // Whitelist таблиц и их колонок
    private static readonly Dictionary<string, HashSet<string>> AllowedColumns = new()
    {
        ["receipts"] = new() { "id", "receipt_date", "shop", "total", "receipt_number" },
        ["receipt_items"] = new() { "id", "receipt_id", "item_id", "quantity", "price", "amount" },
        ["items"] = new() { "id", "name", "product_id" },
        ["products"] = new() { "id", "name", "parent_id" },
        ["labels"] = new() { "id", "name", "color" },
        ["item_labels"] = new() { "item_id", "label_id" }
    };

    // Разрешённые агрегатные функции
    private static readonly HashSet<string> AllowedAggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX"
    };

    // Разрешённые операторы
    private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "=", "!=", "<>", ">", "<", ">=", "<=", 
        "LIKE", "ILIKE", "NOT LIKE", "NOT ILIKE",
        "IN", "NOT IN", "IS NULL", "IS NOT NULL", "BETWEEN"
    };

    public QueryHandler(AppSettings settings, ILogger<QueryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition(
            Name,
            "Выполнить SQL SELECT запрос с поддержкой JOIN, агрегатов, GROUP BY",
            new
            {
                type = "object",
                properties = new
                {
                    table = new
                    {
                        type = "string",
                        @enum = AllowedColumns.Keys.ToArray(),
                        description = "Основная таблица"
                    },
                    columns = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Колонки для SELECT. Формат: 'column' или 'table.column'. Пусто = все колонки основной таблицы"
                    },
                    aggregates = new
                    {
                        type = "array",
                        description = "Агрегатные функции",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                function = new { type = "string", @enum = new[] { "COUNT", "SUM", "AVG", "MIN", "MAX" } },
                                column = new { type = "string", description = "Колонка или * для COUNT(*)" },
                                alias = new { type = "string", description = "Алиас результата" }
                            },
                            required = new[] { "function", "column" }
                        }
                    },
                    joins = new
                    {
                        type = "array",
                        description = "JOIN с другими таблицами",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                table = new { type = "string", @enum = AllowedColumns.Keys.ToArray() },
                                on = new
                                {
                                    type = "array",
                                    items = new { type = "string" },
                                    description = "Условие: ['left.column', 'right.column']"
                                },
                                type = new { type = "string", @enum = new[] { "inner", "left", "right" }, @default = "inner" }
                            },
                            required = new[] { "table", "on" }
                        }
                    },
                    where = new
                    {
                        type = "array",
                        description = "Условия WHERE (объединяются через AND)",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                column = new { type = "string" },
                                op = new { type = "string", @enum = AllowedOperators.ToArray() },
                                value = new { description = "Значение (или массив для IN, или [min, max] для BETWEEN)" }
                            },
                            required = new[] { "column", "op" }
                        }
                    },
                    group_by = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "GROUP BY колонки"
                    },
                    having = new
                    {
                        type = "array",
                        description = "HAVING условия для агрегатов",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                function = new { type = "string", @enum = new[] { "COUNT", "SUM", "AVG", "MIN", "MAX" } },
                                column = new { type = "string" },
                                op = new { type = "string", @enum = new[] { "=", "!=", ">", "<", ">=", "<=" } },
                                value = new { type = "number" }
                            },
                            required = new[] { "function", "column", "op", "value" }
                        }
                    },
                    order_by = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                column = new { type = "string" },
                                direction = new { type = "string", @enum = new[] { "ASC", "DESC" }, @default = "ASC" }
                            },
                            required = new[] { "column" }
                        }
                    },
                    limit = new
                    {
                        type = "integer",
                        @default = 20,
                        maximum = 100,
                        description = "Максимум строк в результате"
                    }
                },
                required = new[] { "table" }
            });
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<QueryArgs>(argumentsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (args == null)
                return ToolResult.Error("Не удалось разобрать аргументы");

            // Валидация основной таблицы
            if (!AllowedColumns.ContainsKey(args.Table))
                return ToolResult.Error($"Неизвестная таблица: {args.Table}. Доступные: {string.Join(", ", AllowedColumns.Keys)}");

            // Валидация JOIN таблиц
            if (args.Joins != null)
            {
                foreach (var join in args.Joins)
                {
                    if (!AllowedColumns.ContainsKey(join.Table))
                        return ToolResult.Error($"Неизвестная таблица для JOIN: {join.Table}");
                }
            }

            // Строим запрос
            var query = BuildQuery(args);

            // Выполняем
            var result = await ExecuteQueryAsync(query, args, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения query");
            return ToolResult.Error($"Ошибка: {ex.Message}");
        }
    }

    private Query BuildQuery(QueryArgs args)
    {
        var query = new Query(args.Table);

        // JOINs
        if (args.Joins != null)
        {
            foreach (var join in args.Joins)
            {
                if (join.On.Count != 2) continue;

                var (left, right) = (ValidateColumn(join.On[0]), ValidateColumn(join.On[1]));
                if (left == null || right == null) continue;

                switch (join.Type?.ToLower())
                {
                    case "left":
                        query = query.LeftJoin(join.Table, left, right);
                        break;
                    case "right":
                        query = query.RightJoin(join.Table, left, right);
                        break;
                    default:
                        query = query.Join(join.Table, left, right);
                        break;
                }
            }
        }

        // SELECT columns
        var hasSelections = false;

        if (args.Columns != null && args.Columns.Count > 0)
        {
            var validColumns = args.Columns
                .Select(ValidateColumn)
                .Where(c => c != null)
                .ToArray();

            if (validColumns.Length > 0)
            {
                query = query.Select(validColumns!);
                hasSelections = true;
            }
        }

        // Aggregates
        if (args.Aggregates != null)
        {
            foreach (var agg in args.Aggregates)
            {
                if (!AllowedAggregateFunctions.Contains(agg.Function))
                    continue;

                var col = agg.Column == "*" ? "*" : ValidateColumn(agg.Column);
                if (col == null && agg.Column != "*") continue;

                var alias = !string.IsNullOrEmpty(agg.Alias) ? agg.Alias : $"{agg.Function.ToLower()}_{agg.Column.Replace(".", "_")}";
                query = query.SelectRaw($"{agg.Function.ToUpper()}({col}) as {alias}");
                hasSelections = true;
            }
        }

        // Если ничего не выбрано — все колонки основной таблицы
        if (!hasSelections)
        {
            query = query.Select(AllowedColumns[args.Table].Select(c => $"{args.Table}.{c}").ToArray());
        }

        // WHERE
        if (args.Where != null)
        {
            foreach (var condition in args.Where)
            {
                var col = ValidateColumn(condition.Column);
                if (col == null) continue;

                query = ApplyWhereCondition(query, col, condition.Op, condition.Value);
            }
        }

        // GROUP BY
        if (args.GroupBy != null && args.GroupBy.Count > 0)
        {
            var validGroupBy = args.GroupBy
                .Select(ValidateColumn)
                .Where(c => c != null)
                .ToArray();

            if (validGroupBy.Length > 0)
            {
                query = query.GroupBy(validGroupBy!);
            }
        }

        // HAVING
        if (args.Having != null)
        {
            foreach (var having in args.Having)
            {
                if (!AllowedAggregateFunctions.Contains(having.Function))
                    continue;

                var col = having.Column == "*" ? "*" : ValidateColumn(having.Column);
                if (col == null && having.Column != "*") continue;

                var op = AllowedOperators.Contains(having.Op) ? having.Op : "=";
                query = query.HavingRaw($"{having.Function.ToUpper()}({col}) {op} ?", having.Value);
            }
        }

        // ORDER BY
        if (args.OrderBy != null)
        {
            foreach (var order in args.OrderBy)
            {
                var col = ValidateColumn(order.Column);
                if (col == null) continue;

                if (order.Direction?.ToUpper() == "DESC")
                    query = query.OrderByDesc(col);
                else
                    query = query.OrderBy(col);
            }
        }

        // LIMIT
        var limit = Math.Min(args.Limit ?? 20, 100);
        query = query.Limit(limit);

        return query;
    }

    private string? ValidateColumn(string column)
    {
        if (string.IsNullOrWhiteSpace(column))
            return null;

        // Формат: "table.column" или "column"
        if (column.Contains('.'))
        {
            var parts = column.Split('.', 2);
            if (parts.Length == 2 &&
                AllowedColumns.TryGetValue(parts[0], out var cols) &&
                cols.Contains(parts[1]))
            {
                return column;
            }
        }
        else
        {
            // Ищем колонку в любой таблице
            foreach (var (table, cols) in AllowedColumns)
            {
                if (cols.Contains(column))
                    return $"{table}.{column}";
            }
        }

        return null;
    }

    private Query ApplyWhereCondition(Query query, string column, string op, object? value)
    {
        var upperOp = op.ToUpper();

        return upperOp switch
        {
            "=" => query.Where(column, "=", value),
            "!=" or "<>" => query.Where(column, "!=", value),
            ">" => query.Where(column, ">", value),
            "<" => query.Where(column, "<", value),
            ">=" => query.Where(column, ">=", value),
            "<=" => query.Where(column, "<=", value),
            "LIKE" => query.WhereLike(column, value?.ToString() ?? "", caseSensitive: true),
            "ILIKE" => query.WhereLike(column, value?.ToString() ?? "", caseSensitive: false),
            "NOT LIKE" => query.WhereNotLike(column, value?.ToString() ?? "", caseSensitive: true),
            "NOT ILIKE" => query.WhereNotLike(column, value?.ToString() ?? "", caseSensitive: false),
            "IN" => value is JsonElement arr && arr.ValueKind == JsonValueKind.Array
                ? query.WhereIn(column, arr.EnumerateArray().Select(e => GetJsonValue(e)).ToArray())
                : query,
            "NOT IN" => value is JsonElement arr2 && arr2.ValueKind == JsonValueKind.Array
                ? query.WhereNotIn(column, arr2.EnumerateArray().Select(e => GetJsonValue(e)).ToArray())
                : query,
            "IS NULL" => query.WhereNull(column),
            "IS NOT NULL" => query.WhereNotNull(column),
            "BETWEEN" => value is JsonElement between && between.ValueKind == JsonValueKind.Array
                ? ApplyBetween(query, column, between)
                : query,
            _ => query
        };
    }

    private Query ApplyBetween(Query query, string column, JsonElement array)
    {
        var values = array.EnumerateArray().ToList();
        if (values.Count == 2)
        {
            return query.WhereBetween(column, GetJsonValue(values[0]), GetJsonValue(values[1]));
        }
        return query;
    }

    private object? GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private async Task<ToolResult> ExecuteQueryAsync(Query query, QueryArgs args, CancellationToken ct)
    {
        var connectionString = _settings.Database.ConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var compiler = new PostgresCompiler();
        var db = new QueryFactory(connection, compiler);

        // Логируем SQL для отладки
        var compiled = compiler.Compile(query);
        _logger.LogDebug("SQL: {Sql}", compiled.Sql);
        _logger.LogDebug("Bindings: {Bindings}", string.Join(", ", compiled.Bindings));

        var rows = await db.FromQuery(query).GetAsync<dynamic>(cancellationToken: ct);
        var rowsList = rows.ToList();

        // Определяем колонки из первой строки
        var columns = new List<string>();
        if (rowsList.Count > 0)
        {
            var first = rowsList[0] as IDictionary<string, object>;
            if (first != null)
                columns = first.Keys.ToList();
        }

        return ToolResult.Ok(new
        {
            table = args.Table,
            columns = columns,
            rows = rowsList,
            total_rows = rowsList.Count,
            truncated = rowsList.Count >= (args.Limit ?? 20)
        });
    }
}
```

## Обновить описание инструмента в system prompt

Добавь в system prompt:

```
2. query — выполнить SQL SELECT запрос
   Параметры:
   - table: основная таблица (receipts, receipt_items, items, products, labels, item_labels)
   - columns: список колонок ["table.column", ...]
   - aggregates: агрегатные функции [{function: "COUNT|SUM|AVG|MIN|MAX", column: "*|column", alias: "..."}]
   - joins: [{table: "...", on: ["left.col", "right.col"], type: "inner|left|right"}]
   - where: [{column: "...", op: "=|ILIKE|>|<|IN|...", value: ...}]
   - group_by: ["column", ...]
   - having: [{function: "SUM", column: "amount", op: ">", value: 1000}]
   - order_by: [{column: "...", direction: "ASC|DESC"}]
   - limit: число (макс 100)

ПРИМЕРЫ:

Сколько чеков и на какую сумму:
{
  "table": "receipts",
  "aggregates": [
    {"function": "COUNT", "column": "*", "alias": "total_receipts"},
    {"function": "SUM", "column": "total", "alias": "total_amount"}
  ]
}

Топ-5 товаров по сумме покупок:
{
  "table": "receipt_items",
  "joins": [{"table": "items", "on": ["items.id", "receipt_items.item_id"]}],
  "columns": ["items.name"],
  "aggregates": [{"function": "SUM", "column": "receipt_items.amount", "alias": "total"}],
  "group_by": ["items.name"],
  "order_by": [{"column": "total", "direction": "DESC"}],
  "limit": 5
}

Найти товары с "кур" в названии:
{
  "table": "items",
  "where": [{"column": "name", "op": "ILIKE", "value": "%кур%"}]
}
```

## Проверка

```bash
dotnet build src/SmartBasket.sln
```

## Критерии готовности

- [ ] SqlKata пакеты установлены
- [ ] QueryArgs поддерживает joins, aggregates, group_by, having
- [ ] QueryHandler использует SqlKata для построения запросов
- [ ] Whitelist валидация сохранена
- [ ] Компилируется без ошибок

## Тестовые запросы

```
User: "Сколько у меня чеков и на какую сумму?"
→ query с aggregates: COUNT(*), SUM(total)
→ "6 чеков на 31 540,91₽"

User: "Какие товары я покупал чаще всего?"
→ query с JOIN items, GROUP BY items.name, COUNT(*), ORDER BY DESC
→ Список топ товаров

User: "Сколько потратил на молочные продукты?"
→ query products WHERE name ILIKE %молоч% → product_id
→ query items WHERE product_id = ... → item_ids
→ query receipt_items с JOIN, SUM(amount)
→ "На молочные продукты: X рублей"
```
