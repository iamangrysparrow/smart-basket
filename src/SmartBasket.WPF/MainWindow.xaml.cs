using System.Collections.Specialized;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Llm;
using SmartBasket.WPF.Services;
using SmartBasket.WPF.Themes;
using SmartBasket.WPF.ViewModels;
using SmartBasket.WPF.ViewModels.Settings;
using SmartBasket.WPF.Views;
using SmartBasket.WPF.Views.Settings;

namespace SmartBasket.WPF;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ProductsItemsViewModel _productsItemsViewModel;
    private readonly AppSettings _appSettings;
    private readonly SettingsService _settingsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAiProviderFactory _aiProviderFactory;
    private LogWindow? _logWindow;
    private bool _userScrolledUp;
    private ScrollViewer? _logScrollViewer;
    private TabItem? _aiChatTabItem;
    private AiChatView? _aiChatView;

    public MainWindow(
        MainViewModel viewModel,
        ProductsItemsViewModel productsItemsViewModel,
        AppSettings appSettings,
        SettingsService settingsService,
        IHttpClientFactory httpClientFactory,
        IAiProviderFactory aiProviderFactory)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _productsItemsViewModel = productsItemsViewModel;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
        _aiProviderFactory = aiProviderFactory;
        DataContext = viewModel;

        // Enable thread-safe collection access BEFORE any async operations
        viewModel.EnableCollectionSynchronization();
        viewModel.EnableCategoryCollectionSynchronization();

        // Auto-scroll log with smart behavior
        if (viewModel.LogEntries is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    // Smart auto-scroll: only if enabled and user hasn't scrolled up
                    if (_viewModel.AutoScrollEnabled && !_userScrolledUp && LogListBox.Items.Count > 0)
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

        // Subscribe to filtered view changes
        viewModel.FilteredLogEntries.CollectionChanged += (s, e) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel.AutoScrollEnabled && !_userScrolledUp && LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            });
        };

        // Update theme icon based on current theme
        UpdateThemeIcon();
        ThemeManager.ThemeChanged += (_, _) => UpdateThemeIcon();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LogPanel.Visibility = Visibility.Visible;

        // Set DataContext for ProductsItemsView
        ProductsItemsViewControl.DataContext = _productsItemsViewModel;

        // Auto-load receipts on startup (first tab is Чеки)
        _viewModel.LoadReceiptsCommand.Execute(null);
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only handle tab control selection changes (not nested controls)
        if (e.Source != MainTabControl) return;

        // Показываем/скрываем заголовок AI чата в зависимости от выбранной вкладки
        if (_aiChatTabItem != null)
        {
            AiChatTabHeader.Visibility = MainTabControl.SelectedItem == _aiChatTabItem
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Обрабатываем стандартные вкладки
        if (MainTabControl.SelectedItem == _aiChatTabItem)
        {
            // AI Chat tab - снимаем выделение с RadioButton'ов
            TabReceipts.IsChecked = false;
            TabProducts.IsChecked = false;
            return;
        }

        switch (MainTabControl.SelectedIndex)
        {
            case 0: // Чеки
                // Load receipts if not already loaded
                if (_viewModel.Receipts.Count == 0 && !_viewModel.IsProcessing)
                {
                    _viewModel.LoadReceiptsCommand.Execute(null);
                }
                break;

            case 1: // Продукты
                // ProductsItemsView handles its own loading in UserControl_Loaded
                break;
        }
    }

    private void TabReceipts_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabControl != null)
        {
            MainTabControl.SelectedIndex = 0;
        }
    }

    private void TabProducts_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabControl != null)
        {
            MainTabControl.SelectedIndex = 1;
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

    private void OpenSettingsWindow_Click(object sender, RoutedEventArgs e)
    {
        // Передаем логгер через BeginInvoke чтобы не блокировать диалог
        Action<string> log = message =>
        {
            Dispatcher.BeginInvoke(() => _viewModel.AddLogEntry(message));
        };

        var settingsVm = new SettingsViewModel(_appSettings, _settingsService, log, _httpClientFactory);
        var settingsWindow = new SettingsWindow(settingsVm)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void OpenAiChat_Click(object sender, RoutedEventArgs e)
    {
        // Если вкладка уже открыта - просто переключаемся на неё
        if (_aiChatTabItem != null)
        {
            MainTabControl.SelectedItem = _aiChatTabItem;
            return;
        }

        // Логгер для AI Chat - пишет в системный лог
        Action<string> log = message =>
        {
            Dispatcher.BeginInvoke(() => _viewModel.AddLogEntry(message));
        };

        // Создаём новую вкладку
        var aiChatViewModel = new AiChatViewModel(_aiProviderFactory, log);
        _aiChatView = new AiChatView
        {
            DataContext = aiChatViewModel
        };

        _aiChatTabItem = new TabItem
        {
            Content = _aiChatView
        };

        MainTabControl.Items.Add(_aiChatTabItem);
        MainTabControl.SelectedItem = _aiChatTabItem;

        // Показываем заголовок вкладки с крестиком
        AiChatTabHeader.Visibility = Visibility.Visible;

        // Снимаем выделение с RadioButton'ов
        TabReceipts.IsChecked = false;
        TabProducts.IsChecked = false;
    }

    private void CloseAiChat_Click(object sender, RoutedEventArgs e)
    {
        if (_aiChatTabItem == null) return;

        // Запоминаем был ли AI чат активным
        var wasSelected = MainTabControl.SelectedItem == _aiChatTabItem;

        // Удаляем вкладку
        MainTabControl.Items.Remove(_aiChatTabItem);
        _aiChatTabItem = null;
        _aiChatView = null;

        // Скрываем заголовок
        AiChatTabHeader.Visibility = Visibility.Collapsed;

        // Если вкладка была активной - переключаемся на первую
        if (wasSelected)
        {
            MainTabControl.SelectedIndex = 0;
            TabReceipts.IsChecked = true;
        }
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

    /// <summary>
    /// Smart auto-scroll: detects manual scroll up, re-enables at bottom
    /// </summary>
    private void LogListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Get ScrollViewer lazily
        _logScrollViewer ??= GetScrollViewer(LogListBox);
        if (_logScrollViewer == null) return;

        // Only react to user-initiated scrolls (not programmatic)
        if (e.ExtentHeightChange == 0)
        {
            // User scrolled manually
            var atBottom = _logScrollViewer.VerticalOffset >= _logScrollViewer.ScrollableHeight - 10;

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

    protected override void OnClosed(EventArgs e)
    {
        _logWindow?.Close();
        base.OnClosed(e);
    }
}
