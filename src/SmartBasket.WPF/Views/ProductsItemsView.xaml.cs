using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        _viewModel.ProductTree.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(BuildContextMenus);

        await _viewModel.InitializeAsync();

        BuildContextMenus();
    }

    private void ProductTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_viewModel == null) return;

        if (e.NewValue is ProductTreeItemViewModel selectedProduct)
        {
            _viewModel.SelectedProduct = selectedProduct;
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

    private void BuildContextMenus()
    {
        if (_viewModel == null) return;

        // Build "Move to Product" submenu
        MoveToProductMenuItem.Items.Clear();
        foreach (var product in _viewModel.ProductTree)
        {
            if (product.IsSpecialNode && !product.IsAllNode) continue;
            AddProductMenuItem(MoveToProductMenuItem, product);
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

    private void AddProductMenuItem(MenuItem parent, ProductTreeItemViewModel product, int depth = 0)
    {
        var indent = new string(' ', depth * 2);
        var menuItem = new MenuItem
        {
            Header = indent + product.Name,
            Tag = product
        };
        menuItem.Click += (s, e) =>
        {
            if (s is MenuItem mi && mi.Tag is ProductTreeItemViewModel prod && prod.Id.HasValue)
            {
                _viewModel?.MoveItemsToProductCommand.Execute(prod);
            }
            e.Handled = true;
        };
        parent.Items.Add(menuItem);

        foreach (var child in product.Children)
        {
            AddProductMenuItem(parent, child, depth + 1);
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

        if (_viewModel.IsProductsMode)
        {
            await ShowProductDialogAsync(isEdit: false);
        }
        else
        {
            await ShowLabelDialogAsync(isEdit: false);
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        if (_viewModel.IsProductsMode && _viewModel.CanEditProduct)
        {
            await ShowProductDialogAsync(isEdit: true);
        }
        else if (_viewModel.IsLabelsMode && _viewModel.CanEditLabel)
        {
            await ShowLabelDialogAsync(isEdit: true);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        if (_viewModel.IsProductsMode && _viewModel.SelectedProduct?.Id != null)
        {
            var (canDelete, itemsCount, childrenCount) = await _viewModel.CanDeleteProductAsync(
                _viewModel.SelectedProduct.Id.Value);

            if (!canDelete)
            {
                MessageBox.Show(
                    $"Нельзя удалить продукт \"{_viewModel.SelectedProduct.Name}\":\n\n" +
                    $"- Связано товаров: {itemsCount}\n" +
                    $"- Дочерних продуктов: {childrenCount}\n\n" +
                    "Сначала переместите товары и удалите дочерние продукты.",
                    "Удаление невозможно",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Удалить продукт \"{_viewModel.SelectedProduct.Name}\"?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _viewModel.DeleteProductCommand.ExecuteAsync(null);
            }
        }
        else if (_viewModel.IsLabelsMode && _viewModel.SelectedLabel?.Id != null)
        {
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
    }

    private async Task ShowProductDialogAsync(bool isEdit)
    {
        if (_viewModel == null) return;

        var dialog = new ProductDialog
        {
            Owner = Window.GetWindow(this)
        };

        // Set up available parents
        dialog.SetAvailableParents(_viewModel.ProductTree.ToList());

        if (isEdit && _viewModel.SelectedProduct?.Id != null)
        {
            dialog.Title = "Редактировать продукт";
            dialog.ProductName = _viewModel.SelectedProduct.Name;
            // Find parent ID from tree (would need additional logic)
        }
        else
        {
            dialog.Title = "Создать продукт";
            // If product selected, offer to create as child
            if (_viewModel.SelectedProduct?.Id != null)
            {
                dialog.SelectedParentId = _viewModel.SelectedProduct.Id;
            }
        }

        if (dialog.ShowDialog() == true)
        {
            if (isEdit && _viewModel.SelectedProduct?.Id != null)
            {
                await _viewModel.UpdateProductAsync(
                    _viewModel.SelectedProduct.Id.Value,
                    dialog.ProductName,
                    dialog.SelectedParentId);
            }
            else
            {
                await _viewModel.CreateProductAsync(dialog.ProductName, dialog.SelectedParentId);
            }

            BuildContextMenus();
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

    #region Inline Product Editing

    private async void AddProductInline_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Add as sibling (same parent) of selected product
        Guid? parentId = _viewModel.SelectedProduct?.ParentId;

        await _viewModel.CreateProductAsync("Новый продукт", parentId);
        BuildContextMenus();
    }

    private async void AddChildProductInline_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.SelectedProduct == null) return;

        // Can't add child to special node
        if (_viewModel.SelectedProduct.IsSpecialNode) return;

        // Add as child of selected product
        Guid? parentId = _viewModel.SelectedProduct.Id;

        var newProduct = await _viewModel.CreateProductAsync("Новый продукт", parentId);

        // Expand parent to show new child
        _viewModel.SelectedProduct.IsExpanded = true;

        BuildContextMenus();
    }

    private void RenameProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedProduct == null || _viewModel.SelectedProduct.IsSpecialNode) return;

        _viewModel.SelectedProduct.StartEdit();
    }

    private async void DeleteProductContext_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Reuse the existing delete logic
        if (_viewModel.IsProductsMode && _viewModel.SelectedProduct?.Id != null)
        {
            var (canDelete, itemsCount, childrenCount) = await _viewModel.CanDeleteProductAsync(
                _viewModel.SelectedProduct.Id.Value);

            if (!canDelete)
            {
                MessageBox.Show(
                    $"Нельзя удалить продукт \"{_viewModel.SelectedProduct.Name}\":\n\n" +
                    $"- Связано товаров: {itemsCount}\n" +
                    $"- Дочерних продуктов: {childrenCount}\n\n" +
                    "Сначала переместите товары и удалите дочерние продукты.",
                    "Удаление невозможно",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Удалить продукт \"{_viewModel.SelectedProduct.Name}\"?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _viewModel.DeleteProductCommand.ExecuteAsync(null);
                BuildContextMenus();
            }
        }
    }

    private void ProductItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ProductTreeItemViewModel product)
            return;

        // Ignore special nodes
        if (product.IsSpecialNode) return;

        // Double-click detection
        var now = DateTime.Now;
        if (_lastClickedItem == product && (now - _lastClickTime).TotalMilliseconds < 500)
        {
            // Double-click - start editing
            product.StartEdit();
            e.Handled = true;
        }

        _lastClickedItem = product;
        _lastClickTime = now;
    }

    private async void EditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not ProductTreeItemViewModel product)
            return;

        if (e.Key == Key.Enter)
        {
            await SaveProductEditAsync(product);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            product.CancelEdit();
            e.Handled = true;
        }
    }

    private async void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not ProductTreeItemViewModel product)
            return;

        if (product.IsEditing)
        {
            await SaveProductEditAsync(product);
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

    private async Task SaveProductEditAsync(ProductTreeItemViewModel product)
    {
        if (_viewModel == null || !product.IsEditing) return;

        var newName = product.EditName?.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            product.CancelEdit();
            return;
        }

        if (newName != product.Name && product.Id.HasValue)
        {
            await _viewModel.UpdateProductAsync(product.Id.Value, newName, product.ParentId);
        }

        product.CancelEdit();
        BuildContextMenus();
    }

    #endregion
}
