using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

public partial class ItemCardDialog : Window
{
    private Guid _itemId;
    private Guid _originalProductId;
    private ProductItem? _originalProduct;
    private List<ProductItem> _allProducts = new();
    private bool _isUpdatingSelection;
    private bool _isFiltering;
    private string _currentFilterText = string.Empty;
    private TextBox? _editableTextBox;

    public ItemCardDialog()
    {
        InitializeComponent();
        Loaded += ItemCardDialog_Loaded;
    }

    private void ItemCardDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Get reference to the editable TextBox inside ComboBox
        _editableTextBox = ProductComboBox.Template.FindName("PART_EditableTextBox", ProductComboBox) as TextBox;
        if (_editableTextBox != null)
        {
            _editableTextBox.TextChanged += EditableTextBox_TextChanged;
        }
    }

    public Guid ItemId => _itemId;

    public Guid? SelectedProductId
    {
        get => (ProductComboBox.SelectedItem as ProductItem)?.Id;
        private set
        {
            if (value == null) return;

            foreach (var item in _allProducts)
            {
                if (item.Id == value)
                {
                    _isUpdatingSelection = true;
                    ProductComboBox.SelectedItem = item;
                    _isUpdatingSelection = false;
                    break;
                }
            }
        }
    }

    public bool ProductChanged => SelectedProductId != _originalProductId;

    public void SetItem(ItemGridViewModel item)
    {
        _itemId = item.Id;
        _originalProductId = item.ProductId;

        ItemNameText.Text = item.Name;
        ShopText.Text = item.Shop;
        UnitText.Text = item.UnitOfMeasure;
        PurchaseCountText.Text = item.PurchaseCount.ToString();

        // Set labels via code-behind
        LabelsPanel.Children.Clear();

        foreach (var label in item.Labels)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 4),
                Background = ParseColorToBrush(label.Color),
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBlock = new TextBlock
            {
                Text = label.Name,
                FontSize = 10,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = textBlock;
            LabelsPanel.Children.Add(border);
        }
    }

    public void SetAvailableProducts(IList<ProductTreeItemViewModel> products)
    {
        _allProducts.Clear();

        foreach (var product in products)
        {
            if (product.IsSpecialNode) continue;
            AddProductWithChildren(_allProducts, product, 0);
        }

        ProductComboBox.ItemsSource = _allProducts;

        // Find and select original product
        _originalProduct = _allProducts.FirstOrDefault(p => p.Id == _originalProductId);
        if (_originalProduct != null)
        {
            _isUpdatingSelection = true;
            ProductComboBox.SelectedItem = _originalProduct;
            ProductComboBox.Text = _originalProduct.Name;
            _isUpdatingSelection = false;
        }
    }

    public void SetAvailableProductsList(IList<ProductListItemViewModel> products)
    {
        _allProducts.Clear();

        foreach (var product in products)
        {
            if (product.IsSpecialNode || !product.Id.HasValue) continue;
            _allProducts.Add(new ProductItem
            {
                Id = product.Id.Value,
                Name = product.Name,
                DisplayName = product.Name,
                Depth = 0
            });
        }

        ProductComboBox.ItemsSource = _allProducts;

        // Find and select original product
        _originalProduct = _allProducts.FirstOrDefault(p => p.Id == _originalProductId);
        if (_originalProduct != null)
        {
            _isUpdatingSelection = true;
            ProductComboBox.SelectedItem = _originalProduct;
            ProductComboBox.Text = _originalProduct.Name;
            _isUpdatingSelection = false;
        }
    }

    private void AddProductWithChildren(List<ProductItem> items, ProductTreeItemViewModel product, int depth)
    {
        var indent = new string(' ', depth * 3);
        items.Add(new ProductItem
        {
            Id = product.Id ?? Guid.Empty,
            Name = product.Name,
            DisplayName = $"{indent}{product.Name}",
            Depth = depth
        });

        foreach (var child in product.Children)
        {
            AddProductWithChildren(items, child, depth + 1);
        }
    }

    private void EditableTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSelection || _isFiltering) return;

        var searchText = _editableTextBox?.Text?.Trim() ?? string.Empty;
        _currentFilterText = searchText;

        // Show/hide clear button based on whether we have filter text
        UpdateClearButtonVisibility();

        // Filter products
        FilterProducts(searchText);
    }

    private void FilterProducts(string searchText)
    {
        _isFiltering = true;

        try
        {
            if (string.IsNullOrEmpty(searchText))
            {
                // Show all products
                ProductComboBox.ItemsSource = _allProducts;
            }
            else
            {
                // Filter products by name (case-insensitive)
                var filtered = _allProducts
                    .Where(p => p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                ProductComboBox.ItemsSource = filtered;

                // Open dropdown to show filtered results
                if (filtered.Any() && !ProductComboBox.IsDropDownOpen)
                {
                    ProductComboBox.IsDropDownOpen = true;
                }
            }
        }
        finally
        {
            _isFiltering = false;
        }
    }

    private void UpdateClearButtonVisibility()
    {
        var hasFilterText = !string.IsNullOrEmpty(_currentFilterText);
        ClearButton.Visibility = hasFilterText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProductComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Open dropdown when user starts typing
        if (!ProductComboBox.IsDropDownOpen)
        {
            ProductComboBox.IsDropDownOpen = true;
        }
    }

    private void ProductComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection || _isFiltering) return;

        var selected = ProductComboBox.SelectedItem as ProductItem;
        if (selected == null) return;

        _isUpdatingSelection = true;

        // Update text to show selected product name
        ProductComboBox.Text = selected.Name;
        _currentFilterText = string.Empty;

        // Restore full list
        ProductComboBox.ItemsSource = _allProducts;

        // Re-select item in full list
        ProductComboBox.SelectedItem = _allProducts.FirstOrDefault(p => p.Id == selected.Id);

        UpdateClearButtonVisibility();

        _isUpdatingSelection = false;
    }

    private void ProductComboBox_DropDownOpened(object sender, EventArgs e)
    {
        // When dropdown opens, scroll to selected item
        var selected = ProductComboBox.SelectedItem;
        if (selected != null)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                var container = ProductComboBox.ItemContainerGenerator.ContainerFromItem(selected) as ComboBoxItem;
                container?.BringIntoView();
            });
        }
    }

    private void ProductComboBox_DropDownClosed(object sender, EventArgs e)
    {
        // When dropdown closes without selection and there's filter text,
        // don't auto-clear - let user see the text they typed
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        // Clear filter and restore original product
        _isUpdatingSelection = true;

        _currentFilterText = string.Empty;
        ProductComboBox.ItemsSource = _allProducts;

        if (_originalProduct != null)
        {
            ProductComboBox.SelectedItem = _originalProduct;
            ProductComboBox.Text = _originalProduct.Name;
        }
        else
        {
            ProductComboBox.SelectedItem = null;
            ProductComboBox.Text = string.Empty;
        }

        UpdateClearButtonVisibility();
        _isUpdatingSelection = false;

        // Focus back on combo
        ProductComboBox.Focus();
    }

    private static SolidColorBrush ParseColorToBrush(string colorString)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorString);
            return new SolidColorBrush(color);
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ProductComboBox.SelectedItem as ProductItem;

        // Validate: must select an existing product
        if (selected == null || selected.Id == Guid.Empty)
        {
            // Check if user typed something that doesn't match any product
            var typedText = ProductComboBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(typedText))
            {
                // Try to find exact match
                var exactMatch = _allProducts.FirstOrDefault(p =>
                    p.Name.Equals(typedText, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    ProductComboBox.SelectedItem = exactMatch;
                    DialogResult = true;
                    return;
                }

                MessageBox.Show(
                    "Продукт не найден. Выберите существующий продукт из списка.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    "Выберите продукт",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            ProductComboBox.Focus();
            return;
        }

        DialogResult = true;
    }

    public class ProductItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Depth { get; set; }

        // Override ToString for ComboBox text display
        public override string ToString() => Name;
    }

}
