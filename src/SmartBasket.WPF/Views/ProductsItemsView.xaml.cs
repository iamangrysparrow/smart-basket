using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartBasket.Core.Configuration;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

public partial class ProductsItemsView : UserControl
{
    private DateTime _lastClickTime;
    private ProductTreeItemViewModel? _lastClickedItem;
    private ProductsItemsViewModel? _viewModel;

    public ProductsItemsView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as ProductsItemsViewModel;
        if (_viewModel == null) return;

        _viewModel.EnableCollectionSynchronization();

        // Rebuild context menus when collections change
        _viewModel.Labels.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(BuildContextMenus);
        _viewModel.ProductList.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(BuildContextMenus);
        _viewModel.CategoryTree.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(BuildContextMenus);

        await _viewModel.InitializeAsync();

        BuildContextMenus();
    }

    private void ProductList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ProductListItemViewModel selectedProduct)
        {
            _viewModel.SelectedProductItem = selectedProduct;
        }
    }

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_viewModel == null) return;

        if (e.NewValue is ProductTreeItemViewModel selectedCategory)
        {
            _viewModel.SelectedCategory = selectedCategory;
        }
    }

    private void LabelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is LabelListItemViewModel selectedLabel)
        {
            _viewModel.SelectedLabel = selectedLabel;
        }
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.MasterSearchText = string.Empty;
        }
    }

    private void ItemsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.SelectedItems.Clear();
        foreach (var item in ItemsDataGrid.SelectedItems.OfType<ItemGridViewModel>())
        {
            _viewModel.SelectedItems.Add(item);
        }
        _viewModel.SelectedItemsCount = _viewModel.SelectedItems.Count;
    }

    private async void ItemsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel == null) return;

        var item = ItemsDataGrid.SelectedItem as ItemGridViewModel;
        if (item == null) return;

        await ShowItemCardDialogAsync(item);
    }

    private async Task ShowItemCardDialogAsync(ItemGridViewModel item)
    {
        if (_viewModel == null) return;

        var dialog = new ItemCardDialog
        {
            Owner = Window.GetWindow(this)
        };

        dialog.SetItem(item);
        // Pass flat product list for selection
        dialog.SetAvailableProductsList(_viewModel.ProductList.Where(p => !p.IsSpecialNode).ToList());

        if (dialog.ShowDialog() == true && dialog.ProductChanged && dialog.SelectedProductId.HasValue)
        {
            await _viewModel.UpdateItemProductAsync(item.Id, dialog.SelectedProductId.Value);
        }
    }

    private void BuildContextMenus()
    {
        if (_viewModel == null) return;

        // Build "Move to Product" submenu (flat list)
        MoveToProductMenuItem.Items.Clear();
        foreach (var product in _viewModel.ProductList)
        {
            if (product.IsSpecialNode) continue;

            var menuItem = new MenuItem
            {
                Header = product.Name,
                Tag = product
            };
            menuItem.Click += (s, e) =>
            {
                if (s is MenuItem mi && mi.Tag is ProductListItemViewModel prod)
                {
                    _viewModel.MoveItemsToProductCommand.Execute(prod);
                }
                e.Handled = true;
            };
            MoveToProductMenuItem.Items.Add(menuItem);
        }

        // Build "Assign Label" submenu
        AssignLabelMenuItem.Items.Clear();
        foreach (var label in _viewModel.Labels)
        {
            if (label.IsSpecialNode) continue;

            var menuItem = new MenuItem
            {
                Header = label.Name,
                Tag = label,
                Icon = CreateColorCircle(label.Color)
            };
            menuItem.Click += (s, e) =>
            {
                if (s is MenuItem mi && mi.Tag is LabelListItemViewModel lbl)
                {
                    _viewModel.AssignLabelToItemsCommand.Execute(lbl);
                }
            };
            AssignLabelMenuItem.Items.Add(menuItem);
        }
    }

    private static FrameworkElement CreateColorCircle(string color)
    {
        return new System.Windows.Shapes.Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color))
        };
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (_viewModel.ViewMode)
        {
            case ProductsViewMode.Products:
                await ShowProductDialogAsync(isEdit: false);
                break;
            case ProductsViewMode.ByCategories:
                await ShowCategoryDialogAsync(isEdit: false);
                break;
            case ProductsViewMode.Labels:
                await ShowLabelDialogAsync(isEdit: false);
                break;
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (_viewModel.ViewMode)
        {
            case ProductsViewMode.Products when _viewModel.CanEditProduct:
                await ShowProductDialogAsync(isEdit: true);
                break;
            case ProductsViewMode.ByCategories when _viewModel.CanEditCategory:
                await ShowCategoryDialogAsync(isEdit: true);
                break;
            case ProductsViewMode.Labels when _viewModel.CanEditLabel:
                await ShowLabelDialogAsync(isEdit: true);
                break;
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (_viewModel.ViewMode)
        {
            case ProductsViewMode.Products when _viewModel.SelectedProductItem?.Id != null:
                await DeleteSelectedProductAsync();
                break;
            case ProductsViewMode.ByCategories when _viewModel.SelectedCategory?.Id != null:
                await DeleteSelectedCategoryAsync();
                break;
            case ProductsViewMode.Labels when _viewModel.SelectedLabel?.Id != null:
                await DeleteSelectedLabelAsync();
                break;
        }
    }

    private async Task DeleteSelectedProductAsync()
    {
        if (_viewModel?.SelectedProductItem?.Id == null) return;

        var (canDelete, itemsCount) = await _viewModel.CanDeleteProductAsync(_viewModel.SelectedProductItem.Id.Value);

        if (!canDelete)
        {
            MessageBox.Show(
                $"Нельзя удалить продукт \"{_viewModel.SelectedProductItem.Name}\":\n\n" +
                $"Связано товаров: {itemsCount}\n\n" +
                "Сначала переместите товары в другой продукт.",
                "Удаление невозможно",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Удалить продукт \"{_viewModel.SelectedProductItem.Name}\"?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteProductCommand.ExecuteAsync(null);
            BuildContextMenus();
        }
    }

    private async Task DeleteSelectedCategoryAsync()
    {
        if (_viewModel?.SelectedCategory?.Id == null) return;

        var (canDelete, productsCount, childrenCount) = await _viewModel._categoryService.CanDeleteAsync(_viewModel.SelectedCategory.Id.Value);

        if (!canDelete)
        {
            MessageBox.Show(
                $"Нельзя удалить категорию \"{_viewModel.SelectedCategory.Name}\":\n\n" +
                $"- Продуктов: {productsCount}\n" +
                $"- Дочерних категорий: {childrenCount}\n\n" +
                "Сначала переместите продукты и удалите дочерние категории.",
                "Удаление невозможно",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Удалить категорию \"{_viewModel.SelectedCategory.Name}\"?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteCategoryCommand.ExecuteAsync(null);
        }
    }

    private async Task DeleteSelectedLabelAsync()
    {
        if (_viewModel?.SelectedLabel?.Id == null) return;

        var itemCount = _viewModel.SelectedLabel.ItemCount;
        var message = itemCount > 0
            ? $"Метка \"{_viewModel.SelectedLabel.Name}\" назначена на {itemCount} товаров.\n\n" +
              "При удалении метка будет снята со всех товаров."
            : $"Удалить метку \"{_viewModel.SelectedLabel.Name}\"?";

        var result = MessageBox.Show(
            message,
            "Удалить метку?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteLabelCommand.ExecuteAsync(null);
        }
    }

    private async Task ShowProductDialogAsync(bool isEdit)
    {
        if (_viewModel == null) return;

        var dialog = new ProductDialog
        {
            Owner = Window.GetWindow(this)
        };

        // Set up available categories (flat list for combo)
        dialog.SetAvailableCategories(_viewModel.CategoryTree.Where(c => !c.IsSpecialNode && c.Name != "Без категории").ToList());

        if (isEdit && _viewModel.SelectedProductItem?.Id != null)
        {
            dialog.Title = "Редактировать продукт";
            dialog.ProductName = _viewModel.SelectedProductItem.Name;
            // Would need to get category ID from service
        }
        else
        {
            dialog.Title = "Создать продукт";
        }

        if (dialog.ShowDialog() == true)
        {
            if (isEdit && _viewModel.SelectedProductItem?.Id != null)
            {
                await _viewModel.UpdateProductAsync(
                    _viewModel.SelectedProductItem.Id.Value,
                    dialog.ProductName,
                    dialog.SelectedCategoryId);
            }
            else
            {
                await _viewModel.CreateProductAsync(dialog.ProductName, dialog.SelectedCategoryId);
            }

            BuildContextMenus();
        }
    }

    private async Task ShowCategoryDialogAsync(bool isEdit)
    {
        if (_viewModel == null) return;

        var dialog = new CategoryDialog
        {
            Owner = Window.GetWindow(this)
        };

        dialog.SetAvailableParents(_viewModel.CategoryTree.Where(c => !c.IsSpecialNode && c.Name != "Без категории").ToList());

        if (isEdit && _viewModel.SelectedCategory?.Id != null)
        {
            dialog.Title = "Редактировать категорию";
            dialog.CategoryName = _viewModel.SelectedCategory.Name;
            dialog.SelectedParentId = _viewModel.SelectedCategory.ParentId;
        }
        else
        {
            dialog.Title = "Создать категорию";
            if (_viewModel.SelectedCategory?.Id != null && !_viewModel.SelectedCategory.IsSpecialNode)
            {
                dialog.SelectedParentId = _viewModel.SelectedCategory.Id;
            }
        }

        if (dialog.ShowDialog() == true)
        {
            if (isEdit && _viewModel.SelectedCategory?.Id != null)
            {
                await _viewModel.UpdateCategoryAsync(
                    _viewModel.SelectedCategory.Id.Value,
                    dialog.CategoryName,
                    dialog.SelectedParentId);
            }
            else
            {
                await _viewModel.CreateCategoryAsync(dialog.CategoryName, dialog.SelectedParentId);
            }
        }
    }

    private async Task ShowLabelDialogAsync(bool isEdit)
    {
        if (_viewModel == null) return;

        var dialog = new LabelDialog
        {
            Owner = Window.GetWindow(this)
        };

        if (isEdit && _viewModel.SelectedLabel?.Id != null)
        {
            dialog.Title = "Редактировать метку";
            dialog.LabelName = _viewModel.SelectedLabel.Name;
            dialog.SelectedColor = _viewModel.SelectedLabel.Color;
        }
        else
        {
            dialog.Title = "Создать метку";
        }

        if (dialog.ShowDialog() == true)
        {
            if (isEdit && _viewModel.SelectedLabel?.Id != null)
            {
                await _viewModel.UpdateLabelAsync(
                    _viewModel.SelectedLabel.Id.Value,
                    dialog.LabelName,
                    dialog.SelectedColor);
            }
            else
            {
                await _viewModel.CreateLabelAsync(dialog.LabelName, dialog.SelectedColor);
            }

            BuildContextMenus();
        }
    }

    #region Category Tree Inline Editing

    private void CategoryItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ProductTreeItemViewModel category)
            return;

        if (category.IsSpecialNode) return;

        var now = DateTime.Now;
        if (_lastClickedItem == category && (now - _lastClickTime).TotalMilliseconds < 500)
        {
            category.StartEdit();
            e.Handled = true;
        }

        _lastClickedItem = category;
        _lastClickTime = now;
    }

    private async void EditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not ProductTreeItemViewModel category)
            return;

        if (e.Key == Key.Enter)
        {
            await SaveCategoryEditAsync(category);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            category.CancelEdit();
            e.Handled = true;
        }
    }

    private async void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not ProductTreeItemViewModel category)
            return;

        if (category.IsEditing)
        {
            await SaveCategoryEditAsync(category);
        }
    }

    private void EditTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private async Task SaveCategoryEditAsync(ProductTreeItemViewModel category)
    {
        if (_viewModel == null || !category.IsEditing) return;

        var newName = category.EditName?.Trim();
        var categoryId = category.Id;

        if (string.IsNullOrEmpty(newName))
        {
            category.CancelEdit();
            return;
        }

        if (newName != category.Name && categoryId.HasValue)
        {
            await _viewModel.UpdateCategoryAsync(categoryId.Value, newName, category.ParentId);
        }

        category.CancelEdit();
    }

    #endregion

    #region Reclassify

    private async void ReclassifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Get selected category (or null for "Без категории" or root)
        Guid? categoryId = null;
        string categoryName = "Все продукты";

        if (_viewModel.SelectedCategory != null)
        {
            if (_viewModel.SelectedCategory.IsProduct)
            {
                MessageBox.Show(
                    "Выберите категорию, а не продукт.\n\nРеклассификация применяется ко всем продуктам выбранной категории.",
                    "Выберите категорию",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_viewModel.SelectedCategory.Name == "Без категории")
            {
                categoryId = null;
                categoryName = "Без категории";
            }
            else if (_viewModel.SelectedCategory.Id.HasValue)
            {
                categoryId = _viewModel.SelectedCategory.Id;
                categoryName = _viewModel.SelectedCategory.Name;
            }
        }

        // Get product names for the category
        var productNames = await _viewModel._classificationService.GetCategoryProductNamesAsync(categoryId);

        if (productNames.Count == 0)
        {
            MessageBox.Show(
                $"В категории \"{categoryName}\" нет продуктов для реклассификации.",
                "Нет продуктов",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Load custom prompt from AI Operations settings
        var appSettings = App.Services.GetRequiredService<AppSettings>();
        var ops = appSettings.AiOperations;
        string? customPrompt = null;

        if (!string.IsNullOrWhiteSpace(ops.Classification))
        {
            customPrompt = ops.GetCustomPrompt("Classification", ops.Classification);
        }

        // Set custom prompt before building prompt for preview
        _viewModel._classificationService.SetCustomPrompt(customPrompt);

        // Build prompt for preview
        var prompt = await _viewModel._classificationService.BuildPromptForProductsAsync(productNames);

        // Show dialog
        var dialog = new ReclassifyDialog(
            _viewModel._classificationService,
            categoryId,
            categoryName,
            productNames,
            prompt)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && dialog.ClassificationApplied)
        {
            // Refresh the view
            await _viewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    #endregion
}
