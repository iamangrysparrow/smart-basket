using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiWebSniffer.Core.Models;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Shopping;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Shopping.Operations;

/// <summary>
/// Реализация операции выбора товара из результатов поиска.
/// Поддерживает два режима:
/// 1. YandexAgent с prompt.variables — эффективнее по токенам
/// 2. Tool calling для других провайдеров (Ollama, etc.)
/// </summary>
public class ProductMatcherOperation : IProductMatcherOperation
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly ILogger<ProductMatcherOperation> _logger;

    private static string? _promptTemplate;
    private const string PromptFileName = "prompt_shopping_select_product.txt";

    public ProductMatcherOperation(
        IAiProviderFactory providerFactory,
        ILogger<ProductMatcherOperation> logger)
    {
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async Task<ProductSelectionResult> SelectProductAsync(
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ProductMatcherOperation] Selecting for '{DraftItem}' from {Count} candidates",
            draftItem.Name, candidates.Count);

        // Если нет кандидатов — сразу возвращаем неуспех
        if (candidates.Count == 0)
        {
            _logger.LogWarning("[ProductMatcherOperation] No candidates for '{DraftItem}'", draftItem.Name);
            return new ProductSelectionResult(
                null,
                "Товары не найдены в магазине",
                new List<ProductSearchResult>(),
                false);
        }

        try
        {
            // Получаем провайдер для ProductMatcher
            var provider = _providerFactory.GetProviderForOperation(AiOperation.ProductMatcher);

            // Fallback на Shopping провайдер если ProductMatcher не настроен
            provider ??= _providerFactory.GetProviderForOperation(AiOperation.Shopping);

            if (provider == null)
            {
                _logger.LogError("[ProductMatcherOperation] No provider configured");
                return new ProductSelectionResult(
                    null,
                    "AI провайдер не настроен",
                    new List<ProductSearchResult>(),
                    false);
            }

            // Выбираем режим работы в зависимости от провайдера
            if (provider is YandexAgentLlmProvider yandexAgent)
            {
                return await SelectWithYandexAgentAsync(yandexAgent, draftItem, candidates, purchaseHistory, ct);
            }
            else
            {
                return await SelectWithToolCallingAsync(provider, draftItem, candidates, purchaseHistory, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[ProductMatcherOperation] Cancelled for '{DraftItem}'", draftItem.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProductMatcherOperation] Error for '{DraftItem}'", draftItem.Name);
            return new ProductSelectionResult(
                null,
                $"Ошибка: {ex.Message}",
                new List<ProductSearchResult>(),
                false);
        }
    }

    /// <summary>
    /// Выбор товара через YandexAgent с prompt.variables (эффективнее по токенам)
    /// </summary>
    private async Task<ProductSelectionResult> SelectWithYandexAgentAsync(
        YandexAgentLlmProvider yandexAgent,
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory,
        CancellationToken ct)
    {
        _logger.LogInformation("[ProductMatcherOperation] Using YandexAgent with variables");

        // Формируем переменные для промпта
        var variables = BuildVariables(draftItem, candidates, purchaseHistory);

        // Вызов через variables
        var result = await yandexAgent.GenerateWithVariablesAsync(
            variables,
            input: "Выполни инструкцию",
            progress: null,
            cancellationToken: ct);

        if (!result.IsSuccess)
        {
            _logger.LogError("[ProductMatcherOperation] YandexAgent call failed: {Error}", result.ErrorMessage);
            return new ProductSelectionResult(
                null,
                $"Ошибка AI: {result.ErrorMessage}",
                new List<ProductSearchResult>(),
                false);
        }

        // Парсим JSON из output_text
        return ParseJsonResponse(result.Response, candidates, draftItem);
    }

    /// <summary>
    /// Выбор товара через Tool Calling (для Ollama и др.)
    /// </summary>
    private async Task<ProductSelectionResult> SelectWithToolCallingAsync(
        ILlmProvider provider,
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory,
        CancellationToken ct)
    {
        _logger.LogInformation("[ProductMatcherOperation] Using Tool Calling ({Provider})", provider.Name);

        // Формируем полный промпт
        var prompt = BuildPrompt(draftItem, candidates, purchaseHistory);

        _logger.LogDebug("[ProductMatcherOperation] Prompt ({Length} chars):\n{Prompt}",
            prompt.Length, prompt);

        // Tool definition
        var tool = CreateSelectProductToolDefinition();

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var result = await provider.ChatAsync(
            messages,
            new[] { tool },
            progress: null,
            cancellationToken: ct);

        if (!result.IsSuccess)
        {
            _logger.LogError("[ProductMatcherOperation] LLM call failed: {Error}", result.ErrorMessage);
            return new ProductSelectionResult(
                null,
                $"Ошибка AI: {result.ErrorMessage}",
                new List<ProductSearchResult>(),
                false);
        }

        // Проверяем tool call
        if (!result.HasToolCalls)
        {
            _logger.LogWarning("[ProductMatcherOperation] LLM did not call tool. Response: {Response}",
                result.Response);

            // Попробуем распарсить как JSON (модель могла вернуть JSON без tool call)
            if (!string.IsNullOrEmpty(result.Response))
            {
                var jsonResult = ParseJsonResponse(result.Response, candidates, draftItem);
                if (jsonResult.Success)
                    return jsonResult;
            }

            // Fallback: выбираем первый доступный товар
            var firstInStock = candidates.FirstOrDefault(c => c.InStock);
            if (firstInStock != null)
            {
                return new ProductSelectionResult(
                    firstInStock,
                    "Выбран первый доступный товар (AI не дал выбор)",
                    candidates.Where(c => c.InStock && c != firstInStock).Take(3).ToList(),
                    true,
                    1);
            }

            return new ProductSelectionResult(
                null,
                "AI не смог выбрать товар",
                new List<ProductSearchResult>(),
                false);
        }

        // Парсим результат tool call
        var selectCall = result.ToolCalls!.FirstOrDefault(t => t.Name == "select_product");
        if (selectCall == null)
        {
            _logger.LogWarning("[ProductMatcherOperation] Wrong tool called");
            return new ProductSelectionResult(
                null,
                "AI вызвал неправильный инструмент",
                new List<ProductSearchResult>(),
                false);
        }

        return ParseResult(selectCall.Arguments, candidates, draftItem);
    }

    /// <summary>
    /// Формирует переменные для YandexAgent
    /// </summary>
    private Dictionary<string, string> BuildVariables(
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory)
    {
        // Табличный формат для экономии токенов
        var searchResultsSb = new StringBuilder();
        searchResultsSb.AppendLine("Id | Name | Price | Unit | Qty | InStock");
        searchResultsSb.AppendLine("---|------|-------|------|-----|--------");
        foreach (var c in candidates)
        {
            var inStock = c.InStock ? "+" : "-";
            searchResultsSb.AppendLine($"{c.Id} | {c.Name} | {c.Price:0.##} | {c.Unit} | {c.Quantity:0.##} | {inStock}");
        }

        // История покупок (если есть)
        var historyText = "";
        if (purchaseHistory != null && purchaseHistory.Count > 0)
        {
            historyText = string.Join(", ", purchaseHistory.Take(5));
        }

        // Категория
        var category = draftItem.Category ?? "";

        return new Dictionary<string, string>
        {
            { "DRAFT_ITEM_NAME", draftItem.Name },
            { "DRAFT_ITEM_QUANTITY", draftItem.Quantity.ToString("0.##") },
            { "DRAFT_ITEM_UNIT", draftItem.Unit ?? "шт" },
            { "DRAFT_ITEM_CATEGORY", category },
            { "PURCHASE_HISTORY", historyText },
            { "SEARCH_RESULTS", searchResultsSb.ToString().TrimEnd() }
        };
    }

    /// <summary>
    /// Формирует полный промпт для Tool Calling режима
    /// </summary>
    private string BuildPrompt(
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory)
    {
        // Загружаем шаблон из файла
        var template = LoadPromptTemplate();

        // Формируем результаты поиска в JSON формате
        var searchResultsSb = new StringBuilder();
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            searchResultsSb.AppendLine($"[{i + 1}] {{");
            searchResultsSb.AppendLine($"  \"Id\": \"{c.Id}\",");
            searchResultsSb.AppendLine($"  \"Name\": \"{c.Name}\",");
            searchResultsSb.AppendLine($"  \"Price\": {c.Price:0.##},");
            searchResultsSb.AppendLine($"  \"Unit\": \"{c.Unit}\",");
            searchResultsSb.AppendLine($"  \"Quantity\": {c.Quantity:0.##},");
            searchResultsSb.AppendLine($"  \"InStock\": {(c.InStock ? "true" : "false")}");
            searchResultsSb.AppendLine("}");
            searchResultsSb.AppendLine();
        }

        // История покупок
        var historySection = "";
        if (purchaseHistory != null && purchaseHistory.Count > 0)
        {
            var historySb = new StringBuilder();
            historySb.AppendLine("## История покупок этого товара:");
            foreach (var item in purchaseHistory.Take(5))
            {
                historySb.AppendLine($"- {item}");
            }
            historySection = historySb.ToString();
        }

        // Категория
        var categorySection = !string.IsNullOrEmpty(draftItem.Category)
            ? $"Категория: {draftItem.Category}"
            : "";

        // Подставляем плейсхолдеры
        var prompt = template
            .Replace("{{DRAFT_ITEM_NAME}}", draftItem.Name)
            .Replace("{{DRAFT_ITEM_QUANTITY}}", draftItem.Quantity.ToString("0.##"))
            .Replace("{{DRAFT_ITEM_UNIT}}", draftItem.Unit ?? "шт")
            .Replace("{{DRAFT_ITEM_CATEGORY}}", categorySection)
            .Replace("{{PURCHASE_HISTORY}}", historySection)
            .Replace("{{SEARCH_RESULTS}}", searchResultsSb.ToString().TrimEnd());

        return prompt;
    }

    private static string LoadPromptTemplate()
    {
        if (_promptTemplate != null)
            return _promptTemplate;

        // Ищем файл рядом с exe
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var promptPath = Path.Combine(exeDir, PromptFileName);

        if (File.Exists(promptPath))
        {
            _promptTemplate = File.ReadAllText(promptPath);
            return _promptTemplate;
        }

        // Fallback: встроенный промпт
        _promptTemplate = GetFallbackPrompt();
        return _promptTemplate;
    }

    private static string GetFallbackPrompt()
    {
        return """
            Выбери лучший товар из результатов поиска.

            ## Что нужно купить
            Название: {{DRAFT_ITEM_NAME}}
            Количество: {{DRAFT_ITEM_QUANTITY}} {{DRAFT_ITEM_UNIT}}
            {{DRAFT_ITEM_CATEGORY}}

            ## Результаты поиска
            {{SEARCH_RESULTS}}

            ## Инструкции
            1. Выбери товар, соответствующий запросу (фильтруй нерелевантные)
            2. Рассчитай количество упаковок: ceil(требуемое / фасовка)
            3. Если ничего не подходит — selected_product_id = null

            Вызови инструмент select_product.
            """;
    }

    /// <summary>
    /// Парсит JSON ответ от модели (для YandexAgent или fallback)
    /// </summary>
    private ProductSelectionResult ParseJsonResponse(string? response, List<ProductSearchResult> candidates, DraftItem draftItem)
    {
        if (string.IsNullOrEmpty(response))
        {
            return new ProductSelectionResult(
                null,
                "Пустой ответ от AI",
                new List<ProductSearchResult>(),
                false);
        }

        _logger.LogDebug("[ProductMatcherOperation] Parsing JSON response: {Response}", response);

        // Ищем JSON в ответе (может быть обёрнут в markdown ```json...```)
        var jsonText = ExtractJson(response);
        if (string.IsNullOrEmpty(jsonText))
        {
            _logger.LogWarning("[ProductMatcherOperation] No JSON found in response");
            return new ProductSelectionResult(
                null,
                "AI не вернул JSON",
                new List<ProductSearchResult>(),
                false);
        }

        return ParseResult(jsonText, candidates, draftItem);
    }

    /// <summary>
    /// Извлекает JSON из текста (убирает markdown обёртку если есть)
    /// </summary>
    private static string? ExtractJson(string text)
    {
        var trimmed = text.Trim();

        // Если начинается с {, это уже JSON
        if (trimmed.StartsWith("{"))
        {
            // Находим конец JSON
            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return trimmed[..(i + 1)];
                    }
                }
            }

            return trimmed; // Возвращаем как есть если не нашли закрывающую скобку
        }

        // Ищем ```json ... ``` или ``` ... ```
        var codeBlockStart = trimmed.IndexOf("```");
        if (codeBlockStart >= 0)
        {
            var afterStart = codeBlockStart + 3;
            // Пропускаем "json" если есть
            if (trimmed.Length > afterStart + 4 && trimmed.Substring(afterStart, 4).Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                afterStart += 4;
            }

            var codeBlockEnd = trimmed.IndexOf("```", afterStart);
            if (codeBlockEnd > afterStart)
            {
                return trimmed.Substring(afterStart, codeBlockEnd - afterStart).Trim();
            }
        }

        return null;
    }

    private ProductSelectionResult ParseResult(string? argumentsJson, List<ProductSearchResult> candidates, DraftItem draftItem)
    {
        if (string.IsNullOrEmpty(argumentsJson))
        {
            return new ProductSelectionResult(
                null,
                "Пустой ответ от AI",
                new List<ProductSearchResult>(),
                false);
        }

        _logger.LogDebug("[ProductMatcherOperation] Parsing: {Args}", argumentsJson);

        try
        {
            var args = JsonSerializer.Deserialize<SelectProductArgs>(argumentsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (args == null)
            {
                return new ProductSelectionResult(
                    null,
                    "Не удалось распарсить ответ AI",
                    new List<ProductSearchResult>(),
                    false);
            }

            // Находим выбранный товар
            ProductSearchResult? selected = null;
            if (!string.IsNullOrEmpty(args.SelectedProductId))
            {
                selected = candidates.FirstOrDefault(c => c.Id == args.SelectedProductId);
                if (selected == null)
                {
                    _logger.LogWarning("[ProductMatcherOperation] Selected ID '{Id}' not found in candidates",
                        args.SelectedProductId);
                }
            }

            // Получаем количество от AI или рассчитываем
            int quantity = args.Quantity ?? 1;
            if (quantity < 1) quantity = 1;

            // Если AI не дал количество — рассчитываем сами
            if (args.Quantity == null && selected != null && selected.Quantity > 0)
            {
                quantity = (int)Math.Ceiling((double)draftItem.Quantity / (double)selected.Quantity);
                if (quantity < 1) quantity = 1;
            }

            // Находим альтернативы (поддерживаем оба формата: ["id1"] и [{"product_id": "id1"}])
            var alternatives = new List<ProductSearchResult>();
            var altIds = args.GetAlternativeIds();
            foreach (var altId in altIds.Take(3))
            {
                var altProduct = candidates.FirstOrDefault(c => c.Id == altId);
                if (altProduct != null && altProduct != selected)
                {
                    alternatives.Add(altProduct);
                }
            }

            var reason = args.Reasoning ?? "Не указано";

            _logger.LogInformation(
                "[ProductMatcherOperation] Result: Selected={Selected}, Qty={Quantity}, Alternatives={AltCount}, Reason={Reason}",
                selected?.Name ?? "null", quantity, alternatives.Count, reason);

            return new ProductSelectionResult(
                selected,
                reason,
                alternatives,
                selected != null,
                quantity);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[ProductMatcherOperation] JSON parse error: {Args}", argumentsJson);
            return new ProductSelectionResult(
                null,
                $"Ошибка парсинга: {ex.Message}",
                new List<ProductSearchResult>(),
                false);
        }
    }

    private static ToolDefinition CreateSelectProductToolDefinition()
    {
        return new ToolDefinition(
            "select_product",
            "Выбрать лучший товар из результатов поиска и рассчитать количество упаковок",
            new
            {
                type = "object",
                properties = new
                {
                    selected_product_id = new
                    {
                        type = "string",
                        nullable = true,
                        description = "ID выбранного товара из результатов поиска. null если ничего не подходит"
                    },
                    quantity = new
                    {
                        type = "integer",
                        description = "Количество УПАКОВОК для покупки. Рассчитай: ceil(требуемое_количество / фасовка_товара)"
                    },
                    reasoning = new
                    {
                        type = "string",
                        description = "Краткое обоснование выбора и расчёта количества"
                    },
                    alternatives = new
                    {
                        type = "array",
                        description = "Альтернативные товары (до 3 шт)",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                product_id = new
                                {
                                    type = "string",
                                    description = "ID альтернативного товара"
                                },
                                quantity = new
                                {
                                    type = "integer",
                                    description = "Количество упаковок для этой альтернативы"
                                },
                                reasoning = new
                                {
                                    type = "string",
                                    description = "Почему это хорошая альтернатива"
                                }
                            },
                            required = new[] { "product_id", "quantity" }
                        }
                    }
                },
                required = new[] { "quantity", "reasoning" }
            });
    }
}

