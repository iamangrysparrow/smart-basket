using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

/// <summary>
/// Detached log window - VS-style dockable/detachable log panel.
/// </summary>
public partial class LogWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isClosingToMainWindow;

    public LogWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Auto-scroll on new entries
        if (viewModel.LogEntries is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += LogEntries_CollectionChanged;
        }
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (AutoScrollCheckBox.IsChecked == true && LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            });
        }
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        _isClosingToMainWindow = true;
        Close();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Unsubscribe
        if (_viewModel.LogEntries is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= LogEntries_CollectionChanged;
        }

        // Notify main window to show docked panel if docking back
        LogWindowClosed?.Invoke(this, _isClosingToMainWindow);
    }

    /// <summary>
    /// Event raised when window closes. dockBack=true means show docked panel.
    /// </summary>
    public event Action<LogWindow, bool>? LogWindowClosed;
}
