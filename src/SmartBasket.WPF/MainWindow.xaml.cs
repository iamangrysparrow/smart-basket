using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using SmartBasket.Core.Configuration;
using SmartBasket.WPF.Services;
using SmartBasket.WPF.Themes;
using SmartBasket.WPF.ViewModels;
using SmartBasket.WPF.Views;

namespace SmartBasket.WPF;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ProductsItemsViewModel _productsItemsViewModel;
    private readonly AppSettings _appSettings;
    private readonly SettingsService _settingsService;
    private LogWindow? _logWindow;

    public MainWindow(
        MainViewModel viewModel,
        ProductsItemsViewModel productsItemsViewModel,
        AppSettings appSettings,
        SettingsService settingsService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _productsItemsViewModel = productsItemsViewModel;
        _appSettings = appSettings;
        _settingsService = settingsService;
        DataContext = viewModel;

        // Enable thread-safe collection access BEFORE any async operations
        viewModel.EnableCollectionSynchronization();
        viewModel.EnableCategoryCollectionSynchronization();

        // Auto-scroll log
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
                    // Auto-show log panel when processing starts
                    if (viewModel.IsProcessing && LogPanel.Visibility == Visibility.Collapsed && _logWindow == null)
                    {
                        LogPanel.Visibility = Visibility.Visible;
                    }
                });
            };
        }

        // Update theme icon based on current theme
        UpdateThemeIcon();
        ThemeManager.ThemeChanged += (_, _) => UpdateThemeIcon();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LogPanel.Visibility = Visibility.Visible;

        // Set password in SettingsView PasswordBox
        var passwordBox = SettingsViewControl.GetPasswordBox();
        passwordBox.Password = _viewModel.EmailPassword;
        passwordBox.PasswordChanged += PasswordBox_PasswordChanged;

        // Set YandexGPT API key in SettingsView PasswordBox
        var yandexApiKeyBox = SettingsViewControl.GetYandexApiKeyBox();
        yandexApiKeyBox.Password = _viewModel.YandexApiKey;
        yandexApiKeyBox.PasswordChanged += YandexApiKeyBox_PasswordChanged;

        // Set DataContext for ProductsItemsView
        ProductsItemsViewControl.DataContext = _productsItemsViewModel;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox pb)
        {
            _viewModel.EmailPassword = pb.Password;
        }
    }

    private void YandexApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox pb)
        {
            _viewModel.YandexApiKey = pb.Password;
        }
    }

    private void ToggleLogPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow != null)
        {
            // If detached, bring to front
            _logWindow.Activate();
            return;
        }

        LogPanel.Visibility = LogPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PopOutLog_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow != null)
        {
            _logWindow.Activate();
            return;
        }

        // Hide docked panel
        LogPanel.Visibility = Visibility.Collapsed;

        // Create detached window
        _logWindow = new LogWindow(_viewModel)
        {
            Owner = this
        };

        _logWindow.LogWindowClosed += OnLogWindowClosed;
        _logWindow.Show();
    }

    private void OnLogWindowClosed(LogWindow window, bool dockBack)
    {
        _logWindow = null;

        if (dockBack)
        {
            LogPanel.Visibility = Visibility.Visible;
        }
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ToggleTheme();

        // Save to settings
        _appSettings.Theme = ThemeManager.CurrentTheme.ToString();
        _ = _settingsService.SaveSettingsAsync(_appSettings);
    }

    private void UpdateThemeIcon()
    {
        Dispatcher.Invoke(() =>
        {
            var iconKey = ThemeManager.CurrentTheme == AppTheme.Dark
                ? "IconSun"
                : "IconMoon";

            if (Application.Current.Resources[iconKey] is Geometry geometry)
            {
                ThemeIcon.Data = geometry;
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _logWindow?.Close();
        base.OnClosed(e);
    }
}
