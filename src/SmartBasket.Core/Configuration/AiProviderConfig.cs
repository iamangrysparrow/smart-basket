namespace SmartBasket.Core.Configuration;

/// <summary>
/// Конфигурация AI провайдера
/// </summary>
public class AiProviderConfig
{
    /// <summary>
    /// Уникальный ключ провайдера
    /// Формат: "Provider/Model", например "Ollama/qwen2.5:1.5b"
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Тип провайдера (Ollama, YandexGPT, OpenAI)
    /// </summary>
    public AiProviderType Provider { get; set; } = AiProviderType.Ollama;

    /// <summary>
    /// Название модели
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Базовый URL API (для Ollama, OpenAI-compatible)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Температура генерации (0.0 - 1.0)
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Таймаут запроса в секундах
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Максимальное количество токенов в ответе
    /// </summary>
    public int? MaxTokens { get; set; }

    // --- YandexGPT specific ---

    /// <summary>
    /// API Key (для YandexGPT, OpenAI)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Folder ID (для YandexGPT)
    /// </summary>
    public string? FolderId { get; set; }

    /// <summary>
    /// Agent ID (для YandexAgent)
    /// </summary>
    public string? AgentId { get; set; }

    // --- Reasoning mode (для YandexGPT) ---

    /// <summary>
    /// Режим рассуждений: Disabled, EnabledHidden
    /// </summary>
    public ReasoningMode ReasoningMode { get; set; } = ReasoningMode.Disabled;

    /// <summary>
    /// Уровень рассуждений: Low, Medium, High
    /// </summary>
    public ReasoningEffort ReasoningEffort { get; set; } = ReasoningEffort.Low;
}

/// <summary>
/// Режим рассуждений для YandexGPT
/// </summary>
public enum ReasoningMode
{
    /// <summary>
    /// Режим рассуждений выключен (по умолчанию)
    /// </summary>
    Disabled,

    /// <summary>
    /// Режим рассуждений включен, но цепочка рассуждений не возвращается
    /// </summary>
    EnabledHidden
}

/// <summary>
/// Уровень рассуждений для YandexGPT
/// </summary>
public enum ReasoningEffort
{
    /// <summary>
    /// Приоритет по скорости и экономии токенов
    /// </summary>
    Low,

    /// <summary>
    /// Баланс между скоростью и точностью рассуждений
    /// </summary>
    Medium,

    /// <summary>
    /// Приоритет более полного и тщательного рассуждения
    /// </summary>
    High
}
