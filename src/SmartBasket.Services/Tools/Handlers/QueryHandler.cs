using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using SqlKata;
using SqlKata.Compilers;
using SqlKata.Execution;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools.Handlers;

/// <summary>
/// Обработчик инструмента query (SqlKata)
/// Универсальный SELECT с поддержкой JOIN, агрегатов, GROUP BY
/// </summary>
public class QueryHandler : IToolHandler
{
    private readonly AppSettings _settings;
    private readonly ILogger<QueryHandler> _logger;

    public string Name => "query";

    // Whitelist таблиц и их колонок (PascalCase как в PostgreSQL)
    private static readonly Dictionary<string, HashSet<string>> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Receipts"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "ReceiptDate", "Shop", "Total", "ReceiptNumber", "EmailId", "Status", "CreatedAt", "UpdatedAt"
        },
        ["ReceiptItems"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "ReceiptId", "ItemId", "Quantity", "Price", "Amount", "CreatedAt", "UpdatedAt"
        },
        ["Items"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "Name", "ProductId", "UnitOfMeasure", "UnitQuantity", "Shop", "CreatedAt", "UpdatedAt"
        },
        ["Products"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "Name", "ParentId", "CreatedAt", "UpdatedAt"
        },
        ["Labels"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "Name", "Color", "CreatedAt", "UpdatedAt"
        },
        ["ItemLabels"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "ItemId", "LabelId"
        },
        ["ProductLabels"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "ProductId", "LabelId"
        }
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
                        description = "Основная таблица (PascalCase)"
                    },
                    columns = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Колонки для SELECT. Формат: 'Column' или 'Table.Column'. Пусто = все колонки основной таблицы"
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
                                alias = new { type = "string", description = "Алиас результата (например total_amount)" }
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
                                    description = "Условие: ['Left.Column', 'Right.Column'], например ['Items.Id', 'ReceiptItems.ItemId']"
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
                                column = new { type = "string", description = "Колонка (Column или Table.Column)" },
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
                                column = new { type = "string", description = "Колонка или алиас агрегата" },
                                direction = new { type = "string", @enum = new[] { "ASC", "DESC" }, @default = "ASC" }
                            },
                            required = new[] { "column" }
                        }
                    },
                    limit = new
                    {
                        type = "integer",
                        @default = 1000,
                        description = "Максимум строк в результате (по умолчанию 1000)"
                    }
                },
                required = new[] { "table" }
            });
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[QueryHandler] ===== TOOL ARGUMENTS START =====");
        _logger.LogDebug("[QueryHandler] Arguments ({Length} chars):\n{Args}", argumentsJson.Length, argumentsJson);
        _logger.LogDebug("[QueryHandler] ===== TOOL ARGUMENTS END =====");

        try
        {
            var args = JsonSerializer.Deserialize<QueryArgs>(argumentsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (args == null)
                return ToolResult.Error("Не удалось разобрать аргументы");

            // Нормализуем имя таблицы к PascalCase
            var tableName = NormalizeTableName(args.Table);
            if (tableName == null || !AllowedColumns.ContainsKey(tableName))
            {
                return ToolResult.Error($"Неизвестная таблица: {args.Table}. Доступные: {string.Join(", ", AllowedColumns.Keys)}");
            }

            // Валидация JOIN таблиц
            if (args.Joins != null)
            {
                foreach (var join in args.Joins)
                {
                    var joinTable = NormalizeTableName(join.Table);
                    if (joinTable == null || !AllowedColumns.ContainsKey(joinTable))
                    {
                        return ToolResult.Error($"Неизвестная таблица для JOIN: {join.Table}. Доступные: {string.Join(", ", AllowedColumns.Keys)}");
                    }
                }
            }

            // Строим и выполняем запрос
            var result = await ExecuteQueryAsync(args, tableName, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryHandler] Ошибка выполнения query");
            return ToolResult.Error($"Ошибка: {ex.Message}");
        }
    }

    private async Task<ToolResult> ExecuteQueryAsync(QueryArgs args, string tableName, CancellationToken ct)
    {
        var connectionString = _settings.Database.ConnectionString;

        // Вычисляем эффективный лимит
        var maxRows = _settings.QueryMaxRows > 0 ? _settings.QueryMaxRows : 1000;
        var requestedLimit = args.Limit; // лимит запрошенный моделью (20 по умолчанию)
        var effectiveLimit = Math.Min(requestedLimit, maxRows);

        // Логируем лимиты на уровне Information (важно для отладки!)
        _logger.LogInformation("[QueryHandler] LIMIT: запрошен={RequestedLimit}, максимум={MaxRows}, применён={EffectiveLimit}",
            requestedLimit, maxRows, effectiveLimit);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var compiler = new PostgresCompiler();
        var db = new QueryFactory(connection, compiler);

        // Строим запрос
        var query = BuildQuery(args, tableName);

        // Полное логирование SQL для отладки
        var compiled = compiler.Compile(query);
        _logger.LogDebug("[QueryHandler] ===== SQL QUERY START =====");
        _logger.LogDebug("[QueryHandler] SQL ({Length} chars):\n{Sql}", compiled.Sql.Length, compiled.Sql);
        _logger.LogDebug("[QueryHandler] Bindings ({Count}): {Bindings}",
            compiled.Bindings.Count, string.Join(", ", compiled.Bindings.Select((b, i) => $"${i}={b}")));
        _logger.LogDebug("[QueryHandler] ===== SQL QUERY END =====");

        // Выполняем
        var rows = (await db.FromQuery(query).GetAsync(cancellationToken: ct)).ToList();

        // Определяем колонки из первой строки
        var columns = new List<string>();
        if (rows.Count > 0)
        {
            var first = rows[0] as IDictionary<string, object>;
            if (first != null)
                columns = first.Keys.ToList();
        }

        // truncated = true если вернулось >= effectiveLimit строк
        var truncated = rows.Count >= effectiveLimit;

        var result = ToolResult.Ok(new
        {
            table = tableName,
            sql = compiled.Sql,
            columns,
            rows,
            row_count = rows.Count,
            truncated,
            limit_applied = effectiveLimit
        });

        // Логируем результат на уровне Information
        _logger.LogInformation("[QueryHandler] Result table={Table}, rows={RowCount}, truncated={Truncated}",
            tableName, rows.Count, truncated);

        _logger.LogDebug("[QueryHandler] ===== QUERY RESULT START =====");
        _logger.LogDebug("[QueryHandler] Result JSON ({Length} chars):\n{Json}",
            result.JsonData.Length, result.JsonData);
        _logger.LogDebug("[QueryHandler] ===== QUERY RESULT END =====");

        return result;
    }

    private Query BuildQuery(QueryArgs args, string tableName)
    {
        // Используем FromRaw чтобы избежать двойного экранирования SqlKata
        var query = new Query().FromRaw($"public.\"{tableName}\"");

        // JOINs - собираем JOIN строки вручную и добавляем через CrossJoin + WhereRaw
        var joinedTables = new HashSet<string>();
        var joinClauses = new List<string>();
        if (args.Joins != null)
        {
            foreach (var join in args.Joins)
            {
                if (join.On.Count != 2) continue;

                var joinTable = NormalizeTableName(join.Table);
                if (joinTable == null) continue;

                joinedTables.Add(joinTable);

                var left = ValidateAndQuoteColumn(join.On[0], tableName);
                var right = ValidateAndQuoteColumn(join.On[1], tableName);
                if (left == null || right == null) continue;

                var joinType = join.Type?.ToLower() switch
                {
                    "left" => "LEFT JOIN",
                    "right" => "RIGHT JOIN",
                    _ => "INNER JOIN"
                };

                joinClauses.Add($"{joinType} public.\"{joinTable}\" ON {left} = {right}");
            }
        }

        // Если есть JOIN-ы, перестраиваем FROM с ними
        if (joinClauses.Count > 0)
        {
            var fromWithJoins = $"public.\"{tableName}\" {string.Join(" ", joinClauses)}";
            query = new Query().FromRaw(fromWithJoins);
        }

        // SELECT columns (используем SelectRaw для избежания двойного экранирования)
        var hasSelections = false;

        if (args.Columns != null && args.Columns.Count > 0)
        {
            var validColumns = args.Columns
                .Select(c => ValidateAndQuoteColumn(c, tableName))
                .Where(c => c != null)
                .ToArray();

            if (validColumns.Length > 0)
            {
                // SelectRaw для каждой колонки
                foreach (var col in validColumns)
                {
                    query = query.SelectRaw(col!);
                }
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

                string colExpr;
                if (agg.Column == "*")
                {
                    colExpr = "*";
                }
                else
                {
                    var col = ValidateAndQuoteColumn(agg.Column, tableName);
                    if (col == null) continue;
                    colExpr = col;
                }

                var alias = !string.IsNullOrEmpty(agg.Alias)
                    ? agg.Alias
                    : $"{agg.Function.ToLower()}_{agg.Column.Replace(".", "_").Replace("\"", "")}";

                query = query.SelectRaw($"{agg.Function.ToUpper()}({colExpr}) as \"{alias}\"");
                hasSelections = true;
            }
        }

        // Если ничего не выбрано — все колонки основной таблицы
        if (!hasSelections)
        {
            var defaultColumns = AllowedColumns[tableName]
                .Select(c => $"public.\"{tableName}\".\"{c}\"");
            foreach (var col in defaultColumns)
            {
                query = query.SelectRaw(col);
            }
        }

        // WHERE
        if (args.Where != null)
        {
            foreach (var condition in args.Where)
            {
                var col = ValidateAndQuoteColumn(condition.Column, tableName);
                if (col == null) continue;

                query = ApplyWhereCondition(query, col, condition.Op, condition.Value);
            }
        }

        // GROUP BY
        if (args.GroupBy != null && args.GroupBy.Count > 0)
        {
            var validGroupBy = args.GroupBy
                .Select(c => ValidateAndQuoteColumn(c, tableName))
                .Where(c => c != null)
                .ToArray();

            if (validGroupBy.Length > 0)
            {
                query = query.GroupByRaw(string.Join(", ", validGroupBy));
            }
        }

        // HAVING
        if (args.Having != null)
        {
            foreach (var having in args.Having)
            {
                if (!AllowedAggregateFunctions.Contains(having.Function))
                    continue;

                string colExpr;
                if (having.Column == "*")
                {
                    colExpr = "*";
                }
                else
                {
                    var col = ValidateAndQuoteColumn(having.Column, tableName);
                    if (col == null) continue;
                    colExpr = col;
                }

                var op = AllowedOperators.Contains(having.Op) ? having.Op : "=";
                var value = GetValue(having.Value);
                query = query.HavingRaw($"{having.Function.ToUpper()}({colExpr}) {op} ?", value);
            }
        }

        // ORDER BY
        if (args.OrderBy != null)
        {
            foreach (var order in args.OrderBy)
            {
                // ORDER BY может быть по алиасу агрегата или по колонке
                var isAggregate = args.Aggregates?.Any(a =>
                    !string.IsNullOrEmpty(a.Alias) &&
                    a.Alias.Equals(order.Column, StringComparison.OrdinalIgnoreCase)) ?? false;

                if (isAggregate)
                {
                    // Сортировка по алиасу агрегата
                    if (order.Direction?.ToUpper() == "DESC")
                        query = query.OrderByRaw($"\"{order.Column}\" DESC");
                    else
                        query = query.OrderByRaw($"\"{order.Column}\" ASC");
                }
                else
                {
                    // Сортировка по колонке
                    var col = ValidateAndQuoteColumn(order.Column, tableName);
                    if (col == null) continue;

                    if (order.Direction?.ToUpper() == "DESC")
                        query = query.OrderByRaw($"{col} DESC");
                    else
                        query = query.OrderByRaw($"{col} ASC");
                }
            }
        }

        // LIMIT - ограничиваем максимумом из настроек
        var maxRows = _settings.QueryMaxRows > 0 ? _settings.QueryMaxRows : 1000;
        var limit = Math.Min(args.Limit, maxRows);
        query = query.Limit(limit);

        return query;
    }

    /// <summary>
    /// Нормализует имя таблицы к PascalCase
    /// </summary>
    private string? NormalizeTableName(string table)
    {
        // Прямое совпадение
        if (AllowedColumns.ContainsKey(table))
            return AllowedColumns.Keys.First(k => k.Equals(table, StringComparison.OrdinalIgnoreCase));

        // snake_case → PascalCase
        var normalized = table.ToLower() switch
        {
            "receipts" => "Receipts",
            "receipt_items" => "ReceiptItems",
            "items" => "Items",
            "products" => "Products",
            "labels" => "Labels",
            "item_labels" => "ItemLabels",
            "product_labels" => "ProductLabels",
            _ => null
        };

        return normalized;
    }

    /// <summary>
    /// Нормализует имя колонки к PascalCase
    /// </summary>
    private string? NormalizeColumnName(string column, string? defaultTable = null)
    {
        // Формат: "Table.Column" или "Column"
        string tablePart;
        string columnPart;

        if (column.Contains('.'))
        {
            var parts = column.Split('.', 2);
            tablePart = parts[0];
            columnPart = parts[1];
        }
        else
        {
            tablePart = defaultTable ?? "";
            columnPart = column;
        }

        // Нормализуем таблицу
        var normalizedTable = !string.IsNullOrEmpty(tablePart) ? NormalizeTableName(tablePart) : null;

        // Нормализуем колонку (ищем case-insensitive)
        string? normalizedColumn = null;

        if (normalizedTable != null && AllowedColumns.TryGetValue(normalizedTable, out var cols))
        {
            normalizedColumn = cols.FirstOrDefault(c => c.Equals(columnPart, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // Ищем колонку в любой таблице
            foreach (var (tbl, tblCols) in AllowedColumns)
            {
                var found = tblCols.FirstOrDefault(c => c.Equals(columnPart, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    normalizedTable = tbl;
                    normalizedColumn = found;
                    break;
                }
            }
        }

        if (normalizedColumn == null)
            return null;

        return normalizedTable != null
            ? $"{normalizedTable}.{normalizedColumn}"
            : normalizedColumn;
    }

    /// <summary>
    /// Валидирует и квотирует колонку для PostgreSQL
    /// </summary>
    private string? ValidateAndQuoteColumn(string column, string? defaultTable = null)
    {
        var normalized = NormalizeColumnName(column, defaultTable);
        if (normalized == null) return null;

        if (normalized.Contains('.'))
        {
            var parts = normalized.Split('.', 2);
            return $"public.\"{parts[0]}\".\"{parts[1]}\"";
        }

        return $"\"{normalized}\"";
    }

    private Query ApplyWhereCondition(Query query, string column, string op, object? value)
    {
        var upperOp = op.ToUpper();
        var val = GetValue(value);

        // Определяем cast для типа данных
        var cast = GetTypeCast(column, val);

        return upperOp switch
        {
            "=" => query.WhereRaw($"{column} = ?{cast}", val),
            "!=" or "<>" => query.WhereRaw($"{column} != ?{cast}", val),
            ">" => query.WhereRaw($"{column} > ?{cast}", val),
            "<" => query.WhereRaw($"{column} < ?{cast}", val),
            ">=" => query.WhereRaw($"{column} >= ?{cast}", val),
            "<=" => query.WhereRaw($"{column} <= ?{cast}", val),
            "LIKE" => query.WhereRaw($"{column} LIKE ?", val),
            "ILIKE" => query.WhereRaw($"{column} ILIKE ?", val),
            "NOT LIKE" => query.WhereRaw($"{column} NOT LIKE ?", val),
            "NOT ILIKE" => query.WhereRaw($"{column} NOT ILIKE ?", val),
            "IN" => ApplyInCondition(query, column, value, false),
            "NOT IN" => ApplyInCondition(query, column, value, true),
            "IS NULL" => query.WhereRaw($"{column} IS NULL"),
            "IS NOT NULL" => query.WhereRaw($"{column} IS NOT NULL"),
            "BETWEEN" => ApplyBetweenCondition(query, column, value),
            _ => query
        };
    }

    /// <summary>
    /// Определяет PostgreSQL cast для значения на основе колонки
    /// </summary>
    private static string GetTypeCast(string column, object? value)
    {
        // Для дат - cast к timestamp
        if (IsDateString(value))
            return "::timestamp";

        // Для UUID колонок (Id, *Id) - cast к uuid
        // Колонка приходит в формате public."Table"."Column"
        if (IsUuidColumn(column) && value is string s && !string.IsNullOrEmpty(s))
        {
            // Проверяем что значение похоже на UUID (или может быть приведено)
            if (Guid.TryParse(s, out _))
                return "::uuid";
        }

        return "";
    }

    /// <summary>
    /// Проверяет, является ли колонка UUID типом (Id или *Id)
    /// </summary>
    private static bool IsUuidColumn(string column)
    {
        // Колонка в формате public."Table"."Column" или "Column"
        // Извлекаем имя колонки
        var parts = column.Split('"');
        var colName = parts.Length >= 2 ? parts[^2] : column;

        // UUID колонки: Id, ReceiptId, ItemId, ProductId, LabelId, ParentId, EmailId
        return colName.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
               colName.EndsWith("Id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDateString(object? value)
    {
        return value is string s && DateTime.TryParse(s, out _) &&
               System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}");
    }

    private Query ApplyInCondition(Query query, string column, object? value, bool notIn)
    {
        var values = GetArrayValues(value);
        if (values.Length == 0) return query;

        // Проверяем нужен ли UUID cast для всех значений
        var needsUuidCast = IsUuidColumn(column) &&
                            values.All(v => v is string s && Guid.TryParse(s, out _));

        var cast = needsUuidCast ? "::uuid" : "";
        var placeholders = string.Join(", ", values.Select(_ => $"?{cast}"));
        var sql = notIn ? $"{column} NOT IN ({placeholders})" : $"{column} IN ({placeholders})";

        return query.WhereRaw(sql, values);
    }

    private Query ApplyBetweenCondition(Query query, string column, object? value)
    {
        var values = GetArrayValues(value);
        if (values.Length != 2) return query;

        // Для дат добавляем cast к timestamp
        if (IsDateString(values[0]) && IsDateString(values[1]))
        {
            return query.WhereRaw($"{column} BETWEEN ?::timestamp AND ?::timestamp", values[0], values[1]);
        }

        return query.WhereRaw($"{column} BETWEEN ? AND ?", values[0], values[1]);
    }

    private object? GetValue(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => je.ToString()
            };
        }
        return value;
    }

    private object[] GetArrayValues(object? value)
    {
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray()
                .Select(e => GetValue(e))
                .Where(v => v != null)
                .ToArray()!;
        }
        return Array.Empty<object>();
    }
}
