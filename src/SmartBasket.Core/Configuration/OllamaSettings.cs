namespace SmartBasket.Core.Configuration;

public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "mistral:latest";
    public double Temperature { get; set; } = 0.3;
    public int TimeoutSeconds { get; set; } = 60;
}
