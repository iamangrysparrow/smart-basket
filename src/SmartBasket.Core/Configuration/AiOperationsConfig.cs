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
    /// Ключ провайдера для модуля закупок (формирование списка покупок).
    /// Рекомендуется YandexAgent с tool calling.
    /// </summary>
    public string? Shopping { get; set; }

    /// <summary>
    /// Ключ провайдера для выбора товара из результатов поиска.
    /// Фильтрует нерелевантные результаты, выбирает лучший + до 3 альтернатив.
    /// Рекомендуется дешёвая модель: Ollama/qwen2.5:7b
    /// </summary>
    public string? ProductMatcher { get; set; }

    /// <summary>
    /// Кастомные промпты для операций в связке с провайдером.
    /// Ключ: "Operation/ProviderKey/system" или "Operation/ProviderKey/user"
    /// Значение: текст промпта
    ///
    /// Для обратной совместимости также поддерживается старый формат:
    /// Ключ: "Operation/ProviderKey" (без /system или /user)
    /// </summary>
    public Dictionary<string, string> Prompts { get; set; } = new();

    /// <summary>
    /// Получить ключ для хранения системного промпта
    /// </summary>
    public static string GetSystemPromptKey(string operation, string providerKey)
        => $"{operation}/{providerKey}/system";

    /// <summary>
    /// Получить ключ для хранения пользовательского промпта
    /// </summary>
    public static string GetUserPromptKey(string operation, string providerKey)
        => $"{operation}/{providerKey}/user";

    /// <summary>
    /// Legacy: Получить ключ для хранения промпта (старый формат)
    /// </summary>
    public static string GetPromptKey(string operation, string providerKey)
        => $"{operation}/{providerKey}";

    /// <summary>
    /// Получить кастомный системный промпт для операции и провайдера
    /// </summary>
    public string? GetSystemPrompt(string operation, string providerKey)
    {
        var key = GetSystemPromptKey(operation, providerKey);
        if (Prompts.TryGetValue(key, out var prompt))
            return prompt;

        // Fallback to legacy key (for backward compatibility)
        var legacyKey = GetPromptKey(operation, providerKey);
        return Prompts.TryGetValue(legacyKey, out var legacyPrompt) ? legacyPrompt : null;
    }

    /// <summary>
    /// Получить кастомный пользовательский промпт для операции и провайдера
    /// </summary>
    public string? GetUserPrompt(string operation, string providerKey)
    {
        var key = GetUserPromptKey(operation, providerKey);
        return Prompts.TryGetValue(key, out var prompt) ? prompt : null;
    }

    /// <summary>
    /// Установить кастомный системный промпт для операции и провайдера
    /// </summary>
    public void SetSystemPrompt(string operation, string providerKey, string? prompt)
    {
        var key = GetSystemPromptKey(operation, providerKey);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Prompts.Remove(key);
        }
        else
        {
            Prompts[key] = prompt;
        }

        // Remove legacy key if exists
        var legacyKey = GetPromptKey(operation, providerKey);
        Prompts.Remove(legacyKey);
    }

    /// <summary>
    /// Установить кастомный пользовательский промпт для операции и провайдера
    /// </summary>
    public void SetUserPrompt(string operation, string providerKey, string? prompt)
    {
        var key = GetUserPromptKey(operation, providerKey);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Prompts.Remove(key);
        }
        else
        {
            Prompts[key] = prompt;
        }
    }

    /// <summary>
    /// Проверить, есть ли кастомные промпты для операции и провайдера
    /// </summary>
    public bool HasCustomPrompts(string operation, string providerKey)
    {
        var systemKey = GetSystemPromptKey(operation, providerKey);
        var userKey = GetUserPromptKey(operation, providerKey);
        var legacyKey = GetPromptKey(operation, providerKey);

        return Prompts.ContainsKey(systemKey) ||
               Prompts.ContainsKey(userKey) ||
               Prompts.ContainsKey(legacyKey);
    }

    /// <summary>
    /// Legacy: Получить кастомный промпт для операции и провайдера
    /// </summary>
    public string? GetCustomPrompt(string operation, string providerKey)
        => GetSystemPrompt(operation, providerKey);

    /// <summary>
    /// Legacy: Установить кастомный промпт для операции и провайдера
    /// </summary>
    public void SetCustomPrompt(string operation, string providerKey, string? prompt)
        => SetSystemPrompt(operation, providerKey, prompt);
}
