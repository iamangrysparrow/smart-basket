using System.Windows;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

public partial class CategoryDialog : Window
{
    public CategoryDialog()
    {
        InitializeComponent();
    }

    public string CategoryName
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

    public void SetAvailableParents(IList<ProductTreeItemViewModel> categories)
    {
        var items = new List<ParentItem>
        {
            new() { Id = null, DisplayName = "(Корневая категория)" }
        };

        foreach (var category in categories)
        {
            if (category.IsSpecialNode) continue;
            AddCategoryWithChildren(items, category, 0);
        }

        ParentComboBox.ItemsSource = items;
        ParentComboBox.SelectedIndex = 0;
    }

    private void AddCategoryWithChildren(List<ParentItem> items, ProductTreeItemViewModel category, int depth)
    {
        var indent = new string(' ', depth * 3);
        items.Add(new ParentItem
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
        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            MessageBox.Show("Введите название категории", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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
