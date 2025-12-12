using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Data;
using SmartBasket.Services;
using SmartBasket.Services.Email;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Parsing;
using SmartBasket.Services.Products;
using SmartBasket.Services.Sources;
using SmartBasket.WPF.Services;
using SmartBasket.WPF.Themes;
using SmartBasket.WPF.ViewModels;
using SmartBasket.WPF.ViewModels.Settings;

namespace SmartBasket.WPF;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private AppSettings? _appSettings;

    /// <summary>
    /// Provides access to the service provider for dependency injection
    /// </summary>
    public static IServiceProvider Services => ((App)Current)._serviceProvider!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers to prevent crashes
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Load configuration first
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        _appSettings = new AppSettings();
        configuration.Bind(_appSettings);

        // Decrypt secrets after loading
        SettingsService.DecryptSecrets(_appSettings);

        // Legacy configuration migration removed - now using AiProviderConfig directly

        // Apply saved theme before showing UI
        var themeName = _appSettings.Theme ?? "Light";
        var theme = themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Dark
            : AppTheme.Light;
        ThemeManager.ApplyTheme(theme);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_appSettings!);

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Database
        var dbProvider = _appSettings!.Database.Provider.ToLowerInvariant() switch
        {
            "postgresql" or "postgres" => DatabaseProviderType.PostgreSQL,
            "sqlite" => DatabaseProviderType.SQLite,
            _ => DatabaseProviderType.PostgreSQL
        };

        services.AddSmartBasketDbContext(dbProvider, _appSettings.Database.ConnectionString);

        // HTTP Client for Ollama
        services.AddHttpClient();

        // AI Providers - must be registered before services that depend on it
        services.AddSingleton<IAiProviderFactory, AiProviderFactory>();

        // LLM Services (use AI providers via factory)
        services.AddSingleton<IResponseParser, ResponseParser>();
        services.AddSingleton<IProductClassificationService, ProductClassificationService>();
        services.AddSingleton<ILabelAssignmentService, LabelAssignmentService>();

        // Parsers
        services.AddSingleton<IReceiptTextParser, InstamartReceiptParser>();
        services.AddSingleton<LlmUniversalParser>();
        services.AddSingleton<ReceiptTextParserFactory>();

        // Email service (used by ReceiptSourceFactory for Email sources)
        services.AddSingleton<IEmailService, EmailService>();

        // Sources
        services.AddSingleton<IReceiptSourceFactory, ReceiptSourceFactory>();

        // Orchestration - Transient to get fresh DbContext each time
        services.AddTransient<IReceiptCollectionService, ReceiptCollectionService>();
        services.AddTransient<IProductCleanupService, ProductCleanupService>();

        services.AddSingleton<SettingsService>();

        // Product/Item/Label services - Transient to avoid DbContext concurrency issues in WPF
        services.AddTransient<IProductService, ProductService>();
        services.AddTransient<ILabelService, LabelService>();
        services.AddTransient<IItemService, ItemService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ProductsItemsViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Windows
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Log to debug output - app may crash after this
        var exception = e.ExceptionObject as Exception;
        System.Diagnostics.Debug.WriteLine($"UNHANDLED: {exception?.Message}");
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Silently handle - prevent crash, errors go to log
        System.Diagnostics.Debug.WriteLine($"DISPATCHER ERROR: {e.Exception.Message}");
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Silently handle - prevent crash
        System.Diagnostics.Debug.WriteLine($"TASK ERROR: {e.Exception?.Message}");
        e.SetObserved();
    }
}
