using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using SmartBasket.Core.Configuration;
using SmartBasket.Data;
using SmartBasket.Services;
using SmartBasket.Services.Email;
using SmartBasket.Services.Export;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Parsing;
using SmartBasket.Services.Products;
using SmartBasket.Services.Sources;
using SmartBasket.Services.Tools;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Shopping;
using SmartBasket.Services.Shopping.Operations;
using SmartBasket.Services.Units;
using SmartBasket.WPF.Logging;
using SmartBasket.WPF.Services;
using SmartBasket.WPF.Themes;
using SmartBasket.WPF.ViewModels;
using SmartBasket.WPF.ViewModels.Settings;
using SmartBasket.WPF.Views.Shopping;

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

        // Diagnostic: Log color/brush values after theme applied
        LogColorDiagnostics();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_appSettings!);

        // Configure UI log limit from settings (default: 10000)
        LogViewerSink.Instance.SetMaxUiEntries(_appSettings!.MaxUiLogEntries);

        // Serilog configuration - separate log file per app launch + daily rotation if running > 24h
        var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        var sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var logsPath = Path.Combine(logsDir, $"smartbasket_{sessionTimestamp}.log");

        var seqUrl = string.IsNullOrWhiteSpace(_appSettings!.SeqUrl)
            ? "http://localhost:5341"
            : _appSettings.SeqUrl;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logsPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 30)
            .WriteTo.LogViewer(LogEventLevel.Debug)
            .WriteTo.Seq(seqUrl)
            .CreateLogger();

        // Logging via Serilog
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: true);
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
        services.AddSingleton<IAiSessionManager, AiSessionManager>();
        services.AddTransient<IProductExtractionService, ProductExtractionService>();
        services.AddTransient<IProductClassificationService, ProductClassificationService>();
        services.AddTransient<ILabelAssignmentService, LabelAssignmentService>();
        services.AddTransient<ITokenUsageService, TokenUsageService>();

        // Parsers
        services.AddSingleton<IReceiptTextParser, InstamartReceiptParser>();
        services.AddSingleton<LlmUniversalParser>();
        services.AddSingleton<ReceiptTextParserFactory>();

        // Email service (used by ReceiptSourceFactory for Email sources)
        services.AddSingleton<IEmailService, EmailService>();

        // Sources
        services.AddSingleton<IReceiptSourceFactory, ReceiptSourceFactory>();

        // Unit conversion service
        services.AddTransient<IUnitConversionService, UnitConversionService>();

        // Orchestration - Transient to get fresh DbContext each time
        services.AddTransient<IReceiptCollectionService, ReceiptCollectionService>();
        services.AddTransient<IProductCleanupService, ProductCleanupService>();

        services.AddSingleton<SettingsService>();

        // Product/Item/Label services - Transient to avoid DbContext concurrency issues in WPF
        services.AddTransient<IProductCategoryService, ProductCategoryService>();
        services.AddTransient<IProductService, ProductService>();
        services.AddTransient<ILabelService, LabelService>();
        services.AddTransient<IItemService, ItemService>();

        // Export service
        services.AddSingleton<IReceiptExportService, ReceiptExportService>();

        // Tools (for AI chat and brain services)
        services.AddTools();

        // Chat service (with tool-use loop)
        services.AddTransient<IChatService, ChatService>();

        // Shopping module - Singleton: one active session per app
        services.Configure<SmartBasket.Core.Shopping.ShoppingSettings>(options =>
        {
            var shoppingSection = _appSettings!.Shopping;
            options.Stores = shoppingSection.Stores;
        });
        services.AddSingleton<IShoppingSessionService, ShoppingSessionService>();
        services.AddSingleton<IShoppingChatService, ShoppingChatService>();
        // ProductSelectorService теперь использует прямой вызов LLM (без ShoppingChatService)
        services.AddTransient<IProductSelectorService, ProductSelectorService>();
        // Shopping Operations (новая архитектура — WorkflowProgress)
        services.AddTransient<IShoppingChatOperation, ShoppingChatOperation>();
        services.AddTransient<IProductMatcherOperation, ProductMatcherOperation>();
        services.AddTransient<IBasketBuilderOperation, BasketBuilderOperation>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ProductsItemsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ShoppingViewModel>();

        // Windows
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
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

    private void LogColorDiagnostics()
    {
        System.Diagnostics.Debug.WriteLine("========== COLOR DIAGNOSTICS ==========");

        // Log merged dictionaries order
        System.Diagnostics.Debug.WriteLine("\n--- Merged Dictionaries Order ---");
        int index = 0;
        foreach (var dict in Resources.MergedDictionaries)
        {
            var source = dict.Source?.OriginalString ?? "[inline]";
            System.Diagnostics.Debug.WriteLine($"  [{index}] {source}");
            index++;
        }

        // Log key colors
        System.Diagnostics.Debug.WriteLine("\n--- Color Values ---");
        LogResourceValue("AccentDefault", isColor: true);
        LogResourceValue("AccentHover", isColor: true);
        LogResourceValue("PrimaryColor", isColor: true);
        LogResourceValue("DarkPrimaryColor", isColor: true);

        // Log key brushes
        System.Diagnostics.Debug.WriteLine("\n--- Brush Values ---");
        LogResourceValue("AccentBrush", isColor: false);
        LogResourceValue("AccentHoverBrush", isColor: false);
        LogResourceValue("PrimaryBrush", isColor: false);
        LogResourceValue("DarkPrimaryBrush", isColor: false);

        System.Diagnostics.Debug.WriteLine("\n========================================");
    }

    private void LogResourceValue(string key, bool isColor)
    {
        try
        {
            var value = Resources[key];
            if (value == null)
            {
                System.Diagnostics.Debug.WriteLine($"  {key}: NOT FOUND");
                return;
            }

            if (isColor && value is Color color)
            {
                System.Diagnostics.Debug.WriteLine($"  {key}: {color} (A={color.A}, R={color.R}, G={color.G}, B={color.B})");
            }
            else if (!isColor && value is SolidColorBrush brush)
            {
                var c = brush.Color;
                System.Diagnostics.Debug.WriteLine($"  {key}: {c} (A={c.A}, R={c.R}, G={c.G}, B={c.B})");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"  {key}: {value.GetType().Name} = {value}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"  {key}: ERROR - {ex.Message}");
        }
    }
}
