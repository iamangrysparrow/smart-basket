using System.Collections.Specialized;
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
        }
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is AiChatViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
            {
                viewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
