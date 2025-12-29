using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

public class LabelAssignmentService : ILabelAssignmentService
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly IResponseParser _responseParser;
    private readonly ILogger<LabelAssignmentService> _logger;
    private string? _systemPromptPath;
    private string? _userPromptPath;
    private bool _promptPathsInitialized;

    public LabelAssignmentService(
        IAiProviderFactory providerFactory,
        IResponseParser responseParser,
        ILogger<LabelAssignmentService> logger)
    {
        _providerFactory = providerFactory;
        _responseParser = responseParser;
        _logger = logger;
    }

    public void SetPromptPaths(string systemPath, string userPath)
    {
        _systemPromptPath = systemPath;
        _userPromptPath = userPath;
        _promptPathsInitialized = true;
        _logger.LogDebug("Prompt paths set: system={SystemPath}, user={UserPath}", systemPath, userPath);
    }

    /// <summary>
    /// Инициализировать пути к файлам промптов из директории приложения
    /// </summary>
    private void EnsurePromptPathsInitialized()
    {
        if (_promptPathsInitialized) return;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var systemPath = Path.Combine(appDir, "prompt_assign_labels_system.txt");
        var userPath = Path.Combine(appDir, "prompt_assign_labels_user.txt");

        if (File.Exists(systemPath))
        {
            _systemPromptPath = systemPath;
            _logger.LogDebug("Auto-detected system prompt: {Path}", systemPath);
        }

        if (File.Exists(userPath))
        {
            _userPromptPath = userPath;
            _logger.LogDebug("Auto-detected user prompt: {Path}", userPath);
        }

        _promptPathsInitialized = true;
    }

    public async Task<LabelAssignmentResult> AssignLabelsAsync(
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LabelAssignmentResult();

        try
        {
            if (availableLabels.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No labels available";
                return result;
            }

            // Получаем провайдер для меток
            _logger.LogDebug("Getting provider for Labels operation");
            var provider = _providerFactory.GetProviderForOperation(AiOperation.Labels);
            if (provider == null)
            {
                var errorMsg = "No AI provider configured for Labels operation. Check AiOperations.Labels in settings.";
                _logger.LogError(errorMsg);
                progress?.Report($"  [Labels] ERROR: {errorMsg}");
                result.IsSuccess = false;
                result.Message = errorMsg;
                return result;
            }
            _logger.LogDebug("Got provider: {ProviderName}", provider.Name);
            progress?.Report($"  [Labels] Using provider: {provider.Name}");

            var (systemPrompt, userPrompt) = BuildSingleItemMessages(itemName, productName, availableLabels, progress);
            progress?.Report($"  [Labels] Assigning labels for: {itemName}");
            progress?.Report($"  [Labels] === SYSTEM PROMPT ===");
            progress?.Report(systemPrompt);
            progress?.Report($"  [Labels] === USER PROMPT ===");
            progress?.Report(userPrompt);
            progress?.Report($"  [Labels] === END ===");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Labels] Sending request via {provider.Name}...");

            var messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            };

            var llmResult = await provider.ChatAsync(
                messages,
                tools: null,
                sessionContext: sessionContext,
                progress: progress,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            progress?.Report($"  [Labels] Response received in {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!llmResult.IsSuccess || string.IsNullOrEmpty(llmResult.Response))
            {
                result.IsSuccess = false;
                result.Message = llmResult.ErrorMessage ?? "Empty response from LLM";
                return result;
            }

            result.RawResponse = llmResult.Response;
            progress?.Report($"  [Labels] Total response: {llmResult.Response.Length} chars");

            // Extract JSON array using unified ResponseParser
            var parseResult = _responseParser.ParseJsonArray<string>(llmResult.Response, progress);

            if (parseResult.IsSuccess && parseResult.Data != null)
            {
                // Filter to only include valid labels
                var validLabels = parseResult.Data
                    .Where(l => availableLabels.Contains(l, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                result.AssignedLabels = validLabels;
                result.IsSuccess = true;
                result.Message = validLabels.Count > 0
                    ? $"Assigned {validLabels.Count} labels (method: {parseResult.ExtractionMethod})"
                    : "No labels assigned";
            }
            else
            {
                // No labels assigned is OK (model might return empty or text response)
                result.IsSuccess = true;
                result.AssignedLabels = new List<string>();
                result.Message = "No labels assigned";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Label assignment error");
        }

        return result;
    }

    public async Task<BatchLabelAssignmentResult> AssignLabelsBatchAsync(
        IReadOnlyList<BatchItemInput> items,
        IReadOnlyList<string> availableLabels,
        LlmSessionContext? sessionContext = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchLabelAssignmentResult();

        try
        {
            if (availableLabels.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No labels available";
                return result;
            }

            if (items.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "No items to classify";
                return result;
            }

            // Получаем провайдер для меток
            _logger.LogDebug("Getting provider for Labels operation (batch)");
            var provider = _providerFactory.GetProviderForOperation(AiOperation.Labels);
            if (provider == null)
            {
                var errorMsg = "No AI provider configured for Labels operation. Check AiOperations.Labels in settings.";
                _logger.LogError(errorMsg);
                progress?.Report($"  [Labels] ERROR: {errorMsg}");
                result.IsSuccess = false;
                result.Message = errorMsg;
                return result;
            }
            _logger.LogDebug("Got provider: {ProviderName}", provider.Name);
            progress?.Report($"  [Labels] Using provider: {provider.Name}");

            var (systemPrompt, userPrompt) = BuildBatchMessages(items, availableLabels);
            progress?.Report($"  [Labels] Assigning labels for {items.Count} items (batch)");
            progress?.Report($"  [Labels] === SYSTEM PROMPT ===");
            progress?.Report(systemPrompt);
            progress?.Report($"  [Labels] === USER PROMPT ===");
            progress?.Report(userPrompt);
            progress?.Report($"  [Labels] === END ===");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Labels] Sending request via {provider.Name}...");

            var messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            };

            var llmResult = await provider.ChatAsync(
                messages,
                tools: null,
                sessionContext: sessionContext,
                progress: progress,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            progress?.Report($"  [Labels] Response received in {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!llmResult.IsSuccess || string.IsNullOrEmpty(llmResult.Response))
            {
                result.IsSuccess = false;
                result.Message = llmResult.ErrorMessage ?? "Empty response from LLM";
                return result;
            }

            result.RawResponse = llmResult.Response;
            progress?.Report($"  [Labels] Total response: {llmResult.Response.Length} chars");

            // Parse batch response
            result.Results = ParseBatchResponse(llmResult.Response, items, availableLabels);
            result.IsSuccess = true;
            result.Message = $"Assigned labels for {result.Results.Count} items";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Batch label assignment error");
        }

        return result;
    }

    private List<BatchItemResult> ParseBatchResponse(
        string response,
        IReadOnlyList<BatchItemInput> items,
        IReadOnlyList<string> availableLabels)
    {
        var results = new List<BatchItemResult>();

        // Extract JSON array using unified ResponseParser
        var parseResult = _responseParser.ParseJsonArray<BatchResponseItem>(response, null);

        if (!parseResult.IsSuccess || parseResult.Data == null)
            return results;

        // Создаем lookup для быстрого поиска
        var availableLabelsSet = new HashSet<string>(availableLabels, StringComparer.OrdinalIgnoreCase);

        foreach (var parsedItem in parseResult.Data)
        {
            if (string.IsNullOrWhiteSpace(parsedItem.Item))
                continue;

            // Фильтруем только валидные метки
            var validLabels = parsedItem.Labels?
                .Where(l => availableLabelsSet.Contains(l))
                .ToList() ?? new List<string>();

            results.Add(new BatchItemResult
            {
                ItemName = parsedItem.Item,
                Labels = validLabels
            });
        }

        return results;
    }

    private (string SystemPrompt, string UserPrompt) BuildBatchMessages(
        IReadOnlyList<BatchItemInput> items,
        IReadOnlyList<string> availableLabels)
    {
        // Автоматически инициализировать пути к промптам, если не установлены
        EnsurePromptPathsInitialized();

        var labelsList = string.Join("\n", availableLabels.Select(l => $"- {l}"));
        var itemsList = string.Join("\n", items.Select((item, i) =>
            $"{i + 1}. \"{item.ItemName}\" (категория: {item.ProductName})"));

        // Load from files
        if (!string.IsNullOrEmpty(_systemPromptPath) && File.Exists(_systemPromptPath) &&
            !string.IsNullOrEmpty(_userPromptPath) && File.Exists(_userPromptPath))
        {
            try
            {
                var systemPrompt = File.ReadAllText(_systemPromptPath);
                var userPrompt = File.ReadAllText(_userPromptPath)
                    .Replace("{{LABELS}}", labelsList)
                    .Replace("{{ITEMS}}", itemsList);
                return (systemPrompt, userPrompt);
            }
            catch
            {
                // Fall through to defaults
            }
        }

        return GetDefaultBatchPrompts(labelsList, itemsList);
    }

    private static (string System, string User) GetDefaultBatchPrompts(string labelsList, string itemsList)
    {
        var system = @"Ты — эксперт по назначению меток товарам.

ПРАВИЛА:
1. Для каждого товара выбери подходящие метки ТОЛЬКО из предоставленного списка
2. Товар может иметь 0, 1 или несколько меток
3. НЕ придумывай новые метки
4. Если ни одна метка не подходит — верни пустой массив labels: []

ФОРМАТ ОТВЕТА (строго JSON массив объектов):
[
  {""item"": ""название товара 1"", ""labels"": [""Метка1"", ""Метка2""]},
  {""item"": ""название товара 2"", ""labels"": [""Метка3""]},
  {""item"": ""название товара 3"", ""labels"": []}
]

ПРИДУМЫВАТЬ СВОИ МЕТКИ ЗАПРЕЩЕНО!";

        var user = $@"ДОСТУПНЫЕ МЕТКИ:
{labelsList}

ТОВАРЫ:
{itemsList}

Назначь подходящие метки для каждого товара из списка.";

        return (system, user);
    }

    private (string SystemPrompt, string UserPrompt) BuildSingleItemMessages(
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
        IProgress<string>? progress = null)
    {
        // Автоматически инициализировать пути к промптам, если не установлены
        EnsurePromptPathsInitialized();

        var labelsList = string.Join("\n", availableLabels.Select(l => $"- {l}"));
        var itemsList = $"1. \"{itemName}\" (категория: {productName})";

        // Load from files - use same format as batch
        if (!string.IsNullOrEmpty(_systemPromptPath) && File.Exists(_systemPromptPath) &&
            !string.IsNullOrEmpty(_userPromptPath) && File.Exists(_userPromptPath))
        {
            try
            {
                var systemPrompt = File.ReadAllText(_systemPromptPath);
                var userPrompt = File.ReadAllText(_userPromptPath)
                    .Replace("{{LABELS}}", labelsList)
                    .Replace("{{ITEMS}}", itemsList);
                progress?.Report($"  [Labels] Loaded prompts from files");
                return (systemPrompt, userPrompt);
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Labels] Failed to load prompts: {ex.Message}, using defaults");
            }
        }
        else
        {
            progress?.Report($"  [Labels] Prompt files not configured, using defaults");
        }

        return GetDefaultBatchPrompts(labelsList, itemsList);
    }

    // DTO for batch response parsing
    private class BatchResponseItem
    {
        [JsonPropertyName("item")]
        public string Item { get; set; } = string.Empty;

        [JsonPropertyName("labels")]
        public List<string>? Labels { get; set; }
    }
}
