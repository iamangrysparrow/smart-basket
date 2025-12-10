using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Email;

namespace SmartBasket.Services.Sources;

/// <summary>
/// Интерфейс фабрики источников чеков
/// </summary>
public interface IReceiptSourceFactory
{
    /// <summary>
    /// Создать источник по конфигурации
    /// </summary>
    IReceiptSource Create(ReceiptSourceConfig config);

    /// <summary>
    /// Создать все источники из списка конфигураций
    /// </summary>
    IReadOnlyList<IReceiptSource> CreateAll(IEnumerable<ReceiptSourceConfig> configs);

    /// <summary>
    /// Создать все включённые источники из AppSettings
    /// </summary>
    IReadOnlyList<IReceiptSource> CreateAllEnabled();

    /// <summary>
    /// Получить источник по имени
    /// </summary>
    IReceiptSource? GetByName(string name);
}

/// <summary>
/// Фабрика источников чеков
/// </summary>
public class ReceiptSourceFactory : IReceiptSourceFactory
{
    private readonly AppSettings _settings;
    private readonly IEmailService _emailService;
    private readonly ILoggerFactory _loggerFactory;

    public ReceiptSourceFactory(
        AppSettings settings,
        IEmailService emailService,
        ILoggerFactory loggerFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public IReceiptSource Create(ReceiptSourceConfig config)
    {
        return config.Type switch
        {
            SourceType.Email => new EmailReceiptSource(
                config,
                _emailService,
                _loggerFactory.CreateLogger<EmailReceiptSource>()),

            SourceType.REST => throw new NotImplementedException("REST source type is not implemented yet"),

            SourceType.FileSystem => throw new NotImplementedException("FileSystem source type is not implemented yet"),

            _ => throw new ArgumentException($"Unknown source type: {config.Type}", nameof(config))
        };
    }

    public IReadOnlyList<IReceiptSource> CreateAll(IEnumerable<ReceiptSourceConfig> configs)
    {
        return configs.Select(Create).ToList();
    }

    public IReadOnlyList<IReceiptSource> CreateAllEnabled()
    {
        return _settings.ReceiptSources
            .Where(s => s.IsEnabled)
            .Select(Create)
            .ToList();
    }

    public IReceiptSource? GetByName(string name)
    {
        var config = _settings.ReceiptSources.FirstOrDefault(s => s.Name == name);
        return config != null ? Create(config) : null;
    }
}
