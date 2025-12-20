using System.Windows;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

public partial class ProductDialog : Window
{
    public ProductDialog()
    {
        InitializeComponent();
    }

    public string ProductName
    {
        get => NameTextBox.Text;
        set => NameTextBox.Text = value;
    }

    // Legacy support for old code using SelectedParentId
    public Guid? SelectedParentId
    {
        get => SelectedCategoryId;
        set => SelectedCategoryId = value;
    }

    public Guid? SelectedCategoryId
    {
        get => (CategoryComboBox.SelectedItem as CategoryItem)?.Id;
        set
        {
            if (value == null)
            {
                CategoryComboBox.SelectedIndex = 0;
            }
            else
            {
                foreach (var item in CategoryComboBox.Items.OfType<CategoryItem>())
                {
                    if (item.Id == value)
                    {
                        CategoryComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }
    }

    // Legacy support for old code
    public void SetAvailableParents(IList<ProductTreeItemViewModel> categories)
    {
        SetAvailableCategories(categories);
    }

    public void SetAvailableCategories(IList<ProductTreeItemViewModel> categories)
    {
        var items = new List<CategoryItem>
        {
            new() { Id = null, DisplayName = "(Без категории)" }
        };

        foreach (var category in categories)
        {
            if (category.IsSpecialNode) continue;
            AddCategoryWithChildren(items, category, 0);
        }

        CategoryComboBox.ItemsSource = items;
        CategoryComboBox.SelectedIndex = 0;
    }

    private void AddCategoryWithChildren(List<CategoryItem> items, ProductTreeItemViewModel category, int depth)
    {
        var indent = new string(' ', depth * 3);
        items.Add(new CategoryItem
        {
            Id = category.Id,
            DisplayName = $"{indent}{category.Name}"
        });

        foreach (var child in category.Children)
        {
            AddCategoryWithChildren(items, child, depth + 1);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProductName))
        {
            MessageBox.Show("Введите название продукта", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    public class CategoryItem
    {
        public Guid? Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
