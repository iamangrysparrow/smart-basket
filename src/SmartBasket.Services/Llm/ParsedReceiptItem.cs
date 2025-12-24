using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Конвертер для decimal? который обрабатывает пустые строки как null.
/// Нужен потому что Ollama иногда возвращает "" вместо числа или null
/// когда не может определить значение (например unit_quantity для товара без веса в названии).
/// </summary>
public class NullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
                return null;
            if (decimal.TryParse(str, out var result))
                return result;
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetDecimal();

        return null;
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

/// <summary>
/// Товарная позиция, распознанная Ollama из текста письма
/// </summary>
public class ParsedReceiptItem
{
    /// <summary>
    /// Название товара
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Количество купленного в чеке
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Единица измерения количества в чеке (шт, кг, л)
    /// JSON: quantity_unit
    /// </summary>
    [JsonPropertyName("quantity_unit")]
    public string? QuantityUnit { get; set; }

    /// <summary>
    /// Цена за единицу
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Итоговая сумма за позицию
    /// </summary>
    [JsonConverter(typeof(NullableDecimalConverter))]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Единица измерения товара (г, кг, мл, л, шт) - выделено из названия
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Количество в единице товара (например 930 для "930 мл") - выделено из названия
    /// JSON: unit_quantity
    /// </summary>
    [JsonPropertyName("unit_quantity")]
    [JsonConverter(typeof(NullableDecimalConverter))]
    public decimal? UnitQuantity { get; set; }
}

/// <summary>
/// Результат парсинга письма
/// </summary>
public class ParsedReceipt
{
    /// <summary>
    /// Название магазина
    /// </summary>
    public string Shop { get; set; } = "Unknown";

    /// <summary>
    /// Дата чека
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>
    /// Номер заказа/чека
    /// </summary>
    public string? OrderNumber { get; set; }

    /// <summary>
    /// Товарные позиции
    /// </summary>
    public List<ParsedReceiptItem> Items { get; set; } = new();

    /// <summary>
    /// Общая сумма
    /// </summary>
    public decimal? Total { get; set; }

    /// <summary>
    /// Успешно ли распознан чек
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Сообщение об ошибке или предупреждение
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Сырой ответ от Ollama (для отладки)
    /// </summary>
    public string? RawResponse { get; set; }
}
