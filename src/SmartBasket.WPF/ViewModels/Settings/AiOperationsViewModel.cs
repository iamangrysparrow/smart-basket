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
        Classification = config.Classification ?? string.Empty;
        Labels = config.Labels ?? string.Empty;

        // Копируем промпты
        foreach (var kvp in config.Prompts)
        {
            _prompts[kvp.Key] = kvp.Value;
        }
    }

    [ObservableProperty]
    private string _classification = string.Empty;

    [ObservableProperty]
    private string _labels = string.Empty;

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
            Classification = string.IsNullOrWhiteSpace(Classification) ? null : Classification,
            Labels = string.IsNullOrWhiteSpace(Labels) ? null : Labels
        };

        // Копируем промпты
        foreach (var kvp in _prompts)
        {
            config.Prompts[kvp.Key] = kvp.Value;
        }

        return config;
    }
}
