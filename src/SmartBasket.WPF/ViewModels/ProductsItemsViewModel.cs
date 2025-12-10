using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SmartBasket.Core.Entities;
using SmartBasket.Services.Products;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// ViewModel for Products & Items tab
/// </summary>
public partial class ProductsItemsViewModel : ObservableObject
{
    private readonly IProductService _productService;
    private readonly ILabelService _labelService;
    private readonly IItemService _itemService;

    public ProductsItemsViewModel(
        IProductService productService,
        ILabelService labelService,
        IItemService itemService)
    {
        _productService = productService;
        _labelService = labelService;
        _itemService = itemService;
    }

    #region Mode

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLabelsMode))]
    [NotifyPropertyChangedFor(nameof(ShowIncludeChildren))]
    private bool _isProductsMode = true;

    public bool IsLabelsMode => !IsProductsMode;
    public bool ShowIncludeChildren => IsProductsMode;

    partial void OnIsProductsModeChanged(bool value)
    {
        if (value)
            LoadProductTreeCommand.Execute(null);
        else
            LoadLabelsCommand.Execute(null);
    }

    #endregion

    #region Filters

    public ObservableCollection<string> ShopFilters { get; } = new() { "Все" };
    private readonly object _shopFiltersLock = new();

    [ObservableProperty]
    private string _selectedShopFilter = "Все";

    [ObservableProperty]
    private bool _includeChildrenItems = true;

    [ObservableProperty]
    private string _masterSearchText = string.Empty;

    [ObservableProperty]
    private string _itemSearchText = string.Empty;

    partial void OnSelectedShopFilterChanged(string value) => LoadItemsCommand.Execute(null);
    partial void OnIncludeChildrenItemsChanged(bool value) => LoadItemsCommand.Execute(null);
    partial void OnItemSearchTextChanged(string value) => LoadItemsCommand.Execute(null);

    #endregion

    #region Products Tree

    public ObservableCollection<ProductTreeItemViewModel> ProductTree { get; } = new();
    private readonly object _productTreeLock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProduct))]
    [NotifyPropertyChangedFor(nameof(CanDeleteProduct))]
    private ProductTreeItemViewModel? _selectedProduct;

    public bool CanEditProduct => SelectedProduct != null && !SelectedProduct.IsSpecialNode;
    public bool CanDeleteProduct => SelectedProduct != null && !SelectedProduct.IsSpecialNode;

    partial void OnSelectedProductChanged(ProductTreeItemViewModel? value)
    {
        if (IsProductsMode && value != null)
            LoadItemsCommand.Execute(null);
    }

    #endregion

    #region Labels List

    public ObservableCollection<LabelListItemViewModel> Labels { get; } = new();
    private readonly object _labelsLock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditLabel))]
    [NotifyPropertyChangedFor(nameof(CanDeleteLabel))]
    private LabelListItemViewModel? _selectedLabel;

    public bool CanEditLabel => SelectedLabel != null && !SelectedLabel.IsSpecialNode;
    public bool CanDeleteLabel => SelectedLabel != null && !SelectedLabel.IsSpecialNode;

    partial void OnSelectedLabelChanged(LabelListItemViewModel? value)
    {
        if (IsLabelsMode && value != null)
            LoadItemsCommand.Execute(null);
    }

    #endregion

    #region Items Grid

    public ObservableCollection<ItemGridViewModel> Items { get; } = new();
    private readonly object _itemsLock = new();

    [ObservableProperty]
    private ItemGridViewModel? _selectedItem;

    public ObservableCollection<ItemGridViewModel> SelectedItems { get; } = new();
    private readonly object _selectedItemsLock = new();

    [ObservableProperty]
    private int _selectedItemsCount;

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalProductsCount;

    [ObservableProperty]
    private int _totalItemsCount;

    [ObservableProperty]
    private int _totalLabelsCount;

    [ObservableProperty]
    private int _itemsWithLabelsCount;

    [ObservableProperty]
    private int _itemsWithoutLabelsCount;

    #endregion

    #region State

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    #endregion

    #region Collection Synchronization

    public void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(ProductTree, _productTreeLock);
        BindingOperations.EnableCollectionSynchronization(Labels, _labelsLock);
        BindingOperations.EnableCollectionSynchronization(Items, _itemsLock);
        BindingOperations.EnableCollectionSynchronization(ShopFilters, _shopFiltersLock);
        BindingOperations.EnableCollectionSynchronization(SelectedItems, _selectedItemsLock);
    }

    #endregion

    #region Commands - Load Data

    [RelayCommand]
    private async Task LoadProductTreeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var products = await Task.Run(() => _productService.GetAllWithHierarchyAsync());

            lock (_productTreeLock)
            {
                ProductTree.Clear();

                // Add "All" node
                var allCount = products.Sum(p => CountItemsRecursive(p));
                ProductTree.Add(new ProductTreeItemViewModel
                {
                    Name = "Все",
                    Icon = "📁",
                    ItemCount = allCount,
                    IsSpecialNode = true,
                    IsAllNode = true
                });

                // Build tree from root products
                var rootProducts = products.Where(p => p.ParentId == null).OrderBy(p => p.Name);
                foreach (var product in rootProducts)
                {
                    ProductTree.Add(BuildProductTreeItem(product, products));
                }
            }

            var (totalProducts, totalItems) = await Task.Run(() => _productService.GetStatisticsAsync());
            TotalProductsCount = totalProducts;
            TotalItemsCount = totalItems;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ProductTreeItemViewModel BuildProductTreeItem(Product product, IReadOnlyList<Product> allProducts)
    {
        var item = new ProductTreeItemViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Icon = "📦",
            ItemCount = product.Items?.Count ?? 0
        };

        var children = allProducts.Where(p => p.ParentId == product.Id).OrderBy(p => p.Name);
        foreach (var child in children)
        {
            item.Children.Add(BuildProductTreeItem(child, allProducts));
            item.ItemCount += CountItemsRecursive(child);
        }

        return item;
    }

    private int CountItemsRecursive(Product product)
    {
        var count = product.Items?.Count ?? 0;
        if (product.Children != null)
        {
            foreach (var child in product.Children)
            {
                count += CountItemsRecursive(child);
            }
        }
        return count;
    }

    [RelayCommand]
    private async Task LoadLabelsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var labelsWithCounts = await Task.Run(() => _labelService.GetAllWithCountsAsync());
            var (_, itemsWithLabels, itemsWithoutLabels) = await Task.Run(() => _labelService.GetStatisticsAsync());

            lock (_labelsLock)
            {
                Labels.Clear();

                // Add "All" node
                Labels.Add(new LabelListItemViewModel
                {
                    Name = "Все",
                    Color = "#808080",
                    ItemCount = itemsWithLabels + itemsWithoutLabels,
                    IsSpecialNode = true,
                    IsAllNode = true
                });

                // Add labels
                foreach (var (label, count) in labelsWithCounts)
                {
                    Labels.Add(new LabelListItemViewModel
                    {
                        Id = label.Id,
                        Name = label.Name,
                        Color = label.Color ?? "#808080",
                        ItemCount = count
                    });
                }

                // Add "Without labels" node
                Labels.Add(new LabelListItemViewModel
                {
                    Name = "Без метки",
                    Color = "#CCCCCC",
                    ItemCount = itemsWithoutLabels,
                    IsSpecialNode = true,
                    IsWithoutLabelsNode = true
                });
            }

            TotalLabelsCount = labelsWithCounts.Count;
            ItemsWithLabelsCount = itemsWithLabels;
            ItemsWithoutLabelsCount = itemsWithoutLabels;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadItemsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var filter = new ItemFilter
            {
                Shop = SelectedShopFilter,
                SearchText = ItemSearchText,
                Take = 500
            };

            if (IsProductsMode && SelectedProduct != null)
            {
                if (SelectedProduct.IsAllNode)
                {
                    // All items - no product filter
                }
                else if (SelectedProduct.Id.HasValue)
                {
                    if (IncludeChildrenItems)
                    {
                        var descendantIds = await Task.Run(() =>
                            _productService.GetDescendantIdsAsync(SelectedProduct.Id.Value));
                        filter.ProductIds = descendantIds.Append(SelectedProduct.Id.Value).ToList();
                    }
                    else
                    {
                        filter.ProductId = SelectedProduct.Id.Value;
                    }
                }
            }
            else if (IsLabelsMode && SelectedLabel != null)
            {
                if (SelectedLabel.IsAllNode)
                {
                    // All items
                }
                else if (SelectedLabel.IsWithoutLabelsNode)
                {
                    filter.WithoutLabels = true;
                }
                else if (SelectedLabel.Id.HasValue)
                {
                    filter.LabelId = SelectedLabel.Id.Value;
                }
            }

            var items = await Task.Run(() => _itemService.GetItemsAsync(filter));

            lock (_itemsLock)
            {
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(new ItemGridViewModel(item));
                }
            }

            // Load shop filters if needed
            if (ShopFilters.Count <= 1)
            {
                var shops = await Task.Run(() => _itemService.GetShopsAsync());
                lock (_shopFiltersLock)
                {
                    ShopFilters.Clear();
                    ShopFilters.Add("Все");
                    foreach (var shop in shops)
                    {
                        ShopFilters.Add(shop);
                    }
                }
            }

            StatusText = $"Показано: {items.Count}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsProductsMode)
        {
            await LoadProductTreeAsync();
            await LoadItemsAsync();
        }
        else
        {
            await LoadLabelsAsync();
            await LoadItemsAsync();
        }
    }

    #endregion

    #region Commands - Product CRUD

    [RelayCommand]
    private async Task AddProductAsync()
    {
        // Will be handled by dialog in View
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task EditProductAsync()
    {
        if (SelectedProduct == null || SelectedProduct.IsSpecialNode) return;
        // Will be handled by dialog in View
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteProductAsync()
    {
        if (SelectedProduct?.Id == null || SelectedProduct.IsSpecialNode) return;

        var (canDelete, itemsCount, childrenCount) = await Task.Run(() =>
            _productService.CanDeleteAsync(SelectedProduct.Id.Value));

        if (!canDelete)
        {
            // Will show error in View
            return;
        }

        var (success, _) = await Task.Run(() =>
            _productService.DeleteAsync(SelectedProduct.Id.Value));

        if (success)
        {
            await LoadProductTreeAsync();
        }
    }

    public async Task<Product> CreateProductAsync(string name, Guid? parentId)
    {
        var product = await Task.Run(() => _productService.CreateAsync(name, parentId));
        await LoadProductTreeAsync();
        return product;
    }

    public async Task<Product> UpdateProductAsync(Guid id, string name, Guid? parentId)
    {
        var product = await Task.Run(() => _productService.UpdateAsync(id, name, parentId));
        await LoadProductTreeAsync();
        return product;
    }

    public async Task<(bool CanDelete, int ItemsCount, int ChildrenCount)> CanDeleteProductAsync(Guid id)
    {
        return await Task.Run(() => _productService.CanDeleteAsync(id));
    }

    #endregion

    #region Commands - Label CRUD

    [RelayCommand]
    private async Task AddLabelAsync()
    {
        // Will be handled by dialog in View
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task EditLabelAsync()
    {
        if (SelectedLabel == null || SelectedLabel.IsSpecialNode) return;
        // Will be handled by dialog in View
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteLabelAsync()
    {
        if (SelectedLabel?.Id == null || SelectedLabel.IsSpecialNode) return;

        await Task.Run(() => _labelService.DeleteAsync(SelectedLabel.Id.Value));
        await LoadLabelsAsync();
    }

    public async Task<Label> CreateLabelAsync(string name, string color)
    {
        var label = await Task.Run(() => _labelService.CreateAsync(name, color));
        await LoadLabelsAsync();
        return label;
    }

    public async Task<Label> UpdateLabelAsync(Guid id, string name, string color)
    {
        var label = await Task.Run(() => _labelService.UpdateAsync(id, name, color));
        await LoadLabelsAsync();
        return label;
    }

    #endregion

    #region Commands - Item Actions

    [RelayCommand]
    private async Task MoveItemsToProductAsync(ProductTreeItemViewModel? targetProduct)
    {
        if (targetProduct?.Id == null || SelectedItems.Count == 0) return;

        var itemIds = SelectedItems.Select(i => i.Id).ToList();
        await Task.Run(() => _itemService.MoveItemsToProductAsync(itemIds, targetProduct.Id.Value));

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AssignLabelToItemsAsync(LabelListItemViewModel? label)
    {
        if (label?.Id == null || SelectedItems.Count == 0) return;

        var itemIds = SelectedItems.Select(i => i.Id).ToList();
        await Task.Run(() => _labelService.AssignLabelToItemsAsync(itemIds, label.Id.Value));

        await LoadItemsAsync();
    }

    [RelayCommand]
    private async Task RemoveLabelFromItemsAsync(LabelListItemViewModel? label)
    {
        if (label?.Id == null || SelectedItems.Count == 0) return;

        var itemIds = SelectedItems.Select(i => i.Id).ToList();
        await Task.Run(() => _labelService.RemoveLabelFromItemsAsync(itemIds, label.Id.Value));

        await LoadItemsAsync();
    }

    [RelayCommand]
    private async Task RemoveAllLabelsFromItemsAsync()
    {
        if (SelectedItems.Count == 0) return;

        foreach (var item in SelectedItems.ToList())
        {
            await Task.Run(() => _labelService.RemoveAllLabelsFromItemAsync(item.Id));
        }

        await LoadItemsAsync();
    }

    #endregion

    #region Initialization

    public async Task InitializeAsync()
    {
        await LoadProductTreeAsync();

        // Select "All" by default
        if (ProductTree.Count > 0)
        {
            SelectedProduct = ProductTree[0];
        }
    }

    #endregion
}
