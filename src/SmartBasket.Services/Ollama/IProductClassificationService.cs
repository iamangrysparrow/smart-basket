namespace SmartBasket.Services.Ollama;

/// <summary>
/// Существующий продукт из БД для передачи в классификацию
/// </summary>
public class ExistingProduct
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }
}

/// <summary>
/// Сервис классификации товаров по продуктам через LLM (Ollama, YandexGPT и др.)
/// </summary>
public interface IProductClassificationService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt для классификации
    /// </summary>
    void SetPromptTemplatePath(string path);

    /// <summary>
    /// Классифицировать товары из чека по продуктам
    /// </summary>
    /// <param name="itemNames">Названия товаров для классификации</param>
    /// <param name="existingProducts">Существующая иерархия продуктов из БД</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат классификации с продуктами и привязками товаров</returns>
    Task<ProductClassificationResult> ClassifyAsync(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<ExistingProduct> existingProducts,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
