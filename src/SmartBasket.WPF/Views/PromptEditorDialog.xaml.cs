using System.Windows;

namespace SmartBasket.WPF.Views;

/// <summary>
/// Диалог редактирования системного промпта для AI чата
/// </summary>
public partial class PromptEditorDialog : Window
{
    public string PromptText { get; private set; } = string.Empty;

    public PromptEditorDialog(string currentPrompt)
    {
        InitializeComponent();

        PromptTextBox.Text = currentPrompt;
        PromptText = currentPrompt;

        UpdateCharCount();
        PromptTextBox.TextChanged += (s, e) => UpdateCharCount();

        // Focus on text box when loaded
        Loaded += (s, e) =>
        {
            PromptTextBox.Focus();
            PromptTextBox.SelectAll();
        };
    }

    private void UpdateCharCount()
    {
        CharCountText.Text = $"{PromptTextBox.Text.Length} символов";
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        PromptText = PromptTextBox.Text;
        DialogResult = true;
        Close();
    }
}
