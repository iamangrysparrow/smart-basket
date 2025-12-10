namespace SmartBasket.Core.Configuration;

/// <summary>
/// Сервис миграции конфигурации из legacy формата в новый
/// </summary>
public class ConfigurationMigrationService
{
    private readonly Action<string>? _log;

    public ConfigurationMigrationService(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>
    /// Проверить и выполнить миграцию legacy конфигурации
    /// Возвращает true если миграция была выполнена
    /// </summary>
    public bool MigrateIfNeeded(AppSettings settings)
    {
        var migrated = false;

        // Миграция Email -> ReceiptSources
        if (HasLegacyEmailSettings(settings) && !HasNewReceiptSources(settings))
        {
            MigrateEmailToReceiptSource(settings);
            migrated = true;
        }

        // Миграция Ollama -> AiProviders
        if (HasLegacyOllamaSettings(settings) && !HasOllamaProvider(settings))
        {
            MigrateOllamaToAiProvider(settings);
            migrated = true;
        }

        // Миграция YandexGpt -> AiProviders
        if (HasLegacyYandexGptSettings(settings) && !HasYandexGptProvider(settings))
        {
            MigrateYandexGptToAiProvider(settings);
            migrated = true;
        }

        // Миграция Llm -> AiOperations
        if (HasLegacyLlmSettings(settings) && !HasNewAiOperations(settings))
        {
            MigrateLlmToAiOperations(settings);
            migrated = true;
        }

        // Добавление встроенных парсеров (если их нет)
        if (!HasBuiltInParsers(settings))
        {
            AddBuiltInParsers(settings);
            migrated = true;
        }

        if (migrated)
        {
            _log?.Invoke("Legacy configuration migrated to new format. Please update your appsettings.json");
        }

        return migrated;
    }

    #region Checks

    private static bool HasLegacyEmailSettings(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return settings.Email != null &&
               !string.IsNullOrWhiteSpace(settings.Email.ImapServer) &&
               !string.IsNullOrWhiteSpace(settings.Email.Username);
#pragma warning restore CS0618
    }

    private static bool HasNewReceiptSources(AppSettings settings)
    {
        return settings.ReceiptSources != null && settings.ReceiptSources.Count > 0;
    }

    private static bool HasLegacyOllamaSettings(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return settings.Ollama != null &&
               !string.IsNullOrWhiteSpace(settings.Ollama.BaseUrl);
#pragma warning restore CS0618
    }

    private static bool HasOllamaProvider(AppSettings settings)
    {
        return settings.AiProviders?.Any(p => p.Provider == AiProviderType.Ollama) ?? false;
    }

    private static bool HasLegacyYandexGptSettings(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return settings.YandexGpt != null &&
               !string.IsNullOrWhiteSpace(settings.YandexGpt.ApiKey);
#pragma warning restore CS0618
    }

    private static bool HasYandexGptProvider(AppSettings settings)
    {
        return settings.AiProviders?.Any(p => p.Provider == AiProviderType.YandexGPT) ?? false;
    }

    private static bool HasLegacyLlmSettings(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return settings.Llm != null;
#pragma warning restore CS0618
    }

    private static bool HasNewAiOperations(AppSettings settings)
    {
        return settings.AiOperations != null &&
               (!string.IsNullOrWhiteSpace(settings.AiOperations.Classification) ||
                !string.IsNullOrWhiteSpace(settings.AiOperations.Labels));
    }

    private static bool HasBuiltInParsers(AppSettings settings)
    {
        return settings.Parsers?.Any(p => p.IsBuiltIn) ?? false;
    }

    #endregion

    #region Migrations

    private void MigrateEmailToReceiptSource(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var legacy = settings.Email!;
#pragma warning restore CS0618

        settings.ReceiptSources ??= new List<ReceiptSourceConfig>();

        var source = new ReceiptSourceConfig
        {
            Name = "Email (migrated)",
            Type = SourceType.Email,
            Parser = "Auto", // будет выбран автоматически
            IsEnabled = true,
            Email = new EmailSourceConfig
            {
                ImapServer = legacy.ImapServer,
                ImapPort = legacy.ImapPort,
                UseSsl = legacy.UseSsl,
                Username = legacy.Username,
                Password = legacy.Password,
                SenderFilter = legacy.SenderFilter,
                SubjectFilter = legacy.SubjectFilter,
                Folder = legacy.Folder,
                SearchDaysBack = legacy.SearchDaysBack
            }
        };

        settings.ReceiptSources.Add(source);
        _log?.Invoke($"Migrated legacy Email settings to ReceiptSources[{source.Name}]");
    }

    private void MigrateOllamaToAiProvider(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var legacy = settings.Ollama!;
#pragma warning restore CS0618

        settings.AiProviders ??= new List<AiProviderConfig>();

        var provider = new AiProviderConfig
        {
            Key = $"Ollama/{legacy.Model}",
            Provider = AiProviderType.Ollama,
            Model = legacy.Model,
            BaseUrl = legacy.BaseUrl,
            Temperature = legacy.Temperature,
            TimeoutSeconds = legacy.TimeoutSeconds
        };

        settings.AiProviders.Add(provider);
        _log?.Invoke($"Migrated legacy Ollama settings to AiProviders[{provider.Key}]");
    }

    private void MigrateYandexGptToAiProvider(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var legacy = settings.YandexGpt!;
#pragma warning restore CS0618

        settings.AiProviders ??= new List<AiProviderConfig>();

        var provider = new AiProviderConfig
        {
            Key = $"YandexGPT/{legacy.Model}",
            Provider = AiProviderType.YandexGPT,
            Model = legacy.Model,
            ApiKey = legacy.ApiKey,
            FolderId = legacy.FolderId,
            Temperature = legacy.Temperature,
            TimeoutSeconds = legacy.TimeoutSeconds,
            MaxTokens = legacy.MaxTokens
        };

        settings.AiProviders.Add(provider);
        _log?.Invoke($"Migrated legacy YandexGpt settings to AiProviders[{provider.Key}]");
    }

    private void MigrateLlmToAiOperations(AppSettings settings)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var legacy = settings.Llm;
        var ollama = settings.Ollama;
        var yandex = settings.YandexGpt;
#pragma warning restore CS0618

        settings.AiOperations ??= new AiOperationsConfig();

        // Определить ключ провайдера для каждой операции
        string? GetProviderKey(LlmProviderType type)
        {
            return type switch
            {
                LlmProviderType.Ollama when ollama != null => $"Ollama/{ollama.Model}",
                LlmProviderType.YandexGpt when yandex != null => $"YandexGPT/{yandex.Model}",
                _ => null
            };
        }

        if (legacy != null)
        {
            settings.AiOperations.Classification = GetProviderKey(legacy.ClassificationProvider);
            settings.AiOperations.Labels = GetProviderKey(legacy.LabelsProvider);
            _log?.Invoke("Migrated legacy Llm settings to AiOperations");
        }
    }