/// <summary>
/// Аргументы инструмента select_product (для десериализации)
/// </summary>
internal class SelectProductArgs
{
    [JsonPropertyName("selected_product_id")]
    public string? SelectedProductId { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    /// <summary>
    /// Альтернативы — может быть массив строк (ID) или массив объектов
    /// </summary>
    [JsonPropertyName("alternatives")]
    public JsonElement? Alternatives { get; set; }

    /// <summary>
    /// Извлечь ID альтернатив из любого формата
    /// </summary>
    public List<string> GetAlternativeIds()
    {
        if (Alternatives == null || Alternatives.Value.ValueKind == JsonValueKind.Null)
            return new List<string>();

        var result = new List<string>();

        if (Alternatives.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in Alternatives.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    // Формат: ["id1", "id2"]
                    var id = item.GetString();
                    if (!string.IsNullOrEmpty(id))
                        result.Add(id);
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    // Формат: [{"product_id": "id1"}, {"product_id": "id2"}]
                    if (item.TryGetProperty("product_id", out var productIdProp) &&
                        productIdProp.ValueKind == JsonValueKind.String)
                    {
                        var id = productIdProp.GetString();
                        if (!string.IsNullOrEmpty(id))
                            result.Add(id);
                    }
                }
            }
        }

        return result;
    }
}
