using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Data;
using SmartBasket.Services.Email;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Ollama;
using SmartBasket.Services.Products;
using SmartBasket.WPF.Services;
using SmartBasket.WPF.Themes;
using SmartBasket.WPF.ViewModels;

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

        // Services
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IOllamaService, OllamaService>();
        services.AddSingleton<ICategoryService, CategoryService>();
        services.AddSingleton<IProductClassificationService, ProductClassificationService>();
        services.AddSingleton<ILabelAssignmentService, LabelAssignmentService>();
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
        services.AddSingleton<IReceiptParsingService, ReceiptParsingService>();
        services.AddSingleton<SettingsService>();

        // Product/Item/Label services
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ILabelService, LabelService>();
        services.AddScoped<IItemService, ItemService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ProductsItemsViewModel>();

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
