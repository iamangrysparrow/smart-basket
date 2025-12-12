# WPF Development Rules

## Critical Rules for WPF + async/await

### 1. Never use `Dispatcher.Invoke` with `lock` inside
```csharp
// BAD - causes deadlocks and infinite error loops
Application.Current.Dispatcher.Invoke(() =>
{
    lock (_lock) { collection.Add(item); }
});

// GOOD - use BindingOperations.EnableCollectionSynchronization
lock (_lock) { collection.Add(item); }
```

### 2. Thread-safe ObservableCollection
```csharp
// In constructor or initialization
public ObservableCollection<string> Items { get; } = new();
private readonly object _itemsLock = new();

// Call from UI thread during init
public void EnableCollectionSynchronization()
{
    BindingOperations.EnableCollectionSynchronization(Items, _itemsLock);
}

// Now safe from any thread
lock (_itemsLock) { Items.Add(item); }
```

### 3. Offload work from UI thread
```csharp
// BAD - blocks UI, can deadlock with SynchronizationContext
var result = await _service.DoWorkAsync();

// GOOD - runs on ThreadPool, UI thread is free
var result = await Task.Run(async () =>
    await _service.DoWorkAsync());
```

### 4. Never use Progress<T> in WPF ViewModels
```csharp
// BAD - Progress<T> captures SynchronizationContext -> deadlock
var progress = new Progress<string>(msg => Log(msg));

// GOOD - use simple delegate wrapper
internal class ThreadSafeProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public ThreadSafeProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}

var progress = new ThreadSafeProgress<string>(msg => Log(msg));
```

### 5. Always use ConfigureAwait(false) in library code
```csharp
// Services, Data Access - always ConfigureAwait(false)
await client.ConnectAsync(server, port, ssl, token).ConfigureAwait(false);
await _dbContext.SaveChangesAsync(token).ConfigureAwait(false);
```

### 6. Prevent concurrent command execution (PROPER WAY)
```csharp
// BAD - manual check, button stays enabled
[RelayCommand]
private async Task DoWorkAsync()
{
    if (IsProcessing) return;  // Button still clickable!
    IsProcessing = true;
    try { /* work */ }
    finally { IsProcessing = false; }
}

// GOOD - CanExecute auto-disables button
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(DoWorkCommand))]  // <-- CRITICAL!
private bool _isProcessing;

private bool CanDoWork() => !IsProcessing;

[RelayCommand(CanExecute = nameof(CanDoWork))]
private async Task DoWorkAsync()
{
    try
    {
        IsProcessing = true;
        // work...
    }
    finally { IsProcessing = false; }
}
```
**Key points:**
- `NotifyCanExecuteChangedFor` tells the command to re-evaluate CanExecute when property changes
- Button auto-disables when `IsProcessing = true`
- No need for manual `if (IsProcessing) return` check

### 7. DbContext Concurrency in WPF - CRITICAL!

**Problem**: In WPF, `Scoped` services act like `Singleton` because there's no request scope like in ASP.NET. Multiple ViewModels sharing one `DbContext` causes:
```
InvalidOperationException: 'A second operation was started on this context instance
before a previous operation completed.'
```

**Root cause**: ViewModels inject services that share one `DbContext`, then call async methods simultaneously.

**Solution**: Use `IDbContextFactory` + `Transient` registration:

```csharp
// In DbContextFactory.cs
public static IServiceCollection AddSmartBasketDbContext(
    this IServiceCollection services,
    DatabaseProviderType providerType,
    string connectionString)
{
    // Register factory (creates new DbContext on each call)
    services.AddDbContextFactory<SmartBasketDbContext>(options =>
    {
        switch (providerType)
        {
            case DatabaseProviderType.PostgreSQL:
                options.UseNpgsql(connectionString);
                break;
            case DatabaseProviderType.SQLite:
                options.UseSqlite(connectionString);
                break;
        }
    });

    // Register DbContext as Transient via factory
    // Each injection gets NEW instance - no concurrency issues!
    services.AddTransient(sp =>
        sp.GetRequiredService<IDbContextFactory<SmartBasketDbContext>>().CreateDbContext());

    return services;
}
```

