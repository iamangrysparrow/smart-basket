namespace SmartBasket.Core.Configuration;

/// <summary>
/// Конфигурация связки AI операций с провайдерами
/// </summary>
public class AiOperationsConfig
{
    /// <summary>
    /// Этап 1: Ключ провайдера для выделения продукта из названия товара (Item → Product name).
    /// Нормализует название: убирает бренды, объёмы, маркировки.
    /// Формат: "Provider/Model", например "Ollama/llama3.2:3b"
    /// </summary>
    public string? ProductExtraction { get; set; }

    /// <summary>
    /// Этап 2: Ключ провайдера для классификации продуктов в иерархию (Product → Category hierarchy)
    /// Формат: "Provider/Model", например "YandexGPT/yandexgpt-lite"
    /// </summary>
    public string? Classification { get; set; }

    /// <summary>
    /// Ключ провайдера для назначения меток (Item → Labels)
    /// </summary>
    public string? Labels { get; set; }

    /// <summary>
    /// Ключ провайдера для AI чата с пользователем
    /// </summary>
    public string? Chat { get; set; }

    /// <summary>
    /// Кастомные промпты для операций в связке с провайдером.
    /// Ключ: "Operation/ProviderKey", например "Classification/Ollama/llama3.2:3b"
    /// Значение: текст промпта
    /// </summary>
    public Dictionary<string, string> Prompts { get; set; } = new();

    /// <summary>
    /// Получить ключ для хранения промпта
    /// </summary>
    public static string GetPromptKey(string operation, string providerKey)
        => $"{operation}/{providerKey}";

    /// <summary>
    /// Получить кастомный промпт для операции и провайдера
    /// </summary>
    public string? GetCustomPrompt(string operation, string providerKey)
    {
        var key = GetPromptKey(operation, providerKey);
        return Prompts.TryGetValue(key, out var prompt) ? prompt : null;
    }

    /// <summary>
    /// Установить кастомный промпт для операции и провайдера
    /// </summary>
    public void SetCustomPrompt(string operation, string providerKey, string? prompt)
    {
        var key = GetPromptKey(operation, providerKey);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Prompts.Remove(key);
        }
        else
        {
            Prompts[key] = prompt;
        }
    }
}
