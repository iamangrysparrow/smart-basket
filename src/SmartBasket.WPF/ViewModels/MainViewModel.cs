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
using SmartBasket.Services.Email;
using SmartBasket.Services.Ollama;
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
    private readonly IEmailService _emailService;
    private readonly IOllamaService _ollamaService;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _cts;

    public MainViewModel(
        SmartBasketDbContext dbContext,
        IEmailService emailService,
        IOllamaService ollamaService,
        AppSettings settings,
        SettingsService settingsService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _ollamaService = ollamaService;
        _settings = settings;
        _settingsService = settingsService;

        // Set prompt template path (look for prompt_template.txt next to exe)
        var promptTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt_template.txt");
        _ollamaService.SetPromptTemplatePath(promptTemplatePath);

        // Initialize from settings
        ImapServer = settings.Email.ImapServer;
        ImapPort = settings.Email.ImapPort;
        UseSsl = settings.Email.UseSsl;
        EmailUsername = settings.Email.Username;
        EmailPassword = settings.Email.Password;
        SenderFilter = settings.Email.SenderFilter ?? string.Empty;
        SubjectFilter = settings.Email.SubjectFilter ?? string.Empty;
        SearchDaysBack = settings.Email.SearchDaysBack;

        OllamaBaseUrl = settings.Ollama.BaseUrl;
        OllamaModel = settings.Ollama.Model;
    }

    // Email Settings
    [ObservableProperty] private string _imapServer = string.Empty;
    [ObservableProperty] private int _imapPort = 993;
    [ObservableProperty] private bool _useSsl = true;
    [ObservableProperty] private string _emailUsername = string.Empty;
    [ObservableProperty] private string _emailPassword = string.Empty;
    [ObservableProperty] private string _senderFilter = string.Empty;
    [ObservableProperty] private string _subjectFilter = string.Empty;
    [ObservableProperty] private int _searchDaysBack = 30;

    // Ollama Settings
    [ObservableProperty] private string _ollamaBaseUrl = "http://localhost:11434";
    [ObservableProperty] private string _ollamaModel = "mistral:latest";

    // State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotProcessing))]
    private bool _isProcessing;

    public bool IsNotProcessing => !IsProcessing;

    [ObservableProperty] private string _statusText = "Ready";

    // Log - thread-safe via BindingOperations.EnableCollectionSynchronization
    public ObservableCollection<string> LogEntries { get; } = new();
    private readonly object _logEntriesLock = new();

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
    public IEnumerable<RawItemViewModel> FilteredItems
    {
        get
        {
            if (SelectedReceipt?.Items == null)
                return Enumerable.Empty<RawItemViewModel>();

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

        // With EnableCollectionSynchronization, we can safely modify from any thread
        // The lock is handled internally by WPF's binding system
        lock (_logEntriesLock)
        {
            LogEntries.Add(entry);
            // Keep only last 500 entries
            while (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(0);
            }
        }
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

    private EmailSettings GetCurrentEmailSettings() => new()
    {
        ImapServer = ImapServer,
        ImapPort = ImapPort,
        UseSsl = UseSsl,
        Username = EmailUsername,
        Password = EmailPassword,
        SenderFilter = string.IsNullOrWhiteSpace(SenderFilter) ? null : SenderFilter,
        SubjectFilter = string.IsNullOrWhiteSpace(SubjectFilter) ? null : SubjectFilter,
        SearchDaysBack = SearchDaysBack
    };

    private OllamaSettings GetCurrentOllamaSettings() => new()
    {
        BaseUrl = OllamaBaseUrl,
        Model = OllamaModel,
        Temperature = _settings.Ollama.Temperature,
        TimeoutSeconds = _settings.Ollama.TimeoutSeconds
    };

    [RelayCommand]
    private async Task TestEmailConnectionAsync()
    {
        if (IsProcessing)
        {
            Log("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        StatusText = "Testing email connection...";
        Log("=== Testing Email Connection ===");
        Log($"Server: {ImapServer}:{ImapPort} (SSL: {UseSsl})");
        Log($"User: {EmailUsername}");

        try
        {
            var settings = GetCurrentEmailSettings();
            // Run on background thread to completely free UI thread
            var (success, message) = await Task.Run(async () =>
                await _emailService.TestConnectionAsync(settings));

            Log(message);
            StatusText = success ? "Email: OK" : "Email: FAILED";
        }
        catch (Exception ex)
        {
            LogException(ex, "TestEmailConnection");
            StatusText = "Email: FAILED";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task TestOllamaConnectionAsync()
    {
        if (IsProcessing)
        {
            Log("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        StatusText = "Testing Ollama connection...";
        Log("=== Testing Ollama Connection ===");
        Log($"URL: {OllamaBaseUrl}");
        Log($"Model: {OllamaModel}");

        try
        {
            var settings = GetCurrentOllamaSettings();
            // Run on background thread to completely free UI thread
            var (success, message) = await Task.Run(async () =>
                await _ollamaService.TestConnectionAsync(settings));

            Log(message);
            StatusText = success ? "Ollama: OK" : "Ollama: FAILED";
        }
        catch (Exception ex)
        {
            LogException(ex, "TestOllamaConnection");
            StatusText = "Ollama: FAILED";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task FetchAndParseEmailsAsync()
    {
        if (IsProcessing)
        {
            Log("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        _cts = new CancellationTokenSource();
        StatusText = "Validating settings...";
        Log("=== Starting email fetch and parse ===");

        try
        {
            // Validate settings first
            var emailSettings = GetCurrentEmailSettings();
            var ollamaSettings = GetCurrentOllamaSettings();

            if (string.IsNullOrWhiteSpace(emailSettings.ImapServer))
            {
                Log("ERROR: IMAP server is not configured");
                StatusText = "Error: Configure IMAP server";
                return;
            }

            if (string.IsNullOrWhiteSpace(emailSettings.Username) || string.IsNullOrWhiteSpace(emailSettings.Password))
            {
                Log("ERROR: Email credentials are not configured");
                StatusText = "Error: Configure email credentials";
                return;
            }

            if (string.IsNullOrWhiteSpace(ollamaSettings.BaseUrl))
            {
                Log("ERROR: Ollama URL is not configured");
                StatusText = "Error: Configure Ollama URL";
                return;
            }

            Log($"Email: {emailSettings.ImapServer}:{emailSettings.ImapPort}, User: {emailSettings.Username}");
            Log($"Filters: Sender='{emailSettings.SenderFilter ?? "(none)"}', Subject='{emailSettings.SubjectFilter ?? "(none)"}', Days={emailSettings.SearchDaysBack}");
            Log($"Ollama: {ollamaSettings.BaseUrl}, Model: {ollamaSettings.Model}");

            // Use ThreadSafeProgress instead of Progress<T> to avoid SynchronizationContext capture deadlock
            var progress = new ThreadSafeProgress<string>(msg => Log(msg));

            // Fetch emails
            StatusText = "Connecting to email server...";

            IReadOnlyList<EmailMessage> emails;
            try
            {
                // Run on background thread to completely free UI thread
                emails = await Task.Run(async () =>
                    await _emailService.FetchEmailsAsync(emailSettings, progress, _cts.Token));
            }
            catch (Exception ex)
            {
                LogException(ex, "FetchEmails");
                StatusText = "Email connection failed";
                return;
            }

            if (emails.Count == 0)
            {
                StatusText = "No emails found";
                Log("No emails found matching filters. Try adjusting filters or search period.");
                return;
            }

            Log($"");
            Log($"=== Found {emails.Count} emails ===");
            for (int i = 0; i < emails.Count; i++)
            {
                var e = emails[i];
                Log($"  [{i + 1}] {e.Date:MM-dd HH:mm} | {e.From} | {e.Subject}");
            }
            Log($"");

            int processed = 0;
            int saved = 0;
            int skipped = 0;
            int errors = 0;

            foreach (var email in emails)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    Log("Cancelled by user");
                    break;
                }

                try
                {
                    // Check if already processed
                    var existingEmail = await _dbContext.EmailHistory
                        .FirstOrDefaultAsync(e => e.EmailId == email.MessageId, _cts.Token).ConfigureAwait(false);

                    if (existingEmail != null)
                    {
                        Log($"[SKIP] Already processed: {email.Subject}");
                        skipped++;
                        continue;
                    }

                    processed++;
                    StatusText = $"Parsing {processed}/{emails.Count}...";
                    Log($"");
                    Log($"[{processed}/{emails.Count}] Processing email:");
                    Log($"  Subject: {email.Subject}");
                    Log($"  From: {email.From}");
                    Log($"  Date: {email.Date:yyyy-MM-dd HH:mm:ss}");
                    Log($"  Body size: {email.Body?.Length ?? 0} chars");

                    if (string.IsNullOrEmpty(email.Body))
                    {
                        Log("  -> Empty body, skipping");
                        await SaveEmailHistoryAsync(email, EmailProcessingStatus.Skipped, "Empty body");
                        skipped++;
                        continue;
                    }

                    // Parse with Ollama
                    ParsedReceipt parsedReceipt;
                    try
                    {
                        // Run on background thread to completely free UI thread
                        parsedReceipt = await Task.Run(async () =>
                            await _ollamaService.ParseReceiptAsync(
                                ollamaSettings, email.Body, email.Date, progress, _cts.Token));
                    }
                    catch (Exception parseEx)
                    {
                        Log($"  -> Ollama error: {parseEx.Message}");
                        await SaveEmailHistoryAsync(email, EmailProcessingStatus.Failed, parseEx.Message);
                        errors++;
                        continue;
                    }

                    if (!parsedReceipt.IsSuccess || parsedReceipt.Items.Count == 0)
                    {
                        Log($"  -> No items: {parsedReceipt.Message}");
                        await SaveEmailHistoryAsync(email, EmailProcessingStatus.Skipped, parsedReceipt.Message);
                        skipped++;
                        continue;
                    }

                    // Save to database
                    var receiptDate = parsedReceipt.Date ?? email.Date;
                    var receipt = new Receipt
                    {
                        Shop = parsedReceipt.Shop ?? "Unknown",
                        ReceiptDate = DateTime.SpecifyKind(receiptDate, DateTimeKind.Utc),
                        ReceiptNumber = parsedReceipt.OrderNumber,
                        Total = parsedReceipt.Total,
                        EmailId = email.MessageId,
                        RawContent = email.Body,
                        Status = ReceiptStatus.Parsed
                    };

                    foreach (var item in parsedReceipt.Items)
                    {
                        receipt.RawItems.Add(new RawReceiptItem
                        {
                            RawName = item.Name ?? "Unknown",
                            RawVolume = item.Volume,
                            RawPrice = item.Price?.ToString(),
                            Unit = item.Unit,
                            Quantity = item.Quantity,
                            CategorizationStatus = CategorizationStatus.Pending
                        });
                    }

                    _dbContext.Receipts.Add(receipt);
                    await _dbContext.SaveChangesAsync(_cts.Token).ConfigureAwait(false);

                    await SaveEmailHistoryAsync(email, EmailProcessingStatus.Processed);

                    saved++;
                    Log($"  -> OK: {parsedReceipt.Items.Count} items from {parsedReceipt.Shop}");

                    // Add to collection (thread-safe via EnableCollectionSynchronization)
                    var receiptVm = new ReceiptViewModel(receipt);
                    lock (_receiptsLock)
                    {
                        Receipts.Add(receiptVm);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log("Cancelled");
                    break;
                }
                catch (Exception emailEx)
                {
                    LogException(emailEx, "ProcessEmail");
                    errors++;
                    try
                    {
                        await SaveEmailHistoryAsync(email, EmailProcessingStatus.Failed, emailEx.ToString());
                    }
                    catch { }
                }
            }

            StatusText = $"Done: {saved} saved, {skipped} skipped, {errors} errors";
            Log($"=== Completed: {saved} saved, {skipped} skipped, {errors} errors ===");
        }
        catch (OperationCanceledException)
        {
            Log("Cancelled by user");
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            LogException(ex, "FetchAndParseEmails");
            StatusText = "Error occurred - see log";
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task SaveEmailHistoryAsync(EmailMessage email, EmailProcessingStatus status, string? errorMessage = null)
    {
        var history = new EmailHistory
        {
            EmailId = email.MessageId,
            Sender = email.From,
            Subject = email.Subject,
            ReceivedAt = email.Date,
            ProcessedAt = DateTime.UtcNow,
            Status = status,
            ErrorMessage = errorMessage
        };

        _dbContext.EmailHistory.Add(history);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);
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
                IQueryable<Receipt> query = _dbContext.Receipts.Include(r => r.RawItems);

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

            Log($"Loaded {receipts.Count} receipts, total: {TotalSum:N2}₽");
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
            lock (_logEntriesLock)
            {
                entries = LogEntries.ToArray();
            }

            File.WriteAllLines(logPath, entries);
            Log($"Log saved to: {logPath}");
            StatusText = $"Log saved: {Path.GetFileName(logPath)}";
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
            _settings.Email.ImapServer = ImapServer;
            _settings.Email.ImapPort = ImapPort;
            _settings.Email.UseSsl = UseSsl;
            _settings.Email.Username = EmailUsername;
            _settings.Email.Password = EmailPassword;
            _settings.Email.SenderFilter = string.IsNullOrWhiteSpace(SenderFilter) ? null : SenderFilter;
            _settings.Email.SubjectFilter = string.IsNullOrWhiteSpace(SubjectFilter) ? null : SubjectFilter;
            _settings.Email.SearchDaysBack = SearchDaysBack;

            _settings.Ollama.BaseUrl = OllamaBaseUrl;
            _settings.Ollama.Model = OllamaModel;

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
        ItemCount = receipt.RawItems.Count;
        Items = receipt.RawItems.Select(i => new RawItemViewModel(i)).ToList();
    }

    public Guid Id { get; }
    public string Shop { get; }
    public DateTime Date { get; }
    public string? OrderNumber { get; }
    public decimal? Total { get; }
    public string Status { get; }
    public int ItemCount { get; }
    public List<RawItemViewModel> Items { get; }

    public string DisplayText => $"{Date:dd.MM.yyyy} - {Shop} ({ItemCount} items) - {Total:N2}₽";
}

public class RawItemViewModel
{
    public RawItemViewModel(RawReceiptItem item)
    {
        Name = item.RawName;
        Volume = item.RawVolume;
        Price = item.RawPrice;
        Unit = item.Unit;
        Quantity = item.Quantity;
        Status = item.CategorizationStatus.ToString();
    }

    public string Name { get; }
    public string? Volume { get; }
    public string? Price { get; }
    public string? Unit { get; }
    public decimal Quantity { get; }
    public string Status { get; }
}
