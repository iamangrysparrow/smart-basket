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

// Get default Ollama provider from AiProviders
var ollamaProvider = settings.AiProviders.FirstOrDefault(p => p.Provider == AiProviderType.Ollama);
if (ollamaProvider == null)
{
    Console.WriteLine("ERROR: No Ollama provider configured in AiProviders!");
    Console.WriteLine("Please add an Ollama provider to appsettings.json");
    return 1;
}

// Get first Email source from ReceiptSources
var emailSource = settings.ReceiptSources.FirstOrDefault(s => s.Type == SourceType.Email && s.IsEnabled);

switch (mode)
{
    case "test-ollama":
    case "test":
        return await TestOllamaAsync(ollamaProvider, args.Length > 1 ? int.Parse(args[1]) : 3);

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
        return await ParseEmailsAsync(emailSource.Email, ollamaProvider);

    case "classify":
        var model = args.Length > 1 ? args[1] : ollamaProvider.Model;
        return await TestClassificationAsync(ollamaProvider, model);

    default:
        Console.WriteLine("=== SmartBasket CLI ===");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- test-ollama [count]  - Test Ollama performance (default: 3 runs)");
        Console.WriteLine("  dotnet run -- email                - Fetch and save emails");
        Console.WriteLine("  dotnet run -- parse                - Fetch emails and parse with Ollama");
        Console.WriteLine("  dotnet run -- classify [model]     - Test classification pipeline");
        Console.WriteLine();
        Console.WriteLine($"Settings: {settingsPath}");
        Console.WriteLine($"Ollama Provider: {ollamaProvider.Key}");
        Console.WriteLine($"  BaseUrl: {ollamaProvider.BaseUrl}");
        Console.WriteLine($"  Model: {ollamaProvider.Model}");
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
