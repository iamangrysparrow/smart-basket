using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Email;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Check command line args
var mode = args.Length > 0 ? args[0].ToLower() : "help";

// JSON options with proper Unicode support
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

// Load settings
var possiblePaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), "..", "SmartBasket.WPF", "appsettings.json"),
    Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
    @"D:\AI\smart-basket\src\SmartBasket.WPF\appsettings.json"
};

string? settingsPath = possiblePaths.FirstOrDefault(File.Exists);
if (settingsPath == null)
{
    Console.WriteLine("ERROR: appsettings.json not found!");
    return 1;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(settingsPath, optional: false)
    .Build();

var settings = new AppSettings();
configuration.Bind(settings);

switch (mode)
{
    case "test-ollama":
    case "test":
        return await TestOllamaAsync(settings, args.Length > 1 ? int.Parse(args[1]) : 3);

    case "email":
        return await FetchEmailsAsync(settings);

    case "parse":
        return await ParseEmailsAsync(settings);

    default:
        Console.WriteLine("=== SmartBasket CLI ===");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- test-ollama [count]  - Test Ollama performance (default: 3 runs)");
        Console.WriteLine("  dotnet run -- email                - Fetch and save emails");
        Console.WriteLine("  dotnet run -- parse                - Fetch emails and parse with Ollama");
        Console.WriteLine();
        Console.WriteLine($"Settings: {settingsPath}");
        Console.WriteLine($"Ollama: {settings.Ollama.BaseUrl}, Model: {settings.Ollama.Model}");
        return 0;
}

// ============= TEST OLLAMA MODE =============
async Task<int> TestOllamaAsync(AppSettings settings, int runCount)
{
    Console.WriteLine("=== Ollama Performance Test ===");
    Console.WriteLine($"URL: {settings.Ollama.BaseUrl}");
    Console.WriteLine($"Model: {settings.Ollama.Model}");
    Console.WriteLine($"Runs: {runCount}");
    Console.WriteLine();

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(30);

    // Simple test prompt - short receipt
    var testPrompt = @"Ты помощник по распознаванию чеков. Извлеки данные из этого чека:

Магазин: АШАН
Дата: 03.12.2024
Товары:
- Молоко 1л - 89.99р
- Хлеб белый - 45.50р
- Яйца 10шт - 120р
Итого: 255.49р

Верни JSON:
{""shop"":""название"",""date"":""YYYY-MM-DD"",""total"":0,""items"":[{""name"":""товар"",""price"":0}]}";

    var results = new List<(int run, double time, int tokens, bool success, string error)>();

    // Warmup - load model
    Console.WriteLine("Warming up (loading model)...");
    var warmupSw = Stopwatch.StartNew();
    try
    {
        var warmupReq = new { model = settings.Ollama.Model, prompt = "Hi", stream = false };
        var warmupResp = await httpClient.PostAsJsonAsync($"{settings.Ollama.BaseUrl}/api/generate", warmupReq);
        warmupSw.Stop();
        Console.WriteLine($"Warmup: {warmupSw.Elapsed.TotalSeconds:F1}s - {warmupResp.StatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warmup FAILED: {ex.Message}");
        return 1;
    }
    Console.WriteLine();

    // Run tests
    for (int i = 1; i <= runCount; i++)
    {
        Console.Write($"Run {i}/{runCount}... ");

        var request = new
        {
            model = settings.Ollama.Model,
            prompt = testPrompt,
            stream = false,
            options = new { temperature = 0.1, num_predict = 512 }
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.PostAsJsonAsync($"{settings.Ollama.BaseUrl}/api/generate", request);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = JsonSerializer.Deserialize<JsonElement>(content);

                var evalCount = json.TryGetProperty("eval_count", out var ec) ? ec.GetInt32() : 0;
                var responseText = json.TryGetProperty("response", out var rt) ? rt.GetString() ?? "" : "";

                // Check if JSON found
                var hasJson = responseText.Contains("{") && responseText.Contains("}");

                results.Add((i, sw.Elapsed.TotalSeconds, evalCount, hasJson, ""));
                Console.WriteLine($"{sw.Elapsed.TotalSeconds:F2}s, {evalCount} tokens, JSON: {(hasJson ? "OK" : "NO")}");

                if (i == 1)
                {
                    Console.WriteLine($"  Response preview: {responseText.Substring(0, Math.Min(200, responseText.Length))}...");
                }
            }
            else
            {
                results.Add((i, sw.Elapsed.TotalSeconds, 0, false, response.StatusCode.ToString()));
                Console.WriteLine($"FAILED: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            results.Add((i, sw.Elapsed.TotalSeconds, 0, false, "TIMEOUT"));
            Console.WriteLine($"TIMEOUT after {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            sw.Stop();
            results.Add((i, sw.Elapsed.TotalSeconds, 0, false, ex.Message));
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }

    // Summary
    Console.WriteLine();
    Console.WriteLine("=== Results ===");
    var successful = results.Where(r => r.success).ToList();
    if (successful.Any())
    {
        Console.WriteLine($"Success: {successful.Count}/{runCount}");
        Console.WriteLine($"Avg time: {successful.Average(r => r.time):F2}s");
        Console.WriteLine($"Min time: {successful.Min(r => r.time):F2}s");
        Console.WriteLine($"Max time: {successful.Max(r => r.time):F2}s");
        Console.WriteLine($"Avg tokens: {successful.Average(r => r.tokens):F0}");

        var avgTokPerSec = successful.Average(r => r.tokens / r.time);
        Console.WriteLine($"Avg tok/s: {avgTokPerSec:F1}");
    }
    else
    {
        Console.WriteLine("All runs failed!");
    }

    var failed = results.Where(r => !r.success).ToList();
    if (failed.Any())
    {
        Console.WriteLine($"Failed: {failed.Count} - {string.Join(", ", failed.Select(f => f.error))}");
    }

    return successful.Count > 0 ? 0 : 1;
}

// ============= FETCH EMAILS MODE =============
async Task<int> FetchEmailsAsync(AppSettings settings)
{
    Console.WriteLine("=== Fetch Emails ===");
    Console.WriteLine($"Server: {settings.Email.ImapServer}:{settings.Email.ImapPort}");
    Console.WriteLine($"User: {settings.Email.Username}");
    Console.WriteLine($"Filters: Sender='{settings.Email.SenderFilter}', Subject='{settings.Email.SubjectFilter}'");
    Console.WriteLine();

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddSingleton(settings);
    services.AddSingleton<IEmailService, EmailService>();
    var provider = services.BuildServiceProvider();
    var emailService = provider.GetRequiredService<IEmailService>();

    var (ok, msg) = await emailService.TestConnectionAsync(settings.Email);
    Console.WriteLine($"Connection: {(ok ? "OK" : "FAILED")} - {msg}");
    if (!ok) return 1;

    var emails = await emailService.FetchEmailsAsync(settings.Email, new Progress<string>(Console.WriteLine));
    Console.WriteLine($"Found: {emails.Count} emails");

    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
    Directory.CreateDirectory(outputDir);

    foreach (var email in emails)
    {
        var safeName = Regex.Replace(email.Subject ?? "no-subject", @"[^\w\-]", "_");
        if (safeName.Length > 40) safeName = safeName[..40];
        var fileName = $"{email.Date:yyyy-MM-dd}_{safeName}";

        Console.WriteLine($"  {email.Date:MM-dd} | {email.Subject}");

        var cleanBody = CleanHtml(email.Body ?? "");
        File.WriteAllText(Path.Combine(outputDir, $"{fileName}_clean.txt"), cleanBody);
    }

    Console.WriteLine($"Saved to: {outputDir}");
    return 0;
}

// ============= PARSE EMAILS MODE =============
async Task<int> ParseEmailsAsync(AppSettings settings)
{
    Console.WriteLine("=== Parse Emails with Ollama ===");

    // First fetch emails
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddSingleton(settings);
    services.AddSingleton<IEmailService, EmailService>();
    var provider = services.BuildServiceProvider();
    var emailService = provider.GetRequiredService<IEmailService>();

    var emails = await emailService.FetchEmailsAsync(settings.Email, new Progress<string>(s => { }));
    Console.WriteLine($"Found: {emails.Count} emails");

    if (emails.Count == 0) return 0;

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(120);

    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
    Directory.CreateDirectory(outputDir);

    foreach (var email in emails)
    {
        var safeName = Regex.Replace(email.Subject ?? "no-subject", @"[^\w\-]", "_");
        if (safeName.Length > 40) safeName = safeName[..40];
        var fileName = $"{email.Date:yyyy-MM-dd}_{safeName}";

        Console.WriteLine($"\n[{email.Date:MM-dd}] {email.Subject}");

        var cleanBody = CleanHtml(email.Body ?? "");
        if (cleanBody.Length > 8000) cleanBody = cleanBody[..8000] + "\n...(truncated)";

        var prompt = BuildPrompt(cleanBody);
        Console.WriteLine($"  Body: {cleanBody.Length} chars, Prompt: {prompt.Length} chars");

        var request = new
        {
            model = settings.Ollama.Model,
            prompt = prompt,
            stream = false,
            options = new { temperature = 0.1, num_predict = 4096 }
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.PostAsJsonAsync($"{settings.Ollama.BaseUrl}/api/generate", request);
            sw.Stop();

            Console.WriteLine($"  Response: {sw.Elapsed.TotalSeconds:F1}s, {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = JsonSerializer.Deserialize<JsonElement>(content);
                var responseText = json.TryGetProperty("response", out var rt) ? rt.GetString() ?? "" : "";

                // Extract JSON
                var match = Regex.Match(responseText, @"\{[\s\S]*\}");
                if (match.Success)
                {
                    var parsed = JsonSerializer.Deserialize<JsonElement>(match.Value);
                    var prettyJson = JsonSerializer.Serialize(parsed, jsonOptions);
                    File.WriteAllText(Path.Combine(outputDir, $"{fileName}_parsed.json"), prettyJson);

                    if (parsed.TryGetProperty("items", out var items))
                        Console.WriteLine($"  Items: {items.GetArrayLength()}");
                    if (parsed.TryGetProperty("total", out var total))
                        Console.WriteLine($"  Total: {total}");
                }
                else
                {
                    Console.WriteLine($"  No JSON in response!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR: {ex.Message}");
        }
    }

    return 0;
}

// ============= HELPERS =============
static string CleanHtml(string html)
{
    if (string.IsNullOrEmpty(html)) return "";

    var result = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
    result = Regex.Replace(result, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
    result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
    result = Regex.Replace(result, @"</?(p|div|tr|li)[^>]*>", "\n", RegexOptions.IgnoreCase);
    result = Regex.Replace(result, @"</(td|th)>", " | ", RegexOptions.IgnoreCase);
    result = Regex.Replace(result, @"<[^>]+>", "");
    result = System.Net.WebUtility.HtmlDecode(result);
    result = Regex.Replace(result, @"[ \t]+", " ");
    result = Regex.Replace(result, @"\n\s*\n", "\n\n");
    return result.Trim();
}

static string BuildPrompt(string body) => $@"Извлеки из чека JSON. Только JSON, без текста:
{{""shop"":"""",""date"":""YYYY-MM-DD"",""order_number"":"""",""total"":0,""items"":[{{""name"":"""",""quantity"":1,""price"":0}}]}}

Чек:
{body}";
