using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartBasket.WPF.Views.Shopping;

public partial class ShoppingView : UserControl
{
    public ShoppingView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShoppingViewModel viewModel)
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

            // Initialize WebView2 for parsers
            await InitializeWebViewAsync(viewModel);
        }
    }

    private async Task InitializeWebViewAsync(ShoppingViewModel viewModel)
    {
        try
        {
            // Ensure WebView2 is initialized
            await ParserWebView.EnsureCoreWebView2Async();

            // Pass WebView2 to ViewModel for use by parsers
            viewModel.SetWebView(ParserWebView);

            System.Diagnostics.Debug.WriteLine("[ShoppingView] WebView2 initialized successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShoppingView] Failed to initialize WebView2: {ex.Message}");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShoppingViewModel.IsProcessing))
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
                // Shift+Enter = new line - but we have AcceptsReturn=False, so ignore
                return;
            }

            // Enter = send message
            if (DataContext is ShoppingViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
            {
                viewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
