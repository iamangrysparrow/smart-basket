using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Core.Configuration;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// ViewModel для редактирования связки AI операций с провайдерами
/// </summary>
public partial class AiOperationsViewModel : ObservableObject
{
    /// <summary>
    /// Кастомные промпты: ключ "Operation/ProviderKey/system" или "Operation/ProviderKey/user" → текст промпта
    /// </summary>
    private readonly Dictionary<string, string> _prompts = new();

    public AiOperationsViewModel() { }

    public AiOperationsViewModel(AiOperationsConfig config)
    {
        ProductExtraction = config.ProductExtraction ?? string.Empty;
        Classification = config.Classification ?? string.Empty;
        Labels = config.Labels ?? string.Empty;
        Shopping = config.Shopping ?? string.Empty;
        ProductMatcher = config.ProductMatcher ?? string.Empty;

        // Копируем промпты
        foreach (var kvp in config.Prompts)
        {
            _prompts[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Этап 1: Выделение продукта из названия товара
    /// </summary>
    [ObservableProperty]
    private string _productExtraction = string.Empty;

    /// <summary>
    /// Этап 2: Классификация продукта в иерархию
    /// </summary>
    [ObservableProperty]
    private string _classification = string.Empty;

    [ObservableProperty]
    private string _labels = string.Empty;

    /// <summary>
    /// Модуль закупок (формирование списка покупок)
    /// </summary>
    [ObservableProperty]
    private string _shopping = string.Empty;

    /// <summary>
    /// Выбор товара из результатов поиска (дешёвая модель)
    /// </summary>
    [ObservableProperty]
    private string _productMatcher = string.Empty;

    /// <summary>
    /// Получить кастомный системный промпт для операции и провайдера
    /// </summary>
    public string? GetSystemPrompt(string operation, string providerKey)
    {
        var key = AiOperationsConfig.GetSystemPromptKey(operation, providerKey);
        if (_prompts.TryGetValue(key, out var prompt))
            return prompt;

        // Fallback to legacy key
        var legacyKey = AiOperationsConfig.GetPromptKey(operation, providerKey);
        return _prompts.TryGetValue(legacyKey, out var legacyPrompt) ? legacyPrompt : null;
    }

    /// <summary>
    /// Получить кастомный пользовательский промпт для операции и провайдера
    /// </summary>
    public string? GetUserPrompt(string operation, string providerKey)
    {
        var key = AiOperationsConfig.GetUserPromptKey(operation, providerKey);
        return _prompts.TryGetValue(key, out var prompt) ? prompt : null;
    }

    /// <summary>
    /// Установить кастомный системный промпт для операции и провайдера
    /// </summary>
    public void SetSystemPrompt(string operation, string providerKey, string? prompt)
    {
        var key = AiOperationsConfig.GetSystemPromptKey(operation, providerKey);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _prompts.Remove(key);
        }
        else
        {
            _prompts[key] = prompt;
        }

        // Remove legacy key if exists
        var legacyKey = AiOperationsConfig.GetPromptKey(operation, providerKey);
        _prompts.Remove(legacyKey);
    }

    /// <summary>
    /// Установить кастомный пользовательский промпт для операции и провайдера
    /// </summary>
    public void SetUserPrompt(string operation, string providerKey, string? prompt)
    {
        var key = AiOperationsConfig.GetUserPromptKey(operation, providerKey);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _prompts.Remove(key);
        }
        else
        {
            _prompts[key] = prompt;
        }
    }

    /// <summary>
    /// Проверить, есть ли кастомные промпты для операции и провайдера
    /// </summary>
    public bool HasCustomPrompt(string operation, string providerKey)
    {
        var systemKey = AiOperationsConfig.GetSystemPromptKey(operation, providerKey);
        var userKey = AiOperationsConfig.GetUserPromptKey(operation, providerKey);
        var legacyKey = AiOperationsConfig.GetPromptKey(operation, providerKey);

        return _prompts.ContainsKey(systemKey) ||
               _prompts.ContainsKey(userKey) ||
               _prompts.ContainsKey(legacyKey);
    }

    /// <summary>
    /// Legacy: Получить кастомный промпт для операции и провайдера
    /// </summary>
    public string? GetPrompt(string operation, string providerKey)
        => GetSystemPrompt(operation, providerKey);

    /// <summary>
    /// Legacy: Установить кастомный промпт для операции и провайдера
    /// </summary>
    public void SetPrompt(string operation, string providerKey, string? prompt)
        => SetSystemPrompt(operation, providerKey, prompt);

    /// <summary>
    /// Преобразование обратно в конфигурацию
    /// </summary>
    public AiOperationsConfig ToConfig()
    {
        var config = new AiOperationsConfig
        {
            ProductExtraction = string.IsNullOrWhiteSpace(ProductExtraction) ? null : ProductExtraction,
            Classification = string.IsNullOrWhiteSpace(Classification) ? null : Classification,
            Labels = string.IsNullOrWhiteSpace(Labels) ? null : Labels,
            Shopping = string.IsNullOrWhiteSpace(Shopping) ? null : Shopping,
            ProductMatcher = string.IsNullOrWhiteSpace(ProductMatcher) ? null : ProductMatcher
        };

        // Копируем промпты
        foreach (var kvp in _prompts)
        {
            config.Prompts[kvp.Key] = kvp.Value;
        }

        return config;
    }
}
