using System.Windows;

namespace SmartBasket.WPF.Views;

public partial class CategoriesDialog : Window
{
    public CategoriesDialog()
    {
        InitializeComponent();
        CategoriesTextBox.TextChanged += (s, e) => UpdateCategoryCount();
    }

    /// <summary>
    /// Список категорий (по одной на строку)
    /// </summary>
    public string CategoriesText
    {
        get => CategoriesTextBox.Text;
        set
        {
            CategoriesTextBox.Text = value;
            UpdateCategoryCount();
        }
    }

    /// <summary>
    /// Получить список категорий как массив строк
    /// </summary>
    public string[] GetCategories()
    {
        return CategoriesTextBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToArray();
    }

    private void UpdateCategoryCount()
    {
        var count = GetCategories().Length;
        CategoryCount.Text = count.ToString();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var categories = GetCategories();
        if (categories.Length == 0)
        {
            MessageBox.Show(
                "Введите хотя бы одну категорию.",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
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
