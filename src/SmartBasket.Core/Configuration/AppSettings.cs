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

    // === Legacy (для обратной совместимости, deprecated) ===

    /// <summary>
    /// [Deprecated] Используйте ReceiptSources с Type=Email
    /// </summary>
    [Obsolete("Use ReceiptSources with Type=Email instead")]
    public EmailSettings? Email { get; set; }

    /// <summary>
    /// [Deprecated] Используйте Parsers и AiOperations
    /// </summary>
    [Obsolete("Use Parsers and AiOperations instead")]
    public LlmSettings? Llm { get; set; }

    /// <summary>
    /// [Deprecated] Используйте AiProviders с Provider=Ollama
    /// </summary>
    [Obsolete("Use AiProviders with Provider=Ollama instead")]
    public OllamaSettings? Ollama { get; set; }

    /// <summary>
    /// [Deprecated] Используйте AiProviders с Provider=YandexGPT
    /// </summary>
    [Obsolete("Use AiProviders with Provider=YandexGPT instead")]
    public YandexGptSettings? YandexGpt { get; set; }
}
