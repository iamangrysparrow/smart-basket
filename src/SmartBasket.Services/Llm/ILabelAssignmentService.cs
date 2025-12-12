namespace SmartBasket.Services.Llm;

/// <summary>
/// Результат назначения меток для товара
/// </summary>
public class LabelAssignmentResult
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public List<string> AssignedLabels { get; set; } = new();
    public string? RawResponse { get; set; }
}

/// <summary>
/// Входные данные для batch-классификации товара
/// </summary>
public class BatchItemInput
{
    public string ItemName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
}

/// <summary>
/// Результат batch-классификации для одного товара
/// </summary>
public class BatchItemResult
{
    public string ItemName { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
}

/// <summary>
/// Результат batch-назначения меток для нескольких товаров
/// </summary>
public class BatchLabelAssignmentResult
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public List<BatchItemResult> Results { get; set; } = new();
    public string? RawResponse { get; set; }
}

/// <summary>
/// Сервис назначения меток товарам через LLM (Ollama, YandexGPT и др.)
/// </summary>
public interface ILabelAssignmentService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt
    /// </summary>
    void SetPromptTemplatePath(string path);

    /// <summary>
    /// Установить кастомный prompt напрямую (приоритет над файлом)
    /// </summary>
    void SetCustomPrompt(string? prompt);

    /// <summary>
    /// Назначить метки для товара (одиночный вызов)
    /// </summary>
    /// <param name="itemName">Название товара</param>
    /// <param name="productName">Название продукта (категории)</param>
    /// <param name="availableLabels">Список доступных меток</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LabelAssignmentResult> AssignLabelsAsync(
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Назначить метки для нескольких товаров за один вызов LLM (batch)
    /// </summary>
    /// <param name="items">Список товаров для классификации</param>
    /// <param name="availableLabels">Список доступных меток</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<BatchLabelAssignmentResult> AssignLabelsBatchAsync(
        IReadOnlyList<BatchItemInput> items,
        IReadOnlyList<string> availableLabels,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
