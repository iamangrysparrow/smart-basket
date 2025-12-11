namespace SmartBasket.Core.Configuration;

public class AppSettings
{
    public string Theme { get; set; } = "Light";
    public DatabaseSettings Database { get; set; } = new();

    // === Новая модульная конфигурация ===

    /// <summary>
    /// Источники чеков (Email, REST, FileSystem)
    /// </summary>
    public List<ReceiptSourceConfig> ReceiptSources { get; set; } = new();

    /// <summary>
    /// Парсеры чеков (Regex, LLM)
    /// </summary>
    public List<ParserConfig> Parsers { get; set; } = new();

    /// <summary>
    /// AI провайдеры (Ollama, YandexGPT, OpenAI)
    /// </summary>
    public List<AiProviderConfig> AiProviders { get; set; } = new();

    /// <summary>
    /// Связка AI операций с провайдерами
    /// </summary>
    public AiOperationsConfig AiOperations { get; set; } = new();

}
