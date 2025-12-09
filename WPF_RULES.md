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

### 6. Prevent concurrent command execution
```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsNotProcessing))]
private bool _isProcessing;

public bool IsNotProcessing => !IsProcessing;

[RelayCommand]
private async Task DoWorkAsync()
{
    if (IsProcessing) return;
    IsProcessing = true;
    try { /* work */ }
    finally { IsProcessing = false; }
}
```

### 7. Global exception handlers
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

### 8. Exception Logging - ALWAYS use DEBUG level with full details
```csharp
// BAD
Log($"ERROR: {ex.Message}");

// GOOD - full info: type, message, InnerException chain, StackTrace
private void LogException(Exception ex, string context = "")
{
    var prefix = string.IsNullOrEmpty(context) ? "ERROR" : $"ERROR [{context}]";
    Log($"{prefix}: {ex.GetType().Name}: {ex.Message}");

    var inner = ex.InnerException;
    var depth = 1;
    while (inner != null)
    {
        var indent = new string(' ', depth * 2);
        Log($"{indent}-> InnerException[{depth}]: {inner.GetType().Name}: {inner.Message}");
        inner = inner.InnerException;
        depth++;
    }

    if (!string.IsNullOrEmpty(ex.StackTrace))
    {
        Log("--- StackTrace ---");
        foreach (var line in ex.StackTrace.Split('\n').Take(15))
        {
            Log($"  {line.Trim()}");
        }
    }
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

## Quick Start Prompt

```
This is a WPF .NET 8 application using CommunityToolkit.Mvvm.

Key rules:
1. Use BindingOperations.EnableCollectionSynchronization for thread-safe collections
2. Wrap service calls in Task.Run() to free UI thread
3. Use ThreadSafeProgress<T> instead of Progress<T>
4. Always ConfigureAwait(false) in services
5. Never use lock inside Dispatcher.Invoke

Theme system:
- Use DynamicResource for colors (theme switching)
- Use StaticResource for styles and icons
- Themes: LightTheme.xaml, DarkTheme.xaml
- ThemeManager.ToggleTheme() to switch

Project: SmartBasket - receipt parser with email (MailKit),
Ollama LLM parsing, PostgreSQL storage (EF Core).
```
