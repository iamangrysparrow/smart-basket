using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        InitializeComponent();

        // Auto-scroll when new messages are added
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiChatViewModel viewModel)
        {
            viewModel.EnableCollectionSynchronization();

            // Subscribe to collection changes for auto-scroll
            if (viewModel.Messages is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged += (s, args) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        MessagesScrollViewer.ScrollToEnd();
                    });
                };
            }

            // Subscribe to IsProcessing changes for auto-scroll when typing indicator appears
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiChatViewModel.IsProcessing))
        {
            Dispatcher.BeginInvoke(() =>
            {
                MessagesScrollViewer.ScrollToEnd();
            });
        }
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Enter = new line (allow default behavior for AcceptsReturn=True)
                return;
            }

            // Enter without Shift = send message
            if (DataContext is AiChatViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
            {
                viewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void PromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiChatViewModel viewModel)
            return;

        var dialog = new PromptEditorDialog(viewModel.SystemPrompt)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.SystemPrompt = dialog.PromptText;
            viewModel.ApplySystemPromptCommand.Execute(null);
        }
    }
}
