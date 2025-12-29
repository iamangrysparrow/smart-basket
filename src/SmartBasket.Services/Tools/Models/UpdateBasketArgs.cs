using System.Text.Json.Serialization;

namespace SmartBasket.Services.Tools.Models;

/// <summary>
/// Аргументы для инструмента update_basket.
/// Используется AI для добавления, удаления или изменения товаров в текущем списке покупок.
/// </summary>
public class UpdateBasketArgs
{
    /// <summary>
    /// Список операций над корзиной
    /// </summary>
    [JsonPropertyName("operations")]
    public required List<BasketOperation> Operations { get; set; }
}

/// <summary>
/// Операция над корзиной
/// </summary>
public class BasketOperation
{
    /// <summary>
    /// Тип действия: add, remove, update
    /// </summary>
    [JsonPropertyName("action")]
    public required string Action { get; set; }

    /// <summary>
    /// Название товара
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Количество (для add и update)
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; set; }

    /// <summary>
    /// Единица измерения: шт, кг, л, г, мл
    /// </summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>
    /// Категория товара (для группировки в UI)
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Полный путь категории от корня до листа.
    /// Формат: "Корневая \ Родительская \ Текущая"
    /// </summary>
    [JsonPropertyName("category_path")]
    public string? CategoryPath { get; set; }
}
