# WPF Async Development Rules

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
// BAD - недостаточно информации для отладки
Log($"ERROR: {ex.Message}");

// GOOD - полная информация: тип, сообщение, вся цепочка InnerException, StackTrace
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

    // StackTrace (первые 15 строк)
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

**Обязательно включать:**
- Тип исключения (`ex.GetType().Name`)
- Сообщение (`ex.Message`)
- Все уровни `InnerException` (особенно важно для EF Core, HttpClient)
- StackTrace (хотя бы первые 10-15 строк)
- Контекст операции (название метода/команды)

## Project Structure
```
SmartBasket/
├── SmartBasket.Core/        # Entities, Configuration, Interfaces
├── SmartBasket.Data/        # EF Core DbContext, Migrations
├── SmartBasket.Services/    # Business logic (Email, Ollama)
├── SmartBasket.WPF/         # WPF UI (MVVM with CommunityToolkit)
└── SmartBasket.CLI/         # Console tools for testing
```

## Key Libraries
- **CommunityToolkit.Mvvm** - MVVM: `[ObservableProperty]`, `[RelayCommand]`
- **MailKit** - IMAP email fetching
- **Entity Framework Core** - PostgreSQL/SQLite
- **Microsoft.Extensions.DependencyInjection** - DI container

## Quick Start Prompt

```
This is a WPF .NET 8 application using CommunityToolkit.Mvvm for MVVM pattern.

Key async rules:
1. Use BindingOperations.EnableCollectionSynchronization for thread-safe ObservableCollection
2. Wrap service calls in Task.Run() to free UI thread
3. Use ThreadSafeProgress<T> instead of Progress<T>
4. Always ConfigureAwait(false) in services
5. Never use lock inside Dispatcher.Invoke

Project: SmartBasket - receipt parser with email fetching (MailKit),
Ollama LLM parsing, PostgreSQL storage (EF Core).
```
