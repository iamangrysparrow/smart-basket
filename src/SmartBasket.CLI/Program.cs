using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Helpers;
using SmartBasket.Data;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Email;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Tools;
using SmartBasket.Services.Tools.Handlers;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Check command line args
var mode = args.Length > 0 ? args[0].ToLower() : "help";

// JSON options with proper Unicode support
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

// Load settings - prefer bin/Debug version (has more configured providers)
var possiblePaths = new[]
{
    @"D:\AI\smart-basket\src\SmartBasket.WPF\bin\Debug\net8.0-windows\appsettings.json",
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

// Decrypt secrets (API keys are encrypted in the config file)
foreach (var provider in settings.AiProviders)
{
    if (!string.IsNullOrEmpty(provider.ApiKey))
    {
        provider.ApiKey = SecretHelper.Decrypt(provider.ApiKey);
    }
}

// Get default Ollama provider from AiProviders
var ollamaProvider = settings.AiProviders.FirstOrDefault(p => p.Provider == AiProviderType.Ollama);

// Get YandexAgent provider if exists
var yandexAgentProvider = settings.AiProviders.FirstOrDefault(p => p.Provider == AiProviderType.YandexAgent);

// Get first Email source from ReceiptSources
var emailSource = settings.ReceiptSources.FirstOrDefault(s => s.Type == SourceType.Email && s.IsEnabled);

switch (mode)
{
    case "test-ollama":
    case "test":
        if (ollamaProvider == null)
        {
            Console.WriteLine("ERROR: No Ollama provider configured!");
            return 1;
        }
        return await TestOllamaAsync(ollamaProvider, args.Length > 1 ? int.Parse(args[1]) : 3);

    case "test-agent-stream":
        if (yandexAgentProvider == null)
        {
            Console.WriteLine("ERROR: No YandexAgent provider configured in AiProviders!");
            return 1;
        }
        return await TestYandexAgentStreamingAsync(yandexAgentProvider);

    case "test-agent-provider":
        if (yandexAgentProvider == null)
        {
            Console.WriteLine("ERROR: No YandexAgent provider configured in AiProviders!");
            return 1;
        }
        return await TestYandexAgentProviderAsync(yandexAgentProvider);

    case "email":
        if (emailSource?.Email == null)
        {
            Console.WriteLine("ERROR: No Email source configured in ReceiptSources!");
            return 1;
        }
        return await FetchEmailsAsync(emailSource.Email);

    case "parse":
        if (emailSource?.Email == null)
        {
            Console.WriteLine("ERROR: No Email source configured in ReceiptSources!");
            return 1;
        }
        if (ollamaProvider == null)
        {
            Console.WriteLine("ERROR: No Ollama provider configured!");
            return 1;
        }
        return await ParseEmailsAsync(emailSource.Email, ollamaProvider);

    case "classify":
        if (ollamaProvider == null)
        {
            Console.WriteLine("ERROR: No Ollama provider configured!");
            return 1;
        }
        var model = args.Length > 1 ? args[1] : ollamaProvider.Model;
        return await TestClassificationAsync(ollamaProvider, model);

    case "test-query":
        return await TestQueryHandlerAsync(settings);

    case "test-chat":
        var providerKey = args.Length > 1 ? args[1] : null;
        var iterations = args.Length > 2 ? int.Parse(args[2]) : 1;
        return await TestChatServiceAsync(settings, providerKey, iterations);

    default:
        Console.WriteLine("=== SmartBasket CLI ===");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- test-ollama [count]  - Test Ollama performance (default: 3 runs)");
        Console.WriteLine("  dotnet run -- test-agent-stream    - Test YandexAgent streaming API (raw HTTP)");
        Console.WriteLine("  dotnet run -- test-agent-provider  - Test YandexAgent via LlmProvider (streaming)");
        Console.WriteLine("  dotnet run -- test-query           - Test QueryHandler (SQL queries to all tables)");
        Console.WriteLine("  dotnet run -- test-chat [provider] [iterations] - Test ChatService with tools");
        Console.WriteLine("  dotnet run -- email                - Fetch and save emails");
        Console.WriteLine("  dotnet run -- parse                - Fetch emails and parse with Ollama");
        Console.WriteLine("  dotnet run -- classify [model]     - Test classification pipeline");
        Console.WriteLine();
        Console.WriteLine($"Settings: {settingsPath}");
        if (ollamaProvider != null)
        {
            Console.WriteLine($"Ollama Provider: {ollamaProvider.Key}");
            Console.WriteLine($"  BaseUrl: {ollamaProvider.BaseUrl}");
            Console.WriteLine($"  Model: {ollamaProvider.Model}");
        }
        if (yandexAgentProvider != null)
        {
            Console.WriteLine($"YandexAgent Provider: {yandexAgentProvider.Key}");
            Console.WriteLine($"  AgentId: {yandexAgentProvider.AgentId}");
            Console.WriteLine($"  FolderId: {yandexAgentProvider.FolderId}");
        }
        if (emailSource != null)
        {
            Console.WriteLine($"Email Source: {emailSource.Name}");
            Console.WriteLine($"  Server: {emailSource.Email?.ImapServer}");
        }
        return 0;
}

// ============= TEST OLLAMA MODE =============
async Task<int> TestOllamaAsync(AiProviderConfig provider, int runCount)
{
    Console.WriteLine("=== Ollama Performance Test ===");
    Console.WriteLine($"Provider: {provider.Key}");
    Console.WriteLine($"URL: {provider.BaseUrl}");
    Console.WriteLine($"Model: {provider.Model}");
    Console.WriteLine($"Runs: {runCount}");
    Console.WriteLine();

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds);

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
        var warmupReq = new { model = provider.Model, prompt = "Hi", stream = false };
        var warmupResp = await httpClient.PostAsJsonAsync($"{provider.BaseUrl}/api/generate", warmupReq);
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
            model = provider.Model,
            prompt = testPrompt,
            stream = false,
            options = new { temperature = provider.Temperature, num_predict = 512 }
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.PostAsJsonAsync($"{provider.BaseUrl}/api/generate", request);
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
async Task<int> FetchEmailsAsync(EmailSourceConfig emailConfig)
{
    Console.WriteLine("=== Fetch Emails ===");
    Console.WriteLine($"Server: {emailConfig.ImapServer}:{emailConfig.ImapPort}");
    Console.WriteLine($"User: {emailConfig.Username}");
    Console.WriteLine($"Filters: Sender='{emailConfig.SenderFilter}', Subject='{emailConfig.SubjectFilter}'");
    Console.WriteLine();

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddSingleton<IEmailService, EmailService>();
    var provider = services.BuildServiceProvider();
    var emailService = provider.GetRequiredService<IEmailService>();

    var (ok, msg) = await emailService.TestConnectionAsync(emailConfig);
    Console.WriteLine($"Connection: {(ok ? "OK" : "FAILED")} - {msg}");
    if (!ok) return 1;

    var emails = await emailService.FetchEmailsAsync(emailConfig, new Progress<string>(Console.WriteLine));
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
async Task<int> ParseEmailsAsync(EmailSourceConfig emailConfig, AiProviderConfig aiProvider)
{
    Console.WriteLine("=== Parse Emails with Ollama ===");
    Console.WriteLine($"Provider: {aiProvider.Key}");

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddSingleton<IEmailService, EmailService>();
    var serviceProvider = services.BuildServiceProvider();
    var emailService = serviceProvider.GetRequiredService<IEmailService>();

    var emails = await emailService.FetchEmailsAsync(emailConfig, new Progress<string>(s => { }));
    Console.WriteLine($"Found: {emails.Count} emails");

    if (emails.Count == 0) return 0;

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(aiProvider.TimeoutSeconds);

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
            model = aiProvider.Model,
            prompt = prompt,
            stream = false,
            options = new { temperature = aiProvider.Temperature, num_predict = aiProvider.MaxTokens ?? 4096 }
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.PostAsJsonAsync($"{aiProvider.BaseUrl}/api/generate", request);
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

// ============= CLASSIFY TEST MODE =============
async Task<int> TestClassificationAsync(AiProviderConfig provider, string model)
{
    Console.WriteLine("=== Classification Pipeline Test ===");
    Console.WriteLine($"Provider: {provider.Key}");
    Console.WriteLine($"URL: {provider.BaseUrl}");
    Console.WriteLine($"Model: {model}");
    Console.WriteLine();

    // Test items
    var testItems = new[]
    {
        "Колбаса вареная Клинский Докторская 400 г",
        "Свекла",
        "Джем Ратибор малиновый 360 г",
        "Голень цыпленка-бройлера Куриное Царство с кожей охлажденное ~1 кг",
        "Яйцо куриное Экстра СО коричневое 10 шт",
        "Макаронные изделия Makfa Спагетти 450 г",
        "Яблоки Ред Делишес новый урожай",
        "Сыр полутвердый Брест-Литовск Финский 45% БЗМЖ 200 г",
        "Бедро куриное «Петелинка» с кожей охлажденное, ~ 1 кг",
        "Капуста квашеная Белоручка Фитнес 1 кг",
        "Томаты красные",
        "Лук репчатый",
        "Огурец среднеплодный 180 г",
        "Мандарины с листочком",
        "Морковь мытая",
        "Кофе Жокей Классический молотый 250 г",
        "Огурцы Каждый День маринованные 680 г",
        "Рис Увелка круглозерный в варочных пакетиках 80 г х 5 шт",
        "Кабачки Цукини зеленые",
        "Картофель Лайт 2 кг",
        "Конфеты шоколадные Акконд Птица дивная с суфлейной начинкой",
        "Вода питьевая Сенежская газированная 1,5 л",
        "Молоко 1,5% пастеризованное 930 мл Простоквашино БЗМЖ",
        "Борщ АШАН Красная птица с курицей, 250 г",
        "Сметана 15% Простоквашино БЗМЖ 300 г",
        "Варенье Вологодское варенье Домашнее малиновое 370 г",
        "Подсолнечное масло Затея рафинированное дезодорированное 1 л",
        "Шампиньоны АШАН Красная птица целые 250 г",
        "Горошек АШАН Красная птица зеленый консервированный 400 г",
        "Батон Коломенский пшеничный в нарезке 400 г"
    };

    // User labels
    var userLabels = new[]
    {
        "Здоровое питание",
        "Диетический продукт",
        "Для детей",
        "Для завтрака",
        "Для выпечки",
        "Для салата",
        "Для супа",
        "Для бутербродов",
        "К чаю/кофе",
        "Любимое",
        "Попробовать новое",
        "Не покупать больше"
    };

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds);

    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
    Directory.CreateDirectory(outputDir);

    var results = new
    {
        model = model,
        timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        classification = new List<object>(),
        items = new List<object>(),
        labels = new List<object>()
    };

    // ========== STEP 1: Product Classification (batches of 5) ==========
    Console.WriteLine("=== STEP 1: Product Classification (batches of 5) ===");
    Console.WriteLine();

    var sw = Stopwatch.StartNew();
    var existingHierarchy = new List<string>(); // Accumulate hierarchy between batches
    var batchSize = 5;
    var batches = testItems.Chunk(batchSize).ToList();
    var totalProducts = 0;
    var totalItemsMapped = 0;

    for (int batchIdx = 0; batchIdx < batches.Count; batchIdx++)
    {
        var batch = batches[batchIdx];
        Console.WriteLine($"\n--- Batch {batchIdx + 1}/{batches.Count} ({batch.Length} items) ---");

        var hierarchyText = existingHierarchy.Count > 0
            ? string.Join("\n", existingHierarchy)
            : "(пусто - создай новые продукты)";

        var classifyPrompt = $@"Выдели продукты из списка товаров и построй иерархию.

СУЩЕСТВУЮЩАЯ ИЕРАРХИЯ ПРОДУКТОВ:
{hierarchyText}

ТОВАРЫ ДЛЯ КЛАССИФИКАЦИИ:
{string.Join("\n", batch.Select((item, i) => $"{i + 1}. {item}"))}

ПРАВИЛА:
1. Продукт - это категория товаров (Молоко, Овощи, Фрукты, Кофе молотый и т.п.)
2. Продукты могут быть иерархичными: Овощи -> Томаты, Овощи -> Горошек консервированный
3. Если продукт уже есть в существующей иерархии - используй его
4. Если продукта нет - создай новый и укажи parent если он вложенный
5. parent должен ссылаться на существующий или новый продукт из этого же ответа

ФОРМАТ ОТВЕТА (строго JSON):
{{
  ""products"": [
    {{""name"": ""Название продукта"", ""parent"": null или ""Название родителя""}}
  ],
  ""items"": [
    {{""name"": ""Полное название товара"", ""product"": ""Название продукта""}}
  ]
}}

Классифицируй товары:";

        var classifyRequest = new
        {
            model = model,
            prompt = classifyPrompt,
            stream = false,
            options = new { temperature = provider.Temperature, num_predict = 2048 }
        };

        sw.Restart();

        try
        {
            var response = await httpClient.PostAsJsonAsync($"{provider.BaseUrl}/api/generate", classifyRequest);
            sw.Stop();
            Console.WriteLine($"Response: {sw.Elapsed.TotalSeconds:F1}s");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = JsonSerializer.Deserialize<JsonElement>(content);
                var responseText = json.TryGetProperty("response", out var rt) ? rt.GetString() ?? "" : "";

                var match = Regex.Match(responseText, @"\{[\s\S]*\}");
                if (match.Success)
                {
                    var parsed = JsonSerializer.Deserialize<JsonElement>(match.Value);
                    var prettyJson = JsonSerializer.Serialize(parsed, jsonOptions);

                    Console.WriteLine(prettyJson);

                    // Count and accumulate
                    if (parsed.TryGetProperty("products", out var products))
                    {
                        totalProducts += products.GetArrayLength();
                        // Add new products to hierarchy for next batches
                        foreach (var p in products.EnumerateArray())
                        {
                            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var parent = p.TryGetProperty("parent", out var pr) && pr.ValueKind != JsonValueKind.Null ? pr.GetString() : null;
                            if (!string.IsNullOrEmpty(name))
                            {
                                var entry = parent != null ? $"- {name} (parent: {parent})" : $"- {name}";
                                if (!existingHierarchy.Contains(entry))
                                    existingHierarchy.Add(entry);
                            }
                        }
                    }
                    if (parsed.TryGetProperty("items", out var items))
                        totalItemsMapped += items.GetArrayLength();

                    ((List<object>)results.classification).Add(new { batch = batchIdx + 1, time = sw.Elapsed.TotalSeconds, response = match.Value });
                }
                else
                {
                    Console.WriteLine("No JSON in response!");
                    Console.WriteLine(responseText.Length > 500 ? responseText[..500] + "..." : responseText);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }

    Console.WriteLine($"\n=== Classification Summary: {totalProducts} products, {totalItemsMapped} items mapped ===");
    Console.WriteLine($"Accumulated hierarchy ({existingHierarchy.Count} entries):");
    foreach (var h in existingHierarchy.Take(20))
        Console.WriteLine($"  {h}");
    if (existingHierarchy.Count > 20)
        Console.WriteLine($"  ... and {existingHierarchy.Count - 20} more");

    // ========== STEP 2: Unit extraction (sample) ==========
    Console.WriteLine("\n=== STEP 2: Unit Extraction (sample of 5 items) ===");
    Console.WriteLine();

    var sampleItems = testItems.Take(5).ToList();
    var unitPrompt = $@"Извлеки единицы измерения из названий товаров.

ТОВАРЫ:
{string.Join("\n", sampleItems.Select((item, i) => $"{i + 1}. {item}"))}

Для каждого товара определи:
- unit_of_measure: единица измерения товара (г/кг/мл/л/шт)
- unit_quantity: количество в единице (число)

ПРИМЕРЫ:
""Молоко Простоквашино 930 мл"" -> unit_of_measure: ""мл"", unit_quantity: 930
""Яйцо куриное 10 шт"" -> unit_of_measure: ""шт"", unit_quantity: 10

ФОРМАТ ОТВЕТА (строго JSON массив):
[{{""name"":"""",""unit_of_measure"":"""",""unit_quantity"":0}}]

Извлеки данные:";

    var unitRequest = new
    {
        model = model,
        prompt = unitPrompt,
        stream = false,
        options = new { temperature = provider.Temperature, num_predict = 1024 }
    };

    Console.WriteLine("Sending unit extraction request...");
    sw.Restart();

    try
    {
        var response = await httpClient.PostAsJsonAsync($"{provider.BaseUrl}/api/generate", unitRequest);
        sw.Stop();
        Console.WriteLine($"Response: {sw.Elapsed.TotalSeconds:F1}s");

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(content);
            var responseText = json.TryGetProperty("response", out var rt) ? rt.GetString() ?? "" : "";

            var match = Regex.Match(responseText, @"\[[\s\S]*?\]");
            if (match.Success)
            {
                var parsed = JsonSerializer.Deserialize<JsonElement>(match.Value);
                var prettyJson = JsonSerializer.Serialize(parsed, jsonOptions);

                Console.WriteLine("\n--- Unit Extraction Result ---");
                Console.WriteLine(prettyJson);

                ((List<object>)results.items).Add(new { time = sw.Elapsed.TotalSeconds, response = match.Value });
            }
            else
            {
                Console.WriteLine("No JSON array in response!");
                Console.WriteLine(responseText);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
    }

    // ========== STEP 3: Label Assignment (sample) ==========
    Console.WriteLine("\n=== STEP 3: Label Assignment (sample of 5 items) ===");
    Console.WriteLine();

    foreach (var item in sampleItems)
    {
        var labelPrompt = $@"Назначь подходящие метки для товара.

ДОСТУПНЫЕ МЕТКИ:
{string.Join("\n", userLabels.Select(l => $"- {l}"))}

ТОВАР:
{item}

ПРАВИЛА:
1. Выбери только те метки, которые точно подходят к товару
2. Товар может иметь 0, 1 или несколько меток
3. Не придумывай новые метки - используй только из списка выше
4. Если ни одна метка не подходит - верни пустой массив

ФОРМАТ ОТВЕТА (строго JSON массив):
[""Метка1"", ""Метка2""]

Назначь метки:";

        var labelRequest = new
        {
            model = model,
            prompt = labelPrompt,
            stream = false,
            options = new { temperature = provider.Temperature, num_predict = 256 }
        };

        Console.Write($"  {item.Substring(0, Math.Min(40, item.Length))}... ");
        sw.Restart();

        try
        {
            var response = await httpClient.PostAsJsonAsync($"{provider.BaseUrl}/api/generate", labelRequest);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = JsonSerializer.Deserialize<JsonElement>(content);
                var responseText = json.TryGetProperty("response", out var rt) ? rt.GetString() ?? "" : "";

                var match = Regex.Match(responseText, @"\[[\s\S]*?\]");
                if (match.Success)
                {
                    var labels = JsonSerializer.Deserialize<string[]>(match.Value) ?? Array.Empty<string>();
                    Console.WriteLine($"[{sw.Elapsed.TotalSeconds:F1}s] {string.Join(", ", labels)}");

                    ((List<object>)results.labels).Add(new { item = item, labels = labels, time = sw.Elapsed.TotalSeconds });
                }
                else
                {
                    Console.WriteLine($"[{sw.Elapsed.TotalSeconds:F1}s] (no labels)");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }

    // Save results
    var outputPath = Path.Combine(outputDir, $"classify_test_{DateTime.Now:yyyyMMdd_HHmmss}.json");
    File.WriteAllText(outputPath, JsonSerializer.Serialize(results, jsonOptions));
    Console.WriteLine($"\n=== Results saved to: {outputPath} ===");

    return 0;
}

// ============= TEST YANDEX AGENT STREAMING =============
async Task<int> TestYandexAgentStreamingAsync(AiProviderConfig provider)
{
    Console.WriteLine("=== YandexAgent Streaming API Test ===");
    Console.WriteLine($"Agent ID: {provider.AgentId}");
    Console.WriteLine($"Folder ID: {provider.FolderId}");
    Console.WriteLine();

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(120);

    var testPrompt = "Напиши короткий тост на день рождения, дружелюбный и смешной. 2-3 предложения.";

    // ========== TEST 1: Non-streaming (current implementation) ==========
    Console.WriteLine("=== TEST 1: Non-streaming request ===");
    {
        var request = new
        {
            prompt = new { id = provider.AgentId },
            input = testPrompt
        };

        var requestJson = JsonSerializer.Serialize(request, jsonOptions);
        Console.WriteLine($"Request: {requestJson}");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://rest-assistant.api.cloud.yandex.net/v1/responses")
        {
            Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {provider.ApiKey}");
        httpRequest.Headers.Add("x-folder-id", provider.FolderId);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.SendAsync(httpRequest);
            sw.Stop();

            Console.WriteLine($"Status: {response.StatusCode} ({sw.Elapsed.TotalSeconds:F1}s)");
            Console.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response ({content.Length} chars):");
            Console.WriteLine(content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }

    Console.WriteLine();

    // ========== TEST 2: Streaming request (stream: true in body) ==========
    Console.WriteLine("=== TEST 2: Streaming request (stream: true) ===");
    {
        var request = new
        {
            prompt = new { id = provider.AgentId },
            input = testPrompt,
            stream = true
        };

        var requestJson = JsonSerializer.Serialize(request, jsonOptions);
        Console.WriteLine($"Request: {requestJson}");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://rest-assistant.api.cloud.yandex.net/v1/responses")
        {
            Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {provider.ApiKey}");
        httpRequest.Headers.Add("x-folder-id", provider.FolderId);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");

            // Try to read as stream
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            Console.WriteLine("--- Stream content: ---");
            var lineCount = 0;
            while (!reader.EndOfStream && lineCount < 50) // Limit to 50 lines
            {
                var line = await reader.ReadLineAsync();
                Console.WriteLine($"[{lineCount++}] {line}");
            }

            sw.Stop();
            Console.WriteLine($"--- End stream ({sw.Elapsed.TotalSeconds:F1}s) ---");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }

    Console.WriteLine();

    // ========== TEST 3: Streaming with Accept header ==========
    Console.WriteLine("=== TEST 3: Streaming with Accept: text/event-stream ===");
    {
        var request = new
        {
            prompt = new { id = provider.AgentId },
            input = testPrompt,
            stream = true
        };

        var requestJson = JsonSerializer.Serialize(request, jsonOptions);
        Console.WriteLine($"Request: {requestJson}");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://rest-assistant.api.cloud.yandex.net/v1/responses")
        {
            Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {provider.ApiKey}");
        httpRequest.Headers.Add("x-folder-id", provider.FolderId);
        httpRequest.Headers.Add("Accept", "text/event-stream");

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");

            // Try to read as stream
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            Console.WriteLine("--- Stream content: ---");
            var lineCount = 0;
            while (!reader.EndOfStream && lineCount < 50) // Limit to 50 lines
            {
                var line = await reader.ReadLineAsync();
                Console.WriteLine($"[{lineCount++}] {line}");
            }

            sw.Stop();
            Console.WriteLine($"--- End stream ({sw.Elapsed.TotalSeconds:F1}s) ---");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }

    return 0;
}

// ============= TEST YANDEX AGENT PROVIDER =============
async Task<int> TestYandexAgentProviderAsync(AiProviderConfig provider)
{
    Console.WriteLine("=== YandexAgent Provider Test (with YandexAgentLlmProvider) ===");
    Console.WriteLine($"Agent ID: {provider.AgentId}");
    Console.WriteLine($"Folder ID: {provider.FolderId}");
    Console.WriteLine();

    // Create the provider using DI
    var services = new ServiceCollection();
    services.AddHttpClient();
    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
    var serviceProvider = services.BuildServiceProvider();

    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var logger = serviceProvider.GetRequiredService<ILogger<SmartBasket.Services.Llm.YandexAgentLlmProvider>>();

    var agentProvider = new SmartBasket.Services.Llm.YandexAgentLlmProvider(httpClientFactory, logger, provider);

    var testPrompt = "Напиши короткий тост на день рождения. 2-3 предложения.";

    Console.WriteLine($"Prompt: {testPrompt}");
    Console.WriteLine();

    // Progress reporter for real-time output
    var progress = new Progress<string>(msg => Console.WriteLine(msg));

    var sw = Stopwatch.StartNew();
    var result = await agentProvider.GenerateAsync(testPrompt, progress: progress);
    sw.Stop();

    Console.WriteLine();
    Console.WriteLine($"=== RESULT ({sw.Elapsed.TotalSeconds:F1}s) ===");
    Console.WriteLine($"Success: {result.IsSuccess}");
    if (result.IsSuccess)
    {
        Console.WriteLine($"Response ({result.Response?.Length ?? 0} chars):");
        Console.WriteLine(result.Response);
    }
    else
    {
        Console.WriteLine($"Error: {result.ErrorMessage}");
    }

    return result.IsSuccess ? 0 : 1;
}

// ============= TEST QUERY HANDLER =============
async Task<int> TestQueryHandlerAsync(AppSettings appSettings)
{
    Console.WriteLine("=== QueryHandler Integration Test ===");
    Console.WriteLine($"Database: {appSettings.Database.ConnectionString?.Substring(0, Math.Min(50, appSettings.Database.ConnectionString?.Length ?? 0))}...");
    Console.WriteLine();

    var services = new ServiceCollection();
    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
    services.AddSingleton(appSettings);
    services.AddTransient<QueryHandler>();
    var serviceProvider = services.BuildServiceProvider();

    var queryHandler = serviceProvider.GetRequiredService<QueryHandler>();

    var testCases = new List<(string name, string json)>
    {
        // 1. Simple SELECT from each table
        ("Receipts - COUNT", """{"table": "Receipts", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),
        ("Receipts - SUM Total", """{"table": "Receipts", "aggregates": [{"function": "SUM", "column": "Total", "alias": "total_sum"}]}"""),
        ("Receipts - SELECT top 3", """{"table": "Receipts", "columns": ["Id", "Shop", "Total", "ReceiptDate"], "order_by": [{"column": "ReceiptDate", "direction": "DESC"}], "limit": 3}"""),

        ("ReceiptItems - COUNT", """{"table": "ReceiptItems", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),
        ("ReceiptItems - SELECT top 3", """{"table": "ReceiptItems", "columns": ["Id", "ReceiptId", "ItemId", "Quantity", "Amount"], "limit": 3}"""),

        ("Items - COUNT", """{"table": "Items", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),
        ("Items - SELECT top 3", """{"table": "Items", "columns": ["Id", "Name", "Shop"], "limit": 3}"""),
        ("Items - ILIKE search", """{"table": "Items", "columns": ["Id", "Name"], "where": [{"column": "Name", "op": "ILIKE", "value": "%молоко%"}], "limit": 5}"""),

        ("Products - COUNT", """{"table": "Products", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),
        ("Products - SELECT top 3", """{"table": "Products", "columns": ["Id", "Name", "ParentId"], "limit": 3}"""),

        ("Labels - COUNT", """{"table": "Labels", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),
        ("Labels - SELECT all", """{"table": "Labels", "columns": ["Id", "Name", "Color"], "limit": 10}"""),

        ("ItemLabels - COUNT", """{"table": "ItemLabels", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),
        ("ProductLabels - COUNT", """{"table": "ProductLabels", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),

        // 2. JOINs
        ("JOIN: ReceiptItems + Items", """{"table": "ReceiptItems", "joins": [{"table": "Items", "on": ["Items.Id", "ReceiptItems.ItemId"]}], "columns": ["ReceiptItems.Id", "Items.Name", "ReceiptItems.Quantity", "ReceiptItems.Amount"], "limit": 5}"""),

        ("JOIN: ReceiptItems + Receipts", """{"table": "ReceiptItems", "joins": [{"table": "Receipts", "on": ["Receipts.Id", "ReceiptItems.ReceiptId"]}], "columns": ["Receipts.Shop", "Receipts.ReceiptDate", "ReceiptItems.Amount"], "limit": 5}"""),

        ("JOIN: Items + Products", """{"table": "Items", "joins": [{"table": "Products", "on": ["Products.Id", "Items.ProductId"], "type": "left"}], "columns": ["Items.Name", "Products.Name"], "limit": 5}"""),

        // 3. Aggregates with GROUP BY
        ("GROUP BY: Items by Shop", """{"table": "Items", "columns": ["Shop"], "aggregates": [{"function": "COUNT", "column": "*", "alias": "item_count"}], "group_by": ["Shop"], "order_by": [{"column": "item_count", "direction": "DESC"}], "limit": 10}"""),

        ("GROUP BY: Receipts by Shop with SUM", """{"table": "Receipts", "columns": ["Shop"], "aggregates": [{"function": "COUNT", "column": "*", "alias": "receipt_count"}, {"function": "SUM", "column": "Total", "alias": "total_sum"}], "group_by": ["Shop"], "order_by": [{"column": "total_sum", "direction": "DESC"}], "limit": 10}"""),

        // 4. Complex query: Top items by total amount
        ("TOP 5 Items by Amount (JOIN + GROUP BY)", """{"table": "ReceiptItems", "joins": [{"table": "Items", "on": ["Items.Id", "ReceiptItems.ItemId"]}], "columns": ["Items.Name"], "aggregates": [{"function": "SUM", "column": "ReceiptItems.Amount", "alias": "total_amount"}, {"function": "COUNT", "column": "*", "alias": "purchase_count"}], "group_by": ["Items.Name"], "order_by": [{"column": "total_amount", "direction": "DESC"}], "limit": 5}"""),

        // 5. WHERE with different operators
        ("WHERE: Receipts with Total > 1000", """{"table": "Receipts", "columns": ["Id", "Shop", "Total", "ReceiptDate"], "where": [{"column": "Total", "op": ">", "value": 1000}], "limit": 5}"""),

        ("WHERE: Date range (BETWEEN)", """{"table": "Receipts", "columns": ["Id", "Shop", "Total", "ReceiptDate"], "where": [{"column": "ReceiptDate", "op": "BETWEEN", "value": ["2024-01-01", "2024-12-31"]}], "limit": 5}"""),

        ("WHERE: Date >= (timestamp cast)", """{"table": "Receipts", "columns": ["Id", "Shop", "ReceiptDate"], "where": [{"column": "ReceiptDate", "op": ">=", "value": "2024-06-01"}], "limit": 5}"""),

        ("WHERE: Date = (timestamp cast)", """{"table": "Receipts", "columns": ["Id", "Shop", "ReceiptDate"], "where": [{"column": "ReceiptDate", "op": "=", "value": "2024-12-01"}], "limit": 5}"""),

        // 6. snake_case input normalization test
        ("snake_case: receipt_items", """{"table": "receipt_items", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}"""),
        ("snake_case: item_labels", """{"table": "item_labels", "aggregates": [{"function": "COUNT", "column": "*", "alias": "total"}]}""")
    };

    var passed = 0;
    var failed = 0;
    var failedTests = new List<string>();

    foreach (var (name, json) in testCases)
    {
        Console.Write($"  {name}... ");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await queryHandler.ExecuteAsync(json);
            sw.Stop();

            if (result.Success)
            {
                var data = JsonSerializer.Deserialize<JsonElement>(result.JsonData);
                var rowCount = data.TryGetProperty("row_count", out var rc) ? rc.GetInt32() : -1;
                var sql = data.TryGetProperty("sql", out var s) ? s.GetString() : "";

                Console.WriteLine($"OK ({sw.ElapsedMilliseconds}ms, {rowCount} rows)");
                Console.WriteLine($"      SQL: {sql?.Substring(0, Math.Min(100, sql?.Length ?? 0))}...");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAILED: {result.ErrorMessage}");
                failed++;
                failedTests.Add($"{name}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"ERROR: {ex.Message}");
            failed++;
            failedTests.Add($"{name}: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("=== Summary ===");
    Console.WriteLine($"Passed: {passed}/{testCases.Count}");
    Console.WriteLine($"Failed: {failed}/{testCases.Count}");

    if (failedTests.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Failed tests:");
        foreach (var f in failedTests)
        {
            Console.WriteLine($"  - {f}");
        }
    }

    return failed == 0 ? 0 : 1;
}

// ============= TEST CHAT SERVICE =============
async Task<int> TestChatServiceAsync(AppSettings appSettings, string? providerKey, int iterations)
{
    Console.WriteLine("=== ChatService Test with Tools ===");
    Console.WriteLine($"Provider key filter: {providerKey ?? "(all)"}");
    Console.WriteLine($"Iterations per provider: {iterations}");
    Console.WriteLine();

    // Get available providers
    var availableProviders = appSettings.AiProviders
        .Where(p => providerKey == null || p.Key.Contains(providerKey, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (availableProviders.Count == 0)
    {
        Console.WriteLine("ERROR: No matching providers found!");
        Console.WriteLine("Available providers:");
        foreach (var p in appSettings.AiProviders)
            Console.WriteLine($"  - {p.Key} ({p.Provider})");
        return 1;
    }

    Console.WriteLine($"Testing {availableProviders.Count} provider(s):");
    foreach (var p in availableProviders)
        Console.WriteLine($"  - {p.Key} ({p.Provider}, Model: {p.Model})");
    Console.WriteLine();

    // Setup DI
    var services = new ServiceCollection();
    services.AddHttpClient();
    services.AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Debug);
        builder.AddFilter("Microsoft", LogLevel.Warning);
        builder.AddFilter("System", LogLevel.Warning);
    });
    services.AddSingleton(appSettings);

    // Register database context
    var dbProvider = appSettings.Database.Provider.ToLowerInvariant() switch
    {
        "postgresql" or "postgres" => DatabaseProviderType.PostgreSQL,
        "sqlite" => DatabaseProviderType.SQLite,
        _ => DatabaseProviderType.PostgreSQL
    };
    services.AddSmartBasketDbContext(dbProvider, appSettings.Database.ConnectionString);

    // Register tools using extension method
    services.AddTools();

    // Register provider factory
    services.AddSingleton<IAiProviderFactory, AiProviderFactory>();

    // Register chat service
    services.AddTransient<IChatService, ChatService>();

    var serviceProvider = services.BuildServiceProvider();

    var testPrompt = "Сколько у меня чеков? На какую сумму?";
    var expectedAnswer = "6 чеков на сумму 34540,91"; // Примерно

    Console.WriteLine($"Test prompt: {testPrompt}");
    Console.WriteLine($"Expected answer (approx): {expectedAnswer}");
    Console.WriteLine();

    var results = new List<(string provider, int iteration, bool success, double time, string response)>();

    foreach (var providerConfig in availableProviders)
    {
        Console.WriteLine($"========================================");
        Console.WriteLine($"PROVIDER: {providerConfig.Key}");
        Console.WriteLine($"========================================");

        for (int i = 1; i <= iterations; i++)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Iteration {i}/{iterations} ---");

            // Create fresh chat service for each iteration
            var chatService = serviceProvider.GetRequiredService<IChatService>();
            chatService.SetProvider(providerConfig.Key);

            // Set system prompt (simplified)
            var systemPrompt = @"Ты - помощник по анализу чеков и покупок.
У тебя есть доступ к базе данных через инструменты.
Отвечай кратко и по существу на русском языке.
Когда получишь результат инструмента - сразу отвечай пользователю на его вопрос.";

            chatService.SetSystemPrompt(systemPrompt);

            var progress = new Progress<string>(msg =>
            {
                Console.WriteLine($"  {msg}");
            });

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await chatService.SendAsync(testPrompt, progress, CancellationToken.None);
                sw.Stop();

                Console.WriteLine();
                Console.WriteLine($"=== RESULT ({sw.Elapsed.TotalSeconds:F1}s) ===");
                Console.WriteLine($"Success: {response.Success}");
                Console.WriteLine($"Response: {response.Content}");

                if (!response.Success)
                {
                    Console.WriteLine($"Error: {response.ErrorMessage}");
                }

                results.Add((providerConfig.Key, i, response.Success, sw.Elapsed.TotalSeconds, response.Content ?? response.ErrorMessage ?? ""));

                // Check if answer looks correct
                var isCorrect = response.Content?.Contains("6") == true &&
                               (response.Content?.Contains("34540") == true ||
                                response.Content?.Contains("31540") == true ||
                                response.Content?.Contains("сумм") == true);

                Console.WriteLine($"Answer looks correct: {isCorrect}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"EXCEPTION: {ex.Message}");
                results.Add((providerConfig.Key, i, false, sw.Elapsed.TotalSeconds, ex.Message));
            }

            Console.WriteLine();
        }
    }

    // Summary
    Console.WriteLine();
    Console.WriteLine("========================================");
    Console.WriteLine("SUMMARY");
    Console.WriteLine("========================================");

    var grouped = results.GroupBy(r => r.provider);
    foreach (var group in grouped)
    {
        var successCount = group.Count(r => r.success);
        var avgTime = group.Where(r => r.success).Select(r => r.time).DefaultIfEmpty(0).Average();

        Console.WriteLine($"{group.Key}:");
        Console.WriteLine($"  Success: {successCount}/{group.Count()}");
        Console.WriteLine($"  Avg time: {avgTime:F1}s");

        foreach (var r in group)
        {
            var preview = r.response.Length > 100 ? r.response[..100] + "..." : r.response;
            Console.WriteLine($"  [{r.iteration}] {(r.success ? "OK" : "FAIL")} ({r.time:F1}s): {preview}");
        }
        Console.WriteLine();
    }

    return results.All(r => r.success) ? 0 : 1;
}
