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
    private string? _promptTemplate;
    private string? _promptTemplatePath;
    private string? _customPrompt;

    public LabelAssignmentService(
        IAiProviderFactory providerFactory,
        IResponseParser responseParser,
        ILogger<LabelAssignmentService> logger)
    {
        _providerFactory = providerFactory;
        _responseParser = responseParser;
        _logger = logger;
    }

    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null;
    }

    public void SetCustomPrompt(string? prompt)
    {
        _customPrompt = prompt;
        _logger.LogDebug("Custom prompt set: {HasPrompt}", !string.IsNullOrWhiteSpace(prompt));
    }

    public async Task<LabelAssignmentResult> AssignLabelsAsync(
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
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

            var prompt = BuildPrompt(itemName, productName, availableLabels, progress);
            progress?.Report($"  [Labels] Assigning labels for: {itemName}");
            progress?.Report($"  [Labels] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [Labels] === PROMPT END ===");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Labels] Sending request via {provider.Name}...");

            var llmResult = await provider.GenerateAsync(
                prompt,
                maxTokens: 256, // Labels are short
                temperature: 0.1,
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

            var prompt = BuildBatchPrompt(items, availableLabels);
            progress?.Report($"  [Labels] Assigning labels for {items.Count} items (batch)");
            progress?.Report($"  [Labels] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [Labels] === PROMPT END ===");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [Labels] Sending request via {provider.Name}...");

            var llmResult = await provider.GenerateAsync(
                prompt,
                maxTokens: 1024, // Больше токенов для batch
                temperature: 0.1,
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

    private string BuildBatchPrompt(
        IReadOnlyList<BatchItemInput> items,
        IReadOnlyList<string> availableLabels)
    {
        var labelsList = string.Join("\n", availableLabels.Select(l => $"- {l}"));
        var itemsList = string.Join("\n", items.Select((item, i) =>
            $"{i + 1}. \"{item.ItemName}\" (категория: {item.ProductName})"));

        // Priority 1: Custom prompt (from settings)
        if (!string.IsNullOrWhiteSpace(_customPrompt))
        {
            return _customPrompt
                .Replace("{{LABELS}}", labelsList)
                .Replace("{{ITEMS}}", itemsList);
        }

        // Priority 2: Template from file
        if (!string.IsNullOrEmpty(_promptTemplatePath) && File.Exists(_promptTemplatePath))
        {
            try
            {
                var template = File.ReadAllText(_promptTemplatePath);
                return template
                    .Replace("{{LABELS}}", labelsList)
                    .Replace("{{ITEMS}}", itemsList);
            }
            catch
            {
                // Fall through to default prompt
            }
        }

        // Priority 3: Default prompt (fallback)
        return $@"Назначь подходящие метки для каждого товара из списка.

ДОСТУПНЫЕ МЕТКИ:
{labelsList}

ТОВАРЫ:
{itemsList}

ФОРМАТ ОТВЕТА (строго JSON массив объектов):
[
  {{""item"": ""название товара 1"", ""labels"": [""Метка1"", ""Метка2""]}},
  {{""item"": ""название товара 2"", ""labels"": [""Метка3""]}}
]

Для каждого товара выбери наиболее подходящие метки из списка ДОСТУПНЫЕ МЕТКИ.
Если ни одна метка не подходит, верни пустой массив labels: [].

Назначь метки:";
    }

    private string BuildPrompt(
        string itemName,
        string productName,
        IReadOnlyList<string> availableLabels,
        IProgress<string>? progress = null)
    {
        var labelsList = string.Join("\n", availableLabels.Select(l => $"- {l}"));

        // Priority 1: Custom prompt (from settings)
        if (!string.IsNullOrWhiteSpace(_customPrompt))
        {
            progress?.Report($"  [Labels] Using custom prompt ({_customPrompt.Length} chars)");
            return _customPrompt
                .Replace("{{LABELS}}", labelsList)
                .Replace("{{ITEM_NAME}}", itemName)
                .Replace("{{PRODUCT_NAME}}", productName)
                .Replace("{{ITEMS}}", itemName); // Support batch placeholder too
        }

        // Priority 2: Template from file
        if (!string.IsNullOrEmpty(_promptTemplatePath) && File.Exists(_promptTemplatePath))
        {
            try
            {
                _promptTemplate = File.ReadAllText(_promptTemplatePath);
            }
            catch
            {
                _promptTemplate = null;
            }
        }

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            return _promptTemplate
                .Replace("{{LABELS}}", labelsList)
                .Replace("{{ITEM_NAME}}", itemName)
                .Replace("{{PRODUCT_NAME}}", productName);
        }

        // Priority 3: Default prompt (fallback)
        return $@"Назначь подходящие метки для товара.

ДОСТУПНЫЕ МЕТКИ:
{labelsList}

ТОВАР: {itemName}
ПРОДУКТ: {productName}

ФОРМАТ ОТВЕТА (строго JSON массив):
[""Метка1"", ""Метка2""]

Назначь метки:";
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
