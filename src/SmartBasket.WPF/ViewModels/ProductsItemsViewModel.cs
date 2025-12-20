using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SmartBasket.Core.Entities;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Products;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// Mode for left panel display
/// </summary>
public enum ProductsViewMode
{
    /// <summary>
    /// Flat list of products sorted alphabetically
    /// </summary>
    Products,

    /// <summary>
    /// Hierarchical view by categories
    /// </summary>
    ByCategories,

    /// <summary>
    /// Labels view
    /// </summary>
    Labels
}

/// <summary>
/// ViewModel for Products & Items tab
/// </summary>
public partial class ProductsItemsViewModel : ObservableObject
{
    private readonly IProductService _productService;
    public readonly IProductCategoryService _categoryService;
    private readonly ILabelService _labelService;
    private readonly IItemService _itemService;
    public readonly IProductClassificationService _classificationService;

    public ProductsItemsViewModel(
        IProductService productService,
        IProductCategoryService categoryService,
        ILabelService labelService,
        IItemService itemService,
        IProductClassificationService classificationService)
    {
        _productService = productService;
        _categoryService = categoryService;
        _labelService = labelService;
        _itemService = itemService;
        _classificationService = classificationService;
    }

    #region Mode

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProductsMode))]
    [NotifyPropertyChangedFor(nameof(IsCategoriesMode))]
    [NotifyPropertyChangedFor(nameof(IsLabelsMode))]
    [NotifyPropertyChangedFor(nameof(ShowIncludeChildren))]
    [NotifyPropertyChangedFor(nameof(ShowProductsList))]
    [NotifyPropertyChangedFor(nameof(ShowCategoriesTree))]
    [NotifyPropertyChangedFor(nameof(ShowLabelsList))]
    private ProductsViewMode _viewMode = ProductsViewMode.Products;

    public bool IsProductsMode
    {
        get => ViewMode == ProductsViewMode.Products;
        set { if (value) ViewMode = ProductsViewMode.Products; }
    }

    public bool IsCategoriesMode
    {
        get => ViewMode == ProductsViewMode.ByCategories;
        set { if (value) ViewMode = ProductsViewMode.ByCategories; }
    }

    public bool IsLabelsMode
    {
        get => ViewMode == ProductsViewMode.Labels;
        set { if (value) ViewMode = ProductsViewMode.Labels; }
    }

    public bool ShowProductsList => ViewMode == ProductsViewMode.Products;
    public bool ShowCategoriesTree => ViewMode == ProductsViewMode.ByCategories;
    public bool ShowLabelsList => ViewMode == ProductsViewMode.Labels;
    public bool ShowIncludeChildren => ViewMode == ProductsViewMode.ByCategories;

    partial void OnViewModeChanged(ProductsViewMode value)
    {
        switch (value)
        {
            case ProductsViewMode.Products:
                LoadProductListCommand.Execute(null);
                break;
            case ProductsViewMode.ByCategories:
                LoadCategoryTreeCommand.Execute(null);
                break;
            case ProductsViewMode.Labels:
                LoadLabelsCommand.Execute(null);
                break;
        }
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
        switch (ViewMode)
        {
            case ProductsViewMode.Products:
                FilterProductList();
                break;
            case ProductsViewMode.ByCategories:
                FilterCategoryTree();
                break;
            case ProductsViewMode.Labels:
                FilterLabels();
                break;
        }
    }

    #endregion

    #region Products List (flat)

    public ObservableCollection<ProductListItemViewModel> ProductList { get; } = new();
    private readonly object _productListLock = new();
    private List<ProductListItemViewModel> _fullProductList = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProduct))]
    [NotifyPropertyChangedFor(nameof(CanDeleteProduct))]
    private ProductListItemViewModel? _selectedProductItem;

    public bool CanEditProduct => SelectedProductItem != null && !SelectedProductItem.IsSpecialNode;
    public bool CanDeleteProduct => SelectedProductItem != null
                                    && !SelectedProductItem.IsSpecialNode
                                    && SelectedProductItem.ItemCount == 0;

    partial void OnSelectedProductItemChanged(ProductListItemViewModel? value)
    {
        if (!_skipAutoLoad && IsProductsMode && value != null)
            LoadItemsCommand.Execute(null);
    }

    #endregion

    #region Categories Tree

    public ObservableCollection<ProductTreeItemViewModel> CategoryTree { get; } = new();
    private readonly object _categoryTreeLock = new();
    private List<ProductTreeItemViewModel> _fullCategoryTree = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditCategory))]
    [NotifyPropertyChangedFor(nameof(CanDeleteCategory))]
    [NotifyPropertyChangedFor(nameof(CanAddChildCategory))]
    private ProductTreeItemViewModel? _selectedCategory;

    public bool CanEditCategory => SelectedCategory != null && !SelectedCategory.IsSpecialNode;
    public bool CanAddChildCategory => SelectedCategory != null && !SelectedCategory.IsAllNode;
    public bool CanDeleteCategory => SelectedCategory != null
                                    && !SelectedCategory.IsSpecialNode
                                    && !SelectedCategory.HasChildren
                                    && SelectedCategory.ItemCount == 0;

    partial void OnSelectedCategoryChanged(ProductTreeItemViewModel? value)
    {
        if (!_skipAutoLoad && IsCategoriesMode && value != null)
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
        if (!_skipAutoLoad && IsLabelsMode && value != null)
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
    private int _totalCategoriesCount;

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

    /// <summary>
    /// Flag to skip auto-loading items when selection changes during refresh
    /// </summary>
    private bool _skipAutoLoad;

    [ObservableProperty]
    private string _statusText = string.Empty;

    #endregion

    #region Collection Synchronization

    public void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(ProductList, _productListLock);
        BindingOperations.EnableCollectionSynchronization(CategoryTree, _categoryTreeLock);
        BindingOperations.EnableCollectionSynchronization(Labels, _labelsLock);
        BindingOperations.EnableCollectionSynchronization(Items, _itemsLock);
        BindingOperations.EnableCollectionSynchronization(ShopFilters, _shopFiltersLock);
        BindingOperations.EnableCollectionSynchronization(SelectedItems, _selectedItemsLock);
    }

    #endregion

    #region Filtering

    private List<LabelListItemViewModel> _fullLabels = new();

    private void FilterProductList()
    {
        var searchText = MasterSearchText?.Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(searchText);

        lock (_productListLock)
        {
            ProductList.Clear();

            foreach (var item in _fullProductList)
            {
                if (item.IsSpecialNode)
                {
                    ProductList.Add(item);
                    continue;
                }

                if (hasSearch && !item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    continue;

                ProductList.Add(item);
            }
        }
    }

    private void FilterCategoryTree()
    {
        var searchText = MasterSearchText?.Trim() ?? string.Empty;

        foreach (var item in _fullCategoryTree)
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
                if (item.IsSpecialNode)
                {
                    if (hasEmptinessFilter && item.IsAllNode)
                        continue;

                    Labels.Add(item);
                    continue;
                }

                if (hasSearchFilter && !item.Name.ToLowerInvariant().Contains(searchLower))
                    continue;

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
    private async Task LoadProductListAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var products = await _productService.GetAllSortedAsync();

            lock (_productListLock)
            {
                ProductList.Clear();
                _fullProductList.Clear();

                // Add "All" node
                var allCount = products.Sum(p => p.Items?.Count ?? 0);
                var allNode = new ProductListItemViewModel
                {
                    Name = "Все",
                    ItemCount = allCount,
                    IsSpecialNode = true,
                    IsAllNode = true
                };
                ProductList.Add(allNode);
                _fullProductList.Add(allNode);

                // Add products
                foreach (var product in products)
                {
                    var item = new ProductListItemViewModel
                    {
                        Id = product.Id,
                        Name = product.Name,
                        ItemCount = product.Items?.Count ?? 0,
                        CategoryName = product.Category?.Name
                    };
                    ProductList.Add(item);
                    _fullProductList.Add(item);
                }
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

    [RelayCommand]
    private async Task LoadCategoryTreeAsync()
    {
        await LoadCategoryTreeAsync(preserveState: false);
    }

    private async Task LoadCategoryTreeAsync(bool preserveState, Guid? selectCategoryId = null)
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            HashSet<Guid> expandedIds = new();
            var selectedId = selectCategoryId ?? SelectedCategory?.Id;

            if (preserveState)
            {
                foreach (var item in _fullCategoryTree)
                {
                    item.CollectExpandedIds(expandedIds);
                }
            }

            var categories = await _categoryService.GetAllWithHierarchyAsync();
            var products = await _productService.GetAllAsync();

            // Group products by category
            var productsByCategory = products
                .Where(p => p.CategoryId.HasValue)
                .GroupBy(p => p.CategoryId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Products without category
            var uncategorizedProducts = products.Where(p => !p.CategoryId.HasValue).ToList();

            lock (_categoryTreeLock)
            {
                CategoryTree.Clear();
                _fullCategoryTree.Clear();

                // Add "All" node
                var allCount = products.Sum(p => p.Items?.Count ?? 0);
                var allNode = new ProductTreeItemViewModel
                {
                    Name = "Все",
                    Icon = "📁",
                    ItemCount = allCount,
                    IsSpecialNode = true,
                    IsAllNode = true
                };
                CategoryTree.Add(allNode);
                _fullCategoryTree.Add(allNode);

                // Build tree from root categories
                var rootCategories = categories.Where(c => c.ParentId == null).OrderBy(c => c.Name);
                foreach (var category in rootCategories)
                {
                    var treeItem = BuildCategoryTreeItem(category, categories, productsByCategory);
                    CategoryTree.Add(treeItem);
                    _fullCategoryTree.Add(treeItem);
                }

                // Add uncategorized products node if any
                if (uncategorizedProducts.Count > 0)
                {
                    var uncategorizedNode = new ProductTreeItemViewModel
                    {
                        Name = "Без категории",
                        Icon = "📁",
                        ItemCount = uncategorizedProducts.Sum(p => p.Items?.Count ?? 0),
                        IsSpecialNode = true,
                        IsProduct = false
                    };

                    // Add uncategorized products as children
                    foreach (var product in uncategorizedProducts.OrderBy(p => p.Name))
                    {
                        var productNode = new ProductTreeItemViewModel
                        {
                            Id = product.Id,
                            Name = product.Name,
                            Icon = "📦",
                            ItemCount = product.Items?.Count ?? 0,
                            IsProduct = true
                        };
                        uncategorizedNode.Children.Add(productNode);
                    }

                    CategoryTree.Add(uncategorizedNode);
                    _fullCategoryTree.Add(uncategorizedNode);
                }

                // Restore expanded state
                if (preserveState && expandedIds.Count > 0)
                {
                    foreach (var item in _fullCategoryTree)
                    {
                        item.RestoreExpandedState(expandedIds);
                    }
                }
            }

            // Restore selection
            if (selectedId.HasValue)
            {
                SelectedCategory = FindCategoryById(CategoryTree, selectedId.Value);
            }

            var (totalCategories, totalProducts, totalItems) = await _categoryService.GetStatisticsAsync();
            TotalCategoriesCount = totalCategories;
            TotalProductsCount = totalProducts;
            TotalItemsCount = totalItems;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ProductTreeItemViewModel BuildCategoryTreeItem(
        ProductCategory category,
        IReadOnlyList<ProductCategory> allCategories,
        Dictionary<Guid, List<Product>> productsByCategory)
    {
        var itemCount = 0;
        List<Product>? categoryProducts = null;
        if (productsByCategory.TryGetValue(category.Id, out categoryProducts))
        {
            itemCount = categoryProducts.Sum(p => p.Items?.Count ?? 0);
        }

        var item = new ProductTreeItemViewModel
        {
            Id = category.Id,
            ParentId = category.ParentId,
            Name = category.Name,
            Icon = "📁",
            ItemCount = itemCount,
            IsProduct = false
        };

        // Add child categories first
        var childCategories = allCategories.Where(c => c.ParentId == category.Id).OrderBy(c => c.Name);
        foreach (var child in childCategories)
        {
            var childItem = BuildCategoryTreeItem(child, allCategories, productsByCategory);
            item.Children.Add(childItem);
            item.ItemCount += childItem.ItemCount;
        }

        // Add products as leaf nodes
        if (categoryProducts != null)
        {
            foreach (var product in categoryProducts.OrderBy(p => p.Name))
            {
                var productNode = new ProductTreeItemViewModel
                {
                    Id = product.Id,
                    ParentId = category.Id,
                    Name = product.Name,
                    Icon = "📦",
                    ItemCount = product.Items?.Count ?? 0,
                    IsProduct = true
                };
                item.Children.Add(productNode);
            }
        }

        return item;
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

            switch (ViewMode)
            {
                case ProductsViewMode.Products:
                    if (SelectedProductItem != null)
                    {
                        if (SelectedProductItem.IsAllNode)
                        {
                            // All items - no filter
                        }
                        else if (SelectedProductItem.Id.HasValue)
                        {
                            filter.ProductId = SelectedProductItem.Id.Value;
                        }
                    }
                    break;

                case ProductsViewMode.ByCategories:
                    if (SelectedCategory != null)
                    {
                        if (SelectedCategory.IsAllNode)
                        {
                            // All items
                        }
                        else if (SelectedCategory.IsProduct && SelectedCategory.Id.HasValue)
                        {
                            // Selected a product node - filter by ProductId
                            filter.ProductId = SelectedCategory.Id.Value;
                        }
                        else if (SelectedCategory.Name == "Без категории")
                        {
                            filter.WithoutCategory = true;
                        }
                        else if (SelectedCategory.Id.HasValue)
                        {
                            // Selected a category - filter by CategoryId
                            if (IncludeChildrenItems)
                            {
                                var descendantIds = await _categoryService.GetDescendantIdsAsync(SelectedCategory.Id.Value);
                                filter.CategoryIds = descendantIds.Append(SelectedCategory.Id.Value).ToList();
                            }
                            else
                            {
                                filter.CategoryId = SelectedCategory.Id.Value;
                            }
                        }
                    }
                    break;

                case ProductsViewMode.Labels:
                    if (SelectedLabel != null)
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
                    break;
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
        // Prevent concurrent refresh
        if (IsBusy) return;

        switch (ViewMode)
        {
            case ProductsViewMode.Products:
                var selectedProductId = SelectedProductItem?.Id;
                var wasProductAllNode = SelectedProductItem?.IsAllNode ?? false;

                // Temporarily disable auto-load on selection change
                _skipAutoLoad = true;
                await LoadProductListAsync();

                if (selectedProductId.HasValue)
                    SelectedProductItem = ProductList.FirstOrDefault(p => p.Id == selectedProductId);
                else if (wasProductAllNode)
                    SelectedProductItem = ProductList.FirstOrDefault(p => p.IsAllNode);
                else if (ProductList.Count > 0)
                    SelectedProductItem = ProductList[0];

                _skipAutoLoad = false;
                await LoadItemsAsync();
                break;

            case ProductsViewMode.ByCategories:
                // Temporarily disable auto-load on selection change
                _skipAutoLoad = true;
                await LoadCategoryTreeAsync(preserveState: true);

                if (SelectedCategory == null && CategoryTree.Count > 0)
                    SelectedCategory = CategoryTree[0];

                _skipAutoLoad = false;
                await LoadItemsAsync();
                break;

            case ProductsViewMode.Labels:
                var selectedLabelId = SelectedLabel?.Id;
                var wasAllNode = SelectedLabel?.IsAllNode ?? false;
                var wasWithoutLabelsNode = SelectedLabel?.IsWithoutLabelsNode ?? false;

                // Temporarily disable auto-load on selection change
                _skipAutoLoad = true;
                await LoadLabelsAsync();

                if (selectedLabelId.HasValue)
                    SelectedLabel = Labels.FirstOrDefault(l => l.Id == selectedLabelId);
                else if (wasAllNode)
                    SelectedLabel = Labels.FirstOrDefault(l => l.IsAllNode);
                else if (wasWithoutLabelsNode)
                    SelectedLabel = Labels.FirstOrDefault(l => l.IsWithoutLabelsNode);
                else if (Labels.Count > 0)
                    SelectedLabel = Labels[0];

                _skipAutoLoad = false;
                await LoadItemsAsync();
                break;
        }
    }

    private ProductTreeItemViewModel? FindCategoryById(IEnumerable<ProductTreeItemViewModel> items, Guid id)
    {
        foreach (var item in items)
        {
            if (item.Id == id)
                return item;

            var found = FindCategoryById(item.Children, id);
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
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task EditProductAsync()
    {
        if (SelectedProductItem == null || SelectedProductItem.IsSpecialNode) return;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteProductAsync()
    {
        if (SelectedProductItem?.Id == null || SelectedProductItem.IsSpecialNode) return;

        var (canDelete, itemsCount) = await _productService.CanDeleteAsync(SelectedProductItem.Id.Value);

        if (!canDelete)
            return;

        var (success, _) = await _productService.DeleteAsync(SelectedProductItem.Id.Value);

        if (success)
        {
            await LoadProductListAsync();
        }
    }

    public async Task<Product> CreateProductAsync(string name, Guid? categoryId)
    {
        var product = await _productService.CreateAsync(name, categoryId);
        await RefreshAsync();
        return product;
    }

    public async Task<Product> UpdateProductAsync(Guid id, string name, Guid? categoryId)
    {
        var product = await _productService.UpdateAsync(id, name, categoryId);
        await RefreshAsync();
        return product;
    }

    public async Task<(bool CanDelete, int ItemsCount)> CanDeleteProductAsync(Guid id)
    {
        return await _productService.CanDeleteAsync(id);
    }

    #endregion

    #region Commands - Category CRUD

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task EditCategoryAsync()
    {
        if (SelectedCategory == null || SelectedCategory.IsSpecialNode) return;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory?.Id == null || SelectedCategory.IsSpecialNode) return;

        var (canDelete, productsCount, childrenCount) = await _categoryService.CanDeleteAsync(SelectedCategory.Id.Value);

        if (!canDelete)
            return;

        var (success, _) = await _categoryService.DeleteAsync(SelectedCategory.Id.Value);

        if (success)
        {
            await LoadCategoryTreeAsync(preserveState: true);
        }
    }

    public async Task<ProductCategory> CreateCategoryAsync(string name, Guid? parentId)
    {
        var category = await _categoryService.CreateAsync(name, parentId);
        await LoadCategoryTreeAsync(preserveState: true, selectCategoryId: category.Id);
        return category;
    }

    public async Task<ProductCategory> UpdateCategoryAsync(Guid id, string name, Guid? parentId)
    {
        var category = await _categoryService.UpdateAsync(id, name, parentId);
        await LoadCategoryTreeAsync(preserveState: true, selectCategoryId: id);
        return category;
    }

    #endregion

    #region Commands - Label CRUD

    [RelayCommand]
    private async Task AddLabelAsync()
    {
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task EditLabelAsync()
    {
        if (SelectedLabel == null || SelectedLabel.IsSpecialNode) return;
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
    private async Task MoveItemsToProductAsync(ProductListItemViewModel? targetProduct)
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

    public async Task UpdateItemProductAsync(Guid itemId, Guid newProductId)
    {
        await _itemService.MoveItemsToProductAsync(new List<Guid> { itemId }, newProductId);
        await LoadItemsAsync();
    }

    #endregion

    #region Shop Filters

    /// <summary>
    /// Load shop filters for combobox
    /// </summary>
    private async Task LoadShopFiltersAsync()
    {
        var shops = await _itemService.GetShopsAsync();

        lock (_shopFiltersLock)
        {
            var currentSelection = SelectedShopFilter;
            ShopFilters.Clear();
            ShopFilters.Add(string.Empty); // "All" option

            foreach (var shop in shops.Where(s => !string.IsNullOrEmpty(s)))
            {
                ShopFilters.Add(shop!);
            }

            // Restore selection if still valid
            if (!string.IsNullOrEmpty(currentSelection) && ShopFilters.Contains(currentSelection))
            {
                SelectedShopFilter = currentSelection;
            }
            else
            {
                SelectedShopFilter = string.Empty;
            }
        }
    }

    #endregion

    #region Initialization

    public async Task InitializeAsync()
    {
        await LoadProductListAsync();
        await LoadShopFiltersAsync();

        if (ProductList.Count > 0)
        {
            SelectedProductItem = ProductList[0];
        }
    }

    #endregion
}

/// <summary>
/// ViewModel for flat product list item
/// </summary>
public partial class ProductListItemViewModel : ObservableObject
{
    public Guid? Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private string? _categoryName;

    public bool IsSpecialNode { get; set; }
    public bool IsAllNode { get; set; }
}