```csharp
// In App.xaml.cs - register services as Transient (NOT Scoped!)
// Each service gets its own DbContext instance
services.AddTransient<IProductService, ProductService>();
services.AddTransient<ILabelService, LabelService>();
services.AddTransient<IItemService, ItemService>();
services.AddTransient<IReceiptCollectionService, ReceiptCollectionService>();

// ViewModels also Transient (they get fresh services each time)
services.AddTransient<MainViewModel>();
services.AddTransient<ProductsItemsViewModel>();
```

**Key rules**:
1. **NEVER use `AddScoped` for DB services in WPF** - there's no scope!
2. **NEVER use `Task.Run()` around async service methods** - they already offload to ThreadPool
3. **Each ViewModel should get fresh service instances** via Transient DI

```csharp
// BAD - Task.Run wraps async call, creates thread chaos
var products = await Task.Run(() => _productService.GetAllWithHierarchyAsync());

// GOOD - just await the async method directly
var products = await _productService.GetAllWithHierarchyAsync();
```

### 8. Global exception handlers
```csharp
// In App.xaml.cs OnStartup
AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
DispatcherUnhandledException += OnDispatcherUnhandledException;
TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
{
    Debug.WriteLine($"ERROR: {e.Exception.Message}");
    e.Handled = true; // Prevent crash
}
```

### 9. Comprehensive Logging - MANDATORY for all operations

**Logging Levels (default: DEBUG):**
- `DEBUG` - Detailed flow: method entry/exit, parameters, intermediate values
- `INFO` - Key operations: user actions, successful completions, state changes
- `WARNING` - Recoverable issues: retries, fallbacks, missing optional config
- `ERROR` - Failures: exceptions, validation errors, API errors

```csharp
// Log level enum
public enum LogLevel { DEBUG, INFO, WARNING, ERROR }

// Log method with level
private void Log(string message, LogLevel level = LogLevel.DEBUG)
{
    var prefix = level switch
    {
        LogLevel.DEBUG => "[DEBUG]",
        LogLevel.INFO => "[INFO]",
        LogLevel.WARNING => "[WARN]",
        LogLevel.ERROR => "[ERROR]",
        _ => "[LOG]"
    };
    AddLogEntry($"{prefix} {message}");
}
```

**EVERY operation MUST log:**
```csharp
// BAD - silent operation, impossible to debug
private async Task SaveAsync()
{
    await _service.SaveAsync(_settings);
}

// GOOD - full traceability
private async Task SaveAsync()
{
    Log($"SaveAsync called", LogLevel.DEBUG);
    Log($"Saving {Sources.Count} sources, {Parsers.Count} parsers", LogLevel.DEBUG);

    try
    {
        Log($"Writing to: {_settingsService.SettingsPath}", LogLevel.DEBUG);
        await _service.SaveAsync(_settings);
        Log($"Save completed successfully", LogLevel.INFO);
    }
    catch (Exception ex)
    {
        Log($"Save failed: {ex.Message}", LogLevel.ERROR);
        LogException(ex, "SaveAsync");
        throw;
    }
}
```

**What to log:**
- Method entry with key parameters (mask passwords/keys!)
- Decision points: `if/else` branches taken
- External calls: API requests/responses, file I/O
- State changes: before/after values
- Completion status: success/failure with details

**Exception Logging - full details:**
```csharp
private void LogException(Exception ex, string context = "")
{
    Log($"[{context}] {ex.GetType().Name}: {ex.Message}", LogLevel.ERROR);

    var inner = ex.InnerException;
    var depth = 1;
    while (inner != null)
    {
        Log($"  -> Inner[{depth}]: {inner.GetType().Name}: {inner.Message}", LogLevel.ERROR);
        inner = inner.InnerException;
        depth++;
    }

    if (!string.IsNullOrEmpty(ex.StackTrace))
    {
        Log("--- StackTrace ---", LogLevel.DEBUG);
        foreach (var line in ex.StackTrace.Split('\n').Take(15))
            Log($"  {line.Trim()}", LogLevel.DEBUG);
    }
}
```

**Masking sensitive data:**
```csharp
// NEVER log passwords, API keys, tokens directly
Log($"ApiKey: {apiKey}");  // BAD!

// Mask sensitive values
Log($"ApiKey: {(string.IsNullOrEmpty(apiKey) ? "NOT SET" : "***SET***")}");
Log($"Password: {new string('*', password?.Length ?? 0)}");

// For JSON with secrets
json = Regex.Replace(json,
    @"""(Password|ApiKey|Token)""\s*:\s*""[^""]+""",
    @"""$1"": ""***MASKED***""");
```

