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

    public Guid? SelectedParentId
    {
        get => (ParentComboBox.SelectedItem as ParentItem)?.Id;
        set
        {
            if (value == null)
            {
                ParentComboBox.SelectedIndex = 0;
            }
            else
            {
                foreach (var item in ParentComboBox.Items.OfType<ParentItem>())
                {
                    if (item.Id == value)
                    {
                        ParentComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }
    }

    public void SetAvailableParents(IList<ProductTreeItemViewModel> products)
    {
        var items = new List<ParentItem>
        {
            new() { Id = null, DisplayName = "(Нет - корневой)" }
        };

        foreach (var product in products)
        {
            if (product.IsSpecialNode) continue;
            AddProductWithChildren(items, product, 0);
        }

        ParentComboBox.ItemsSource = items;
        ParentComboBox.SelectedIndex = 0;
    }

    private void AddProductWithChildren(List<ParentItem> items, ProductTreeItemViewModel product, int depth)
    {
        var indent = new string(' ', depth * 3);
        items.Add(new ParentItem
        {
            Id = product.Id,
            DisplayName = $"{indent}{product.Name}"
        });

        foreach (var child in product.Children)
        {
            AddProductWithChildren(items, child, depth + 1);
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

    public class ParentItem
    {
        public Guid? Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
