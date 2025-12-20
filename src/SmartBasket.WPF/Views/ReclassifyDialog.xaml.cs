using System.Windows;
using System.Windows.Media;
using SmartBasket.Services.Llm;

namespace SmartBasket.WPF.Views;

/// <summary>
/// Диалог реклассификации продуктов с превью промпта
/// </summary>
public partial class ReclassifyDialog : Window
{
    private readonly IProductClassificationService _classificationService;
    private readonly Guid? _categoryId;
    private readonly string _categoryName;
    private readonly List<string> _productNames;
    private CancellationTokenSource? _cts;
    private bool _isProcessing;

    public bool ClassificationApplied { get; private set; }
    public ClassificationApplyResult? Result { get; private set; }

    public ReclassifyDialog(
        IProductClassificationService classificationService,
        Guid? categoryId,
        string categoryName,
        List<string> productNames,
        string preparedPrompt)
    {
        InitializeComponent();

        _classificationService = classificationService;
        _categoryId = categoryId;
        _categoryName = categoryName;
        _productNames = productNames;

        CategoryInfoText.Text = $"Категория: {categoryName} | Продуктов: {productNames.Count}";
        StatsText.Text = $"Продуктов для классификации: {productNames.Count}";
        PromptTextBox.Text = preparedPrompt;

        UpdateCharCount();
        PromptTextBox.TextChanged += (s, e) => UpdateCharCount();

        Loaded += (s, e) => PromptTextBox.Focus();
        Closing += OnClosing;
    }

    private void UpdateCharCount()
    {
        CharCountText.Text = $"{PromptTextBox.Text.Length:N0} символов";
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isProcessing)
        {
            var result = MessageBox.Show(
                "Классификация в процессе. Отменить?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _cts?.Cancel();
            }
            else
            {
                e.Cancel = true;
            }
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;

        _isProcessing = true;
        _cts = new CancellationTokenSource();

        // Update UI
        SendButton.IsEnabled = false;
        PromptTextBox.IsReadOnly = true;
        ProgressPanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        ProgressText.Text = "Отправка запроса к AI...";

        try
        {
            var progress = new Progress<string>(msg =>
            {
                Dispatcher.Invoke(() => ProgressText.Text = msg);
            });

            // Set custom prompt before classification
            _classificationService.SetCustomPrompt(PromptTextBox.Text);

            // Run reclassification for the selected category
            Result = await _classificationService.ReclassifyCategoryAsync(_categoryId, progress, _cts.Token);

            // Show result
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;

            if (Result.IsSuccess)
            {
                ResultPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"));
                ResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
                ResultText.Text = $"Классификация завершена!\n\n" +
                    $"Классифицировано продуктов: {Result.ProductsClassified}\n" +
                    $"Создано категорий: {Result.CategoriesCreated}\n" +
                    $"Удалено пустых: {Result.CategoriesDeleted}\n" +
                    $"Осталось без категории: {Result.ProductsRemaining}";

                ClassificationApplied = true;
                SendButton.Content = "Закрыть";
                SendButton.IsEnabled = true;
                SendButton.Click -= SendButton_Click;
                SendButton.Click += (_, _) => { DialogResult = true; Close(); };
                CancelButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                ResultPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
                ResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"));
                ResultText.Text = $"Ошибка: {Result.Message}";

                SendButton.Content = "Повторить";
                SendButton.IsEnabled = true;
                PromptTextBox.IsReadOnly = false;
            }
        }
        catch (OperationCanceledException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            ResultPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0"));
            ResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"));
            ResultText.Text = "Операция отменена";

            SendButton.Content = "Повторить";
            SendButton.IsEnabled = true;
            PromptTextBox.IsReadOnly = false;
        }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            ResultPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
            ResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"));
            ResultText.Text = $"Ошибка: {ex.Message}";

            SendButton.Content = "Повторить";
            SendButton.IsEnabled = true;
            PromptTextBox.IsReadOnly = false;
        }
        finally
        {
            _isProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
        {
            _cts?.Cancel();
        }
        else
        {
            DialogResult = ClassificationApplied;
            Close();
        }
    }
}
