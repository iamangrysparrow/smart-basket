using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Ollama;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Сервис парсинга чеков, использующий выбранный LLM провайдер через фабрику
/// </summary>
public class ReceiptParsingService : IReceiptParsingService
{
    private readonly ILlmProviderFactory _providerFactory;
    private readonly AppSettings _settings;
    private readonly ILogger<ReceiptParsingService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;

    public ReceiptParsingService(
        ILlmProviderFactory providerFactory,
        AppSettings settings,
        ILogger<ReceiptParsingService> logger)
    {
        _providerFactory = providerFactory;
        _settings = settings;
        _logger = logger;
    }

    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null; // Reset cache
    }

    public async Task<ParsedReceipt> ParseReceiptAsync(
        string emailBody,
        DateTime emailDate,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ParsedReceipt();

        try
        {
            // Получаем текущий провайдер
            var provider = _providerFactory.GetCurrentProvider();
            progress?.Report($"  [LLM] Using provider: {provider.Name}");

            progress?.Report("  [LLM] Cleaning email body...");

            // Очистить HTML теги
            var cleanedBody = CleanHtmlTags(emailBody);
            var originalLength = emailBody.Length;
            var cleanedLength = cleanedBody.Length;

            progress?.Report($"  [LLM] Body: {originalLength} -> {cleanedLength} chars after HTML cleanup");

            // Обрезать слишком длинные тексты
            if (cleanedBody.Length > 8000)
            {
                cleanedBody = cleanedBody.Substring(0, 8000) + "\n... (truncated)";
                progress?.Report($"  [LLM] Truncated to 8000 chars");
            }

            var prompt = BuildParsingPrompt(cleanedBody, emailDate, progress);

            progress?.Report($"  [LLM] Prompt ready: {prompt.Length} chars");
            progress?.Report($"  [LLM] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [LLM] === PROMPT END ===");

            // Генерируем ответ через LLM провайдер
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report($"  [LLM] Sending request via {provider.Name}...");

            var llmResult = await provider.GenerateAsync(
                prompt,
                maxTokens: 4000,
                temperature: _settings.Ollama.Temperature, // Используем температуру из настроек
                progress: progress,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            progress?.Report($"  [LLM] Response received in {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (!llmResult.IsSuccess || string.IsNullOrEmpty(llmResult.Response))
            {
                result.IsSuccess = false;
                result.Message = llmResult.ErrorMessage ?? "Empty response from LLM";
                return result;
            }

            result.RawResponse = llmResult.Response;
            progress?.Report($"  [LLM] Total response: {llmResult.Response.Length} chars");

            // Извлекаем JSON из ответа
            progress?.Report("  [LLM] Extracting JSON from response...");
            var jsonMatch = Regex.Match(llmResult.Response, @"\{[\s\S]*\}", RegexOptions.Multiline);

            if (jsonMatch.Success)
            {
                progress?.Report($"  [LLM] Found JSON: {jsonMatch.Value.Length} chars");
                try
                {
                    var parsedJson = JsonSerializer.Deserialize<ParsedReceiptDto>(
                        jsonMatch.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (parsedJson != null)
                    {
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
                                    Unit = item.Unit,
                                    Price = item.Price,
                                    Amount = item.Amount,
                                    UnitOfMeasure = item.UnitOfMeasure,
                                    UnitQuantity = item.UnitQuantity
                                });
                            }
                        }

                        result.IsSuccess = result.Items.Count > 0;
                        result.Message = result.IsSuccess
                            ? $"Parsed {result.Items.Count} items from {result.Shop}"
                            : "No items found in receipt";

                        progress?.Report($"  [LLM] {result.Message}");
                    }
                }
                catch (JsonException ex)
                {
                    progress?.Report($"  [LLM] JSON parse error: {ex.Message}");
                    result.IsSuccess = false;
                    result.Message = $"Failed to parse JSON: {ex.Message}";
                }
            }
            else
            {
                progress?.Report("  [LLM] No JSON found in response");
                result.IsSuccess = false;
                result.Message = "No JSON found in LLM response";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress?.Report("  [LLM] Cancelled by user");
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"  [LLM] Error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Receipt parsing error");
        }

        return result;
    }

    private string CleanHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Удалить style и script теги с содержимым
        var result = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);

        // Заменить <br>, <p>, <div>, <tr> на переносы строк
        result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</?(p|div|tr|li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</(td|th)>", " | ", RegexOptions.IgnoreCase);

        // Удалить все остальные HTML теги
        result = Regex.Replace(result, @"<[^>]+>", "");

        // Декодировать HTML entities
        result = System.Net.WebUtility.HtmlDecode(result);

        // Убрать множественные пробелы и переносы
        result = Regex.Replace(result, @"[ \t]+", " ");
        result = Regex.Replace(result, @"\n\s*\n", "\n\n");

        return result.Trim();
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
                progress?.Report($"  [LLM] Loaded prompt template from: {_promptTemplatePath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [LLM] Failed to load template: {ex.Message}, using default");
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
        public string? Unit { get; set; }
        public decimal? Price { get; set; }
        public decimal? Amount { get; set; }

        [JsonPropertyName("unit_of_measure")]
        public string? UnitOfMeasure { get; set; }

        [JsonPropertyName("unit_quantity")]
        public decimal? UnitQuantity { get; set; }
    }
}
