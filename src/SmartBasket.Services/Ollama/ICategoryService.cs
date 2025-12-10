namespace SmartBasket.Services.Ollama;

/// <summary>
/// Пример категоризации для few-shot learning
/// </summary>
public class CategoryExample
{
    public string OriginalItemName { get; set; } = string.Empty;
    public string NormalizedItemName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
}

/// <summary>
/// Результат категоризации одного товара
/// </summary>
public class CategorizedItem
{
    public string OriginalName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public int Confidence { get; set; }
}

/// <summary>
/// Результат batch-категоризации
/// </summary>
public class CategorizationResult
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public List<CategorizedItem> Items { get; set; } = new();
    public string? RawResponse { get; set; }
}

/// <summary>
/// Сервис категоризации товаров через LLM (Ollama, YandexGPT и др.)
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt для категоризации
    /// </summary>
    void SetPromptTemplatePath(string path);

    /// <summary>
    /// Категоризировать партию товаров
    /// </summary>
    /// <param name="itemNames">Названия товаров для категоризации</param>
    /// <param name="categories">Список доступных категорий пользователя</param>
    /// <param name="examples">Примеры категоризации для few-shot learning (до 2 на категорию)</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат категоризации</returns>
    Task<CategorizationResult> CategorizeItemsAsync(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> categories,
        IReadOnlyList<CategoryExample>? examples = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
