using System.Text.Json.Serialization;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Продукт или категория из результата классификации.
/// Модель возвращает и категории и продукты в одном списке.
/// </summary>
public class ClassifiedProduct
{
    /// <summary>
    /// Название продукта или категории
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Название родительской категории (null для корневых)
    /// </summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    /// <summary>
    /// true = это конечный продукт (лист дерева)
    /// false = это категория (может иметь потомков)
    /// </summary>
    [JsonPropertyName("product")]
    public bool IsProduct { get; set; }
}

/// <summary>
/// Результат классификации от LLM (JSON ответ)
/// </summary>
public class ClassificationResponse
{
    [JsonPropertyName("products")]
    public List<ClassifiedProduct> Products { get; set; } = new();
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
    /// Список продуктов и категорий
    /// </summary>
    public List<ClassifiedProduct> Products { get; set; } = new();

    /// <summary>
    /// Сырой ответ от LLM (для отладки)
    /// </summary>
    public string? RawResponse { get; set; }
}
