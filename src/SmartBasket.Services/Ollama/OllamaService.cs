using System.IO;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Ollama;

public class OllamaService : IOllamaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaService> _logger;
    private string? _promptTemplate;
    private string? _promptTemplatePath;

    public OllamaService(IHttpClientFactory httpClientFactory, ILogger<OllamaService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Установить путь к файлу шаблона prompt
    /// </summary>
    public void SetPromptTemplatePath(string path)
    {
        _promptTemplatePath = path;
        _promptTemplate = null; // Reset cache
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(
        OllamaSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync($"{settings.BaseUrl}/api/tags", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var tagsResponse = JsonSerializer.Deserialize<OllamaTagsResponse>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var models = tagsResponse?.Models?.Select(m => m.Name).ToList() ?? new List<string>();

                if (models.Count == 0)
                {
                    return (false, "Ollama connected but no models available. Please pull a model first.");
                }

                var modelExists = models.Any(m => m.StartsWith(settings.Model.Split(':')[0]));
                var modelInfo = modelExists
                    ? $"Model '{settings.Model}' is available."
                    : $"Model '{settings.Model}' not found. Available: {string.Join(", ", models)}";

                return (true, $"Connected to Ollama. {modelInfo}");
            }

            return (false, $"Ollama returned status {response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Cannot connect to Ollama at {settings.BaseUrl}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Error testing Ollama connection: {ex.Message}");
        }
    }

    public async Task<ParsedReceipt> ParseReceiptAsync(
        OllamaSettings settings,
        string emailBody,
        DateTime emailDate,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ParsedReceipt();

        try
        {
            progress?.Report("  [Ollama] Cleaning email body...");

            // Очистить HTML теги если они есть
            var cleanedBody = CleanHtmlTags(emailBody);
            var originalLength = emailBody.Length;
            var cleanedLength = cleanedBody.Length;

            progress?.Report($"  [Ollama] Body: {originalLength} -> {cleanedLength} chars after HTML cleanup");

            // Обрезать слишком длинные тексты
            if (cleanedBody.Length > 8000)
            {
                cleanedBody = cleanedBody.Substring(0, 8000) + "\n... (truncated)";
                progress?.Report($"  [Ollama] Truncated to 8000 chars");
            }

            var prompt = BuildParsingPrompt(cleanedBody, emailDate, progress);

            progress?.Report($"  [Ollama] Prompt ready: {prompt.Length} chars");
            progress?.Report($"  [Ollama] === PROMPT START ===");
            progress?.Report(prompt);
            progress?.Report($"  [Ollama] === PROMPT END ===");

            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(settings.TimeoutSeconds, 120); // Minimum 2 minutes
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Streaming request
            var request = new OllamaGenerateRequest
            {
                Model = settings.Model,
                Prompt = prompt,
                Stream = true, // Enable streaming
                Options = new OllamaOptions
                {
                    Temperature = settings.Temperature
                }
            };

            progress?.Report($"  [Ollama] Sending STREAMING request to {settings.BaseUrl}/api/generate (model: {settings.Model}, timeout: {timeoutSeconds}s)...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl}/api/generate")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                stopwatch.Stop();
                progress?.Report($"  [Ollama] TIMEOUT after {stopwatch.Elapsed.TotalSeconds:F1}s");
                result.IsSuccess = false;
                result.Message = $"Ollama timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                progress?.Report($"  [Ollama] Error response: {errorContent}");
                result.IsSuccess = false;
                result.Message = $"Ollama returned {response.StatusCode}: {errorContent}";
                return result;
            }

            // Read streaming response
            progress?.Report("  [Ollama] === STREAMING RESPONSE ===");
            var fullResponse = new StringBuilder();
            var lineBuffer = new StringBuilder(); // Buffer for accumulating text until newline

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk?.Response != null)
                    {
                        fullResponse.Append(chunk.Response);

                        // Buffer text and output line by line
                        foreach (var ch in chunk.Response)
                        {
                            if (ch == '\n')
                            {
                                // Output accumulated line
                                progress?.Report($"  {lineBuffer}");
                                lineBuffer.Clear();
                            }
                            else
                            {
                                lineBuffer.Append(ch);
                            }
                        }
                    }

                    if (chunk?.Done == true)
                    {
                        // Output any remaining text in buffer
                        if (lineBuffer.Length > 0)
                        {
                            progress?.Report($"  {lineBuffer}");
                            lineBuffer.Clear();
                        }
                        break;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            stopwatch.Stop();
            progress?.Report($"  [Ollama] === END STREAMING ({stopwatch.Elapsed.TotalSeconds:F1}s) ===");

            var ollamaResponse = fullResponse.ToString();
            result.RawResponse = ollamaResponse;

            progress?.Report($"  [Ollama] Total response: {ollamaResponse.Length} chars");

            // Попытаться извлечь JSON из ответа
            progress?.Report("  [Ollama] Extracting JSON from response...");
            var jsonMatch = Regex.Match(ollamaResponse, @"\{[\s\S]*\}", RegexOptions.Multiline);
            if (jsonMatch.Success)
            {
                progress?.Report($"  [Ollama] Found JSON: {jsonMatch.Value.Length} chars");
                try
                {
                    var parsedJson = JsonSerializer.Deserialize<OllamaParsedReceipt>(
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

                        progress?.Report($"  [Ollama] {result.Message}");
                    }
                }
                catch (JsonException ex)
                {
                    progress?.Report($"  [Ollama] JSON parse error: {ex.Message}");
                    result.IsSuccess = false;
                    result.Message = $"Failed to parse JSON: {ex.Message}";
                }
            }
            else
            {
                progress?.Report("  [Ollama] No JSON found in response");
                result.IsSuccess = false;
                result.Message = "No JSON found in Ollama response";
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            progress?.Report("  [Ollama] Request timed out");
            result.IsSuccess = false;
            result.Message = "Ollama request timed out";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress?.Report("  [Ollama] Cancelled by user");
            result.IsSuccess = false;
            result.Message = "Operation cancelled by user";
            throw;
        }
        catch (OperationCanceledException)
        {
            progress?.Report("  [Ollama] Timed out");
            result.IsSuccess = false;
            result.Message = "Ollama request timed out";
        }
        catch (HttpRequestException ex)
        {
            progress?.Report($"  [Ollama] HTTP error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"HTTP error: {ex.Message}";
        }
        catch (Exception ex)
        {
            progress?.Report($"  [Ollama] Error: {ex.Message}");
            result.IsSuccess = false;
            result.Message = $"Error: {ex.Message}";
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
                // Reload template if file changed (simple cache invalidation by checking each time)
                _promptTemplate = File.ReadAllText(_promptTemplatePath);
                progress?.Report($"  [Ollama] Loaded prompt template from: {_promptTemplatePath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  [Ollama] Failed to load template: {ex.Message}, using default");
                _promptTemplate = null;
            }
        }

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            // Replace placeholders
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

    // DTOs для Ollama API
    private class OllamaGenerateRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool Stream { get; set; }
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; } = 4096;

        [JsonPropertyName("num_ctx")]
        public int NumCtx { get; set; } = 8192;
    }

    private class OllamaGenerateResponse
    {
        public string Response { get; set; } = string.Empty;
    }

    private class OllamaStreamChunk
    {
        public string? Response { get; set; }
        public bool Done { get; set; }
    }

    private class OllamaTagsResponse
    {
        public List<OllamaModel>? Models { get; set; }
    }

    private class OllamaModel
    {
        public string Name { get; set; } = string.Empty;
    }

    private class OllamaParsedReceipt
    {
        public string? Shop { get; set; }
        public string? Date { get; set; }

        [JsonPropertyName("order_datetime")]
        public string? OrderDatetime { get; set; }

        [JsonPropertyName("order_number")]
        public string? OrderNumber { get; set; }

        public decimal? Total { get; set; }
        public List<OllamaParsedItem>? Items { get; set; }
    }

    private class OllamaParsedItem
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
