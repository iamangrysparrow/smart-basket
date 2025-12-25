using System.Text;
using System.Text.Json;
using AiWebSniffer.Core.Models;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Shopping;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Shopping;

/// <summary>
/// Реализация сервиса AI-выбора товаров.
/// Делает ОДИН прямой вызов LLM с tool calling (без итеративного loop).
/// </summary>
public class ProductSelectorService : IProductSelectorService
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly ILogger<ProductSelectorService> _logger;

    public ProductSelectorService(
        IAiProviderFactory providerFactory,
        ILogger<ProductSelectorService> logger)
    {
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async Task<ProductSelection?> SelectBestProductAsync(
        DraftItem draftItem,
        List<ProductSearchResult> searchResults,
        string storeId,
        string storeName,
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ProductSelectorService] Selecting product for '{DraftItem}' from {Count} results in {Store}",
            draftItem.Name, searchResults.Count, storeName);

        // Если нет результатов — сразу возвращаем null
        if (searchResults.Count == 0)
        {
            _logger.LogWarning("[ProductSelectorService] No search results for '{DraftItem}'", draftItem.Name);
            return null;
        }

        try
        {
            // Получаем провайдер для Shopping операции
            var provider = _providerFactory.GetProviderForOperation(AiOperation.Shopping);
            if (provider == null)
            {
                _logger.LogError("[ProductSelectorService] No provider configured for Shopping operation");
                return null;
            }

            // Загружаем шаблон промпта
            var template = LoadPromptTemplate("prompt_shopping_select_product.txt");

            // Форматируем результаты поиска
            var resultsText = FormatSearchResults(searchResults);

            // Подставляем плейсхолдеры
            var prompt = template
                .Replace("{{DRAFT_ITEM_ID}}", draftItem.Id.ToString())
                .Replace("{{DRAFT_ITEM_NAME}}", draftItem.Name)
                .Replace("{{DRAFT_ITEM_QUANTITY}}", draftItem.Quantity.ToString("0.##"))
                .Replace("{{DRAFT_ITEM_UNIT}}", draftItem.Unit)
                .Replace("{{DRAFT_ITEM_CATEGORY}}", draftItem.Category ?? "Не указана")
                .Replace("{{STORE_NAME}}", storeName)
                .Replace("{{SEARCH_RESULTS}}", resultsText);

            _logger.LogInformation("[ProductSelectorService] Prompt length: {Length} chars", prompt.Length);
            _logger.LogDebug("[ProductSelectorService] Full prompt:\n{Prompt}", prompt);

            // Создаём tool definition для select_product
            var selectProductTool = CreateSelectProductToolDefinition();

            // Сообщения для LLM
            var messages = new List<LlmChatMessage>
            {
                new()
                {
                    Role = "user",
                    Content = prompt
                }
            };

            // Один прямой вызов LLM
            _logger.LogInformation("[ProductSelectorService] Calling LLM ({Provider})...", provider.Name);

            var result = await provider.ChatAsync(
                messages,
                new[] { selectProductTool },
                progress: null, // Не используем streaming для этой операции
                cancellationToken: ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("[ProductSelectorService] LLM call failed: {Error}", result.ErrorMessage);
                return null;
            }

            // Проверяем что LLM вызвал tool
            if (!result.HasToolCalls)
            {
                _logger.LogWarning("[ProductSelectorService] LLM did not call select_product tool. Response: {Response}",
                    result.Response);
                return null;
            }

            // Ищем вызов select_product
            var selectCall = result.ToolCalls!.FirstOrDefault(t => t.Name == "select_product");
            if (selectCall == null)
            {
                _logger.LogWarning("[ProductSelectorService] LLM called wrong tool(s): {Tools}",
                    string.Join(", ", result.ToolCalls.Select(t => t.Name)));
                return null;
            }

            // Парсим результат
            var selection = ParseToolCallArguments(selectCall.Arguments, draftItem.Id.ToString());

            if (selection == null)
            {
                _logger.LogWarning("[ProductSelectorService] Failed to parse tool call arguments");
                return null;
            }

            _logger.LogInformation(
                "[ProductSelectorService] Selected: ProductId={ProductId}, Qty={Qty}, Alternatives={AltCount}",
                selection.SelectedProductId ?? "null",
                selection.Quantity,
                selection.Alternatives?.Count ?? 0);
            _logger.LogInformation("[ProductSelectorService] Reasoning: {Reason}", selection.Reasoning);

            // Уведомляем UI о результате
            progress?.Report(new ChatProgress(
                ChatProgressType.ToolResult,
                ToolName: "select_product",
                ToolResult: $"Выбран: {selection.SelectedProductId ?? "ничего"}, кол-во: {selection.Quantity}",
                ToolSuccess: true));

            return selection;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[ProductSelectorService] Selection cancelled for '{DraftItem}'", draftItem.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProductSelectorService] Error selecting product for '{DraftItem}'", draftItem.Name);
            return null;
        }
    }

    private ProductSelection? ParseToolCallArguments(string argumentsJson, string expectedDraftItemId)
    {
        _logger.LogDebug("[ProductSelectorService] Parsing arguments: {Args}", argumentsJson);

        try
        {
            var args = JsonSerializer.Deserialize<SelectProductArgs>(argumentsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (args == null)
            {
                _logger.LogError("[ProductSelectorService] Failed to deserialize arguments");
                return null;
            }

            // Используем expected ID если AI вернул что-то другое
            var draftItemId = args.DraftItemId;
            if (string.IsNullOrWhiteSpace(draftItemId) || draftItemId != expectedDraftItemId)
            {
                _logger.LogWarning(
                    "[ProductSelectorService] AI returned wrong draft_item_id: '{Actual}', expected: '{Expected}'. Using expected.",
                    draftItemId, expectedDraftItemId);
                draftItemId = expectedDraftItemId;
            }

            // Парсим альтернативы
            List<ProductAlternative>? alternatives = null;
            if (args.Alternatives != null && args.Alternatives.Count > 0)
            {
                alternatives = args.Alternatives
                    .Where(a => !string.IsNullOrEmpty(a.ProductId))
                    .Select(a => new ProductAlternative(a.ProductId!, a.Quantity ?? 1, a.Reasoning ?? ""))
                    .ToList();

                _logger.LogInformation("[ProductSelectorService] Parsed {Count} alternatives", alternatives.Count);
            }

            return new ProductSelection(
                draftItemId,
                args.SelectedProductId,
                args.Quantity ?? 1,
                args.Reasoning ?? "Не указано",
                alternatives);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[ProductSelectorService] JSON parsing error: {Args}", argumentsJson);
            return null;
        }
    }

    private ToolDefinition CreateSelectProductToolDefinition()
    {
        return new ToolDefinition(
            "select_product",
            "Выбрать лучший товар из результатов поиска в магазине",
            new
            {
                type = "object",
                properties = new
                {
                    draft_item_id = new
                    {
                        type = "string",
                        description = "ID товара из списка покупок"
                    },
                    selected_product_id = new
                    {
                        type = "string",
                        nullable = true,
                        description = "ID выбранного товара из результатов поиска. null если ничего не подходит"
                    },
                    quantity = new
                    {
                        type = "integer",
                        description = "Количество единиц товара для покупки (с учётом фасовки)"
                    },
                    reasoning = new
                    {
                        type = "string",
                        description = "Краткое обоснование выбора (1-2 предложения)"
                    },
                    alternatives = new
                    {
                        type = "array",
                        description = "Альтернативные товары (2-3 шт) — на случай если основной не подойдёт",
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
                                    description = "Количество единиц"
                                },
                                reasoning = new
                                {
                                    type = "string",
                                    description = "Почему это хорошая альтернатива"
                                }
                            },
                            required = new[] { "product_id", "quantity", "reasoning" }
                        }
                    }
                },
                required = new[] { "draft_item_id", "quantity", "reasoning" }
            });
    }

    private string FormatSearchResults(List<ProductSearchResult> results)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] ID: {r.Id}");
            sb.AppendLine($"    Название: {r.Name}");
            sb.AppendLine($"    Цена: {r.Price:N2}₽");

            if (r.Quantity > 0 && !string.IsNullOrEmpty(r.Unit))
            {
                sb.AppendLine($"    Фасовка: {r.Quantity:0.##} {r.Unit}");
            }

            sb.AppendLine($"    В наличии: {(r.InStock ? "Да" : "Нет")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string LoadPromptTemplate(string fileName)
    {
        // Ищем в папке приложения
        var path = Path.Combine(AppContext.BaseDirectory, fileName);

        if (!File.Exists(path))
        {
            // Fallback: ищем рядом с exe
            path = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                fileName);
        }

        if (!File.Exists(path))
        {
            _logger.LogError("[ProductSelectorService] Prompt template not found: {FileName}", fileName);
            throw new FileNotFoundException($"Prompt template not found: {fileName}", fileName);
        }

        return File.ReadAllText(path);
    }
}

/// <summary>
/// Аргументы инструмента select_product (для десериализации)
/// </summary>
internal class SelectProductArgs
{
    [System.Text.Json.Serialization.JsonPropertyName("draft_item_id")]
    public string? DraftItemId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("selected_product_id")]
    public string? SelectedProductId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("alternatives")]
    public List<AlternativeArg>? Alternatives { get; set; }
}

/// <summary>
/// Аргументы альтернативного товара
/// </summary>
internal class AlternativeArg
{
    [System.Text.Json.Serialization.JsonPropertyName("product_id")]
    public string? ProductId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
}
