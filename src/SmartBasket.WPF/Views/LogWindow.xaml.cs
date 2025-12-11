using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

/// <summary>
/// Detached log window - VS-style dockable/detachable log panel.
/// </summary>
public partial class LogWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isClosingToMainWindow;
    private bool _userScrolledUp;
    private ScrollViewer? _scrollViewer;

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

        // Subscribe to filtered view changes for auto-scroll
        viewModel.FilteredLogEntries.CollectionChanged += FilteredLogEntries_CollectionChanged;
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.InvokeAsync(AutoScrollIfEnabled);
        }
    }

    private void FilteredLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.InvokeAsync(AutoScrollIfEnabled);
        }
    }

    private void AutoScrollIfEnabled()
    {
        if (_viewModel.AutoScrollEnabled && !_userScrolledUp && LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }
    }

    /// <summary>
    /// Smart auto-scroll: detects manual scroll up, re-enables at bottom
    /// </summary>
    private void LogListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Get ScrollViewer lazily
        _scrollViewer ??= GetScrollViewer(LogListBox);
        if (_scrollViewer == null) return;

        // Only react to user-initiated scrolls (not programmatic)
        if (e.ExtentHeightChange == 0)
        {
            // User scrolled manually
            var atBottom = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 10;

            if (atBottom)
            {
                // User scrolled to bottom - re-enable auto-scroll
                _userScrolledUp = false;
                _viewModel.AutoScrollEnabled = true;
            }
            else if (e.VerticalChange < 0)
            {
                // User scrolled up - disable auto-scroll
                _userScrolledUp = true;
                _viewModel.AutoScrollEnabled = false;
            }
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject obj)
    {
        if (obj is ScrollViewer sv) return sv;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
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
        _viewModel.FilteredLogEntries.CollectionChanged -= FilteredLogEntries_CollectionChanged;

        // Notify main window to show docked panel if docking back
        LogWindowClosed?.Invoke(this, _isClosingToMainWindow);
    }

    /// <summary>
    /// Event raised when window closes. dockBack=true means show docked panel.
    /// </summary>
    public event Action<LogWindow, bool>? LogWindowClosed;
}
