namespace SmartBasket.Core.Entities;

/// <summary>
/// Связь многие-ко-многим между Item и Label
/// </summary>
public class ItemLabel
{
    public Guid ItemId { get; set; }
    public Guid LabelId { get; set; }

    // Navigation properties
    public Item? Item { get; set; }
    public Label? Label { get; set; }
}
