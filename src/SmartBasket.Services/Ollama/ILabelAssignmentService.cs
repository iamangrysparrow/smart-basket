using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Ollama;

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
/// Сервис назначения меток товарам через Ollama
/// </summary>
public interface ILabelAssignmentService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt
    /// </summary>
    void SetPromptTemplatePath(string path);

    /// <summary>
    /// Назначить метки для товара
    /// </summary>
    /// <param name="settings">Настройки Ollama</param>
    /// <param name="itemName">Название товара</param>
    /// <param name="productName">Название продукта (категории)</param>
    /// <param name="availableLabels">Список доступных меток</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LabelAssignmentResult> AssignLabelsAsync(
        OllamaSettings settings,
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
