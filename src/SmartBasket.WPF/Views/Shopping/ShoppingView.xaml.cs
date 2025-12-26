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
            // WebView2 внутри невидимой вкладки не инициализируется
            // Временно переключаемся на вкладку "Поиск" для инициализации
            var originalTab = LeftTabControl.SelectedIndex;
            LeftTabControl.SelectedItem = SearchTab;

            // Даём время на отрисовку вкладки
            await Task.Delay(100);

            // Ensure WebView2 is initialized
            await ParserWebView.EnsureCoreWebView2Async();

            // Возвращаемся на исходную вкладку
            LeftTabControl.SelectedIndex = originalTab;

            // Pass WebView2 to ViewModel for use by parsers
            viewModel.SetWebView(ParserWebView);

            System.Diagnostics.Debug.WriteLine("[ShoppingView] WebView2 initialized successfully");

            // Initialize welcome screen after WebView is ready
            if (viewModel.InitializeWelcomeScreenCommand.CanExecute(null))
            {
                await viewModel.InitializeWelcomeScreenCommand.ExecuteAsync(null);
            }
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
            // Enter = отправить сообщение
            // Shift+Enter = новая строка
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Enter = новая строка (TextBox с AcceptsReturn обработает сам)
                return;
            }

            // Enter без Shift = отправить сообщение
            if (DataContext is ShoppingViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
            {
                viewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Copy message text to clipboard
    /// </summary>
    private void CopyMessageText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && !string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // Ignore clipboard errors
            }
        }
    }

    /// <summary>
    /// Пробрасывает MouseWheel из вложенных контролов в главный ScrollViewer.
    /// Это нужно чтобы прокрутка мышкой работала когда курсор над MarkdownScrollViewer
    /// или другими вложенными ScrollViewer-ами.
    /// </summary>
    private void OnChildPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Если вложенный ScrollViewer не может скроллить дальше, передаём событие родителю
        if (sender is ScrollViewer scrollViewer)
        {
            var atTop = scrollViewer.VerticalOffset <= 0 && e.Delta > 0;
            var atBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight && e.Delta < 0;

            if (atTop || atBottom || scrollViewer.ScrollableHeight <= 0)
            {
                // Пробрасываем событие в родительский ScrollViewer
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = MouseWheelEvent,
                    Source = sender
                };
                MessagesScrollViewer.RaiseEvent(eventArg);
            }
        }
        else
        {
            // Для MarkdownScrollViewer и других контролов просто пробрасываем всегда
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = sender
            };
            MessagesScrollViewer.RaiseEvent(eventArg);
        }
    }
}
