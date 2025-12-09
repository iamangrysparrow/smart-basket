using System.Windows;

namespace SmartBasket.WPF.Views;

public partial class ChangeCategoryDialog : Window
{
    public ChangeCategoryDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Название товарной позиции
    /// </summary>
    public string ItemName
    {
        get => ItemNameText.Text;
        set => ItemNameText.Text = value;
    }

    /// <summary>
    /// Текущая категория (или null если не задана)
    /// </summary>
    public string? CurrentCategory
    {
        get => CurrentCategoryText.Text == "Не задана" ? null : CurrentCategoryText.Text;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                CurrentCategoryText.Text = "Не задана";
                CurrentCategoryText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            else
            {
                CurrentCategoryText.Text = value;
                CurrentCategoryText.Foreground = System.Windows.Media.Brushes.Green;
            }
        }
    }

    /// <summary>
    /// Список доступных категорий для выбора
    /// </summary>
    public void SetAvailableCategories(IEnumerable<string> categories)
    {
        CategoryComboBox.ItemsSource = categories.ToList();
        if (CategoryComboBox.Items.Count > 0)
        {
            CategoryComboBox.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Выбранная/введённая категория
    /// </summary>
    public string? SelectedCategory { get; private set; }

    /// <summary>
    /// True если создаётся новая категория
    /// </summary>
    public bool IsNewCategory { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectExistingRadio.IsChecked == true)
        {
            if (CategoryComboBox.SelectedItem == null)
            {
                MessageBox.Show("Выберите категорию из списка.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedCategory = CategoryComboBox.SelectedItem.ToString();
            IsNewCategory = false;
        }
        else
        {
            var newCategory = NewCategoryTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newCategory))
            {
                MessageBox.Show("Введите название новой категории.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedCategory = newCategory;
            IsNewCategory = true;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
