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

        // Get current custom prompt (or null for default)
        var currentPrompt = vm.AiOperations.GetPrompt(operation, providerKey);

        // Open editor window
        var editor = new PromptEditorWindow(operation, providerKey, currentPrompt, vm.Log)
        {
            Owner = Window.GetWindow(this)
        };

        if (editor.ShowDialog() == true)
        {
            // Save prompt if it's custom, remove if it's same as default
            if (editor.IsCustomPrompt)
            {
                vm.AiOperations.SetPrompt(operation, providerKey, editor.PromptText);
            }
            else
            {
                vm.AiOperations.SetPrompt(operation, providerKey, null);
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
    }
}
