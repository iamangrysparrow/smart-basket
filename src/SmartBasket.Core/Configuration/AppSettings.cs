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

    /// <summary>
    /// Максимальное количество записей лога в UI.
    /// Ограничивает ObservableCollection для производительности ListBox.
    /// Полный лог (без лимита) всегда доступен для экспорта в файл.
    /// Default: 10000. Настраивается в appsettings.json: "MaxUiLogEntries": 10000
    /// </summary>
    public int MaxUiLogEntries { get; set; } = 10000;

}
