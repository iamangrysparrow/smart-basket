using System.Text.Json.Serialization;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Продукт из результата классификации (новый формат с номером категории).
/// </summary>
public class ClassifiedProduct
{
    /// <summary>
    /// Название продукта
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Номер категории из промпта (1, 2, 3...)
    /// </summary>
    [JsonPropertyName("category")]
    public int CategoryNumber { get; set; }

    // --- Legacy поля для обратной совместимости ---

    /// <summary>
    /// [Legacy] Название родительской категории (null для корневых)
    /// </summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    /// <summary>
    /// [Legacy] true = это конечный продукт (лист дерева)
    /// </summary>
    [JsonPropertyName("product")]
    public bool IsProduct { get; set; } = true;
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
    /// Маппинг: номер категории из промпта → Guid категории в БД
    /// </summary>
    public Dictionary<int, Guid> CategoryNumberToGuid { get; set; } = new();

    /// <summary>
    /// Сырой ответ от LLM (для отладки)
    /// </summary>
    public string? RawResponse { get; set; }
}
