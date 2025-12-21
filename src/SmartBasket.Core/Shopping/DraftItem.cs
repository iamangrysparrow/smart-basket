namespace SmartBasket.Core.Shopping;

/// <summary>
/// Товар в черновике списка покупок (этап 1).
/// Может быть добавлен из анализа чеков или вручную через чат.
/// </summary>
public class DraftItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Название товара (как ввёл пользователь или AI)
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Количество
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Единица измерения: шт, кг, л, г, мл
    /// </summary>
    public string Unit { get; set; } = "шт";

    /// <summary>
    /// Категория товара (для группировки в UI)
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Примечание пользователя
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Источник добавления
    /// </summary>
    public DraftItemSource Source { get; set; } = DraftItemSource.Manual;

    /// <summary>
    /// Ссылка на Item из БД (если товар найден в истории)
    /// </summary>
    public Guid? OriginalItemId { get; set; }
}

/// <summary>
/// Источник добавления товара в черновик
/// </summary>
public enum DraftItemSource
{
    /// <summary>
    /// Добавлен на основе анализа чеков
    /// </summary>
    FromReceipts,

    /// <summary>
    /// Добавлен вручную через чат
    /// </summary>
    Manual
}
