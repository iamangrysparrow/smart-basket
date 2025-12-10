namespace SmartBasket.Core.Configuration;

public class AppSettings
{
    public string Theme { get; set; } = "Light";
    public DatabaseSettings Database { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public LlmSettings Llm { get; set; } = new();
    public OllamaSettings Ollama { get; set; } = new();
    public YandexGptSettings YandexGpt { get; set; } = new();
}
