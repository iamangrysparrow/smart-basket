using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Core.Configuration;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// ViewModel для редактирования AI провайдера
/// </summary>
public partial class AiProviderViewModel : ObservableObject
{
    public AiProviderViewModel() { }

    public AiProviderViewModel(AiProviderConfig config)
    {
        _originalKey = config.Key;
        Key = config.Key;
        Provider = config.Provider;
        Model = config.Model;
        BaseUrl = config.BaseUrl ?? string.Empty;
        Temperature = config.Temperature;
        TimeoutSeconds = config.TimeoutSeconds;
        MaxTokens = config.MaxTokens;
        ApiKey = config.ApiKey ?? string.Empty;
        FolderId = config.FolderId ?? string.Empty;
    }

    private string _originalKey = string.Empty;

    /// <summary>
    /// Возвращает оригинальный ключ (до редактирования)
    /// </summary>
    public string OriginalKey => _originalKey;

    /// <summary>
    /// Был ли ключ изменён
    /// </summary>
    public bool KeyWasRenamed => !string.IsNullOrEmpty(_originalKey) && _originalKey != Key;

    /// <summary>
    /// Фиксирует текущий ключ как оригинальный (после сохранения)
    /// </summary>
    public void CommitKey()
    {
        _originalKey = Key;
    }

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOllama))]
    [NotifyPropertyChangedFor(nameof(IsYandexGpt))]
    [NotifyPropertyChangedFor(nameof(IsOpenAi))]
    [NotifyPropertyChangedFor(nameof(ShowBaseUrl))]
    [NotifyPropertyChangedFor(nameof(ShowApiKey))]
    [NotifyPropertyChangedFor(nameof(ShowFolderId))]
    private AiProviderType _provider = AiProviderType.Ollama;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private double _temperature = 0.1;

    [ObservableProperty]
    private int _timeoutSeconds = 60;

    [ObservableProperty]
    private int? _maxTokens;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _folderId = string.Empty;

    // Helper properties for UI visibility
    public bool IsOllama => Provider == AiProviderType.Ollama;
    public bool IsYandexGpt => Provider == AiProviderType.YandexGPT;
    public bool IsOpenAi => Provider == AiProviderType.OpenAI;

    public bool ShowBaseUrl => IsOllama || IsOpenAi;
    public bool ShowApiKey => IsYandexGpt || IsOpenAi;
    public bool ShowFolderId => IsYandexGpt;

    public bool IsNew => string.IsNullOrEmpty(_originalKey);

    /// <summary>
    /// Генерирует ключ в формате "Provider/Model"
    /// </summary>
    public void GenerateKey()
    {
        if (!string.IsNullOrWhiteSpace(Model))
        {
            Key = $"{Provider}/{Model}";
        }
    }

    /// <summary>
    /// Преобразование обратно в конфигурацию
    /// </summary>
    public AiProviderConfig ToConfig()
    {
        return new AiProviderConfig
        {
            Key = Key,
            Provider = Provider,
            Model = Model,
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl,
            Temperature = Temperature,
            TimeoutSeconds = TimeoutSeconds,
            MaxTokens = MaxTokens,
            ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            FolderId = string.IsNullOrWhiteSpace(FolderId) ? null : FolderId
        };
    }
}
