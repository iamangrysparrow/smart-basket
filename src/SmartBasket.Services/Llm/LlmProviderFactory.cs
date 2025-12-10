using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Интерфейс фабрики LLM провайдеров
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>
    /// Получить текущий активный провайдер на основе настроек
    /// </summary>
    ILlmProvider GetCurrentProvider();

    /// <summary>
    /// Получить провайдер по типу
    /// </summary>
    ILlmProvider GetProvider(LlmProviderType providerType);

    /// <summary>
    /// Получить список доступных провайдеров
    /// </summary>
    IReadOnlyList<(LlmProviderType Type, string Name)> GetAvailableProviders();
}

/// <summary>
/// Фабрика LLM провайдеров
/// </summary>
public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AppSettings _settings;

    public LlmProviderFactory(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        AppSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _settings = settings;
    }

    public ILlmProvider GetCurrentProvider()
    {
        return GetProvider(_settings.Llm.Provider);
    }

    public ILlmProvider GetProvider(LlmProviderType providerType)
    {
        return providerType switch
        {
            LlmProviderType.Ollama => new OllamaLlmProvider(
                _httpClientFactory,
                _loggerFactory.CreateLogger<OllamaLlmProvider>(),
                _settings.Ollama),

            LlmProviderType.YandexGpt => new YandexGptLlmProvider(
                _httpClientFactory,
                _loggerFactory.CreateLogger<YandexGptLlmProvider>(),
                _settings.YandexGpt),

            _ => throw new ArgumentException($"Unknown LLM provider type: {providerType}")
        };
    }

    public IReadOnlyList<(LlmProviderType Type, string Name)> GetAvailableProviders()
    {
        return new[]
        {
            (LlmProviderType.Ollama, "Ollama (локальный)"),
            (LlmProviderType.YandexGpt, "YandexGPT (облако)")
        };
    }
}
