using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Services.Llm;

namespace SmartBasket.Services.Parsing;

/// <summary>
/// Универсальный LLM-парсер чеков.
/// Использует AI для парсинга любых чеков, когда regex-парсеры не подходят.
/// </summary>
public class LlmUniversalParser : IReceiptTextParser
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly IResponseParser _responseParser;
    private readonly ILogger<LlmUniversalParser> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;

    /// <summary>
    /// Ключ AI провайдера для парсинга (из конфигурации парсера)
    /// </summary>
    public string? AiProviderKey { get; set; }

    public LlmUniversalParser(
        IAiProviderFactory providerFactory,
        IResponseParser responseParser,
        ILogger<LlmUniversalParser> logger)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Уникальный идентификатор парсера для конфигурации
    /// </summary>
    public string Name => "LlmUniversalParser";

    /// <summary>
    /// Универсальный парсер поддерживает любые магазины (для будущего авто-определения)
    /// </summary>
    public IReadOnlyList<string> SupportedShops { get; } = new[] { "*" };

    /// <summary>
    /// LLM парсер может парсить любой текст
    /// </summary>
    public bool CanParse(string receiptText)
    {
        // LLM парсер - универсальный fallback, всегда может попробовать
        return !string.IsNullOrWhiteSpace(receiptText);
    }

    /// <summary>
    /// Установить путь к файлу шаблона prompt
    /// </summary>
    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null; // Reset cache
    }

    public ParsedReceipt Parse(string receiptText, DateTime emailDate, string? subject = null)
    {
        // Синхронная обёртка для async метода
        // LLM парсер извлекает магазин из текста, поэтому subject пока игнорируется
        return ParseAsync(receiptText, emailDate, null, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Асинхронный парсинг с прогрессом
    /// </summary>
    public async Task<ParsedReceipt> ParseAsync(
        string receiptText,
        DateTime emailDate,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = new ParsedReceipt();

        try
        {
            _logger.LogDebug("LlmUniversalParser.ParseAsync started");
            progress?.Report("  [LLM Parser] Starting...");

            // Очистить HTML теги
            var cleanedBody = CleanHtmlTags(receiptText);
            var originalLength = receiptText.Length;
            var cleanedLength = cleanedBody.Length;

            progress?.Report($"  [LLM Parser] Body: {originalLength} -> {cleanedLength} chars after HTML cleanup");

            // Получить AI провайдер
            ILlmProvider? provider = null;

            // Сначала пробуем по ключу из конфигурации парсера
            if (!string.IsNullOrEmpty(AiProviderKey))
            {
                provider = _providerFactory.GetProvider(AiProviderKey);
                _logger.LogDebug("Using configured provider: {ProviderKey}", AiProviderKey);
            }

            // Fallback на провайдер для Classification
            if (provider == null)
            {
                provider = _providerFactory.GetProviderForOperation(AiOperation.Classification);
                _logger.LogDebug("Using Classification operation provider");
            }

            if (provider == null)
            {
                var errorMsg = "No AI provider configured for LLM parsing. Check AiProviders and AiOperations.Classification in settings.";
                _logger.LogError(errorMsg);
                progress?.Report($"  [LLM Parser] ERROR: {errorMsg}");
                result.IsSuccess = false;
                result.Message = errorMsg;
                return result;
            }

            _logger.LogDebug("Got provider: {ProviderName}", provider.Name);
            progress?.Report($"  [LLM Parser] Using provider: {provider.Name}");

            // Обрезать слишком длинные тексты
            if (cleanedBody.Length > 8000)
            {
                cleanedBody = cleanedBody.Substring(0, 8000) + "\n... (truncated)";
                progress?.Report($"  [LLM Parser] Truncated to 8000 chars");
            }

            var prompt = BuildParsingPrompt(cleanedBody, emailDate, progress);

            progress?.Report($"  [LLM Parser] Prompt ready: {prompt.Length} chars");
            _logger.LogDebug("Prompt length: {Length}", prompt.Length);

            // Генерируем ответ через LLM провайдер
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [LLM Parser] Sending request via {provider.Name}...");

            var llmResult = await provider.GenerateAsync(
                prompt,
                progress: progress,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            progress?.Report($"  [LLM Parser] Response received in {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!llmResult.IsSuccess || string.IsNullOrEmpty(llmResult.Response))
            {
                result.IsSuccess = false;
                result.Message = llmResult.ErrorMessage ?? "Empty response from LLM";
                return result;
            }

            result.RawResponse = llmResult.Response;
            progress?.Report($"  [LLM Parser] Total response: {llmResult.Response.Length} chars");

            // Извлекаем JSON из ответа используя унифицированный ResponseParser
            progress?.Report("  [LLM Parser] Extracting JSON from response...");
            var parseResult = _responseParser.ParseJsonObject<ParsedReceiptDto>(llmResult.Response, progress);

            if (parseResult.IsSuccess && parseResult.Data != null)
            {
                var parsedJson = parseResult.Data;
                progress?.Report($"  [LLM Parser] Found JSON (method: {parseResult.ExtractionMethod})");

                result.Shop = parsedJson.Shop ?? "Unknown";
                result.OrderNumber = parsedJson.OrderNumber;
                result.Total = parsedJson.Total;

                // Try parsing date/datetime
                if (!string.IsNullOrEmpty(parsedJson.OrderDatetime) &&
                    DateTime.TryParse(parsedJson.OrderDatetime.Replace(":", " "), out var datetime))
                {
                    result.Date = datetime;
                }
                else if (!string.IsNullOrEmpty(parsedJson.Date) &&
                         DateTime.TryParse(parsedJson.Date, out var date))
                {
                    result.Date = date;
                }

                if (parsedJson.Items != null)
                {
                    foreach (var item in parsedJson.Items)
                    {
                        result.Items.Add(new ParsedReceiptItem
                        {
                            Name = item.Name ?? "Unknown",
                            Quantity = item.Quantity ?? 1,
                            QuantityUnit = item.QuantityUnit,
                            Price = item.Price,
                            Amount = item.Amount,
                            Unit = item.Unit,
                            UnitQuantity = item.UnitQuantity
                        });
                    }
                }

                result.IsSuccess = result.Items.Count > 0;
                result.Message = result.IsSuccess
                    ? $"Parsed {result.Items.Count} items from {result.Shop}"
                    : "No items found in receipt";

                progress?.Report($"  [LLM Parser] {result.Message}");
            }
            else
            {
                progress?.Report($"  [LLM Parser] JSON parse error: {parseResult.ErrorMessage}");
                result.IsSuccess = false;
                result.Message = parseResult.ErrorMessage ?? "No JSON found in LLM response";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress?.Report("  [LLM Parser] Cancelled by user");
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"  [LLM Parser] Error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "LLM receipt parsing error");
        }

        return result;
    }

    private static string CleanHtmlTags(string html)
    {
        return HtmlHelper.CleanHtml(html);
    }

    private string BuildParsingPrompt(string emailBody, DateTime emailDate, IProgress<string>? progress = null)
    {
        var year = emailDate.Year;

        // Try to load template from file
        if (!string.IsNullOrEmpty(_promptTemplatePath) && File.Exists(_promptTemplatePath))
        {
            try
            {
                _promptTemplate = File.ReadAllText(_promptTemplatePath);
                progress?.Report($"  [LLM Parser] Loaded prompt template from: {_promptTemplatePath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [LLM Parser] Failed to load template: {ex.Message}, using default");
                _promptTemplate = null;
            }
        }

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            return _promptTemplate
                .Replace("{{YEAR}}", year.ToString())
                .Replace("{{RECEIPT_TEXT}}", emailBody);
        }

        // Default prompt (fallback)
        return $@"Извлеки данные чека в JSON. Текст чека:
{emailBody}

Правила:
- shop: название магазина (АШАН, Пятёрочка и т.п.)
- date: дата в формате YYYY-MM-DD (год {year} если не указан)
- order_number: номер заказа (например H03255764114)
- total: итоговая сумма (число)
- items: массив товаров

Для каждого товара:
- name: название (без веса/объема в названии)
- quantity: количество (число)
- price: цена (число, итоговая цена за позицию)

ИГНОРИРУЙ строки: ""Собрано"", ""Оплата"", ""Доставка"", ""Сервисный сбор"", телефоны, адреса.

JSON:
{{""shop"":"""",""date"":"""",""order_number"":"""",""total"":0,""items"":[{{""name"":"""",""quantity"":1,""price"":0}}]}}";
    }

    // DTOs for JSON parsing
    private class ParsedReceiptDto
    {
        public string? Shop { get; set; }
        public string? Date { get; set; }

        [JsonPropertyName("order_datetime")]
        public string? OrderDatetime { get; set; }

        [JsonPropertyName("order_number")]
        public string? OrderNumber { get; set; }

        public decimal? Total { get; set; }
        public List<ParsedItemDto>? Items { get; set; }
    }

    private class ParsedItemDto
    {
        public string? Name { get; set; }
        public decimal? Quantity { get; set; }

        [JsonPropertyName("quantity_unit")]
        public string? QuantityUnit { get; set; }

        public decimal? Price { get; set; }
        public decimal? Amount { get; set; }

        public string? Unit { get; set; }

        [JsonPropertyName("unit_quantity")]
        public decimal? UnitQuantity { get; set; }
    }
}
