namespace SmartBasket.Core.Entities;

/// <summary>
/// Кэш категоризации для быстрого сопоставления товаров
/// </summary>
public class CategorizationCache : BaseEntity
{
    /// <summary>
    /// Сырое название товара
    /// </summary>
    public required string RawItemName { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>
    /// Уверенность категоризации (0-100)
    /// </summary>
    public decimal? Confidence { get; set; }

    /// <summary>
    /// Способ категоризации: exact, fuzzy, ollama, manual
    /// </summary>
    public string CategorizedBy { get; set; } = "manual";

    // Navigation properties
    public Product? Product { get; set; }
}
