using System.Text.Json.Serialization;

namespace SmartBasket.Services.Tools.Models;

/// <summary>
/// Аргументы для инструмента query (SqlKata)
/// </summary>
public class QueryArgs
{
    /// <summary>
    /// Основная таблица запроса
    /// </summary>
    [JsonPropertyName("table")]
    public required string Table { get; set; }

    /// <summary>
    /// Колонки для SELECT (пусто = все колонки основной таблицы)
    /// Формат: "Column" или "Table.Column"
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
    public int Limit { get; set; } = 20;
}

/// <summary>
/// Агрегатная функция
/// </summary>
public class AggregateColumn
{
    /// <summary>
    /// Функция: COUNT, SUM, AVG, MIN, MAX
    /// </summary>
    [JsonPropertyName("function")]
    public required string Function { get; set; }

    /// <summary>
    /// Колонка (или * для COUNT)
    /// </summary>
    [JsonPropertyName("column")]
    public required string Column { get; set; }

    /// <summary>
    /// Алиас в результате
    /// </summary>
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }
}

/// <summary>
/// JOIN с другой таблицей
/// </summary>
public class JoinClause
{
    /// <summary>
    /// Таблица для JOIN
    /// </summary>
    [JsonPropertyName("table")]
    public required string Table { get; set; }

    /// <summary>
    /// Условие: [левая_колонка, правая_колонка]
    /// Пример: ["Items.Id", "ReceiptItems.ItemId"]
    /// </summary>
    [JsonPropertyName("on")]
    public required List<string> On { get; set; }

    /// <summary>
    /// Тип JOIN: inner (default), left, right
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "inner";
}

/// <summary>
/// Условие WHERE
/// </summary>
public class WhereCondition
{
    /// <summary>
    /// Имя колонки (Column или Table.Column)
    /// </summary>
    [JsonPropertyName("column")]
    public required string Column { get; set; }

    /// <summary>
    /// Оператор: =, !=, >, <, >=, <=, ILIKE, LIKE, IN, NOT IN, IS NULL, IS NOT NULL, BETWEEN
    /// </summary>
    [JsonPropertyName("op")]
    public required string Op { get; set; }

    /// <summary>
    /// Значение (может быть string, number, boolean, array, null)
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// Условие HAVING
/// </summary>
public class HavingCondition
{
    /// <summary>
    /// Агрегатная функция: COUNT, SUM, AVG, MIN, MAX
    /// </summary>
    [JsonPropertyName("function")]
    public required string Function { get; set; }

    /// <summary>
    /// Колонка
    /// </summary>
    [JsonPropertyName("column")]
    public required string Column { get; set; }

    /// <summary>
    /// Оператор: =, !=, >, <, >=, <=
    /// </summary>
    [JsonPropertyName("op")]
    public required string Op { get; set; }

    /// <summary>
    /// Значение
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// Условие ORDER BY
/// </summary>
public class OrderByClause
{
    /// <summary>
    /// Имя колонки или алиас агрегата
    /// </summary>
    [JsonPropertyName("column")]
    public required string Column { get; set; }

    /// <summary>
    /// Направление: ASC или DESC
    /// </summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "ASC";
}
