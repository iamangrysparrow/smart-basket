using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Core.Configuration;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// ViewModel для редактирования связки AI операций с провайдерами
/// </summary>
public partial class AiOperationsViewModel : ObservableObject
{
    /// <summary>
    /// Кастомные промпты: ключ "Operation/ProviderKey" → текст промпта
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
    /// Получить кастомный промпт для операции и провайдера
    /// </summary>
    public string? GetPrompt(string operation, string providerKey)
    {
        var key = AiOperationsConfig.GetPromptKey(operation, providerKey);
        return _prompts.TryGetValue(key, out var prompt) ? prompt : null;
    }

    /// <summary>
    /// Установить кастомный промпт для операции и провайдера
    /// </summary>
    public void SetPrompt(string operation, string providerKey, string? prompt)
    {
        var key = AiOperationsConfig.GetPromptKey(operation, providerKey);
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
    /// Проверить, есть ли кастомный промпт для операции и провайдера
    /// </summary>
    public bool HasCustomPrompt(string operation, string providerKey)
    {
        var key = AiOperationsConfig.GetPromptKey(operation, providerKey);
        return _prompts.ContainsKey(key);
    }

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