---

## CommunityToolkit.Mvvm - Critical Rules

### 10. RelayCommand naming convention
```csharp
// Method name -> Generated command name
private void Save() {}           // -> SaveCommand
private async Task SaveAsync() {} // -> SaveCommand (Async stripped!)
private void OnSave() {}         // -> OnSaveCommand

// WRONG - trying to use SaveAsyncCommand
Command="{Binding SaveAsyncCommand}"  // DOES NOT EXIST!

// CORRECT
Command="{Binding SaveCommand}"
```

### 11. Logging from modal dialogs (ShowDialog)
```csharp
// BAD - blocks if called from ShowDialog context
Action<string> log = message => _viewModel.AddLogEntry(message);

// GOOD - async dispatch, doesn't block modal dialog
Action<string> log = message =>
{
    Dispatcher.BeginInvoke(() => _viewModel.AddLogEntry(message));
};

var dialog = new SettingsWindow(vm);
dialog.ShowDialog();  // Log calls won't block now
```

### 12. ObservableCollection - don't Clear() if bound to ComboBox
```csharp
// BAD - clears selection in bound ComboBox/ListBox
AvailableItems.Clear();
foreach (var item in newItems)
    AvailableItems.Add(item);

// GOOD - sync without losing selection
private void SyncCollection(IList<string> newItems)
{
    // Remove extra
    while (Collection.Count > newItems.Count)
        Collection.RemoveAt(Collection.Count - 1);

    // Update existing, add new
    for (int i = 0; i < newItems.Count; i++)
    {
        if (i < Collection.Count)
        {
            if (Collection[i] != newItems[i])
                Collection[i] = newItems[i];
        }
        else
        {
            Collection.Add(newItems[i]);
        }
    }
}
```

### 13. Track renames for cascading updates
```csharp
// When entity Key can be renamed, track original for updates
public class EntityViewModel
{
    private string _originalKey = string.Empty;

    public string OriginalKey => _originalKey;
    public bool WasRenamed => !string.IsNullOrEmpty(_originalKey) && _originalKey != Key;

    public void CommitKey() => _originalKey = Key;  // Call after save
}

// In Save command - update references
foreach (var entity in Entities)
{
    if (entity.WasRenamed)
    {
        // Update all references from OldKey to NewKey
        if (Settings.Reference == entity.OriginalKey)
            Settings.Reference = entity.Key;
    }
    entity.CommitKey();
}
```

---

## Theme System

### Architecture
```
src/SmartBasket.WPF/
├── Themes/
│   ├── ThemeManager.cs      # Static theme switcher
│   ├── LightTheme.xaml      # Light color palette
│   ├── DarkTheme.xaml       # Dark color palette
│   ├── Icons.xaml           # Path-based vector icons
│   └── SharedStyles.xaml    # Reusable styles (use DynamicResource)
├── App.xaml                 # Merges theme resources
```

### Using ThemeManager
```csharp
// Apply theme on startup (App.xaml.cs)
ThemeManager.ApplyTheme(AppTheme.Light);

// Toggle theme
ThemeManager.ToggleTheme();

// Get current theme
var current = ThemeManager.CurrentTheme;

// Subscribe to changes
ThemeManager.ThemeChanged += (_, theme) => UpdateUI();
```

### Color Resources (DynamicResource required!)
```xml
<!-- Background layers -->
BackgroundBrush        <!-- Main window background -->
SurfaceBrush           <!-- Cards, panels -->
SurfaceAltBrush        <!-- Alternate surface -->
ToolbarBrush           <!-- Toolbars, headers -->

<!-- Borders -->
BorderBrush            <!-- Primary borders -->
BorderLightBrush       <!-- Subtle borders -->
DividerBrush           <!-- Separators -->

<!-- Text -->
TextPrimaryBrush       <!-- Main text -->
TextSecondaryBrush     <!-- Muted text -->
TextTertiaryBrush      <!-- Very muted text -->
TextOnAccentBrush      <!-- Text on accent buttons -->

<!-- Accent -->
AccentBrush            <!-- Primary action color -->
AccentHoverBrush       <!-- Hover state -->
AccentLightBrush       <!-- Light accent background -->
AccentGradientBrush    <!-- Gradient for headers -->

<!-- Semantic -->
SuccessBrush / SuccessLightBrush
ErrorBrush / ErrorLightBrush
WarningBrush / WarningLightBrush
InfoBrush / InfoLightBrush

<!-- Interactive states -->
HoverBrush             <!-- Mouse hover -->
SelectedBrush          <!-- Selected item -->
PressedBrush           <!-- Pressed state -->

<!-- Log panel -->
LogBackgroundBrush / LogHeaderBrush / LogTextBrush
```

