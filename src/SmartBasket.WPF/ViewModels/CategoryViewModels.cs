using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Core.Entities;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// ViewModel для элемента дерева категорий (продуктов) - legacy
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
/// ViewModel для товара (Item) в списке - legacy
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
/// ViewModel для продукта (группы товаров) - legacy
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
/// ViewModel для метки (Label) - legacy
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

#region New ViewModels for ProductsItemsView

/// <summary>
/// ViewModel for product tree item with hierarchy support
/// </summary>
public partial class ProductTreeItemViewModel : ObservableObject
{
    public Guid? Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = "📦";

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private bool _isExpanded;

    public bool IsSpecialNode { get; set; }
    public bool IsAllNode { get; set; }

    public ObservableCollection<ProductTreeItemViewModel> Children { get; } = new();
}

/// <summary>
/// ViewModel for label list item
/// </summary>
public partial class LabelListItemViewModel : ObservableObject
{
    public Guid? Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _color = "#808080";

    [ObservableProperty]
    private int _itemCount;

    public bool IsSpecialNode { get; set; }
    public bool IsAllNode { get; set; }
    public bool IsWithoutLabelsNode { get; set; }
}

/// <summary>
/// ViewModel for item in DataGrid
/// </summary>
public partial class ItemGridViewModel : ObservableObject
{
    public ItemGridViewModel() { }

    public ItemGridViewModel(Item item)
    {
        Id = item.Id;
        Name = item.Name;
        ProductId = item.ProductId;
        ProductName = item.Product?.Name ?? "—";
        UnitOfMeasure = item.UnitOfMeasure ?? "шт";
        Shop = item.Shop ?? "—";
        PurchaseCount = item.ReceiptItems?.Count ?? 0;

        // Labels
        if (item.ItemLabels != null)
        {
            foreach (var il in item.ItemLabels.Where(il => il.Label != null))
            {
                Labels.Add(new LabelViewModel(il.Label!));
            }
        }
    }

    public Guid Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    public Guid ProductId { get; set; }

    [ObservableProperty]
    private string _productName = "—";

    [ObservableProperty]
    private string _unitOfMeasure = "шт";

    [ObservableProperty]
    private string _shop = "—";

    [ObservableProperty]
    private int _purchaseCount;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<LabelViewModel> Labels { get; } = new();
}

#endregion
