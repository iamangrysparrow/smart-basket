namespace SmartBasket.Core.Entities;

/// <summary>
/// Связь многие-ко-многим между Product и Label
/// </summary>
public class ProductLabel
{
    public Guid ProductId { get; set; }
    public Guid LabelId { get; set; }

    // Navigation properties
    public Product? Product { get; set; }
    public Label? Label { get; set; }
}
