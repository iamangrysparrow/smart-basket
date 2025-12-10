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
/// Тип операции LLM
/// </summary>
public enum LlmOperationType
{
    /// <summary>
    /// Парсинг чеков (извлечение товаров из текста)
    /// </summary>
    Parsing,

    /// <summary>
    /// Классификация товаров по продуктам
    /// </summary>
    Classification,

    /// <summary>
    /// Назначение меток товарам
    /// </summary>
    Labels
}

/// <summary>
/// Общие настройки LLM с раздельным выбором провайдера для каждой операции
/// </summary>
public class LlmSettings
{
    /// <summary>
    /// Провайдер для парсинга чеков
    /// </summary>
    public LlmProviderType ParsingProvider { get; set; } = LlmProviderType.Ollama;

    /// <summary>
    /// Провайдер для классификации товаров
    /// </summary>
    public LlmProviderType ClassificationProvider { get; set; } = LlmProviderType.Ollama;

    /// <summary>
    /// Провайдер для назначения меток
    /// </summary>
    public LlmProviderType LabelsProvider { get; set; } = LlmProviderType.Ollama;

    /// <summary>
    /// Получить провайдер для указанной операции
    /// </summary>
    public LlmProviderType GetProviderForOperation(LlmOperationType operation) => operation switch
    {
        LlmOperationType.Parsing => ParsingProvider,
        LlmOperationType.Classification => ClassificationProvider,
        LlmOperationType.Labels => LabelsProvider,
        _ => LlmProviderType.Ollama
    };
}
