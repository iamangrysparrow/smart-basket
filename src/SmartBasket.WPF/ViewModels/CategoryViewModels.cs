using SmartBasket.Core.Entities;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// ViewModel для элемента дерева категорий (продуктов)
/// </summary>
public class CategoryTreeItemViewModel
{
    public Guid? ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public int Count { get; set; }
    public bool IsUncategorized { get; set; }
    public List<CategoryTreeItemViewModel> Items { get; set; } = new();
}

/// <summary>
/// ViewModel для товара (Item) в списке
/// </summary>
public class ItemViewModel
{
    public ItemViewModel() { }

    public ItemViewModel(Item item)
    {
        Id = item.Id;
        Name = item.Name;
        ProductId = item.ProductId;
        ProductName = item.Product?.Name ?? "Не задана";
        UnitOfMeasure = item.UnitOfMeasure;
        UnitQuantity = item.UnitQuantity;
        Shop = item.Shop;
        PurchaseCount = item.ReceiptItems?.Count ?? 0;
        StatusText = item.Product != null ? "✓" : "⚠️";
    }

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "Не задана";
    public string? UnitOfMeasure { get; set; }
    public decimal? UnitQuantity { get; set; }
    public string? Shop { get; set; }
    public int PurchaseCount { get; set; }
    public string StatusText { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel для продукта (группы товаров)
/// </summary>
public class ProductViewModel
{
    public ProductViewModel() { }

    public ProductViewModel(Product product)
    {
        Id = product.Id;
        ParentId = product.ParentId;
        Name = product.Name;
        ItemsCount = product.Items?.Count ?? 0;
    }

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ItemsCount { get; set; }
}

/// <summary>
/// ViewModel для метки (Label)
/// </summary>
public class LabelViewModel
{
    public LabelViewModel() { }

    public LabelViewModel(Label label)
    {
        Id = label.Id;
        Name = label.Name;
        Color = label.Color ?? "#808080";
    }

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#808080";
}
