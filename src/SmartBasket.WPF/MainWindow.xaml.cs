using System.Collections.Specialized;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Llm;
using SmartBasket.WPF.Services;
using SmartBasket.WPF.Themes;
using SmartBasket.WPF.ViewModels;
using SmartBasket.WPF.ViewModels.Settings;
using SmartBasket.Services.Shopping;
using SmartBasket.WPF.Views;
using SmartBasket.WPF.Views.Settings;
using SmartBasket.WPF.Views.Shopping;

namespace SmartBasket.WPF;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ProductsItemsViewModel _productsItemsViewModel;
    private readonly AppSettings _appSettings;
    private readonly SettingsService _settingsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAiProviderFactory _aiProviderFactory;
    private readonly IChatService _chatService;
    private readonly IShoppingSessionService _shoppingSessionService;
    private LogWindow? _logWindow;
    private bool _userScrolledUp;
    private ScrollViewer? _logScrollViewer;
    private TabItem? _aiChatTabItem;
    private AiChatView? _aiChatView;
    private TabItem? _shoppingTabItem;
    private ShoppingView? _shoppingView;

    public MainWindow(
        MainViewModel viewModel,
        ProductsItemsViewModel productsItemsViewModel,
        AppSettings appSettings,
        SettingsService settingsService,
        IHttpClientFactory httpClientFactory,
        IAiProviderFactory aiProviderFactory,
        IChatService chatService,
        IShoppingSessionService shoppingSessionService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _productsItemsViewModel = productsItemsViewModel;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
        _aiProviderFactory = aiProviderFactory;
        _chatService = chatService;
        _shoppingSessionService = shoppingSessionService;
        DataContext = viewModel;

        // Set ProductsItemsView DataContext early (before Loaded event fires)
        // This prevents binding errors when the view tries to bind to MainViewModel
        ProductsItemsViewControl.DataContext = _productsItemsViewModel;

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

        // Auto-load receipts on startup (first tab is Чеки)
        _viewModel.LoadReceiptsCommand.Execute(null);
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only handle tab control selection changes (not nested controls)
        if (e.Source != MainTabControl) return;

        // Обрабатываем стандартные вкладки
        if (MainTabControl.SelectedItem == _aiChatTabItem)
        {
            // AI Chat tab - снимаем выделение с RadioButton'ов Чеки/Продукты
            // (TabAiChat уже выбран через Checked event)
            return;
        }

        switch (MainTabControl.SelectedIndex)
        {
            case 0: // Чеки
                // Refresh receipts when switching to this tab
                if (!_viewModel.IsProcessing)
                {
                    _viewModel.LoadReceiptsCommand.Execute(null);
                }
                break;

            case 1: // Продукты
                // Refresh products/categories when switching to this tab
                if (!_productsItemsViewModel.IsBusy)
                {
                    _productsItemsViewModel.RefreshCommand.Execute(null);
                }
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
            TabAiChat.IsChecked = true;
            return;
        }

        // Логгер для AI Chat - пишет в системный лог
        Action<string> log = message =>
        {
            Dispatcher.BeginInvoke(() => _viewModel.AddLogEntry(message));
        };

        // Создаём новую вкладку с ChatService для tool calling
        var aiChatViewModel = new AiChatViewModel(_chatService, _aiProviderFactory, _appSettings, _settingsService, log);
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

        // Показываем вкладку AI Chat в toolbar и выделяем её
        AiChatTabPanel.Visibility = Visibility.Visible;
        TabAiChat.IsChecked = true;
    }

    private void TabAiChat_Checked(object sender, RoutedEventArgs e)
    {
        if (_aiChatTabItem != null && MainTabControl != null)
        {
            MainTabControl.SelectedItem = _aiChatTabItem;
        }
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

        // Скрываем вкладку AI Chat в toolbar
        AiChatTabPanel.Visibility = Visibility.Collapsed;

        // Если вкладка была активной - переключаемся на первую
        if (wasSelected)
        {
            MainTabControl.SelectedIndex = 0;
            TabReceipts.IsChecked = true;
        }
    }

    private void OpenShopping_Click(object sender, RoutedEventArgs e)
    {
        // Если вкладка уже открыта - просто переключаемся на неё
        if (_shoppingTabItem != null)
        {
            MainTabControl.SelectedItem = _shoppingTabItem;
            TabShopping.IsChecked = true;
            return;
        }

        // Получаем ShoppingViewModel из DI
        var shoppingViewModel = App.Services.GetRequiredService<ShoppingViewModel>();
        _shoppingView = new ShoppingView
        {
            DataContext = shoppingViewModel
        };

        _shoppingTabItem = new TabItem
        {
            Content = _shoppingView
        };

        MainTabControl.Items.Add(_shoppingTabItem);
        MainTabControl.SelectedItem = _shoppingTabItem;

        // Показываем вкладку Shopping в toolbar и выделяем её
        ShoppingTabPanel.Visibility = Visibility.Visible;
        TabShopping.IsChecked = true;

        _viewModel.AddLogEntry("Модуль закупок открыт");
    }

    private void TabShopping_Checked(object sender, RoutedEventArgs e)
    {
        if (_shoppingTabItem != null && MainTabControl != null)
        {
            MainTabControl.SelectedItem = _shoppingTabItem;
        }
    }

    private void CloseShopping_Click(object sender, RoutedEventArgs e)
    {
        if (_shoppingTabItem == null) return;

        // Запоминаем была ли вкладка активной
        var wasSelected = MainTabControl.SelectedItem == _shoppingTabItem;

        // Удаляем вкладку
        MainTabControl.Items.Remove(_shoppingTabItem);
        _shoppingTabItem = null;
        _shoppingView = null;

        // Скрываем вкладку Shopping в toolbar
        ShoppingTabPanel.Visibility = Visibility.Collapsed;

        // Если вкладка была активной - переключаемся на первую
        if (wasSelected)
        {
            MainTabControl.SelectedIndex = 0;
            TabReceipts.IsChecked = true;
        }

        _viewModel.AddLogEntry("Модуль закупок закрыт");
    }

    private void ShowAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Smart Basket\n\n" +
            "Приложение для автоматического сбора и анализа чеков из различных источников с использованием AI для категоризации.\n\n" +
            "Version: 1.0.0\n" +
            "© 2024",
            "О программе",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DeleteSelectedReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedReceipt == null)
        {
            MessageBox.Show("Выберите чек для удаления", "Нет выбранного чека", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var receipt = _viewModel.SelectedReceipt;
        var result = MessageBox.Show(
            $"Удалить чек?\n\n{receipt.Shop} от {receipt.Date:dd.MM.yyyy}\n{receipt.ItemCount} позиций на сумму {receipt.Total:N2}₽",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.AddLogEntry($"Удаление чека {receipt.Shop} от {receipt.Date:dd.MM.yyyy} - TODO: implement");
            // TODO: Implement actual deletion via ViewModel command
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
