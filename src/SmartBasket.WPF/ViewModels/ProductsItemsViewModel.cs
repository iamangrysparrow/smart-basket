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

    // Label emptiness filter: "Все", "Непустые", "Пустые"
    public ObservableCollection<string> LabelEmptinessFilters { get; } = new() { "Все", "Непустые", "Пустые" };

    [ObservableProperty]
    private string _selectedLabelEmptinessFilter = "Все";

    partial void OnSelectedShopFilterChanged(string value) => LoadItemsCommand.Execute(null);
    partial void OnIncludeChildrenItemsChanged(bool value) => LoadItemsCommand.Execute(null);
    partial void OnItemSearchTextChanged(string value) => LoadItemsCommand.Execute(null);
    partial void OnSelectedLabelEmptinessFilterChanged(string value) => FilterLabels();
    partial void OnMasterSearchTextChanged(string value)
    {
        if (IsProductsMode)
            FilterProductTree();
        else
            FilterLabels();
    }

    #endregion

    #region Products Tree

    public ObservableCollection<ProductTreeItemViewModel> ProductTree { get; } = new();
    private readonly object _productTreeLock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProduct))]
    [NotifyPropertyChangedFor(nameof(CanDeleteProduct))]
    [NotifyPropertyChangedFor(nameof(CanAddChildProduct))]
    private ProductTreeItemViewModel? _selectedProduct;

    public bool CanEditProduct => SelectedProduct != null && !SelectedProduct.IsSpecialNode;

    /// <summary>
    /// Can add child: any product except "All" node
    /// </summary>
    public bool CanAddChildProduct => SelectedProduct != null && !SelectedProduct.IsAllNode;

    /// <summary>
    /// Can delete: must be non-special, have no children and no items
    /// </summary>
    public bool CanDeleteProduct => SelectedProduct != null
                                    && !SelectedProduct.IsSpecialNode
                                    && !SelectedProduct.HasChildren
                                    && SelectedProduct.ItemCount == 0;

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

    #region Product Tree Filtering

    // Full tree for filtering (cached)
    private List<ProductTreeItemViewModel> _fullProductTree = new();

    // Full labels for filtering (cached)
    private List<LabelListItemViewModel> _fullLabels = new();

    private void FilterProductTree()
    {
        var searchText = MasterSearchText?.Trim() ?? string.Empty;

        // Apply search highlighting and expansion to all items
        foreach (var item in _fullProductTree)
        {
            if (item.IsAllNode)
            {
                item.ClearSearch();
            }
            else
            {
                item.ApplySearch(searchText);
            }
        }

        // If no search - just show full tree (highlighting already cleared)
        // If search - show items that match (already expanded via ApplySearch)
        // We don't hide non-matching items because the tree structure is needed
        // Instead we just highlight matching items
    }

    private void FilterLabels()
    {
        var hasSearchFilter = !string.IsNullOrWhiteSpace(MasterSearchText);
        var searchLower = hasSearchFilter ? MasterSearchText.ToLowerInvariant() : "";
        var hasEmptinessFilter = SelectedLabelEmptinessFilter != "Все";

        lock (_labelsLock)
        {
            Labels.Clear();

            foreach (var item in _fullLabels)
            {
                // Special nodes (All, Without Labels) always shown unless emptiness filter
                if (item.IsSpecialNode)
                {
                    // For emptiness filter, hide "All" if filtering
                    if (hasEmptinessFilter && item.IsAllNode)
                        continue;

                    Labels.Add(item);
                    continue;
                }

                // Apply search filter
                if (hasSearchFilter && !item.Name.ToLowerInvariant().Contains(searchLower))
                    continue;

                // Apply emptiness filter
                if (SelectedLabelEmptinessFilter == "Непустые" && item.ItemCount == 0)
                    continue;
                if (SelectedLabelEmptinessFilter == "Пустые" && item.ItemCount > 0)
                    continue;

                Labels.Add(item);
            }
        }
    }

    #endregion

    #region Commands - Load Data

    [RelayCommand]
    private async Task LoadProductTreeAsync()
    {
        await LoadProductTreeAsync(preserveState: false);
    }

    private async Task LoadProductTreeAsync(bool preserveState, Guid? selectProductId = null)
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            // Save current state before reload
            HashSet<Guid> expandedIds = new();
            var selectedId = selectProductId ?? SelectedProduct?.Id;

            if (preserveState)
            {
                foreach (var item in _fullProductTree)
                {
                    item.CollectExpandedIds(expandedIds);
                }
            }

            var products = await _productService.GetAllWithHierarchyAsync();

            lock (_productTreeLock)
            {
                ProductTree.Clear();
                _fullProductTree.Clear();

                // Add "All" node
                var allCount = products.Sum(p => CountItemsRecursive(p));
                var allNode = new ProductTreeItemViewModel
                {
                    Name = "Все",
                    Icon = "📁",
                    ItemCount = allCount,
                    IsSpecialNode = true,
                    IsAllNode = true
                };
                ProductTree.Add(allNode);
                _fullProductTree.Add(allNode);

                // Build tree from root products
                var rootProducts = products.Where(p => p.ParentId == null).OrderBy(p => p.Name);
                foreach (var product in rootProducts)
                {
                    var treeItem = BuildProductTreeItem(product, products);
                    ProductTree.Add(treeItem);
                    _fullProductTree.Add(treeItem);
                }

                // Restore expanded state
                if (preserveState && expandedIds.Count > 0)
                {
                    foreach (var item in _fullProductTree)
                    {
                        item.RestoreExpandedState(expandedIds);
                    }
                }
            }

            // Restore selection
            if (selectedId.HasValue)
            {
                SelectedProduct = FindProductById(ProductTree, selectedId.Value);
            }

            var (totalProducts, totalItems) = await _productService.GetStatisticsAsync();
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
            ParentId = product.ParentId,
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
            var labelsWithCounts = await _labelService.GetAllWithCountsAsync();
            var (_, itemsWithLabels, itemsWithoutLabels) = await _labelService.GetStatisticsAsync();

            lock (_labelsLock)
            {
                Labels.Clear();
                _fullLabels.Clear();

                // Add "All" node
                var allNode = new LabelListItemViewModel
                {
                    Name = "Все",
                    Color = "#808080",
                    ItemCount = itemsWithLabels + itemsWithoutLabels,
                    IsSpecialNode = true,
                    IsAllNode = true
                };
                Labels.Add(allNode);
                _fullLabels.Add(allNode);

                // Add labels
                foreach (var (label, count) in labelsWithCounts)
                {
                    var labelItem = new LabelListItemViewModel
                    {
                        Id = label.Id,
                        Name = label.Name,
                        Color = label.Color ?? "#808080",
                        ItemCount = count
                    };
                    Labels.Add(labelItem);
                    _fullLabels.Add(labelItem);
                }

                // Add "Without labels" node
                var withoutNode = new LabelListItemViewModel
                {
                    Name = "Без метки",
                    Color = "#CCCCCC",
                    ItemCount = itemsWithoutLabels,
                    IsSpecialNode = true,
                    IsWithoutLabelsNode = true
                };
                Labels.Add(withoutNode);
                _fullLabels.Add(withoutNode);
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
                        var descendantIds = await _productService.GetDescendantIdsAsync(SelectedProduct.Id.Value);
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

            var items = await _itemService.GetItemsAsync(filter);

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
                var shops = await _itemService.GetShopsAsync();
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
            await LoadProductTreeAsync(preserveState: true);

            // Ensure selection is valid
            if (SelectedProduct == null && ProductTree.Count > 0)
            {
                SelectedProduct = ProductTree[0]; // Select "All"
            }

            await LoadItemsAsync();
        }
        else
        {
            // Save selected label ID
            var selectedId = SelectedLabel?.Id;
            var wasAllNode = SelectedLabel?.IsAllNode ?? false;
            var wasWithoutLabelsNode = SelectedLabel?.IsWithoutLabelsNode ?? false;

            await LoadLabelsAsync();

            // Restore selection
            if (selectedId.HasValue)
            {
                SelectedLabel = Labels.FirstOrDefault(l => l.Id == selectedId);
            }
            else if (wasAllNode)
            {
                SelectedLabel = Labels.FirstOrDefault(l => l.IsAllNode);
            }
            else if (wasWithoutLabelsNode)
            {
                SelectedLabel = Labels.FirstOrDefault(l => l.IsWithoutLabelsNode);
            }
            else if (Labels.Count > 0)
            {
                SelectedLabel = Labels[0]; // Select "All"
            }

            await LoadItemsAsync();
        }
    }

    private ProductTreeItemViewModel? FindProductById(IEnumerable<ProductTreeItemViewModel> items, Guid id)
    {
        foreach (var item in items)
        {
            if (item.Id == id)
                return item;

            var found = FindProductById(item.Children, id);
            if (found != null)
                return found;
        }
        return null;
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

        var (canDelete, itemsCount, childrenCount) = await _productService.CanDeleteAsync(SelectedProduct.Id.Value);

        if (!canDelete)
        {
            // Will show error in View
            return;
        }

        var (success, _) = await _productService.DeleteAsync(SelectedProduct.Id.Value);

        if (success)
        {
            await LoadProductTreeAsync(preserveState: true);
        }
    }

    public async Task<Product> CreateProductAsync(string name, Guid? parentId)
    {
        var product = await _productService.CreateAsync(name, parentId);
        await LoadProductTreeAsync(preserveState: true, selectProductId: product.Id);
        return product;
    }

    public async Task<Product> UpdateProductAsync(Guid id, string name, Guid? parentId)
    {
        var product = await _productService.UpdateAsync(id, name, parentId);
        await LoadProductTreeAsync(preserveState: true, selectProductId: id);
        return product;
    }

    public async Task<(bool CanDelete, int ItemsCount, int ChildrenCount)> CanDeleteProductAsync(Guid id)
    {
        return await _productService.CanDeleteAsync(id);
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

        await _labelService.DeleteAsync(SelectedLabel.Id.Value);
        await LoadLabelsAsync();
    }

    public async Task<Label> CreateLabelAsync(string name, string color)
    {
        var label = await _labelService.CreateAsync(name, color);
        await LoadLabelsAsync();
        return label;
    }

    public async Task<Label> UpdateLabelAsync(Guid id, string name, string color)
    {
        var label = await _labelService.UpdateAsync(id, name, color);
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
        await _itemService.MoveItemsToProductAsync(itemIds, targetProduct.Id.Value);

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AssignLabelToItemsAsync(LabelListItemViewModel? label)
    {
        if (label?.Id == null || SelectedItems.Count == 0) return;

        var itemIds = SelectedItems.Select(i => i.Id).ToList();
        await _labelService.AssignLabelToItemsAsync(itemIds, label.Id.Value);

        await LoadItemsAsync();
    }

    [RelayCommand]
    private async Task RemoveLabelFromItemsAsync(LabelListItemViewModel? label)
    {
        if (label?.Id == null || SelectedItems.Count == 0) return;

        var itemIds = SelectedItems.Select(i => i.Id).ToList();
        await _labelService.RemoveLabelFromItemsAsync(itemIds, label.Id.Value);

        await LoadItemsAsync();
    }

    [RelayCommand]
    private async Task RemoveAllLabelsFromItemsAsync()
    {
        if (SelectedItems.Count == 0) return;

        foreach (var item in SelectedItems.ToList())
        {
            await _labelService.RemoveAllLabelsFromItemAsync(item.Id);
        }

        await LoadItemsAsync();
    }

    /// <summary>
    /// Updates the product assignment for a single item (from ItemCardDialog)
    /// </summary>
    public async Task UpdateItemProductAsync(Guid itemId, Guid newProductId)
    {
        await _itemService.MoveItemsToProductAsync(new List<Guid> { itemId }, newProductId);
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
