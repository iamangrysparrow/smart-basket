using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// AI операция для выбора провайдера
/// </summary>
public enum AiOperation
{
    Classification,
    Labels
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
}

/// <summary>
/// Фабрика AI провайдеров (новая версия с ключами)
/// </summary>
public class AiProviderFactory : IAiProviderFactory
{
    private readonly AppSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, ILlmProvider> _providersCache = new();

    public AiProviderFactory(
        AppSettings settings,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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
            AiOperation.Classification => _settings.AiOperations.Classification,
            AiOperation.Labels => _settings.AiOperations.Labels,
            _ => null
        };

        return string.IsNullOrWhiteSpace(key) ? null : GetProvider(key);
    }

    public IReadOnlyList<string> GetAvailableProviders()
    {
        return _settings.AiProviders.Select(p => p.Key).ToList();
    }

    private ILlmProvider CreateProvider(AiProviderConfig config)
    {
        return config.Provider switch
        {
            AiProviderType.Ollama => CreateOllamaProvider(config),
            AiProviderType.YandexGPT => CreateYandexGptProvider(config),
            AiProviderType.OpenAI => throw new NotImplementedException("OpenAI provider is not implemented yet"),
            _ => throw new ArgumentException($"Unknown provider type: {config.Provider}")
        };
    }

    private ILlmProvider CreateOllamaProvider(AiProviderConfig config)
    {
        return new OllamaLlmProvider(
            _httpClientFactory,
            _loggerFactory.CreateLogger<OllamaLlmProvider>(),
            config);
    }

    private ILlmProvider CreateYandexGptProvider(AiProviderConfig config)
    {
        return new YandexGptLlmProvider(
            _httpClientFactory,
            _loggerFactory.CreateLogger<YandexGptLlmProvider>(),
            config);
    }
}
