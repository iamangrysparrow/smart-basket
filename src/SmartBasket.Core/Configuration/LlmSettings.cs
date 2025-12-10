namespace SmartBasket.Core.Configuration;

/// <summary>
/// Тип LLM провайдера
/// </summary>
public enum LlmProviderType
{
    Ollama,
    YandexGpt
}

/// <summary>
/// Общие настройки LLM
/// </summary>
public class LlmSettings
{
    /// <summary>
    /// Выбранный провайдер LLM
    /// </summary>
    public LlmProviderType Provider { get; set; } = LlmProviderType.Ollama;
}
