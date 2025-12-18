using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartBasket.Core.Configuration;
using SmartBasket.WPF.Services;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// Главная ViewModel для окна настроек с древовидной навигацией
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly Action<string>? _log;
    private readonly IHttpClientFactory? _httpClientFactory;

    public SettingsViewModel(AppSettings settings, SettingsService settingsService,
        Action<string>? log = null, IHttpClientFactory? httpClientFactory = null)
    {
        _settings = settings;
        _settingsService = settingsService;
        _log = log;
        _httpClientFactory = httpClientFactory;

        LoadFromSettings();
        BuildCategoryTree();
    }

    /// <summary>
    /// Делегат логирования для передачи в дочерние окна
    /// </summary>
    public Action<string>? Log => _log;

    #region Navigation Tree

    /// <summary>
    /// Категории настроек для дерева навигации
    /// </summary>
    public ObservableCollection<SettingsCategoryItem> Categories { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSourcesEditor))]
    [NotifyPropertyChangedFor(nameof(ShowParsersEditor))]
    [NotifyPropertyChangedFor(nameof(ShowAiProvidersEditor))]
    [NotifyPropertyChangedFor(nameof(ShowAiOperationsEditor))]
    private SettingsCategory _selectedCategory = SettingsCategory.Sources;

    public bool ShowSourcesEditor => SelectedCategory == SettingsCategory.Sources;
    public bool ShowParsersEditor => SelectedCategory == SettingsCategory.Parsers;
    public bool ShowAiProvidersEditor => SelectedCategory == SettingsCategory.AiProviders;
    public bool ShowAiOperationsEditor => SelectedCategory == SettingsCategory.AiOperations;

    #endregion

    #region Sources

    public ObservableCollection<ReceiptSourceViewModel> Sources { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSource))]
    private ReceiptSourceViewModel? _selectedSource;

    public bool HasSelectedSource => SelectedSource != null;

    /// <summary>
    /// Доступные парсеры для выбора в источнике
    /// </summary>
    public ObservableCollection<string> AvailableParsers { get; } = new();

    #endregion

    #region Parsers

    public ObservableCollection<ParserViewModel> Parsers { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedParser))]
    private ParserViewModel? _selectedParser;

    public bool HasSelectedParser => SelectedParser != null;

    #endregion

    #region AI Providers

    public ObservableCollection<AiProviderViewModel> AiProviders { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAiProvider))]
    private AiProviderViewModel? _selectedAiProvider;

    public bool HasSelectedAiProvider => SelectedAiProvider != null;

    /// <summary>
    /// Доступные ключи провайдеров для выбора в парсерах и операциях
    /// </summary>
    public ObservableCollection<string> AvailableProviderKeys { get; } = new();

    /// <summary>
    /// Доступные модели Ollama (загружаются динамически)
    /// </summary>
    public ObservableCollection<string> OllamaModels { get; } = new();

    [ObservableProperty]
    private bool _isOllamaAvailable;

    [ObservableProperty]
    private string _ollamaStatusMessage = string.Empty;

    #endregion

    #region AI Operations

    [ObservableProperty]
    private AiOperationsViewModel _aiOperations = new();

    #endregion

    #region State

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    public string SaveButtonText => IsSaving ? "Сохранение..." : "Сохранить";

    #endregion

    #region Load/Save

    private void LoadFromSettings()
    {
        // Sources
        Sources.Clear();
        foreach (var source in _settings.ReceiptSources)
        {
            Sources.Add(new ReceiptSourceViewModel(source));
        }

        // Parsers
        Parsers.Clear();
        foreach (var parser in _settings.Parsers)
        {
            Parsers.Add(new ParserViewModel(parser));
        }
        UpdateAvailableParsers();

        // AI Providers
        AiProviders.Clear();
        foreach (var provider in _settings.AiProviders)
        {
            AiProviders.Add(new AiProviderViewModel(provider));
        }
        UpdateAvailableProviderKeys();

        // AI Operations
        AiOperations = new AiOperationsViewModel(_settings.AiOperations);

        HasUnsavedChanges = false;

        // Проверяем доступность Ollama асинхронно
        _ = CheckOllamaAvailabilityAsync();
    }

    /// <summary>
    /// Проверяет доступность Ollama и загружает список моделей
    /// </summary>
    private async Task CheckOllamaAvailabilityAsync()
    {
        if (_httpClientFactory == null)
        {
            IsOllamaAvailable = false;
            OllamaStatusMessage = "HttpClient недоступен";
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync("http://localhost:11434/api/tags");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var models = ParseOllamaModels(json);

                OllamaModels.Clear();
                foreach (var model in models)
                {
                    OllamaModels.Add(model);
                }

                IsOllamaAvailable = true;
                OllamaStatusMessage = $"Ollama доступен ({models.Count} моделей)";
                _log?.Invoke($"[Settings] Ollama: найдено {models.Count} моделей");
            }
            else
            {
                IsOllamaAvailable = false;
                OllamaStatusMessage = $"Ollama: HTTP {(int)response.StatusCode}";
            }
        }
        catch (HttpRequestException)
        {
            IsOllamaAvailable = false;
            OllamaStatusMessage = "Ollama недоступен";
            _log?.Invoke("[Settings] Ollama недоступен (не запущен?)");
        }
        catch (TaskCanceledException)
        {
            IsOllamaAvailable = false;
            OllamaStatusMessage = "Ollama: таймаут";
        }
        catch (Exception ex)
        {
            IsOllamaAvailable = false;
            OllamaStatusMessage = $"Ollama: ошибка";
            _log?.Invoke($"[Settings] Ошибка проверки Ollama: {ex.Message}");
        }
    }

    /// <summary>
    /// Парсит JSON ответ Ollama API /api/tags
    /// </summary>
    private static List<string> ParseOllamaModels(string json)
    {
        var models = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var name))
                    {
                        models.Add(name.GetString() ?? "");
                    }
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return models;
    }

    private void BuildCategoryTree()
    {
        Categories.Clear();

        // Sources category
        var sourcesCategory = new SettingsCategoryItem
        {
            Name = "Источники",
            Icon = "\uD83D\uDCE7", // envelope
            Category = SettingsCategory.Sources
        };
        foreach (var source in Sources)
        {
            sourcesCategory.Items.Add(new SettingsItemViewModel
            {
                Name = source.Name,
                Key = source.Name,
                Icon = source.Type == SourceType.Email ? "\uD83D\uDCE7" : "\uD83D\uDCC1",
                IsEnabled = source.IsEnabled,
                ParentCategory = SettingsCategory.Sources
            });
        }
        Categories.Add(sourcesCategory);

        // Parsers category
        var parsersCategory = new SettingsCategoryItem
        {
            Name = "Парсеры",
            Icon = "\uD83D\uDCDD", // memo
            Category = SettingsCategory.Parsers
        };
        foreach (var parser in Parsers)
        {
            parsersCategory.Items.Add(new SettingsItemViewModel
            {
                Name = parser.Name,
                Key = parser.Name,
                Icon = parser.Type == ParserType.LLM ? "\uD83E\uDD16" : "\uD83D\uDD0D",
                ParentCategory = SettingsCategory.Parsers
            });
        }
        Categories.Add(parsersCategory);

        // AI Providers category
        var aiProvidersCategory = new SettingsCategoryItem
        {
            Name = "AI Провайдеры",
            Icon = "\uD83E\uDD16", // robot
            Category = SettingsCategory.AiProviders
        };
        foreach (var provider in AiProviders)
        {
            aiProvidersCategory.Items.Add(new SettingsItemViewModel
            {
                Name = provider.Key,
                Key = provider.Key,
                Icon = GetProviderIcon(provider.Provider),
                ParentCategory = SettingsCategory.AiProviders
            });
        }
        Categories.Add(aiProvidersCategory);

        // AI Operations category
        var aiOperationsCategory = new SettingsCategoryItem
        {
            Name = "AI Операции",
            Icon = "\u2699\uFE0F", // gear
            Category = SettingsCategory.AiOperations
        };
        Categories.Add(aiOperationsCategory);
    }

    private static string GetProviderIcon(AiProviderType provider)
    {
        return provider switch
        {
            AiProviderType.Ollama => "\uD83E\uDDA9", // llama
            AiProviderType.YandexGPT => "\uD83C\uDDF7\uD83C\uDDFA", // RU flag
            AiProviderType.OpenAI => "\uD83D\uDFE2", // green circle
            _ => "\uD83E\uDD16"
        };
    }

    private void UpdateAvailableParsers()
    {
        AvailableParsers.Clear();
        foreach (var parser in Parsers)
        {
            AvailableParsers.Add(parser.Name);
        }
    }

    private void UpdateAvailableProviderKeys()
    {
        AvailableProviderKeys.Clear();
        foreach (var provider in AiProviders)
        {
            AvailableProviderKeys.Add(provider.Key);
        }
    }

    /// <summary>
    /// Синхронизирует список ключей провайдеров без сброса выбранных значений в ComboBox.
    /// Заменяет ключи по индексу чтобы сохранить связь.
    /// </summary>
    private void SyncAvailableProviderKeys()
    {
        // Синхронизируем по индексу - если провайдер изменил ключ, обновляем в том же месте
        var currentKeys = AiProviders.Select(p => p.Key).ToList();

        // Удаляем лишние
        while (AvailableProviderKeys.Count > currentKeys.Count)
        {
            AvailableProviderKeys.RemoveAt(AvailableProviderKeys.Count - 1);
        }

        // Обновляем существующие и добавляем новые
        for (int i = 0; i < currentKeys.Count; i++)
        {
            if (i < AvailableProviderKeys.Count)
            {
                if (AvailableProviderKeys[i] != currentKeys[i])
                {
                    AvailableProviderKeys[i] = currentKeys[i];
                }
            }
            else
            {
                AvailableProviderKeys.Add(currentKeys[i]);
            }
        }
    }

    /// <summary>
    /// Обновляет ссылки в AI операциях при переименовании провайдеров
    /// </summary>
    private void UpdateAiOperationsAfterRename()
    {
        foreach (var provider in AiProviders)
        {
            if (provider.KeyWasRenamed)
            {
                var oldKey = provider.OriginalKey;
                var newKey = provider.Key;
                _log?.Invoke($"[Settings] Provider renamed: {oldKey} -> {newKey}");

                // Update Classification reference
                if (AiOperations.Classification == oldKey)
                {
                    AiOperations.Classification = newKey;
                    _log?.Invoke($"[Settings] Updated Classification: {newKey}");
                }

                // Update Labels reference
                if (AiOperations.Labels == oldKey)
                {
                    AiOperations.Labels = newKey;
                    _log?.Invoke($"[Settings] Updated Labels: {newKey}");
                }
            }
        }
    }

    #endregion

    #region Commands - Sources

    [RelayCommand]
    private void AddSource()
    {
        var newSource = new ReceiptSourceViewModel
        {
            Name = $"Source{Sources.Count + 1}",
            Type = SourceType.Email,
            IsEnabled = true
        };
        Sources.Add(newSource);
        SelectedSource = newSource;
        SelectedCategory = SettingsCategory.Sources;
        HasUnsavedChanges = true;
        BuildCategoryTree();
    }

    [RelayCommand]
    private void DeleteSource()
    {
        if (SelectedSource == null) return;

        var result = MessageBox.Show(
            $"Удалить источник '{SelectedSource.Name}'?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Sources.Remove(SelectedSource);
            SelectedSource = Sources.FirstOrDefault();
            HasUnsavedChanges = true;
            BuildCategoryTree();
        }
    }

    [RelayCommand]
    private async Task TestSource()
    {
        if (SelectedSource == null) return;

        var sourceName = SelectedSource.Name;
        StatusMessage = $"Тестирование {sourceName}...";
        _log?.Invoke($"[Settings] Тестирование источника: {sourceName}");

        try
        {
            if (SelectedSource.Type == SourceType.Email)
            {
                // Проверка Email подключения (свойства плоские в ViewModel)
                _log?.Invoke($"[Settings] IMAP: {SelectedSource.ImapServer}:{SelectedSource.ImapPort}, SSL: {SelectedSource.UseSsl}");

                // Простая проверка - если поля заполнены
                if (string.IsNullOrWhiteSpace(SelectedSource.ImapServer) ||
                    string.IsNullOrWhiteSpace(SelectedSource.Username) ||
                    string.IsNullOrWhiteSpace(SelectedSource.Password))
                {
                    StatusMessage = $"Тест {sourceName}: Заполните все поля";
                    _log?.Invoke($"[Settings] Ошибка: не все поля заполнены");
                    return;
                }

                // TODO: Реальное подключение к IMAP
                await Task.Delay(300);
                StatusMessage = $"Тест {sourceName}: OK (проверены настройки)";
                _log?.Invoke($"[Settings] Тест {sourceName} пройден");
            }
            else
            {
                StatusMessage = $"Тест {sourceName}: неподдерживаемый тип";
                _log?.Invoke($"[Settings] Тип источника не поддерживается для теста");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Тест {sourceName}: Ошибка - {ex.Message}";
            _log?.Invoke($"[Settings] Ошибка теста: {ex.Message}");
        }
    }

    #endregion

    #region Commands - Parsers

    [RelayCommand]
    private void AddParser()
    {
        var newParser = new ParserViewModel
        {
            Name = $"Parser{Parsers.Count + 1}",
            Type = ParserType.Regex,
            RequiresAI = false
        };
        Parsers.Add(newParser);
        SelectedParser = newParser;
        SelectedCategory = SettingsCategory.Parsers;
        HasUnsavedChanges = true;
        UpdateAvailableParsers();
        BuildCategoryTree();
    }

    [RelayCommand]
    private void DeleteParser()
    {
        if (SelectedParser == null) return;

        var result = MessageBox.Show(
            $"Удалить парсер '{SelectedParser.Name}'?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Parsers.Remove(SelectedParser);
            SelectedParser = Parsers.FirstOrDefault();
            HasUnsavedChanges = true;
            UpdateAvailableParsers();
            BuildCategoryTree();
        }
    }

    #endregion

    #region Commands - AI Providers

    [RelayCommand]
    private void AddAiProvider()
    {
        var newProvider = new AiProviderViewModel
        {
            Provider = AiProviderType.Ollama,
            Model = "llama3.2:3b",
            BaseUrl = "http://localhost:11434",
            Temperature = 0.1,
            TimeoutSeconds = 60
        };
        newProvider.GenerateKey();
        AiProviders.Add(newProvider);
        SelectedAiProvider = newProvider;
        SelectedCategory = SettingsCategory.AiProviders;
        HasUnsavedChanges = true;
        UpdateAvailableProviderKeys();
        BuildCategoryTree();
    }

    [RelayCommand]
    private void DeleteAiProvider()
    {
        if (SelectedAiProvider == null) return;

        var result = MessageBox.Show(
            $"Удалить провайдер '{SelectedAiProvider.Key}'?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            AiProviders.Remove(SelectedAiProvider);
            SelectedAiProvider = AiProviders.FirstOrDefault();
            HasUnsavedChanges = true;
            UpdateAvailableProviderKeys();
            BuildCategoryTree();
        }
    }

    [RelayCommand]
    private async Task TestAiProvider()
    {
        if (SelectedAiProvider == null) return;

        var providerKey = SelectedAiProvider.Key;
        StatusMessage = $"Тестирование {providerKey}...";
        _log?.Invoke($"[Settings] Тестирование AI провайдера: {providerKey}");

        try
        {
            if (SelectedAiProvider.Provider == AiProviderType.Ollama)
            {
                var baseUrl = SelectedAiProvider.BaseUrl ?? "http://localhost:11434";
                _log?.Invoke($"[Settings] Ollama URL: {baseUrl}");

                if (_httpClientFactory == null)
                {
                    StatusMessage = $"Тест {providerKey}: HttpClient недоступен";
                    _log?.Invoke($"[Settings] Ошибка: HttpClientFactory не инициализирован");
                    return;
                }

                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                // Проверяем доступность Ollama
                var response = await client.GetAsync($"{baseUrl}/api/tags");
                if (response.IsSuccessStatusCode)
                {
                    StatusMessage = $"Тест {providerKey}: OK";
                    _log?.Invoke($"[Settings] Ollama доступен, тест пройден");
                }
                else
                {
                    StatusMessage = $"Тест {providerKey}: HTTP {(int)response.StatusCode}";
                    _log?.Invoke($"[Settings] Ollama вернул: {response.StatusCode}");
                }
            }
            else if (SelectedAiProvider.Provider == AiProviderType.YandexGPT)
            {
                // Проверка обязательных полей
                if (string.IsNullOrWhiteSpace(SelectedAiProvider.ApiKey))
                {
                    StatusMessage = $"Тест {providerKey}: API ключ не указан";
                    _log?.Invoke($"[Settings] YandexGPT: API ключ отсутствует");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedAiProvider.FolderId))
                {
                    StatusMessage = $"Тест {providerKey}: Folder ID не указан";
                    _log?.Invoke($"[Settings] YandexGPT: Folder ID отсутствует");
                    return;
                }

                if (_httpClientFactory == null)
                {
                    StatusMessage = $"Тест {providerKey}: HttpClient недоступен";
                    _log?.Invoke($"[Settings] Ошибка: HttpClientFactory не инициализирован");
                    return;
                }

                // Реальный тест YandexGPT API (OpenAI-совместимый endpoint)
                _log?.Invoke($"[Settings] YandexGPT: тестовый запрос к OpenAI-совместимому API...");

                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Формируем modelUri
                // Если модель уже содержит версию (/rc, /latest, /deprecated), не добавляем /latest
                var model = SelectedAiProvider.Model ?? "yandexgpt-lite";
                var folderId = SelectedAiProvider.FolderId;
                bool hasVersion = model.StartsWith("general:") ||
                                  model.EndsWith("/rc") ||
                                  model.EndsWith("/latest") ||
                                  model.EndsWith("/deprecated") ||
                                  model.Contains("/latest@");
                string modelUri = hasVersion
                    ? $"gpt://{folderId}/{model}"
                    : $"gpt://{folderId}/{model}/latest";

                // OpenAI-совместимый формат запроса
                var requestBody = new
                {
                    model = modelUri,
                    messages = new[]
                    {
                        new { role = "user", content = "Ответь одним словом: Работает" }
                    },
                    max_tokens = 10,
                    temperature = 0.1,
                    stream = false
                };

                var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // OpenAI-совместимый endpoint
                var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Post,
                    "https://llm.api.cloud.yandex.net/v1/chat/completions")
                {
                    Content = content
                };

                // Авторизация
                var apiKey = SelectedAiProvider.ApiKey;
                if (apiKey.StartsWith("t1.") || apiKey.StartsWith("y"))
                {
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                }
                else
                {
                    request.Headers.Add("Authorization", $"Api-Key {apiKey}");
                }
                request.Headers.Add("x-folder-id", folderId);

                _log?.Invoke($"[Settings] YandexGPT URL: https://llm.api.cloud.yandex.net/v1/chat/completions");
                _log?.Invoke($"[Settings] YandexGPT Model: {modelUri}");

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _log?.Invoke($"[Settings] YandexGPT ответ: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}...");
                    StatusMessage = $"Тест {providerKey}: OK";
                    _log?.Invoke($"[Settings] YandexGPT: тест пройден успешно!");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    StatusMessage = $"Тест {providerKey}: HTTP {(int)response.StatusCode}";
                    _log?.Invoke($"[Settings] YandexGPT ошибка: {response.StatusCode}");
                    _log?.Invoke($"[Settings] YandexGPT ответ: {errorBody}");
                }
            }
            else if (SelectedAiProvider.Provider == AiProviderType.YandexAgent)
            {
                // Проверка обязательных полей
                if (string.IsNullOrWhiteSpace(SelectedAiProvider.ApiKey))
                {
                    StatusMessage = $"Тест {providerKey}: API ключ не указан";
                    _log?.Invoke($"[Settings] YandexAgent: API ключ отсутствует");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedAiProvider.FolderId))
                {
                    StatusMessage = $"Тест {providerKey}: Folder ID не указан";
                    _log?.Invoke($"[Settings] YandexAgent: Folder ID отсутствует");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedAiProvider.AgentId))
                {
                    StatusMessage = $"Тест {providerKey}: Agent ID не указан";
                    _log?.Invoke($"[Settings] YandexAgent: Agent ID отсутствует");
                    return;
                }

                if (_httpClientFactory == null)
                {
                    StatusMessage = $"Тест {providerKey}: HttpClient недоступен";
                    _log?.Invoke($"[Settings] Ошибка: HttpClientFactory не инициализирован");
                    return;
                }

                // Реальный тест YandexAgent через REST Assistant API
                _log?.Invoke($"[Settings] YandexAgent: тестовый запрос к REST Assistant API...");

                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(60);

                // REST Assistant API формат
                var folderId = SelectedAiProvider.FolderId;
                var agentId = SelectedAiProvider.AgentId;

                var requestBody = new
                {
                    prompt = new { id = agentId },
                    input = "Привет! Ответь одним словом: Работает"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(requestBody, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                _log?.Invoke($"[Settings] YandexAgent REQUEST BODY:");
                _log?.Invoke(json);

                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Post,
                    "https://rest-assistant.api.cloud.yandex.net/v1/responses")
                {
                    Content = content
                };

                // Авторизация - Bearer token
                var apiKey = SelectedAiProvider.ApiKey;
                var authHeader = $"Bearer {apiKey}";
                request.Headers.Add("Authorization", authHeader);
                request.Headers.Add("x-folder-id", folderId);

                _log?.Invoke($"[Settings] YandexAgent URL: https://rest-assistant.api.cloud.yandex.net/v1/responses");
                _log?.Invoke($"[Settings] YandexAgent Authorization: {authHeader.Substring(0, Math.Min(20, authHeader.Length))}...");
                _log?.Invoke($"[Settings] YandexAgent x-folder-id: {folderId}");
                _log?.Invoke($"[Settings] YandexAgent Agent ID: {agentId}");

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _log?.Invoke($"[Settings] YandexAgent ответ: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}...");
                    StatusMessage = $"Тест {providerKey}: OK";
                    _log?.Invoke($"[Settings] YandexAgent: тест пройден успешно!");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    StatusMessage = $"Тест {providerKey}: HTTP {(int)response.StatusCode}";
                    _log?.Invoke($"[Settings] YandexAgent ошибка: {response.StatusCode}");
                    _log?.Invoke($"[Settings] YandexAgent ответ: {errorBody}");
                }
            }
            else
            {
                StatusMessage = $"Тест {providerKey}: неподдерживаемый провайдер";
                _log?.Invoke($"[Settings] Провайдер {SelectedAiProvider.Provider} не поддерживается");
            }
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Тест {providerKey}: Недоступен";
            _log?.Invoke($"[Settings] Ошибка подключения: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            StatusMessage = $"Тест {providerKey}: Таймаут";
            _log?.Invoke($"[Settings] Таймаут подключения к провайдеру");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Тест {providerKey}: Ошибка";
            _log?.Invoke($"[Settings] Ошибка теста: {ex.Message}");
        }
    }

    #endregion

    #region Commands - Save/Cancel

    private bool CanSave() => !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        _log?.Invoke("[Settings] SaveAsync called");

        try
        {
            IsSaving = true;
            StatusMessage = "Сохранение...";
            _log?.Invoke("[Settings] Starting save...");

            // Update settings from ViewModels (UI thread)
            _settings.ReceiptSources.Clear();
            foreach (var source in Sources)
            {
                _settings.ReceiptSources.Add(source.ToConfig());
            }
            _log?.Invoke($"[Settings] Sources: {Sources.Count}");

            _settings.Parsers.Clear();
            foreach (var parser in Parsers)
            {
                _settings.Parsers.Add(parser.ToConfig());
            }
            _log?.Invoke($"[Settings] Parsers: {Parsers.Count}");

            _settings.AiProviders.Clear();
            foreach (var provider in AiProviders)
            {
                _settings.AiProviders.Add(provider.ToConfig());
            }
            _log?.Invoke($"[Settings] AiProviders: {AiProviders.Count}");

            _settings.AiOperations = AiOperations.ToConfig();

            // Log settings JSON (masked)
            LogSettingsJson();

            // Save to file - offload to ThreadPool (WPF_RULES #3)
            var settingsPath = _settingsService.SettingsPath;
            _log?.Invoke($"[Settings] Saving to: {settingsPath}");

            await Task.Run(async () =>
            {
                await _settingsService.SaveSettingsAsync(_settings).ConfigureAwait(false);
            }).ConfigureAwait(true); // Return to UI thread

            HasUnsavedChanges = false;
            StatusMessage = "Настройки сохранены";
            _log?.Invoke("[Settings] Save completed successfully!");

            // Update AI Operations references if provider keys were renamed
            UpdateAiOperationsAfterRename();

            // Commit new keys as original (for next rename detection)
            foreach (var provider in AiProviders)
            {
                provider.CommitKey();
            }

            // Rebuild category tree to reflect name changes
            BuildCategoryTree();

            // Sync available keys (for ComboBox dropdowns)
            SyncAvailableProviderKeys();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
            _log?.Invoke($"[Settings] Save ERROR: {ex.Message}");
            MessageBox.Show(
                $"Ошибка сохранения настроек:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Логирует JSON настроек с маскированными секретами
    /// </summary>
    private void LogSettingsJson()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Mask secrets
            json = System.Text.RegularExpressions.Regex.Replace(
                json,
                @"""(Password|ApiKey|apiKey|password)""\s*:\s*""[^""]+""",
                @"""$1"": ""***MASKED***""");

            _log?.Invoke($"[Settings] JSON:\n{json}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Settings] Failed to serialize JSON: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "Есть несохранённые изменения. Отменить?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;
        }

        // Reload from settings
        LoadFromSettings();
        BuildCategoryTree();
        StatusMessage = "Изменения отменены";
    }

    #endregion

    #region Navigation

    public void SelectCategory(SettingsCategory category)
    {
        SelectedCategory = category;
    }

    public void SelectItem(SettingsItemViewModel item)
    {
        SelectedCategory = item.ParentCategory;

        switch (item.ParentCategory)
        {
            case SettingsCategory.Sources:
                SelectedSource = Sources.FirstOrDefault(s => s.Name == item.Key);
                break;
            case SettingsCategory.Parsers:
                SelectedParser = Parsers.FirstOrDefault(p => p.Name == item.Key);
                break;
            case SettingsCategory.AiProviders:
                SelectedAiProvider = AiProviders.FirstOrDefault(p => p.Key == item.Key);
                break;
        }
    }

    #endregion

    /// <summary>
    /// Отмечает, что есть несохранённые изменения
    /// </summary>
    public void MarkAsChanged()
    {
        HasUnsavedChanges = true;
    }
}
