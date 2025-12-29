using System.Windows;
using System.Windows.Controls;
using SmartBasket.WPF.ViewModels.Settings;

namespace SmartBasket.WPF.Views.Settings;

public partial class AiOperationsSettingsView : UserControl
{
    public AiOperationsSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdatePromptIndicators();
    }

    private SettingsViewModel? SettingsViewModel => DataContext as SettingsViewModel;

    private void EditProductExtractionPrompt_Click(object sender, RoutedEventArgs e)
    {
        EditPrompt("ProductExtraction", SettingsViewModel?.AiOperations.ProductExtraction);
    }

    private void EditClassificationPrompt_Click(object sender, RoutedEventArgs e)
    {
        EditPrompt("Classification", SettingsViewModel?.AiOperations.Classification);
    }

    private void EditLabelsPrompt_Click(object sender, RoutedEventArgs e)
    {
        EditPrompt("Labels", SettingsViewModel?.AiOperations.Labels);
    }

    private void EditShoppingPrompt_Click(object sender, RoutedEventArgs e)
    {
        EditPrompt("Shopping", SettingsViewModel?.AiOperations.Shopping);
    }

    private void EditProductMatcherPrompt_Click(object sender, RoutedEventArgs e)
    {
        EditPrompt("ProductMatcher", SettingsViewModel?.AiOperations.ProductMatcher);
    }

    private void EditPrompt(string operation, string? providerKey)
    {
        var vm = SettingsViewModel;
        if (vm == null) return;

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            MessageBox.Show(
                "Сначала выберите провайдер для этой операции.",
                "Провайдер не выбран",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Get current custom prompts (or null for default)
        var currentSystemPrompt = vm.AiOperations.GetSystemPrompt(operation, providerKey);
        var currentUserPrompt = vm.AiOperations.GetUserPrompt(operation, providerKey);

        // Open editor window with both prompts
        var editor = new PromptEditorWindow(
            operation,
            providerKey,
            currentSystemPrompt,
            currentUserPrompt,
            vm.Log)
        {
            Owner = Window.GetWindow(this)
        };

        if (editor.ShowDialog() == true)
        {
            // Save system prompt if custom, remove if same as default
            if (editor.IsSystemPromptCustom)
            {
                vm.AiOperations.SetSystemPrompt(operation, providerKey, editor.SystemPromptText);
            }
            else
            {
                vm.AiOperations.SetSystemPrompt(operation, providerKey, null);
            }

            // Save user prompt if custom, remove if same as default
            if (editor.IsUserPromptCustom)
            {
                vm.AiOperations.SetUserPrompt(operation, providerKey, editor.UserPromptText);
            }
            else
            {
                vm.AiOperations.SetUserPrompt(operation, providerKey, null);
            }

            vm.MarkAsChanged();
            UpdatePromptIndicators();
        }
    }

    /// <summary>
    /// Обновить индикаторы кастомных промптов
    /// </summary>
    public void UpdatePromptIndicators()
    {
        var vm = SettingsViewModel;
        if (vm == null) return;

        // ProductExtraction indicator
        var extractionProvider = vm.AiOperations.ProductExtraction;
        if (!string.IsNullOrWhiteSpace(extractionProvider) &&
            vm.AiOperations.HasCustomPrompt("ProductExtraction", extractionProvider))
        {
            ProductExtractionPromptIndicator.Text = " (настроен)";
        }
        else
        {
            ProductExtractionPromptIndicator.Text = "";
        }

        // Classification indicator
        var classificationProvider = vm.AiOperations.Classification;
        if (!string.IsNullOrWhiteSpace(classificationProvider) &&
            vm.AiOperations.HasCustomPrompt("Classification", classificationProvider))
        {
            ClassificationPromptIndicator.Text = " (настроен)";
        }
        else
        {
            ClassificationPromptIndicator.Text = "";
        }

        // Labels indicator
        var labelsProvider = vm.AiOperations.Labels;
        if (!string.IsNullOrWhiteSpace(labelsProvider) &&
            vm.AiOperations.HasCustomPrompt("Labels", labelsProvider))
        {
            LabelsPromptIndicator.Text = " (настроен)";
        }
        else
        {
            LabelsPromptIndicator.Text = "";
        }

        // Shopping indicator
        var shoppingProvider = vm.AiOperations.Shopping;
        if (!string.IsNullOrWhiteSpace(shoppingProvider) &&
            vm.AiOperations.HasCustomPrompt("Shopping", shoppingProvider))
        {
            ShoppingPromptIndicator.Text = " (настроен)";
        }
        else
        {
            ShoppingPromptIndicator.Text = "";
        }

        // ProductMatcher indicator
        var productMatcherProvider = vm.AiOperations.ProductMatcher;
        if (!string.IsNullOrWhiteSpace(productMatcherProvider) &&
            vm.AiOperations.HasCustomPrompt("ProductMatcher", productMatcherProvider))
        {
            ProductMatcherPromptIndicator.Text = " (настроен)";
        }
        else
        {
            ProductMatcherPromptIndicator.Text = "";
        }
    }
}
