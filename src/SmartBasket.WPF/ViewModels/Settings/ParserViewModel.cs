using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Core.Configuration;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// ViewModel для редактирования парсера
/// </summary>
public partial class ParserViewModel : ObservableObject
{
    public ParserViewModel() { }

    public ParserViewModel(ParserConfig config)
    {
        _originalName = config.Name;
        Name = config.Name;
        Type = config.Type;
        RequiresAI = config.RequiresAI;
        AiProvider = config.AiProvider ?? string.Empty;
        Description = config.Description;
        IsBuiltIn = config.IsBuiltIn;
        IsEnabled = config.IsEnabled;

        SupportedShops.Clear();
        foreach (var shop in config.SupportedShops)
        {
            SupportedShops.Add(shop);
        }

        SupportedFormats.Clear();
        foreach (var format in config.SupportedFormats)
        {
            SupportedFormats.Add(format);
        }
    }

    private readonly string _originalName = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLlmParser))]
    private ParserType _type = ParserType.Regex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAiProviderSelection))]
    private bool _requiresAI;

    [ObservableProperty]
    private string _aiProvider = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isBuiltIn;

    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// Поддерживаемые магазины
    /// </summary>
    public ObservableCollection<string> SupportedShops { get; } = new();

    /// <summary>
    /// Поддерживаемые форматы
    /// </summary>
    public ObservableCollection<string> SupportedFormats { get; } = new();

    /// <summary>
    /// Строковое представление поддерживаемых магазинов
    /// </summary>
    public string SupportedShopsDisplay => SupportedShops.Count > 0
        ? string.Join(", ", SupportedShops)
        : "Не указано";

    /// <summary>
    /// Строковое представление поддерживаемых форматов
    /// </summary>
    public string SupportedFormatsDisplay => SupportedFormats.Count > 0
        ? string.Join(", ", SupportedFormats).ToUpperInvariant()
        : "Любой";

    public bool IsLlmParser => Type == ParserType.LLM;
    public bool ShowAiProviderSelection => RequiresAI;

    public bool IsNew => string.IsNullOrEmpty(_originalName);

    /// <summary>
    /// Можно ли редактировать (встроенные парсеры только на просмотр)
    /// </summary>
    public bool CanEdit => !IsBuiltIn;

    /// <summary>
    /// Преобразование обратно в конфигурацию
    /// </summary>
    public ParserConfig ToConfig()
    {
        return new ParserConfig
        {
            Name = Name,
            Type = Type,
            RequiresAI = RequiresAI,
            AiProvider = string.IsNullOrWhiteSpace(AiProvider) ? null : AiProvider,
            Description = Description,
            SupportedShops = SupportedShops.ToList(),
            SupportedFormats = SupportedFormats.ToList(),
            IsBuiltIn = IsBuiltIn,
            IsEnabled = IsEnabled
        };
    }
}
