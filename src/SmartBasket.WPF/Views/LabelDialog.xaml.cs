using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SmartBasket.WPF.Views;

public partial class LabelDialog : Window
{
    private static readonly string[] PresetColors =
    {
        "#3498db", // Blue
        "#2ecc71", // Green
        "#f1c40f", // Yellow
        "#e67e22", // Orange
        "#e74c3c", // Red
        "#9b59b6", // Purple
        "#2c3e50", // Dark
        "#95a5a6", // Gray
        "#8b4513", // Brown
        "#ff69b4"  // Pink
    };

    private Border? _selectedColorBorder;

    public LabelDialog()
    {
        InitializeComponent();
        BuildColorPalette();
    }

    public string LabelName
    {
        get => NameTextBox.Text;
        set => NameTextBox.Text = value;
    }

    public string SelectedColor { get; set; } = PresetColors[0];

    private void BuildColorPalette()
    {
        foreach (var color in PresetColors)
        {
            var ellipse = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Cursor = Cursors.Hand
            };

            var border = new Border
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Child = ellipse,
                Tag = color
            };

            border.MouseLeftButtonUp += ColorBorder_Click;

            ColorPalette.Children.Add(border);

            // Select first color by default
            if (color == SelectedColor)
            {
                SelectColor(border);
            }
        }
    }

    private void ColorBorder_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border)
        {
            SelectColor(border);
        }
    }

    private void SelectColor(Border border)
    {
        // Deselect previous
        if (_selectedColorBorder != null)
        {
            _selectedColorBorder.BorderBrush = Brushes.Transparent;
        }

        // Select new
        _selectedColorBorder = border;
        _selectedColorBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
        SelectedColor = (string)border.Tag;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LabelName))
        {
            MessageBox.Show("Введите название метки", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
