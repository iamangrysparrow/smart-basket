using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Entities;
using SmartBasket.Data;
using Microsoft.Win32;
using SmartBasket.Services;
using SmartBasket.Services.Export;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Products;
using SmartBasket.WPF.Logging;
using SmartBasket.WPF.Models;
using SmartBasket.WPF.Services;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// Thread-safe progress reporter that does NOT capture SynchronizationContext.
/// Uses Action directly instead of posting to captured context.
/// </summary>
internal class ThreadSafeProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public ThreadSafeProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value)
    {
        _handler(value);
    }
}

public partial class MainViewModel : ObservableObject
{
    private readonly SmartBasketDbContext _dbContext;
    private readonly IReceiptCollectionService _receiptCollectionService;
    private readonly IProductCleanupService _productCleanupService;
    private readonly IReceiptExportService _receiptExportService;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _cts;

    public MainViewModel(
        SmartBasketDbContext dbContext,
        IReceiptCollectionService receiptCollectionService,
        IProductCleanupService productCleanupService,
        IReceiptExportService receiptExportService,
        AppSettings settings,
        SettingsService settingsService,
        ILogger<MainViewModel> logger)
    {
        _dbContext = dbContext;
        _receiptCollectionService = receiptCollectionService;
        _productCleanupService = productCleanupService;
        _receiptExportService = receiptExportService;
        _settings = settings;
        _settingsService = settingsService;
        _logger = logger;
    }

    // State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotProcessing))]
    private bool _isProcessing;

    public bool IsNotProcessing => !IsProcessing;

    [ObservableProperty] private string _statusText = "Ready";

    /// <summary>
    /// Записи лога из Serilog LogViewerSink
    /// </summary>
    public ObservableCollection<LogEntry> LogEntries => LogViewerSink.Instance.LogEntries;

    // Log filtering by level
    [ObservableProperty] private bool _showDebug = true;
    [ObservableProperty] private bool _showInfo = true;
    [ObservableProperty] private bool _showWarning = true;
    [ObservableProperty] private bool _showError = true;

    // Auto-scroll state
    [ObservableProperty] private bool _autoScrollEnabled = true;

    // Filtered view of log entries
    public ICollectionView FilteredLogEntries { get; private set; } = null!;

    partial void OnShowDebugChanged(bool value) => FilteredLogEntries?.Refresh();
    partial void OnShowInfoChanged(bool value) => FilteredLogEntries?.Refresh();
    partial void OnShowWarningChanged(bool value) => FilteredLogEntries?.Refresh();
    partial void OnShowErrorChanged(bool value) => FilteredLogEntries?.Refresh();

