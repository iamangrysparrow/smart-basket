namespace SmartBasket.Core.Configuration;

/// <summary>
/// Конфигурация связки AI операций с провайдерами
/// </summary>
public class AiOperationsConfig
{
    /// <summary>
    /// Ключ провайдера для классификации товаров (Item → Product)
    /// Формат: "Provider/Model", например "Ollama/llama3.2:3b"
    /// </summary>
    public string? Classification { get; set; }

    /// <summary>
    /// Ключ провайдера для назначения меток (Item → Labels)
    /// </summary>
    public string? Labels { get; set; }
}
