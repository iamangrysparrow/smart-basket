namespace SmartBasket.Services.Ollama;

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
    /// Количество
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Единица измерения (шт, кг, л, г, мл)
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Цена
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Объем/вес (если указан отдельно)
    /// </summary>
    public string? Volume { get; set; }
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
