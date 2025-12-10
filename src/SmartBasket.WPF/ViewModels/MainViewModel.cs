using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Entities;
using SmartBasket.Data;
using SmartBasket.Services.Email;
using SmartBasket.Services.Llm;
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
    private readonly IReceiptParsingService _receiptParsingService;
    private readonly ICategoryService _categoryService;
    private readonly IProductClassificationService _classificationService;
    private readonly ILabelAssignmentService _labelAssignmentService;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly ReceiptProcessingService _receiptProcessingService;
    private CancellationTokenSource? _cts;

    public MainViewModel(
        SmartBasketDbContext dbContext,
        IEmailService emailService,
        IOllamaService ollamaService,
        IReceiptParsingService receiptParsingService,
        ICategoryService categoryService,
        IProductClassificationService classificationService,
        ILabelAssignmentService labelAssignmentService,
        AppSettings settings,
        SettingsService settingsService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _ollamaService = ollamaService;
        _receiptParsingService = receiptParsingService;
        _categoryService = categoryService;
        _classificationService = classificationService;
        _labelAssignmentService = labelAssignmentService;
        _settings = settings;
        _settingsService = settingsService;
        _receiptProcessingService = new ReceiptProcessingService(dbContext, labelAssignmentService);

        // Set prompt template paths
        var promptTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt_template.txt");
        _ollamaService.SetPromptTemplatePath(promptTemplatePath);
        _receiptParsingService.SetPromptTemplatePath(promptTemplatePath);

        var categoriesTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt_categories.txt");
        _categoryService.SetPromptTemplatePath(categoriesTemplatePath);

        var classifyTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt_classify_products.txt");
        _classificationService.SetPromptTemplatePath(classifyTemplatePath);

        var labelsTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt_assign_labels.txt");
        _labelAssignmentService.SetPromptTemplatePath(labelsTemplatePath);

        var userLabelsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_labels.txt");
        _receiptProcessingService.SetUserLabelsPath(userLabelsPath);

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

        // YandexGPT settings
        YandexFolderId = settings.YandexGpt.FolderId;
        YandexApiKey = settings.YandexGpt.ApiKey;
        YandexModel = settings.YandexGpt.Model;

        // LLM Provider selection (per operation)
        ParsingProviderIndex = (int)settings.Llm.ParsingProvider;
        ClassificationProviderIndex = (int)settings.Llm.ClassificationProvider;
        LabelsProviderIndex = (int)settings.Llm.LabelsProvider;
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

    // YandexGPT Settings
    [ObservableProperty] private string _yandexFolderId = string.Empty;
    [ObservableProperty] private string _yandexApiKey = string.Empty;
    [ObservableProperty] private string _yandexModel = "yandexgpt-lite";

    // LLM Provider Selection (per operation)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOllamaUsed))]
    [NotifyPropertyChangedFor(nameof(IsYandexGptUsed))]
    private int _parsingProviderIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOllamaUsed))]
    [NotifyPropertyChangedFor(nameof(IsYandexGptUsed))]
    private int _classificationProviderIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOllamaUsed))]
    [NotifyPropertyChangedFor(nameof(IsYandexGptUsed))]
    private int _labelsProviderIndex;

    // Helper properties for UI visibility
    public bool IsOllamaUsed => ParsingProviderIndex == 0 || ClassificationProviderIndex == 0 || LabelsProviderIndex == 0;
    public bool IsYandexGptUsed => ParsingProviderIndex == 1 || ClassificationProviderIndex == 1 || LabelsProviderIndex == 1;

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

        // Categories are now created automatically during classification
        // No need to check for existing categories

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

            // Validate LLM provider settings based on what's selected for each operation
            if (IsOllamaUsed && string.IsNullOrWhiteSpace(ollamaSettings.BaseUrl))
            {
                Log("ERROR: Ollama URL is not configured (used for some operations)");
                StatusText = "Error: Configure Ollama URL";
                return;
            }

            if (IsYandexGptUsed)
            {
                if (string.IsNullOrWhiteSpace(_settings.YandexGpt.ApiKey))
                {
                    Log("ERROR: YandexGPT API key is not configured (used for some operations)");
                    StatusText = "Error: Configure YandexGPT API key";
                    return;
                }
                if (string.IsNullOrWhiteSpace(_settings.YandexGpt.FolderId))
                {
                    Log("ERROR: YandexGPT Folder ID is not configured (used for some operations)");
                    StatusText = "Error: Configure YandexGPT Folder ID";
                    return;
                }
            }

            Log($"Email: {emailSettings.ImapServer}:{emailSettings.ImapPort}, User: {emailSettings.Username}");
            Log($"Filters: Sender='{emailSettings.SenderFilter ?? "(none)"}', Subject='{emailSettings.SubjectFilter ?? "(none)"}', Days={emailSettings.SearchDaysBack}");
            Log($"LLM Providers:");
            Log($"  Parsing: {_settings.Llm.ParsingProvider}");
            Log($"  Classification: {_settings.Llm.ClassificationProvider}");
            Log($"  Labels: {_settings.Llm.LabelsProvider}");
            if (IsOllamaUsed)
            {
                Log($"  Ollama: {ollamaSettings.BaseUrl}, Model: {ollamaSettings.Model}");
            }
            if (IsYandexGptUsed)
            {
                Log($"  YandexGPT: Model: {_settings.YandexGpt.Model}");
            }

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

                    // Parse with LLM (uses selected provider: Ollama or YandexGPT)
                    ParsedReceipt parsedReceipt;
                    try
                    {
                        // Run on background thread to completely free UI thread
                        parsedReceipt = await Task.Run(async () =>
                            await _receiptParsingService.ParseReceiptAsync(
                                email.Body, email.Date, progress, _cts.Token));
                    }
                    catch (Exception parseEx)
                    {
                        Log($"  -> LLM error: {parseEx.Message}");
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

                    // Step 2: Classify products via LLM (uses selected provider)
                    Log($"  -> Classifying {parsedReceipt.Items.Count} items...");

                    var existingProducts = await Task.Run(async () =>
                        await _receiptProcessingService.GetExistingProductsAsync(_cts.Token));

                    var itemNames = parsedReceipt.Items.Select(i => i.Name).ToList();

                    ProductClassificationResult classification;
                    try
                    {
                        classification = await Task.Run(async () =>
                            await _classificationService.ClassifyAsync(
                                itemNames, existingProducts, progress, _cts.Token));
                    }
                    catch (Exception classifyEx)
                    {
                        Log($"  -> Classification error: {classifyEx.Message}");
                        // Continue without classification - will use "Не категоризировано"
                        classification = new ProductClassificationResult
                        {
                            IsSuccess = false,
                            Message = classifyEx.Message
                        };
                    }

                    if (!classification.IsSuccess)
                    {
                        Log($"  -> Classification failed: {classification.Message}, using defaults");
                    }

                    // Step 3: Save to database
                    var receiptDate = parsedReceipt.Date ?? email.Date;
                    var receipt = new Receipt
                    {
                        Shop = parsedReceipt.Shop ?? "Unknown",
                        ReceiptDate = DateTime.SpecifyKind(receiptDate, DateTimeKind.Utc),
                        ReceiptNumber = parsedReceipt.OrderNumber,
                        Total = parsedReceipt.Total,
                        EmailId = email.MessageId,
                        Status = ReceiptStatus.Parsed
                    };

                    _dbContext.Receipts.Add(receipt);
                    await _dbContext.SaveChangesAsync(_cts.Token).ConfigureAwait(false);

                    // Step 4: Process classification - create Products, Items, ReceiptItems, Labels
                    var processingResult = await Task.Run(async () =>
                        await _receiptProcessingService.ProcessClassificationAsync(
                            receipt,
                            parsedReceipt,
                            classification,
                            parsedReceipt.Shop ?? "Unknown",
                            progress,
                            _cts.Token));

                    await SaveEmailHistoryAsync(email, EmailProcessingStatus.Processed);

                    saved++;
                    Log($"  -> OK: {parsedReceipt.Items.Count} items, {processingResult.ProductsCreated} new products, {processingResult.ItemsCreated} new items, {processingResult.LabelsAssigned} labels");

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

            // Reload receipts to show new data
            if (saved > 0 && !_cts.Token.IsCancellationRequested)
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

            // YandexGPT settings
            _settings.YandexGpt.FolderId = YandexFolderId;
            _settings.YandexGpt.ApiKey = YandexApiKey;
            _settings.YandexGpt.Model = YandexModel;

            // LLM Provider selection (per operation)
            _settings.Llm.ParsingProvider = (LlmProviderType)ParsingProviderIndex;
            _settings.Llm.ClassificationProvider = (LlmProviderType)ClassificationProviderIndex;
            _settings.Llm.LabelsProvider = (LlmProviderType)LabelsProviderIndex;

            _settingsService.Save(_settings);

            Log($"Settings saved to: {_settingsService.SettingsPath}");
            Log($"LLM Providers - Parsing: {_settings.Llm.ParsingProvider}, Classification: {_settings.Llm.ClassificationProvider}, Labels: {_settings.Llm.LabelsProvider}");
            StatusText = "Settings saved";
        }
        catch (Exception ex)
        {
            LogException(ex, "SaveSettings");
            StatusText = "Settings save FAILED";
        }
    }

    [RelayCommand]
    private async Task TestYandexGptConnectionAsync()
    {
        if (IsProcessing)
        {
            Log("Operation already in progress, please wait...");
            return;
        }

        IsProcessing = true;
        StatusText = "Testing YandexGPT connection...";
        Log("=== Testing YandexGPT Connection ===");
        Log($"Folder ID: {YandexFolderId}");
        Log($"Model: {YandexModel}");

        try
        {
            // Create temporary settings for test
            var testSettings = new YandexGptSettings
            {
                FolderId = YandexFolderId,
                ApiKey = YandexApiKey,
                Model = YandexModel,
                TimeoutSeconds = 30
            };

            var httpClientFactory = (IHttpClientFactory)App.Services.GetService(typeof(IHttpClientFactory))!;
            var loggerFactory = (Microsoft.Extensions.Logging.ILoggerFactory)App.Services.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory))!;

            var provider = new YandexGptLlmProvider(
                httpClientFactory,
                loggerFactory.CreateLogger<YandexGptLlmProvider>(),
                testSettings);

            var (success, message) = await Task.Run(async () =>
                await provider.TestConnectionAsync());

            Log(message);
            StatusText = success ? "YandexGPT: OK" : "YandexGPT: FAILED";
        }
        catch (Exception ex)
        {
            LogException(ex, "TestYandexGptConnection");
            StatusText = "YandexGPT: FAILED";
        }
        finally
        {
            IsProcessing = false;
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
