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
/// Поддерживает три режима:
/// 1. YandexAgent с prompt.variables — эффективнее по токенам
/// 2. Tool calling для провайдеров с поддержкой tools (Ollama, GigaChat с functions)
/// 3. JSON response для провайдеров без tool calling (GigaChat-Lite, YandexGPT)
/// </summary>
public class ProductMatcherOperation : IProductMatcherOperation
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly ILogger<ProductMatcherOperation> _logger;

    private const string SystemPromptFileName = "prompt_shopping_select_product_system.txt";
    private const string UserPromptFileName = "prompt_shopping_select_product_user.txt";
    private const string OperationName = "ProductMatcher";

    // Кэш для промптов (только для файловых, кастомные всегда читаются из настроек)
    private static string? _cachedSystemPromptFile;
    private static string? _cachedUserPromptFile;

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
        string? llmSessionId = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ProductMatcherOperation] Selecting for '{DraftItem}' from {Count} candidates (session: {Session})",
            draftItem.Name, candidates.Count, llmSessionId ?? "none");

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
            var providerKey = _providerFactory.GetProviderKeyForOperation(AiOperation.ProductMatcher);

            // Fallback на Shopping провайдер если ProductMatcher не настроен
            if (provider == null)
            {
                provider = _providerFactory.GetProviderForOperation(AiOperation.Shopping);
                providerKey = _providerFactory.GetProviderKeyForOperation(AiOperation.Shopping);
            }

            if (provider == null || string.IsNullOrEmpty(providerKey))
            {
                _logger.LogError("[ProductMatcherOperation] No provider configured");
                return new ProductSelectionResult(
                    null,
                    "AI провайдер не настроен",
                    new List<ProductSearchResult>(),
                    false);
            }

            _logger.LogInformation("[ProductMatcherOperation] Using provider: {Provider}, key: {Key}",
                provider.Name, providerKey);

            // Выбираем режим работы в зависимости от провайдера
            if (provider is YandexAgentLlmProvider yandexAgent)
            {
                return await SelectWithYandexAgentAsync(yandexAgent, draftItem, candidates, purchaseHistory, llmSessionId, ct);
            }
            else
            {
                return await SelectWithChatAsync(provider, providerKey, draftItem, candidates, purchaseHistory, llmSessionId, ct);
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
        string? llmSessionId,
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
    /// Выбор товара через ChatAsync (с tool calling или без)
    /// </summary>
    private async Task<ProductSelectionResult> SelectWithChatAsync(
        ILlmProvider provider,
        string providerKey,
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory,
        string? llmSessionId,
        CancellationToken ct)
    {
        // Формируем промпты (из настроек или файлов)
        var (systemPrompt, userPrompt) = LoadPrompts(providerKey, draftItem, candidates, purchaseHistory);

        _logger.LogInformation("[ProductMatcherOperation] Provider: {Provider}, SupportsTools: {SupportsTools}",
            provider.Name, provider.SupportsTools);
        _logger.LogDebug("[ProductMatcherOperation] System prompt ({SysLen} chars):\n{SystemPrompt}",
            systemPrompt.Length, systemPrompt);
        _logger.LogDebug("[ProductMatcherOperation] User prompt ({UserLen} chars):\n{UserPrompt}",
            userPrompt.Length, userPrompt);

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = userPrompt }
        };

        // Создаём sessionContext для кэширования токенов
        var sessionContext = !string.IsNullOrEmpty(llmSessionId)
            ? new LlmSessionContext { SessionId = llmSessionId, OperationType = "shopping" }
            : null;

        // Определяем режим: с tool calling или без
        // Для GigaChat-Lite и YandexGPT без tools используем JSON response mode
        var useToolCalling = provider.SupportsTools;

        LlmGenerationResult result;
        if (useToolCalling)
        {
            _logger.LogInformation("[ProductMatcherOperation] Using Tool Calling mode");
            var tool = CreateSelectProductToolDefinition();

            result = await provider.ChatAsync(
                messages,
                new[] { tool },
                sessionContext: sessionContext,
                progress: null,
                cancellationToken: ct);
        }
        else
        {
            _logger.LogInformation("[ProductMatcherOperation] Using JSON response mode (no tools)");

            result = await provider.ChatAsync(
                messages,
                tools: null,
                sessionContext: sessionContext,
                progress: null,
                cancellationToken: ct);
        }

        if (!result.IsSuccess)
        {
            _logger.LogError("[ProductMatcherOperation] LLM call failed: {Error}", result.ErrorMessage);
            return new ProductSelectionResult(
                null,
                $"Ошибка AI: {result.ErrorMessage}",
                new List<ProductSearchResult>(),
                false);
        }

        // Сначала пробуем распарсить как tool call
        if (result.HasToolCalls)
        {
            var selectCall = result.ToolCalls!.FirstOrDefault(t => t.Name == "select_product");
            if (selectCall != null)
            {
                _logger.LogInformation("[ProductMatcherOperation] Got tool call response");
                return ParseResult(selectCall.Arguments, candidates, draftItem);
            }
            _logger.LogWarning("[ProductMatcherOperation] Wrong tool called: {Tools}",
                string.Join(", ", result.ToolCalls.Select(t => t.Name)));
        }

        // Пробуем распарсить как JSON из текстового ответа
        if (!string.IsNullOrEmpty(result.Response))
        {
            _logger.LogInformation("[ProductMatcherOperation] Parsing JSON from text response");
            var jsonResult = ParseJsonResponse(result.Response, candidates, draftItem);
            if (jsonResult.Success)
                return jsonResult;
        }

        // Fallback: выбираем первый доступный товар
        _logger.LogWarning("[ProductMatcherOperation] AI did not return valid selection. Response: {Response}",
            result.Response);
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

    /// <summary>
    /// Загружает промпты с приоритетом:
    /// 1. Кастомные из настроек (AiOperations.Prompts)
    /// 2. Из файлов prompt_shopping_select_product_*.txt
    /// 3. Fallback hardcoded
    /// </summary>
    private (string SystemPrompt, string UserPrompt) LoadPrompts(
        string providerKey,
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory)
    {
        var aiOps = _providerFactory.AiOperations;

        // 1. Пробуем загрузить кастомные промпты из настроек
        var customSystemPrompt = aiOps.GetSystemPrompt(OperationName, providerKey);
        var customUserPrompt = aiOps.GetUserPrompt(OperationName, providerKey);

        string systemPrompt;
        string userPromptTemplate;

        if (!string.IsNullOrWhiteSpace(customSystemPrompt))
        {
            _logger.LogInformation("[ProductMatcherOperation] Using custom system prompt from settings");
            systemPrompt = customSystemPrompt;
        }
        else
        {
            // 2. Пробуем загрузить из файла
            systemPrompt = LoadSystemPromptFromFile();
        }

        if (!string.IsNullOrWhiteSpace(customUserPrompt))
        {
            _logger.LogInformation("[ProductMatcherOperation] Using custom user prompt from settings");
            userPromptTemplate = customUserPrompt;
        }
        else
        {
            // 2. Пробуем загрузить из файла
            userPromptTemplate = LoadUserPromptFromFile();
        }

        // Подставляем плейсхолдеры в user prompt
        var userPrompt = BuildUserPrompt(userPromptTemplate, draftItem, candidates, purchaseHistory);

        return (systemPrompt, userPrompt);
    }

    private string LoadSystemPromptFromFile()
    {
        if (_cachedSystemPromptFile != null)
            return _cachedSystemPromptFile;

        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var promptPath = Path.Combine(exeDir, SystemPromptFileName);

        if (File.Exists(promptPath))
        {
            _cachedSystemPromptFile = File.ReadAllText(promptPath);
            _logger.LogDebug("[ProductMatcherOperation] Loaded system prompt from file: {Path}", promptPath);
            return _cachedSystemPromptFile;
        }

        _logger.LogWarning("[ProductMatcherOperation] System prompt file not found: {Path}, using fallback", promptPath);
        _cachedSystemPromptFile = GetFallbackSystemPrompt();
        return _cachedSystemPromptFile;
    }

    private string LoadUserPromptFromFile()
    {
        if (_cachedUserPromptFile != null)
            return _cachedUserPromptFile;

        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var promptPath = Path.Combine(exeDir, UserPromptFileName);

        if (File.Exists(promptPath))
        {
            _cachedUserPromptFile = File.ReadAllText(promptPath);
            _logger.LogDebug("[ProductMatcherOperation] Loaded user prompt from file: {Path}", promptPath);
            return _cachedUserPromptFile;
        }

        _logger.LogWarning("[ProductMatcherOperation] User prompt file not found: {Path}, using fallback", promptPath);
        _cachedUserPromptFile = GetFallbackUserPrompt();
        return _cachedUserPromptFile;
    }

    private static string GetFallbackSystemPrompt()
    {
        return """
            Ты — эксперт по выбору товаров из результатов поиска интернет-магазинов.

            ## Твоя задача
            1. ФИЛЬТРУЙ нерелевантные товары (паста томатная != томаты, кефир != молоко)
            2. Выбери товар с лучшим соотношением цена/объём
            3. Рассчитай количество упаковок для требуемого объёма
            4. Предложи 2-3 альтернативы
            5. Если ничего не подходит — selected_product_id = null

            ## ОТВЕТ
            Верни ТОЛЬКО JSON (без markdown, без ```):
            {
              "selected_product_id": "id товара или null",
              "quantity": 4,
              "reasoning": "Причина выбора",
              "alternatives": [{"product_id": "id1", "quantity": 2, "reasoning": "причина"}]
            }
            """;
    }

    private static string GetFallbackUserPrompt()
    {
        return """
            ## Что ищет пользователь
            Название: {{DRAFT_ITEM_NAME}}
            Требуемое количество: {{DRAFT_ITEM_QUANTITY}} {{DRAFT_ITEM_UNIT}}
            {{DRAFT_ITEM_CATEGORY}}

            {{PURCHASE_HISTORY}}

            ## Результаты поиска в магазине
            {{SEARCH_RESULTS}}

            Выбери лучший товар из результатов поиска.
            """;
    }

    /// <summary>
    /// Подставляет плейсхолдеры в user prompt
    /// </summary>
    private string BuildUserPrompt(
        string template,
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory)
    {
        // Формируем результаты поиска
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

        // Категория — приоритет: CategoryPath (полный путь), затем Category (короткое имя)
        var categoryValue = !string.IsNullOrEmpty(draftItem.CategoryPath)
            ? draftItem.CategoryPath
            : draftItem.Category ?? "";

        // Подставляем плейсхолдеры
        var prompt = template
            .Replace("{{DRAFT_ITEM_NAME}}", draftItem.Name)
            .Replace("{{DRAFT_ITEM_QUANTITY}}", draftItem.Quantity.ToString("0.##"))
            .Replace("{{DRAFT_ITEM_UNIT}}", draftItem.Unit ?? "шт")
            .Replace("{{DRAFT_ITEM_CATEGORY}}", categoryValue)
            .Replace("{{PURCHASE_HISTORY}}", historySection)
            .Replace("{{SEARCH_RESULTS}}", searchResultsSb.ToString().TrimEnd());

        return prompt;
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

        // Категория — приоритет: CategoryPath (полный путь), затем Category (короткое имя)
        var category = !string.IsNullOrEmpty(draftItem.CategoryPath)
            ? draftItem.CategoryPath
            : draftItem.Category ?? "";

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

        // Пробуем найти JSON где угодно в тексте
        var jsonStart = trimmed.IndexOf('{');
        if (jsonStart >= 0)
        {
            return ExtractJson(trimmed[jsonStart..]);
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
