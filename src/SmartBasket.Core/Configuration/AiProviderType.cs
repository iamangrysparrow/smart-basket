namespace SmartBasket.Core.Configuration;

/// <summary>
/// Тип AI провайдера
/// </summary>
public enum AiProviderType
{
    /// <summary>
    /// Локальная Ollama
    /// </summary>
    Ollama,

    /// <summary>
    /// Yandex GPT
    /// </summary>
    YandexGPT,

    /// <summary>
    /// OpenAI API (GPT-4, GPT-3.5)
    /// </summary>
    OpenAI,

    /// <summary>
    /// Yandex AI Agent (использует OpenAI SDK с кастомным агентом)
    /// </summary>
    YandexAgent,

    /// <summary>
    /// GigaChat (Sber)
    /// </summary>
    GigaChat
}
