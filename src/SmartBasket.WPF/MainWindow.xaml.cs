using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Enable thread-safe collection access BEFORE any async operations
        // This must be called on UI thread during initialization
        viewModel.EnableCollectionSynchronization();

        // Auto-scroll log - use Dispatcher to ensure thread safety
        if (viewModel.LogEntries is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (LogListBox.Items.Count > 0)
                    {
                        LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                    }
                });
            };
        }

        // Set password from settings (PasswordBox doesn't support binding)
        PasswordBox.Password = viewModel.EmailPassword;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.EmailPassword = PasswordBox.Password;
        }
    }
}
