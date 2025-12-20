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
        CategoryId = product.CategoryId;
        Name = product.Name;
        ItemsCount = product.Items?.Count ?? 0;
    }

    public Guid Id { get; set; }
    public Guid? CategoryId { get; set; }
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
    public Guid? ParentId { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = "📦";

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>
    /// Current search text for highlighting
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// True if this item matches the search (for highlighting)
    /// </summary>
    [ObservableProperty]
    private bool _isMatching;

    /// <summary>
    /// True if this item should be visible (matches or has matching children)
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    public bool IsSpecialNode { get; set; }
    public bool IsAllNode { get; set; }

    /// <summary>
    /// True if this is a product (leaf node), false if category
    /// </summary>
    public bool IsProduct { get; set; }

    public ObservableCollection<ProductTreeItemViewModel> Children { get; } = new();

    /// <summary>
    /// True if this item has children (for styling parent items differently)
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    public void StartEdit()
    {
        EditName = Name;
        IsEditing = true;
    }

    public void CancelEdit()
    {
        IsEditing = false;
        EditName = string.Empty;
    }

    /// <summary>
    /// Apply search - set IsMatching, IsVisible, and SearchText recursively.
    /// Returns true if this item or any child matches.
    /// </summary>
    public bool ApplySearch(string searchText)
    {
        SearchText = searchText;
        var hasMatch = false;

        // Check if this item matches
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            IsMatching = Name.Contains(searchText, StringComparison.OrdinalIgnoreCase);
            hasMatch = IsMatching;
        }
        else
        {
            IsMatching = false;
        }

        // Apply to children recursively
        foreach (var child in Children)
        {
            if (child.ApplySearch(searchText))
            {
                hasMatch = true;
            }
        }

        // Set visibility: visible if matches or has matching descendants
        // When no search text - everything is visible
        IsVisible = string.IsNullOrWhiteSpace(searchText) || hasMatch;

        // Expand if any child matches (to show matching descendants)
        if (hasMatch && !string.IsNullOrWhiteSpace(searchText))
        {
            IsExpanded = true;
        }

        return hasMatch;
    }

    /// <summary>
    /// Clear search highlighting recursively
    /// </summary>
    public void ClearSearch()
    {
        SearchText = string.Empty;
        IsMatching = false;
        IsVisible = true;
        foreach (var child in Children)
        {
            child.ClearSearch();
        }
    }

    /// <summary>
    /// Collect expanded node IDs recursively
    /// </summary>
    public void CollectExpandedIds(HashSet<Guid> expandedIds)
    {
        if (IsExpanded && Id.HasValue)
        {
            expandedIds.Add(Id.Value);
        }
        foreach (var child in Children)
        {
            child.CollectExpandedIds(expandedIds);
        }
    }

    /// <summary>
    /// Restore expanded state from saved IDs recursively
    /// </summary>
    public void RestoreExpandedState(HashSet<Guid> expandedIds)
    {
        if (Id.HasValue && expandedIds.Contains(Id.Value))
        {
            IsExpanded = true;
        }
        foreach (var child in Children)
        {
            child.RestoreExpandedState(expandedIds);
        }
    }
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
