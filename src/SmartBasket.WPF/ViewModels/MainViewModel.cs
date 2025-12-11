using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Entities;
using SmartBasket.Data;
using SmartBasket.Services;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Products;
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
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _cts;

    public MainViewModel(
        SmartBasketDbContext dbContext,
        IReceiptCollectionService receiptCollectionService,
        IProductCleanupService productCleanupService,
        AppSettings settings,
        SettingsService settingsService)
    {
        _dbContext = dbContext;
        _receiptCollectionService = receiptCollectionService;
        _productCleanupService = productCleanupService;
        _settings = settings;
        _settingsService = settingsService;
    }

    // State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotProcessing))]
    private bool _isProcessing;

    public bool IsNotProcessing => !IsProcessing;

    [ObservableProperty] private string _statusText = "Ready";

    // Log - thread-safe via BindingOperations.EnableCollectionSynchronization
    public ObservableCollection<string> LogEntries { get; } = new();
    private readonly object _logEntriesLock = new();

    // Full log for saving (not truncated)
    private readonly List<string> _fullLog = new();
    private readonly object _fullLogLock = new();

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
        // This WPF method allows ObservableCollection to be modified from any thread
        // It uses the provided lock object to synchronize access
        BindingOperations.EnableCollectionSynchronization(LogEntries, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(Receipts, _receiptsLock);
        BindingOperations.EnableCollectionSynchronization(ShopFilters, _shopFiltersLock);
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var entry = $"[{timestamp}] {message}";

        // Save to full log (never truncated) for file export
        lock (_fullLogLock)
        {
            _fullLog.Add(entry);
        }

        // With EnableCollectionSynchronization, we can safely modify from any thread
        // The lock is handled internally by WPF's binding system
        lock (_logEntriesLock)
        {
            LogEntries.Add(entry);
            // Keep only last 500 entries in UI for performance
            while (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Публичный метод для добавления записей в лог из других компонентов
    /// </summary>
    public void AddLogEntry(string message)
    {
        Log(message);
    }

    /// <summary>
    /// Логирует исключение с полной информацией: Message, InnerException, StackTrace
    /// </summary>
    private void LogException(Exception ex, string context = "")
    {
        var prefix = string.IsNullOrEmpty(context) ? "ERROR" : $"ERROR [{context}]";
        Log($"{prefix}: {ex.GetType().Name}: {ex.Message}");

        // Логируем всю цепочку InnerException
        var inner = ex.InnerException;
        var depth = 1;
        while (inner != null)
        {
            var indent = new string(' ', depth * 2);
            Log($"{indent}-> InnerException[{depth}]: {inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
            depth++;
        }

        // StackTrace
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            Log("--- StackTrace ---");
            foreach (var line in ex.StackTrace.Split('\n').Take(15)) // Первые 15 строк
            {
                Log($"  {line.Trim()}");
            }
            if (ex.StackTrace.Split('\n').Length > 15)
            {
                Log("  ... (truncated)");
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
            Log("Operation already in progress, please wait...");
            return;
        }

        // Проверить наличие настроенных источников
        if (_settings.ReceiptSources == null || _settings.ReceiptSources.Count == 0)
        {
            Log("No receipt sources configured. Please configure sources in Settings.");
            StatusText = "No sources configured";
            return;
        }

        var enabledSources = _settings.ReceiptSources.Where(s => s.IsEnabled).ToList();
        if (enabledSources.Count == 0)
        {
            Log("No enabled receipt sources. Please enable at least one source in Settings.");
            StatusText = "No enabled sources";
            return;
        }

        IsProcessing = true;
        _cts = new CancellationTokenSource();
        StatusText = "Collecting receipts...";
        Log("=== Starting receipt collection (Phase 4) ===");
        Log($"Enabled sources: {string.Join(", ", enabledSources.Select(s => s.Name))}");

        try
        {
            var progress = new ThreadSafeProgress<string>(msg => Log(msg));

            var result = await Task.Run(async () =>
                await _receiptCollectionService.CollectAsync(
                    sourceNames: null, // все enabled источники
                    progress: progress,
                    cancellationToken: _cts.Token));

            // Очистка осиротевших Products после обработки
            if (result.ReceiptsSaved > 0)
            {
                Log("");
                Log("=== Cleaning up orphaned products ===");
                var cleanedUp = await Task.Run(async () =>
                    await _productCleanupService.CleanupOrphanedProductsAsync(_cts.Token));
                if (cleanedUp > 0)
                {
                    Log($"Removed {cleanedUp} orphaned products");
                }
            }

            StatusText = $"Done: {result.ReceiptsSaved} saved, {result.ReceiptsSkipped} skipped, {result.Errors} errors";

            // Обновить UI
            if (result.ReceiptsSaved > 0 && !_cts.Token.IsCancellationRequested)
            {
                Log("");
                Log("Reloading receipts...");
                await LoadReceiptsAsync();
                await LoadCategoryTreeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            Log("Cancelled by user");
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            LogException(ex, "CollectReceipts");
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
            Log("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        StatusText = "Cleaning up orphaned products...";
        Log("=== Cleanup Orphaned Products ===");

        try
        {
            // Сначала показать что будет удалено
            var orphaned = await Task.Run(async () =>
                await _productCleanupService.GetOrphanedProductsAsync());

            if (orphaned.Count == 0)
            {
                Log("No orphaned products found");
                StatusText = "No orphaned products";
                return;
            }

            Log($"Found {orphaned.Count} orphaned products:");
            foreach (var p in orphaned.Take(20))
            {
                Log($"  - {p.Name}" + (p.ParentName != null ? $" (parent: {p.ParentName})" : ""));
            }
            if (orphaned.Count > 20)
            {
                Log($"  ... and {orphaned.Count - 20} more");
            }

            var result = MessageBox.Show(
                $"Delete {orphaned.Count} orphaned products?\n\nOrphaned products have no associated items and no child products.",
                "Confirm Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                Log("Cleanup cancelled by user");
                StatusText = "Cleanup cancelled";
                return;
            }

            var deleted = await Task.Run(async () =>
                await _productCleanupService.CleanupOrphanedProductsAsync());

            Log($"Deleted {deleted} orphaned products");
            StatusText = $"Deleted {deleted} products";

            await LoadCategoryTreeAsync();
        }
        catch (Exception ex)
        {
            LogException(ex, "CleanupOrphanedProducts");
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
        Log("Cancellation requested...");
        StatusText = "Cancelling...";
    }

    [RelayCommand]
    private async Task LoadReceiptsAsync()
    {
        if (IsProcessing)
        {
            Log("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        Log("Loading receipts from database...");

        try
        {
            // Run on background thread to completely free UI thread
            var (receipts, shops) = await Task.Run(async () =>
            {
                // Build query with filters
                IQueryable<Receipt> query = _dbContext.Receipts.Include(r => r.Items).ThenInclude(i => i.Item);

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
            lock (_shopFiltersLock)
            {
                ShopFilters.Clear();
                ShopFilters.Add("Все");
                foreach (var shop in shops)
                {
                    ShopFilters.Add(shop);
                }
            }

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

            Log($"Loaded {receipts.Count} receipts, total: {TotalSum:N2}\u20BD");
            StatusText = $"Loaded {receipts.Count} receipts";
        }
        catch (Exception ex)
        {
            LogException(ex, "LoadReceipts");
            StatusText = "Load failed - see log";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        Log($"Applying filters: Shop='{SelectedShopFilter}', From={FilterDateFrom:dd.MM.yyyy}, To={FilterDateTo:dd.MM.yyyy}");
        await LoadReceiptsAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SelectedShopFilter = "Все";
        FilterDateFrom = null;
        FilterDateTo = null;
        Log("Filters cleared");
        await LoadReceiptsAsync();
    }

    [RelayCommand]
    private async Task InitializeDatabaseAsync()
    {
        if (IsProcessing)
        {
            Log("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        StatusText = "Initializing database...";
        Log("=== Initializing Database ===");
        Log($"Connection: {_settings.Database.ConnectionString}");

        try
        {
            // Run on background thread to completely free UI thread
            await Task.Run(async () =>
                await _dbContext.Database.EnsureCreatedAsync());
            Log("Database initialized successfully");
            StatusText = "Database: OK";
        }
        catch (Exception ex)
        {
            LogException(ex, "InitializeDatabase");
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
            Log("Operation already in progress, please wait...");
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
        Log("=== Resetting Database ===");

        try
        {
            // Run on background thread to completely free UI thread
            await Task.Run(async () =>
            {
                await _dbContext.Database.EnsureDeletedAsync();
                await _dbContext.Database.EnsureCreatedAsync();
            });
            Log("Database deleted and recreated");

            // Clear collection (thread-safe via EnableCollectionSynchronization)
            lock (_receiptsLock)
            {
                Receipts.Clear();
            }

            Log("Reset complete");
            StatusText = "Database: Reset OK";
        }
        catch (Exception ex)
        {
            LogException(ex, "ResetDatabase");
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
        lock (_fullLogLock)
        {
            _fullLog.Clear();
        }
        lock (_logEntriesLock)
        {
            LogEntries.Clear();
        }
        Log("Log cleared");
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

            string[] entries;
            lock (_fullLogLock)
            {
                // Save full log (not truncated UI version)
                entries = _fullLog.ToArray();
            }

            File.WriteAllLines(logPath, entries);
            Log($"Log saved to: {logPath} ({entries.Length} entries)");
            StatusText = $"Log saved: {Path.GetFileName(logPath)} ({entries.Length} entries)";
        }
        catch (Exception ex)
        {
            LogException(ex, "SaveLog");
            StatusText = "Log save FAILED";
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        Log("=== Saving Settings ===");
        try
        {
            // TODO: Legacy UI settings removed - need to implement new settings UI
            // Settings are now managed through ReceiptSources, AiProviders, etc.
            // For now, just save current settings as-is
            _settingsService.Save(_settings);

            Log($"Settings saved to: {_settingsService.SettingsPath}");
            StatusText = "Settings saved";
        }
        catch (Exception ex)
        {
            LogException(ex, "SaveSettings");
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
            LogException(ex, "LoadCategoryItems");
        }
    }

    [RelayCommand]
    private async Task LoadCategoryTreeAsync()
    {
        try
        {
            Log("Loading category tree...");

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

            Log($"Loaded {products.Count} categories");
        }
        catch (Exception ex)
        {
            LogException(ex, "LoadCategoryTree");
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
            LogException(ex, "OpenCategoriesDialog");
        }
    }

    private async Task SaveCategoriesAsync(string[] categories)
    {
        try
        {
            Log($"Saving {categories.Length} categories...");

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
                        Log($"  + Added: {category}");
                    }
                }

                await _dbContext.SaveChangesAsync().ConfigureAwait(false);
            });

            Log("Categories saved");
            await LoadCategoryTreeAsync();
        }
        catch (Exception ex)
        {
            LogException(ex, "SaveCategories");
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
            LogException(ex, "ChangeCategory");
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
                    Log($"Created new category: {categoryName}");
                }

                if (product == null)
                {
                    Log($"ERROR: Category not found: {categoryName}");
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
                    Log($"Moved '{existingItem.Name}' to category '{categoryName}'");
                }
            });

            await LoadCategoryTreeAsync();
        }
        catch (Exception ex)
        {
            LogException(ex, "ApplyCategoryChange");
        }
    }

    [RelayCommand]
    private async Task CategorizeAllAsync()
    {
        // TODO: Refactor categorization for new schema
        // Old logic used RawReceiptItem with CategorizationStatus
        // New schema should work directly with Items
        Log("Categorization not yet implemented for new schema");
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
    }

    public Guid Id { get; }
    public string Name { get; }
    public string ProductName { get; }
    public decimal? Price { get; }
    public decimal Quantity { get; }
    public decimal? Amount { get; }
    public string? UnitOfMeasure { get; }
}