### Style Keys
```xml
<!-- Buttons -->
PrimaryButton          <!-- Main action (accent colored) -->
SecondaryButton        <!-- Secondary action (neutral) -->
DangerButton           <!-- Destructive action (red) -->
ToolbarButton          <!-- Compact toolbar button -->
IconButtonBase         <!-- Icon-only button -->

<!-- Containers -->
Card                   <!-- Elevated surface with shadow -->
SectionHeader          <!-- Panel header background -->
Badge                  <!-- Small info tag -->

<!-- Text -->
H1, H2, H3             <!-- Headings -->
TextPrimary            <!-- Normal text -->
TextSecondary          <!-- Muted text -->
PriceText              <!-- Green price display -->
BadgeText              <!-- Badge label text -->

<!-- Lists -->
ReceiptListBox         <!-- Styled ListBox -->
ReceiptListBoxItem     <!-- List item with selection -->
ReceiptCard            <!-- Receipt item card -->
ItemCard               <!-- Product item card -->

<!-- Controls -->
TextInput              <!-- Styled TextBox -->
ModernTabControl       <!-- Tab container -->
ModernTabItem          <!-- Tab header -->
ModernTreeView         <!-- Tree container -->
ModernDataGrid         <!-- Data grid -->
ToolbarSeparator       <!-- Vertical separator -->
```

### Icon Usage
```xml
<!-- Icons are Geometry resources, use with Path -->
<Path Data="{StaticResource IconEmail}"
      Fill="{DynamicResource TextPrimaryBrush}"
      Width="14" Height="14" Stretch="Uniform"/>

<!-- Available icons -->
IconEmail, IconCancel, IconRefresh, IconDatabase, IconTrash,
IconLog, IconCategories, IconCategorize, IconSettings, IconSave,
IconPlug, IconClear, IconEdit, IconSearch, IconFilter,
IconArrowDown, IconArrowUp, IconPopOut, IconDock,
IconSun, IconMoon, IconReceipt, IconProducts, IconInfo,
IconCheck, IconWarning
```

### Creating New Views

1. **Always use DynamicResource for theme colors:**
```xml
<!-- CORRECT -->
Background="{DynamicResource BackgroundBrush}"

<!-- WRONG - won't update on theme change -->
Background="{StaticResource BackgroundBrush}"
```

