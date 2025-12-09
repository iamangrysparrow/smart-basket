namespace SmartBasket.Core.Configuration;

public class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public OllamaSettings Ollama { get; set; } = new();
}