    private void AddBuiltInParsers(AppSettings settings)
    {
        settings.Parsers ??= new List<ParserConfig>();

        // InstamartParser - встроенный regex-парсер для СберМаркет/Instamart
        var instamartParser = new ParserConfig
        {
            Name = "InstamartParser",
            Type = ParserType.Regex,
            RequiresAI = false,
            Description = "Парсер HTML-чеков от СберМаркет (Instamart)",
            SupportedShops = new List<string> { "СберМаркет", "Instamart", "sbermarket.ru" },
            SupportedFormats = new List<string> { "html" },
            IsBuiltIn = true,
            IsEnabled = true
        };
        settings.Parsers.Insert(0, instamartParser);
        _log?.Invoke($"Added built-in parser: {instamartParser.Name}");

        // LLM Universal Parser - fallback парсер на основе AI
        var llmParser = new ParserConfig
        {
            Name = "LlmUniversalParser",
            Type = ParserType.LLM,
            RequiresAI = true,
            AiProvider = settings.AiProviders?.FirstOrDefault()?.Key,
            Description = "Универсальный AI-парсер для любых форматов чеков",
            SupportedShops = new List<string> { "*" },
            SupportedFormats = new List<string> { "html", "text", "json" },
            IsBuiltIn = true,
            IsEnabled = true
        };
        settings.Parsers.Add(llmParser);
        _log?.Invoke($"Added built-in parser: {llmParser.Name}");
    }

    #endregion
}