2. **Use StaticResource for styles (they don't change):**
```xml
<Button Style="{StaticResource PrimaryButton}"/>
<Border Style="{StaticResource Card}"/>
```

3. **Set Window background:**
```xml
<Window Background="{DynamicResource BackgroundBrush}">
```

4. **Use shared styles instead of inline:**
```xml
<!-- WRONG -->
<TextBlock FontWeight="Bold" FontSize="20" Foreground="#1B1B1B"/>

<!-- CORRECT -->
<TextBlock Style="{StaticResource H1}"/>
```

---

## MVVM Architecture

### One ViewModel per View
Each complex UserControl should have its **own dedicated ViewModel**. This is the recommended MVVM pattern.

```
┌─────────────────────────────────────────────────────────────┐
│ MainWindow                                                   │
│   └─ DataContext: MainViewModel                             │
│       ├─ HistoryView (UserControl)                          │
│       │     └─ Inherits MainViewModel (simple, no own VM)   │
│       ├─ ProductsItemsView (UserControl)                    │
│       │     └─ DataContext: ProductsItemsViewModel          │
│       └─ SettingsView (UserControl)                         │
│             └─ Inherits MainViewModel (simple, no own VM)   │
└─────────────────────────────────────────────────────────────┘
```

### When to create separate ViewModel:
- Complex UserControl with its own business logic
- Reusable components
- Need for unit testing in isolation
- Many commands and properties (>10-15)

### When to inherit parent DataContext:
- Simple visual components
- Tightly coupled to parent
- Few bindings, no commands

### ViewModel instantiation in XAML
```xml
<UserControl x:Class="MyApp.Views.ProductsItemsView" ...>
    <UserControl.DataContext>
        <viewModels:ProductsItemsViewModel/>
    </UserControl.DataContext>
    <!-- Or via ViewModelLocator / DI -->
</UserControl>
```

### Services for Business Logic
- Extract DB operations into services (IProductService, ILabelService)
- ViewModels call services, not DbContext directly
- Services are injected via DI
- Makes ViewModels testable

```csharp
public class ProductsItemsViewModel : ObservableObject
{
    private readonly IProductService _productService;
    private readonly ILabelService _labelService;

    public ProductsItemsViewModel(IProductService productService, ILabelService labelService)
    {
        _productService = productService;
        _labelService = labelService;
    }
}
```

### Communication between ViewModels
Use **WeakReferenceMessenger** from CommunityToolkit.Mvvm:

```csharp
// Define message
public record ProductUpdatedMessage(Guid ProductId);

// Send from one VM
WeakReferenceMessenger.Default.Send(new ProductUpdatedMessage(product.Id));

// Receive in another VM (register in constructor)
WeakReferenceMessenger.Default.Register<ProductUpdatedMessage>(this, (r, m) =>
{
    // Handle message
});
```

### Decompose Large Views
Break down complex UI into smaller UserControls:

```
ProductsItemsView
├── ProductTreePanel (UserControl) - TreeView with products
├── ItemsGridPanel (UserControl) - DataGrid with items
└── Toolbar (inline or UserControl)
```

Each sub-control can either:
1. Have own ViewModel (if complex)
2. Bind to parent's ViewModel properties (if simple)

---

## Project Structure
```
SmartBasket/
├── SmartBasket.Core/        # Entities, Configuration, Interfaces
├── SmartBasket.Data/        # EF Core DbContext, Migrations
├── SmartBasket.Services/    # Business logic (Email, Ollama)
├── SmartBasket.WPF/         # WPF UI (MVVM with CommunityToolkit)
│   ├── Themes/              # Color palettes, icons, shared styles
│   ├── Views/               # Dialogs and secondary windows
│   ├── ViewModels/          # MVVM ViewModels
│   └── Services/            # WPF-specific services
└── SmartBasket.CLI/         # Console tools for testing
```

## Key Libraries
- **CommunityToolkit.Mvvm** - MVVM: `[ObservableProperty]`, `[RelayCommand]`
- **MailKit** - IMAP email fetching
- **Entity Framework Core** - PostgreSQL/SQLite
- **Microsoft.Extensions.DependencyInjection** - DI container

## Detachable Log Panel

The log panel supports VS-style docking:
- **Docked**: Resizable panel at bottom with GridSplitter
- **Detached**: Separate window (LogWindow.xaml)
- **Dock back**: Click dock button to return to main window

```csharp
// In MainWindow.xaml.cs
private LogWindow? _logWindow;

private void PopOutLog_Click(object sender, RoutedEventArgs e)
{
    LogPanel.Visibility = Visibility.Collapsed;
    _logWindow = new LogWindow(_viewModel) { Owner = this };
    _logWindow.LogWindowClosed += OnLogWindowClosed;
    _logWindow.Show();
}
```

## UI Development Checklist

### Before implementing any Button with Command:
- [ ] Method `DoWorkAsync()` generates `DoWorkCommand` (not `DoWorkAsyncCommand`)
- [ ] Add `CanExecute` method if button should be disabled during operation
- [ ] Add `[NotifyCanExecuteChangedFor(nameof(DoWorkCommand))]` to state property
- [ ] Test: click button rapidly 10 times - should execute only once

### Before implementing Settings/Editor with Save:
- [ ] Track `_originalKey` for entities that can be renamed
- [ ] After Save: update all references to renamed entities
- [ ] After Save: call `CommitKey()` on all entities
- [ ] After Save: sync collections WITHOUT `Clear()` (use index-based update)
- [ ] Test: rename entity, save, check all references updated

### Before opening modal dialogs (ShowDialog):
- [ ] Wrap any callback to main window in `Dispatcher.BeginInvoke()`
- [ ] Especially logging callbacks - they will deadlock otherwise
- [ ] Test: perform action in dialog that logs, verify main window responsive

### Dialog Window Sizing:
- [ ] **NEVER use fixed Height** for dialogs with dynamic content
- [ ] Use `SizeToContent="Height"` (or `WidthAndHeight`) - window adapts to content
- [ ] Fixed `Width` is OK for consistent layout
- [ ] If content may overflow: add `MaxHeight` and `ScrollViewer`
```xml
<!-- WRONG - content may be clipped if doesn't fit -->
<Window Width="450" Height="350" ...>

<!-- CORRECT - auto-size to content -->
<Window Width="450" SizeToContent="Height" ...>

<!-- CORRECT - with max height limit -->
<Window Width="450" SizeToContent="Height" MaxHeight="600" ...>
```
- [ ] Test: open dialog with varying content (0 labels vs 10 labels) - all should be visible

### Before adding editable ComboBox bound to collection:
- [ ] `IsEditable="True"` + `Text="{Binding Value}"` + `ItemsSource="{Binding Options}"`
- [ ] Never `Clear()` the ItemsSource collection - it resets Text binding
- [ ] Test: select value, trigger refresh, verify selection preserved

### TextBox for decimal values (Temperature, etc):
- [ ] Use `UpdateSourceTrigger=LostFocus` (not PropertyChanged)
- [ ] Create converter that handles both `.` and `,` as decimal separator
- [ ] Use `CultureInfo.InvariantCulture` for parsing
- [ ] Test: enter "0.5" and "0,5" - both should work

### Logging - MANDATORY for every new method/feature:
- [ ] Log method entry with key parameters (DEBUG level)
- [ ] Log decision branches taken (DEBUG level)
- [ ] Log external calls: URL, request summary (DEBUG level)
- [ ] Log success completion (INFO level)
- [ ] Log failures with full exception details (ERROR level)
- [ ] NEVER log passwords, API keys, tokens - mask them!
- [ ] Test: perform operation, verify log shows complete flow

### ListBox with virtualization (for large collections):
- [ ] Set `VirtualizingPanel.IsVirtualizing="True"`
- [ ] Set `VirtualizingPanel.VirtualizationMode="Recycling"`
- [ ] Set `ScrollViewer.CanContentScroll="True"` (required for virtualization)
- [ ] **CRITICAL**: Set fixed `Height` on `ListBoxItem` in `ItemContainerStyle`
- [ ] Use `TextBlock` in `ItemTemplate`, NOT `TextBox` (TextBox breaks virtualization on click)
- [ ] For multiline log messages: split into separate entries in ViewModel

```xml
<!-- Example: Fixed height ListBox for logs -->
<ListBox ItemsSource="{Binding LogEntries}"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         ScrollViewer.CanContentScroll="True">
    <ListBox.ItemContainerStyle>
        <Style TargetType="ListBoxItem">
            <Setter Property="Height" Value="18"/>  <!-- CRITICAL for smooth scroll -->
        </Style>
    </ListBox.ItemContainerStyle>
</ListBox>
```

---

## Quick Start Prompt

```
This is a WPF .NET 8 application using CommunityToolkit.Mvvm.

Key rules:
1. Use BindingOperations.EnableCollectionSynchronization for thread-safe collections
2. DON'T wrap async service calls in Task.Run() - await directly
3. Use ThreadSafeProgress<T> instead of Progress<T>
4. Always ConfigureAwait(false) in services
5. Never use lock inside Dispatcher.Invoke
6. [RelayCommand] on SaveAsync() generates SaveCommand (not SaveAsyncCommand!)
7. DbContext concurrency: use AddDbContextFactory + Transient services (NOT Scoped!)
8. Use CanExecute + NotifyCanExecuteChangedFor to disable buttons during operations
9. Use Dispatcher.BeginInvoke for callbacks from modal dialogs
10. Don't Clear() ObservableCollection bound to ComboBox - sync by index instead
11. LOG EVERYTHING! Every method must log: entry, parameters, decisions, results
    - Levels: DEBUG (default), INFO, WARNING, ERROR
    - Mask passwords/API keys in logs

Theme system:
- Use DynamicResource for colors (theme switching)
- Use StaticResource for styles and icons
- Themes: LightTheme.xaml, DarkTheme.xaml
- ThemeManager.ToggleTheme() to switch

Project: SmartBasket - receipt parser with email (MailKit),
Ollama LLM parsing, PostgreSQL storage (EF Core).
```
