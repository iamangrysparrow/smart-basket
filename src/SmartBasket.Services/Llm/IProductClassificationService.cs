namespace SmartBasket.Services.Llm;

/// <summary>
/// Существующая категория из БД для передачи в классификацию
/// </summary>
public class ExistingCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ParentName { get; set; }
}

/// <summary>
/// Результат применения классификации к БД
/// </summary>
public class ClassificationApplyResult
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public int ProductsClassified { get; set; }
    public int CategoriesCreated { get; set; }
    public int CategoriesDeleted { get; set; }
    public int ProductsRemaining { get; set; }
}

/// <summary>
/// Сервис классификации продуктов по иерархии через LLM.
/// </summary>
public interface IProductClassificationService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt для классификации
    /// </summary>
    void SetPromptTemplatePath(string path);

    /// <summary>
    /// Установить кастомный prompt напрямую (приоритет над файлом)
    /// </summary>
    void SetCustomPrompt(string? prompt);

    /// <summary>
    /// Классифицировать продукты, построив иерархию категорий.
    /// </summary>
    /// <param name="productNames">Названия продуктов для классификации</param>
    /// <param name="existingCategories">Существующая иерархия категорий (без продуктов)</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат классификации с категориями и продуктами</returns>
    Task<ProductClassificationResult> ClassifyAsync(
        IReadOnlyList<string> productNames,
        IReadOnlyList<ExistingCategory> existingCategories,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Классифицировать все некатегоризированные продукты и применить результат к БД.
    /// Вызывается после загрузки чеков.
    /// </summary>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат применения классификации</returns>
    Task<ClassificationApplyResult> ClassifyAndApplyAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Построить промпт для классификации указанных продуктов.
    /// Используется для предпросмотра в диалоге реклассификации.
    /// </summary>
    /// <param name="productNames">Названия продуктов для классификации</param>
    /// <returns>Готовый промпт для отправки в LLM</returns>
    Task<string> BuildPromptForProductsAsync(IReadOnlyList<string> productNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Реклассифицировать продукты конкретной категории (включая все дочерние).
    /// </summary>
    /// <param name="categoryId">ID корневой категории (null для "Без категории")</param>
    /// <param name="progress">Отчёт о прогрессе</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат применения классификации</returns>
    Task<ClassificationApplyResult> ReclassifyCategoryAsync(
        Guid? categoryId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить список продуктов категории и всех её потомков для реклассификации.
    /// </summary>
    Task<List<string>> GetCategoryProductNamesAsync(Guid? categoryId, CancellationToken cancellationToken = default);
}
