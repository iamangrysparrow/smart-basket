using System.Text.Json.Serialization;

namespace SmartBasket.Services.Ollama;

/// <summary>
/// Продукт из результата классификации
/// </summary>
public class ClassifiedProduct
{
    /// <summary>
    /// Название продукта
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Название родительского продукта (null для корневых)
    /// </summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; set; }
}

/// <summary>
/// Товар с привязкой к продукту из результата классификации
/// </summary>
public class ClassifiedItem
{
    /// <summary>
    /// Полное название товара из чека
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Название продукта к которому относится товар
    /// </summary>
    [JsonPropertyName("product")]
    public string Product { get; set; } = string.Empty;
}

/// <summary>
/// Результат классификации от Ollama (JSON ответ)
/// </summary>
public class ClassificationResponse
{
    [JsonPropertyName("products")]
    public List<ClassifiedProduct> Products { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ClassifiedItem> Items { get; set; } = new();
}

/// <summary>
/// Полный результат классификации товаров
/// </summary>
public class ProductClassificationResult
{
    /// <summary>
    /// Успешно ли выполнена классификация
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Сообщение об ошибке или статусе
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Список продуктов (существующих и новых)
    /// </summary>
    public List<ClassifiedProduct> Products { get; set; } = new();

    /// <summary>
    /// Список товаров с привязкой к продуктам
    /// </summary>
    public List<ClassifiedItem> Items { get; set; } = new();

    /// <summary>
    /// Сырой ответ от Ollama (для отладки)
    /// </summary>
    public string? RawResponse { get; set; }
}
