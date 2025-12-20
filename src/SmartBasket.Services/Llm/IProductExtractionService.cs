using System.Text.Json.Serialization;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Результат выделения продукта из названия товара
/// </summary>
public class ExtractedItem
{
    /// <summary>
    /// Полное название товара из чека
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Выделенный продукт (нормализованное название)
    /// </summary>
    [JsonPropertyName("product")]
    public string Product { get; set; } = string.Empty;
}

/// <summary>
/// Ответ от LLM для выделения продуктов
/// </summary>
public class ExtractionResponse
{
    [JsonPropertyName("items")]
    public List<ExtractedItem> Items { get; set; } = new();
}

/// <summary>
/// Результат выделения продуктов из товаров
/// </summary>
public class ProductExtractionResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RawResponse { get; set; }

    /// <summary>
    /// Список товаров с выделенными продуктами
    /// </summary>
    public List<ExtractedItem> Items { get; set; } = new();
}

/// <summary>
/// Сервис выделения продуктов из названий товаров через LLM.
/// Этап 1: Item → Product (нормализация названия).
/// </summary>
public interface IProductExtractionService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt для выделения продуктов
    /// </summary>
    void SetPromptTemplatePath(string path);

    /// <summary>
    /// Установить кастомный prompt напрямую (приоритет над файлом)
    /// </summary>
    void SetCustomPrompt(string? prompt);

    /// <summary>
    /// Выделить продукты из названий товаров.
    /// Нормализует названия: убирает бренды, объёмы, маркировки.
    /// </summary>
    /// <param name="itemNames">Названия товаров для обработки</param>
    /// <param name="existingProducts">Список существующих продуктов для переиспользования</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат с нормализованными продуктами</returns>
    Task<ProductExtractionResult> ExtractAsync(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string>? existingProducts = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
