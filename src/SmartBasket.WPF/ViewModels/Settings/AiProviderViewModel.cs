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
        AgentId = config.AgentId ?? string.Empty;
        ReasoningMode = config.ReasoningMode;
        ReasoningEffort = config.ReasoningEffort;
        GigaChatScope = config.GigaChatScope;
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
    [NotifyPropertyChangedFor(nameof(IsYandexAgent))]
    [NotifyPropertyChangedFor(nameof(IsOpenAi))]
    [NotifyPropertyChangedFor(nameof(IsGigaChat))]
    [NotifyPropertyChangedFor(nameof(ShowBaseUrl))]
    [NotifyPropertyChangedFor(nameof(ShowApiKey))]
    [NotifyPropertyChangedFor(nameof(ShowFolderId))]
    [NotifyPropertyChangedFor(nameof(ShowAgentId))]
    [NotifyPropertyChangedFor(nameof(ShowModel))]
    [NotifyPropertyChangedFor(nameof(ShowGigaChatScope))]
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

    [ObservableProperty]
    private string _agentId = string.Empty;

    [ObservableProperty]
    private ReasoningMode _reasoningMode = ReasoningMode.Disabled;

    [ObservableProperty]
    private ReasoningEffort _reasoningEffort = ReasoningEffort.Medium;

    [ObservableProperty]
    private GigaChatScope _gigaChatScope = GigaChatScope.PERS;

    // Helper properties for UI visibility
    public bool IsOllama => Provider == AiProviderType.Ollama;
    public bool IsYandexGpt => Provider == AiProviderType.YandexGPT;
    public bool IsYandexAgent => Provider == AiProviderType.YandexAgent;
    public bool IsOpenAi => Provider == AiProviderType.OpenAI;
    public bool IsGigaChat => Provider == AiProviderType.GigaChat;

    public bool ShowBaseUrl => IsOllama || IsOpenAi;
    public bool ShowApiKey => IsYandexGpt || IsYandexAgent || IsOpenAi || IsGigaChat;
    public bool ShowFolderId => IsYandexGpt || IsYandexAgent;
    public bool ShowAgentId => IsYandexAgent;
    public bool ShowModel => !IsYandexAgent;
    public bool ShowReasoning => IsYandexAgent;
    public bool ShowGigaChatScope => IsGigaChat;

    /// <summary>
    /// Включён ли режим рассуждений (для биндинга чекбокса)
    /// </summary>
    public bool IsReasoningEnabled
    {
        get => ReasoningMode == ReasoningMode.EnabledHidden;
        set
        {
            ReasoningMode = value ? ReasoningMode.EnabledHidden : ReasoningMode.Disabled;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Доступные уровни рассуждений
    /// </summary>
    public static IReadOnlyList<ReasoningEffort> ReasoningEffortOptions { get; } =
        [ReasoningEffort.Low, ReasoningEffort.Medium, ReasoningEffort.High];

    /// <summary>
    /// Доступные scope для GigaChat
    /// </summary>
    public static IReadOnlyList<GigaChatScope> GigaChatScopeOptions { get; } =
        [GigaChatScope.PERS, GigaChatScope.B2B, GigaChatScope.CORP];

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
            FolderId = string.IsNullOrWhiteSpace(FolderId) ? null : FolderId,
            AgentId = string.IsNullOrWhiteSpace(AgentId) ? null : AgentId,
            ReasoningMode = ReasoningMode,
            ReasoningEffort = ReasoningEffort,
            GigaChatScope = GigaChatScope
        };
    }
}