    // Results - thread-safe via BindingOperations.EnableCollectionSynchronization
    public ObservableCollection<ReceiptViewModel> Receipts { get; } = new();
    private readonly object _receiptsLock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredItems))]
    private ReceiptViewModel? _selectedReceipt;

    // Filters
    public ObservableCollection<string> ShopFilters { get; } = new() { "Все" };
    private readonly object _shopFiltersLock = new();
    [ObservableProperty] private string _selectedShopFilter = "Все";
    [ObservableProperty] private DateTime? _filterDateFrom;
    [ObservableProperty] private DateTime? _filterDateTo;

    // Auto-apply filters on change
    partial void OnSelectedShopFilterChanged(string value) => ApplyFiltersIfNotProcessing();
    partial void OnFilterDateFromChanged(DateTime? value) => ApplyFiltersIfNotProcessing();
    partial void OnFilterDateToChanged(DateTime? value) => ApplyFiltersIfNotProcessing();

    private void ApplyFiltersIfNotProcessing()
    {
        if (!IsProcessing && _receiptsLoaded)
        {
            LoadReceiptsCommand.Execute(null);
        }
    }

    // Track if receipts were loaded at least once (to avoid loading on init)
    private bool _receiptsLoaded;

    // Search in receipt items
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredItems))]
    private string _itemSearchText = string.Empty;

    // Filtered items for current receipt
    public IEnumerable<ReceiptItemViewModel> FilteredItems
    {
        get
        {
            if (SelectedReceipt?.Items == null)
                return Enumerable.Empty<ReceiptItemViewModel>();

            var items = SelectedReceipt.Items.AsEnumerable();

            // Apply item search filter
            if (!string.IsNullOrWhiteSpace(ItemSearchText))
            {
                var search = ItemSearchText.ToLowerInvariant();
                items = items.Where(i => i.Name.ToLowerInvariant().Contains(search));
            }

            return items;
        }
    }

    // Statistics
    [ObservableProperty] private int _totalReceiptsCount;
    [ObservableProperty] private decimal _totalSum;

    /// <summary>
    /// Must be called from UI thread during initialization to enable thread-safe collection access.
    /// </summary>
    public void EnableCollectionSynchronization()
    {
        // LogViewerSink manages its own collection synchronization
        LogViewerSink.Instance.EnableCollectionSynchronization();

        // This WPF method allows ObservableCollection to be modified from any thread
        BindingOperations.EnableCollectionSynchronization(Receipts, _receiptsLock);
        BindingOperations.EnableCollectionSynchronization(ShopFilters, _shopFiltersLock);

        // Create filtered view for log entries with level filtering
        FilteredLogEntries = CollectionViewSource.GetDefaultView(LogEntries);
        FilteredLogEntries.Filter = LogEntryFilter;
    }

    private bool LogEntryFilter(object item)
    {
        if (item is not LogEntry entry)
            return true;

        return entry.Level switch
        {
            Models.LogLevel.Debug => ShowDebug,
            Models.LogLevel.Info => ShowInfo,
            Models.LogLevel.Warning => ShowWarning,
            Models.LogLevel.Error => ShowError,
            _ => true
        };
    }

    /// <summary>
    /// Публичный метод для добавления записей в лог из других компонентов (legacy).
    /// Использует ILogger через Serilog.
    /// </summary>
    public void AddLogEntry(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        // Разбиваем многострочные сообщения на отдельные записи
        var lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var entry = LogEntry.FromMessage(line);
            switch (entry.Level)
            {
                case Models.LogLevel.Error:
                    _logger.LogError("{Message}", line);
                    break;
                case Models.LogLevel.Warning:
                    _logger.LogWarning("{Message}", line);
                    break;
                case Models.LogLevel.Info:
                    _logger.LogInformation("{Message}", line);
                    break;
                default:
                    _logger.LogDebug("{Message}", line);
                    break;
            }
        }
    }

    /// <summary>
    /// Собрать чеки из всех настроенных источников (Phase 4: новый API)
    /// Использует ReceiptCollectionService для оркестрации
    /// </summary>
    [RelayCommand]
    private async Task CollectReceiptsAsync()
    {
        if (IsProcessing)
        {
            _logger.LogDebug("Operation already in progress, please wait...");
            return;
        }

        // Проверить наличие настроенных источников
        if (_settings.ReceiptSources == null || _settings.ReceiptSources.Count == 0)
        {
            _logger.LogDebug("No receipt sources configured. Please configure sources in Settings.");
            StatusText = "No sources configured";
            return;
        }

        var enabledSources = _settings.ReceiptSources.Where(s => s.IsEnabled).ToList();
        if (enabledSources.Count == 0)
        {
            _logger.LogDebug("No enabled receipt sources. Please enable at least one source in Settings.");
            StatusText = "No enabled sources";
            return;
        }

        IsProcessing = true;
        _cts = new CancellationTokenSource();
        StatusText = "Collecting receipts...";
        _logger.LogDebug("=== Starting receipt collection (Phase 4) ===");
        _logger.LogDebug("Enabled sources: {Sources}", string.Join(", ", enabledSources.Select(s => s.Name)));

        try
        {
            var progress = new ThreadSafeProgress<string>(msg => _logger.LogDebug("{Message}", msg));

            var result = await Task.Run(async () =>
                await _receiptCollectionService.CollectAsync(
                    sourceNames: null, // все enabled источники
                    progress: progress,
                    cancellationToken: _cts.Token));

            // Очистка осиротевших Products после обработки
            if (result.ReceiptsSaved > 0)
            {
                _logger.LogDebug("");
                _logger.LogDebug("=== Cleaning up orphaned products ===");
                var cleanedUp = await Task.Run(async () =>
                    await _productCleanupService.CleanupOrphanedProductsAsync(_cts.Token));
                if (cleanedUp > 0)
                {
                    _logger.LogDebug("Removed {Count} orphaned products", cleanedUp);
                }
            }

            StatusText = $"Done: {result.ReceiptsSaved} saved, {result.ReceiptsSkipped} skipped, {result.Errors} errors";

            // Обновить UI
            if (result.ReceiptsSaved > 0 && !_cts.Token.IsCancellationRequested)
            {
                _logger.LogDebug("");
                _logger.LogDebug("Reloading receipts...");
                await LoadReceiptsAsync();
                await LoadCategoryTreeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Cancelled by user");
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CollectReceipts error");
            StatusText = "Error occurred - see log";
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Очистить осиротевшие Products (без Items и без дочерних)
    /// </summary>
    [RelayCommand]
    private async Task CleanupOrphanedProductsAsync()
    {
        if (IsProcessing)
        {
            _logger.LogDebug("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        StatusText = "Cleaning up orphaned products...";
        _logger.LogDebug("=== Cleanup Orphaned Products ===");

        try
        {
            // Сначала показать что будет удалено
            var orphaned = await Task.Run(async () =>
                await _productCleanupService.GetOrphanedProductsAsync());

            if (orphaned.Count == 0)
            {
                _logger.LogDebug("No orphaned products found");
                StatusText = "No orphaned products";
                return;
            }

            _logger.LogDebug("Found {Count} orphaned products:", orphaned.Count);
            foreach (var p in orphaned.Take(20))
            {
                _logger.LogDebug("  - {Name}{Parent}", p.Name, p.ParentName != null ? $" (parent: {p.ParentName})" : "");
            }
            if (orphaned.Count > 20)
            {
                _logger.LogDebug("  ... and {Count} more", orphaned.Count - 20);
            }

            var result = MessageBox.Show(
                $"Delete {orphaned.Count} orphaned products?\n\nOrphaned products have no associated items and no child products.",
                "Confirm Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                _logger.LogDebug("Cleanup cancelled by user");
                StatusText = "Cleanup cancelled";
                return;
            }

            var deleted = await Task.Run(async () =>
                await _productCleanupService.CleanupOrphanedProductsAsync());

            _logger.LogDebug("Deleted {Count} orphaned products", deleted);
            StatusText = $"Deleted {deleted} products";

            await LoadCategoryTreeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CleanupOrphanedProducts error");
            StatusText = "Cleanup failed - see log";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
        _logger.LogDebug("Cancellation requested...");
        StatusText = "Cancelling...";
    }

    [RelayCommand]
    private async Task LoadReceiptsAsync()
    {
        if (IsProcessing)
        {
            _logger.LogDebug("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        _logger.LogDebug("Loading receipts from database...");

        try
        {
            // Run on background thread to completely free UI thread
            var (receipts, shops) = await Task.Run(async () =>
            {
                // Build query with filters
                IQueryable<Receipt> query = _dbContext.Receipts
                    .Include(r => r.Items)
                        .ThenInclude(i => i.Item)
                            .ThenInclude(item => item!.Product)
                    .Include(r => r.Items)
                        .ThenInclude(i => i.Item)
                            .ThenInclude(item => item!.ItemLabels)
                                .ThenInclude(il => il.Label);

                // Apply shop filter
                if (!string.IsNullOrEmpty(SelectedShopFilter) && SelectedShopFilter != "Все")
                {
                    query = query.Where(r => r.Shop == SelectedShopFilter);
                }

                // Apply date filters
                if (FilterDateFrom.HasValue)
                {
                    var fromUtc = DateTime.SpecifyKind(FilterDateFrom.Value.Date, DateTimeKind.Utc);
                    query = query.Where(r => r.ReceiptDate >= fromUtc);
                }
                if (FilterDateTo.HasValue)
                {
                    var toUtc = DateTime.SpecifyKind(FilterDateTo.Value.Date.AddDays(1), DateTimeKind.Utc);
                    query = query.Where(r => r.ReceiptDate < toUtc);
                }

                var receiptList = await query
                    .OrderByDescending(r => r.ReceiptDate)
                    .Take(100)
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Get unique shops for filter dropdown
                var shopList = await _dbContext.Receipts
                    .Select(r => r.Shop)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return (receiptList, shopList);
            });

            // Update shop filters (thread-safe via EnableCollectionSynchronization)
            // Save current selection to restore after clearing
            var currentFilter = SelectedShopFilter;
            lock (_shopFiltersLock)
            {
                ShopFilters.Clear();
                ShopFilters.Add("Все");
                foreach (var shop in shops)
                {
                    ShopFilters.Add(shop);
                }
            }
            // Restore selection (must be done after collection is updated)
            SelectedShopFilter = ShopFilters.Contains(currentFilter) ? currentFilter : "Все";

            // Update collection (thread-safe via EnableCollectionSynchronization)
            lock (_receiptsLock)
            {
                Receipts.Clear();
                foreach (var receipt in receipts)
                {
                    Receipts.Add(new ReceiptViewModel(receipt));
                }
            }

            // Update statistics
            TotalReceiptsCount = receipts.Count;
            TotalSum = receipts.Sum(r => r.Total ?? 0);

            _logger.LogDebug("Loaded {Count} receipts, total: {Total:N2}\u20BD", receipts.Count, TotalSum);
            StatusText = $"Loaded {receipts.Count} receipts";

            // Select first receipt if none selected
            if (SelectedReceipt == null && Receipts.Count > 0)
            {
                SelectedReceipt = Receipts[0];
            }

            _receiptsLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadReceipts error");
            StatusText = "Load failed - see log";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        // Temporarily disable auto-reload
        var wasLoaded = _receiptsLoaded;
        _receiptsLoaded = false;

        SelectedShopFilter = "Все";
        FilterDateFrom = null;
        FilterDateTo = null;

        _receiptsLoaded = wasLoaded;
        _logger.LogInformation("Фильтры сброшены");

        // Reload once after all filters cleared
        if (_receiptsLoaded)
        {
            LoadReceiptsCommand.Execute(null);
        }
    }

    #region Receipt Export

    /// <summary>
    /// Экспорт выбранного чека в полный JSON формат
    /// </summary>
    [RelayCommand]
    private async Task ExportReceiptFullAsync()
    {
        if (SelectedReceipt == null)
        {
            _logger.LogWarning("Не выбран чек для экспорта");
            return;
        }

        try
        {
            // Получаем полные данные чека из БД
            var receipt = await _dbContext.Receipts
                .Include(r => r.Items)
                    .ThenInclude(i => i.Item)
                        .ThenInclude(item => item!.Product)
                            .ThenInclude(p => p!.Parent)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Item)
                        .ThenInclude(item => item!.ItemLabels)
                            .ThenInclude(il => il.Label)
                .FirstOrDefaultAsync(r => r.Id == SelectedReceipt.Id);

            if (receipt == null)
            {
                _logger.LogError("Чек не найден: {Id}", SelectedReceipt.Id);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Экспорт чека (полный формат)",
                Filter = "JSON файлы (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"receipt_{receipt.Shop}_{receipt.ReceiptDate:yyyy-MM-dd}_full.json"
            };

            if (dialog.ShowDialog() == true)
            {
                await _receiptExportService.SaveToFileFullAsync(receipt, dialog.FileName);
                _logger.LogInformation("Чек экспортирован: {FileName}", dialog.FileName);
                StatusText = $"Экспортировано: {System.IO.Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportReceiptFull error");
            StatusText = "Ошибка экспорта";
        }
    }

    /// <summary>
    /// Экспорт выбранного чека в минимальный JSON формат
    /// </summary>
    [RelayCommand]
    private async Task ExportReceiptMinimalAsync()
    {
        if (SelectedReceipt == null)
        {
            _logger.LogWarning("Не выбран чек для экспорта");
            return;
        }

        try
        {
            // Получаем данные чека из БД (минимальный набор)
            var receipt = await _dbContext.Receipts
                .Include(r => r.Items)
                    .ThenInclude(i => i.Item)
                .FirstOrDefaultAsync(r => r.Id == SelectedReceipt.Id);

            if (receipt == null)
            {
                _logger.LogError("Чек не найден: {Id}", SelectedReceipt.Id);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Экспорт чека (минимальный формат)",
                Filter = "JSON файлы (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"receipt_{receipt.Shop}_{receipt.ReceiptDate:yyyy-MM-dd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                await _receiptExportService.SaveToFileMinimalAsync(receipt, dialog.FileName);
                _logger.LogInformation("Чек экспортирован: {FileName}", dialog.FileName);
                StatusText = $"Экспортировано: {System.IO.Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportReceiptMinimal error");
            StatusText = "Ошибка экспорта";
        }
    }

    #endregion

    [RelayCommand]
    private async Task InitializeDatabaseAsync()
    {
        if (IsProcessing)
        {
            _logger.LogDebug("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        StatusText = "Initializing database...";
        _logger.LogDebug("=== Initializing Database ===");
        _logger.LogDebug("Connection: {ConnectionString}", _settings.Database.ConnectionString);

        try
        {
            // Run on background thread to completely free UI thread
            await Task.Run(async () =>
                await _dbContext.Database.EnsureCreatedAsync());
            _logger.LogDebug("Database initialized successfully");
            StatusText = "Database: OK";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitializeDatabase error");
            StatusText = "Database: FAILED";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ResetDatabaseAsync()
    {
        if (IsProcessing)
        {
            _logger.LogDebug("Operation already in progress, please wait...");
            return;
        }

        var result = MessageBox.Show(
            "WARNING: This will DELETE all data and recreate the database. Are you sure?",
            "Reset Database",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        result = MessageBox.Show(
            "This action cannot be undone. Last chance to cancel.",
            "Confirm Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsProcessing = true;
        StatusText = "Resetting database...";
        _logger.LogDebug("=== Resetting Database ===");

        try
        {
            // Run on background thread to completely free UI thread
            await Task.Run(async () =>
            {
                await _dbContext.Database.EnsureDeletedAsync();
                await _dbContext.Database.EnsureCreatedAsync();
            });
            _logger.LogDebug("Database deleted and recreated");

            // Clear collection (thread-safe via EnableCollectionSynchronization)
            lock (_receiptsLock)
            {
                Receipts.Clear();
            }

            _logger.LogDebug("Reset complete");
            StatusText = "Database: Reset OK";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResetDatabase error");
            StatusText = "Database: Reset FAILED";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogViewerSink.Instance.Clear();
        _logger.LogInformation("Log cleared");
    }

    [RelayCommand]
    private void SaveLog()
    {
        try
        {
            var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logsDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var logPath = Path.Combine(logsDir, $"smartbasket_{timestamp}.log");

            var entries = LogViewerSink.Instance.GetFullLogLines();
            File.WriteAllLines(logPath, entries);

            _logger.LogInformation("Log saved to: {LogPath} ({Count} entries)", logPath, entries.Length);
            StatusText = $"Log saved: {Path.GetFileName(logPath)} ({entries.Length} entries)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveLog error");
            StatusText = "Log save FAILED";
        }
    }

    /// <summary>
    /// Copy all visible log entries to clipboard
    /// </summary>
    [RelayCommand]
    private void CopyLog()
    {
        try
        {
            var entries = LogViewerSink.Instance.GetVisibleLogLines(e => LogEntryFilter(e));

            if (entries.Length > 0)
            {
                Clipboard.SetText(string.Join(Environment.NewLine, entries));
                StatusText = $"Copied {entries.Length} log entries to clipboard";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CopyLog error");
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _logger.LogDebug("=== Saving Settings ===");
        try
        {
            // TODO: Legacy UI settings removed - need to implement new settings UI
            // Settings are now managed through ReceiptSources, AiProviders, etc.
            // For now, just save current settings as-is
            _settingsService.Save(_settings);

            _logger.LogDebug("Settings saved to: {Path}", _settingsService.SettingsPath);
            StatusText = "Settings saved";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveSettings error");
            StatusText = "Settings save FAILED";
        }
    }

    #region Categories Tab

    // Category tree items
    public ObservableCollection<CategoryTreeItemViewModel> CategoryTreeItems { get; } = new();
    private readonly object _categoryTreeLock = new();

    // Category items list
    public ObservableCollection<ItemViewModel> CategoryItems { get; } = new();
    private readonly object _categoryItemsLock = new();

    // Category filters
    public ObservableCollection<string> CategoryFilters { get; } = new() { "Все", "Не категоризировано" };
    private readonly object _categoryFiltersLock = new();

    [ObservableProperty]
    private string _selectedCategoryFilter = "Все";

    [ObservableProperty]
    private string _categoryItemSearchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategoryName = "Выберите категорию";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    private ItemViewModel? _selectedCategoryItem;

    public bool HasSelectedItem => SelectedCategoryItem != null;

    // Statistics
    [ObservableProperty]
    private int _totalCategoriesCount;

    [ObservableProperty]
    private int _totalItemsCount;

    [ObservableProperty]
    private int _uncategorizedCount;

    // Categorization progress
    [ObservableProperty]
    private bool _isCategorizing;

    [ObservableProperty]
    private string _categorizationProgress = string.Empty;

    /// <summary>
    /// Enable synchronization for category collections (call from UI thread)
    /// </summary>
    public void EnableCategoryCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(CategoryTreeItems, _categoryTreeLock);
        BindingOperations.EnableCollectionSynchronization(CategoryItems, _categoryItemsLock);
        BindingOperations.EnableCollectionSynchronization(CategoryFilters, _categoryFiltersLock);
    }

    /// <summary>
    /// Handle tree item selection from code-behind
    /// </summary>
    public void OnCategoryTreeItemSelected(CategoryTreeItemViewModel item)
    {
        SelectedCategoryName = item.Name;
        LoadCategoryItemsAsync(item).ConfigureAwait(false);
    }

    private async Task LoadCategoryItemsAsync(CategoryTreeItemViewModel treeItem)
    {
        try
        {
            var items = await Task.Run(async () =>
            {
                if (treeItem.ProductId.HasValue)
                {
                    // Load items for this product
                    return await _dbContext.Items
                        .Include(i => i.Product)
                        .Include(i => i.ReceiptItems)
                        .Where(i => i.ProductId == treeItem.ProductId.Value)
                        .OrderBy(i => i.Name)
                        .Take(200)
                        .Select(i => new ItemViewModel(i))
                        .ToListAsync()
                        .ConfigureAwait(false);
                }

                // TODO: Handle uncategorized items differently in new schema
                return new List<ItemViewModel>();
            });

            lock (_categoryItemsLock)
            {
                CategoryItems.Clear();
                foreach (var item in items)
                {
                    CategoryItems.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadCategoryItems error");
        }
    }

    [RelayCommand]
    private async Task LoadCategoryTreeAsync()
    {
        try
        {
            _logger.LogDebug("Loading category tree...");

            var products = await Task.Run(async () =>
            {
                return await _dbContext.Products
                    .Include(p => p.Items)
                    .OrderBy(p => p.Name)
                    .ToListAsync()
                    .ConfigureAwait(false);
            });

            lock (_categoryTreeLock)
            {
                CategoryTreeItems.Clear();

                // Add products
                foreach (var product in products)
                {
                    CategoryTreeItems.Add(new CategoryTreeItemViewModel
                    {
                        ProductId = product.Id,
                        Name = product.Name,
                        Icon = "\uD83D\uDCE6",
                        Count = product.Items.Count
                    });
                }
            }

            // Update filters
            lock (_categoryFiltersLock)
            {
                CategoryFilters.Clear();
                CategoryFilters.Add("Все");
                foreach (var product in products)
                {
                    CategoryFilters.Add(product.Name);
                }
            }

            // Update statistics
            TotalCategoriesCount = products.Count;
            TotalItemsCount = products.Sum(p => p.Items.Count);
            UncategorizedCount = 0; // TODO: Calculate based on new schema

            _logger.LogDebug("Loaded {Count} categories", products.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadCategoryTree error");
        }
    }

    [RelayCommand]
    private void OpenCategoriesDialog()
    {
        try
        {
            var dialog = new Views.CategoriesDialog { Owner = Application.Current.MainWindow };

            // Load existing categories
            var existingCategories = _dbContext.Products
                .OrderBy(p => p.Name)
                .Select(p => p.Name)
                .ToList();

            dialog.CategoriesText = string.Join("\n", existingCategories);

            if (dialog.ShowDialog() == true)
            {
                var categories = dialog.GetCategories();
                SaveCategoriesAsync(categories).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCategoriesDialog error");
        }
    }

    private async Task SaveCategoriesAsync(string[] categories)
    {
        try
        {
            _logger.LogDebug("Saving {Count} categories...", categories.Length);

            await Task.Run(async () =>
            {
                // Get existing products
                var existing = await _dbContext.Products.ToListAsync().ConfigureAwait(false);
                var existingNames = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Add new categories
                foreach (var category in categories)
                {
                    if (!existingNames.Contains(category))
                    {
                        _dbContext.Products.Add(new Product
                        {
                            Name = category
                        });
                        _logger.LogDebug("  + Added: {Category}", category);
                    }
                }

                await _dbContext.SaveChangesAsync().ConfigureAwait(false);
            });

            _logger.LogDebug("Categories saved");
            await LoadCategoryTreeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveCategories error");
        }
    }

    [RelayCommand]
    private void ChangeCategory()
    {
        if (SelectedCategoryItem == null) return;

        try
        {
            var dialog = new Views.ChangeCategoryDialog { Owner = Application.Current.MainWindow };
            dialog.ItemName = SelectedCategoryItem.Name;
            dialog.CurrentCategory = SelectedCategoryItem.ProductName == "Не задана" ? null : SelectedCategoryItem.ProductName;

            var categories = _dbContext.Products.OrderBy(p => p.Name).Select(p => p.Name).ToList();
            dialog.SetAvailableCategories(categories);

            if (dialog.ShowDialog() == true)
            {
                ApplyCategoryChangeAsync(SelectedCategoryItem, dialog.SelectedCategory!, dialog.IsNewCategory)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChangeCategory error");
        }
    }

    private async Task ApplyCategoryChangeAsync(ItemViewModel item, string categoryName, bool isNewCategory)
    {
        try
        {
            await Task.Run(async () =>
            {
                // Get or create product
                var product = await _dbContext.Products
                    .FirstOrDefaultAsync(p => p.Name == categoryName)
                    .ConfigureAwait(false);

                if (product == null && isNewCategory)
                {
                    product = new Product { Name = categoryName };
                    _dbContext.Products.Add(product);
                    await _dbContext.SaveChangesAsync().ConfigureAwait(false);
                    _logger.LogDebug("Created new category: {Category}", categoryName);
                }

                if (product == null)
                {
                    _logger.LogError("Category not found: {Category}", categoryName);
                    return;
                }

                // Update the item - move to new product
                var existingItem = await _dbContext.Items
                    .FirstOrDefaultAsync(i => i.Id == item.Id)
                    .ConfigureAwait(false);

                if (existingItem != null)
                {
                    existingItem.ProductId = product.Id;
                    await _dbContext.SaveChangesAsync().ConfigureAwait(false);
                    _logger.LogDebug("Moved '{ItemName}' to category '{Category}'", existingItem.Name, categoryName);
                }
            });

            await LoadCategoryTreeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyCategoryChange error");
        }
    }

    [RelayCommand]
    private async Task CategorizeAllAsync()
    {
        // TODO: Refactor categorization for new schema
        // Old logic used RawReceiptItem with CategorizationStatus
        // New schema should work directly with Items
        _logger.LogDebug("Categorization not yet implemented for new schema");
        CategorizationProgress = "TODO: Refactor for new schema";
        await Task.CompletedTask;
    }

    /// <summary>
    /// Check if categories exist before allowing email fetch
    /// </summary>
    public async Task<bool> EnsureCategoriesExistAsync()
    {
        var count = await _dbContext.Products.CountAsync();
        if (count > 0) return true;

        var result = MessageBox.Show(
            "Для работы с чеками необходимо сначала добавить категории продуктов.\n\nОткрыть диалог добавления категорий?",
            "Нет категорий",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            OpenCategoriesDialog();
            return await _dbContext.Products.CountAsync() > 0;
        }

        return false;
    }

    #endregion
}

public class ReceiptViewModel
{
    public ReceiptViewModel(Receipt receipt)
    {
        Id = receipt.Id;
        Shop = receipt.Shop;
        Date = receipt.ReceiptDate;
        OrderNumber = receipt.ReceiptNumber;
        Total = receipt.Total;
        Status = receipt.Status.ToString();
        ItemCount = receipt.Items.Count;
        Items = receipt.Items.Select(i => new ReceiptItemViewModel(i)).ToList();
    }

    public Guid Id { get; }
    public string Shop { get; }
    public DateTime Date { get; }
    public string? OrderNumber { get; }
    public decimal? Total { get; }
    public string Status { get; }
    public int ItemCount { get; }
    public List<ReceiptItemViewModel> Items { get; }

    public string DisplayText => $"{Date:dd.MM.yyyy} - {Shop} ({ItemCount} items) - {Total:N2}₽";
}

public class ReceiptItemViewModel
{
    public ReceiptItemViewModel(ReceiptItem item)
    {
        Id = item.Id;
        Name = item.Item?.Name ?? "Unknown";
        ProductName = item.Item?.Product?.Name ?? "Не задана";
        Price = item.Price;
        Quantity = item.Quantity;
        Amount = item.Amount;
        UnitOfMeasure = item.Item?.UnitOfMeasure;

        // Load labels from Item
        if (item.Item?.ItemLabels != null)
        {
            Labels = item.Item.ItemLabels
                .Where(il => il.Label != null)
                .Select(il => new LabelViewModel(il.Label!))
                .ToList();
        }
    }

    public Guid Id { get; }
    public string Name { get; }
    public string ProductName { get; }
    public decimal? Price { get; }
    public decimal Quantity { get; }
    public decimal? Amount { get; }
    public string? UnitOfMeasure { get; }
    public List<LabelViewModel> Labels { get; } = new();
}
