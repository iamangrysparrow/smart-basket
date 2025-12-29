using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// AI операция для выбора провайдера
/// </summary>
public enum AiOperation
{
    /// <summary>
    /// Этап 1: Выделение продукта из названия товара (Item → Product name)
    /// </summary>
    ProductExtraction,

    /// <summary>
    /// Этап 2: Классификация продукта в иерархию (Product → Category hierarchy)
    /// </summary>
    Classification,

    /// <summary>
    /// Назначение меток товарам
    /// </summary>
    Labels,

    /// <summary>
    /// AI чат с пользователем
    /// </summary>
    Chat,

    /// <summary>
    /// Модуль закупок (Shopping) — формирование списка покупок, диалог с AI
    /// </summary>
    Shopping,

    /// <summary>
    /// Выбор товара из результатов поиска (дешёвая модель)
    /// Фильтрует нерелевантные результаты, выбирает лучший + альтернативы
    /// </summary>
    ProductMatcher
}

/// <summary>
/// Интерфейс фабрики AI провайдеров (новая версия с ключами)
/// </summary>
public interface IAiProviderFactory
{
    /// <summary>
    /// Получить провайдер по ключу (например "Ollama/qwen2.5:1.5b")
    /// </summary>
    ILlmProvider? GetProvider(string key);

    /// <summary>
    /// Получить провайдер для операции (из AiOperations конфигурации)
    /// </summary>
    ILlmProvider? GetProviderForOperation(AiOperation operation);

    /// <summary>
    /// Получить список доступных провайдеров (ключей)
    /// </summary>
    IReadOnlyList<string> GetAvailableProviders();

    /// <summary>
    /// Получить конфигурацию AI операций (для доступа к кастомным промптам)
    /// </summary>
    AiOperationsConfig AiOperations { get; }

    /// <summary>
    /// Получить ключ провайдера для операции
    /// </summary>
    string? GetProviderKeyForOperation(AiOperation operation);
}

/// <summary>
/// Фабрика AI провайдеров (новая версия с ключами)
/// </summary>
public class AiProviderFactory : IAiProviderFactory
{
    private readonly AppSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITokenUsageService _tokenUsageService;
    private readonly Dictionary<string, ILlmProvider> _providersCache = new();

    public AiOperationsConfig AiOperations => _settings.AiOperations;

    public AiProviderFactory(
        AppSettings settings,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ITokenUsageService tokenUsageService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _tokenUsageService = tokenUsageService ?? throw new ArgumentNullException(nameof(tokenUsageService));
    }

    public ILlmProvider? GetProvider(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        // Check cache first
        if (_providersCache.TryGetValue(key, out var cached))
            return cached;

        // Find config by key
        var config = _settings.AiProviders.FirstOrDefault(p => p.Key == key);
        if (config == null)
            return null;

        // Create provider
        var provider = CreateProvider(config);
        _providersCache[key] = provider;
        return provider;
    }

    public ILlmProvider? GetProviderForOperation(AiOperation operation)
    {
        var key = operation switch
        {
            AiOperation.ProductExtraction => _settings.AiOperations.ProductExtraction,
            AiOperation.Classification => _settings.AiOperations.Classification,
            AiOperation.Labels => _settings.AiOperations.Labels,
            AiOperation.Chat => _settings.AiOperations.Chat,
            AiOperation.Shopping => _settings.AiOperations.Shopping,
            AiOperation.ProductMatcher => _settings.AiOperations.ProductMatcher,
            _ => null
        };

        return string.IsNullOrWhiteSpace(key) ? null : GetProvider(key);
    }

    public IReadOnlyList<string> GetAvailableProviders()
    {
        return _settings.AiProviders.Select(p => p.Key).ToList();
    }

    public string? GetProviderKeyForOperation(AiOperation operation)
    {
        return operation switch
        {
            AiOperation.ProductExtraction => _settings.AiOperations.ProductExtraction,
            AiOperation.Classification => _settings.AiOperations.Classification,
            AiOperation.Labels => _settings.AiOperations.Labels,
            AiOperation.Chat => _settings.AiOperations.Chat,
            AiOperation.Shopping => _settings.AiOperations.Shopping,
            AiOperation.ProductMatcher => _settings.AiOperations.ProductMatcher,
            _ => null
        };
    }

    private ILlmProvider CreateProvider(AiProviderConfig config)
    {
        return config.Provider switch
        {
            AiProviderType.Ollama => CreateOllamaProvider(config),
            AiProviderType.YandexGPT => CreateYandexGptProvider(config),
            AiProviderType.YandexAgent => CreateYandexAgentProvider(config),
            AiProviderType.GigaChat => CreateGigaChatProvider(config),
            AiProviderType.OpenAI => throw new NotImplementedException("OpenAI provider is not implemented yet"),
            _ => throw new ArgumentException($"Unknown provider type: {config.Provider}")
        };
    }

    private ILlmProvider CreateOllamaProvider(AiProviderConfig config)
    {
        return new OllamaLlmProvider(
            _httpClientFactory,
            _loggerFactory.CreateLogger<OllamaLlmProvider>(),
            config,
            _tokenUsageService);
    }

    private ILlmProvider CreateYandexGptProvider(AiProviderConfig config)
    {
        return new YandexGptLlmProvider(
            _httpClientFactory,
            _loggerFactory.CreateLogger<YandexGptLlmProvider>(),
            config,
            _tokenUsageService);
    }

    private ILlmProvider CreateYandexAgentProvider(AiProviderConfig config)
    {
        return new YandexAgentLlmProvider(
            _httpClientFactory,
            _loggerFactory.CreateLogger<YandexAgentLlmProvider>(),
            config,
            _tokenUsageService);
    }

    private ILlmProvider CreateGigaChatProvider(AiProviderConfig config)
    {
        return new GigaChatLlmProvider(
            _httpClientFactory,
            _loggerFactory.CreateLogger<GigaChatLlmProvider>(),
            config,
            _tokenUsageService);
    }
}
