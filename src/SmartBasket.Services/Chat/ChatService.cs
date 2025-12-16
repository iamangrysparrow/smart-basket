using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Tools;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Chat;

/// <summary>
/// Сервис чата с tool-use loop
/// </summary>
public class ChatService : IChatService
{
    private readonly IToolExecutor _tools;
    private readonly IAiProviderFactory _providerFactory;
    private readonly ILogger<ChatService> _logger;
    private readonly List<LlmChatMessage> _history = new();

    private const int MaxIterations = 20;
    private string? _systemPrompt;
    private string? _currentProviderKey;
    private bool _isPrimed; // Флаг что контекст БД уже загружен

    public IReadOnlyList<LlmChatMessage> History => _history.AsReadOnly();
    public string? CurrentProviderKey => _currentProviderKey;

    public ChatService(
        IToolExecutor tools,
        IAiProviderFactory providerFactory,
        ILogger<ChatService> logger)
    {
        _tools = tools;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public void SetProvider(string providerKey)
    {
        if (_currentProviderKey != providerKey)
        {
            _logger.LogInformation("[ChatService] Provider changed: {Old} -> {New}",
                _currentProviderKey ?? "(none)", providerKey);
            _currentProviderKey = providerKey;
        }
    }

    public void ClearHistory()
    {
        var count = _history.Count;
        _history.Clear();
        _isPrimed = false; // Сбрасываем флаг priming при очистке истории
        _logger.LogInformation("[ChatService] History cleared ({Count} messages removed), priming reset", count);
    }

    public void SetSystemPrompt(string systemPrompt)
    {
        _systemPrompt = systemPrompt;
        _isPrimed = false; // Сбрасываем priming при изменении промпта
        _logger.LogInformation("[ChatService] System prompt set ({Length} chars), priming reset", systemPrompt.Length);
        _logger.LogDebug("[ChatService] System prompt:\n{Prompt}", systemPrompt);
    }

    /// <summary>
    /// Auto-priming: добавляет контекст БД в начало диалога для "тупых" моделей
    /// </summary>
    private async Task<string> GetPrimingContextAsync(CancellationToken ct)
    {
        _logger.LogInformation("[ChatService] >>> AUTO-PRIMING: загружаю контекст БД...");

        try
        {
            // Выполняем describe_data чтобы получить схему и примеры
            var describeResult = await _tools.ExecuteAsync("describe_data", "{}", ct);

            if (!describeResult.Success)
            {
                _logger.LogWarning("[ChatService] describe_data failed: {Error}", describeResult.ErrorMessage);
                return "";
            }

            var now = DateTime.Now;
            var primingContext = $@"
=== КОНТЕКСТ БАЗЫ ДАННЫХ ===

ТЕКУЩАЯ ДАТА И ВРЕМЯ: {now:yyyy-MM-dd HH:mm:ss} ({now:dddd}, {GetRussianMonth(now.Month)} {now.Year})

СХЕМА И ДАННЫЕ:
{describeResult.JsonData}

=== ИНСТРУКЦИИ ПО ИСПОЛЬЗОВАНИЮ ИНСТРУМЕНТОВ ===

У тебя есть 2 инструмента:
1. describe_data - схема БД (УЖЕ ВЫЗВАН выше, НЕ вызывай повторно)
2. query - универсальный SELECT запрос

Для query используй JSON:
{{
  ""table"": ""Receipts"",
  ""columns"": [""Shop"", ""Total""],
  ""aggregates"": [{{""function"": ""SUM"", ""column"": ""Total"", ""alias"": ""total_sum""}}],
  ""where"": [{{""column"": ""ReceiptDate"", ""op"": "">="", ""value"": ""2024-01-01""}}],
  ""group_by"": [""Shop""],
  ""order_by"": [{{""column"": ""total_sum"", ""direction"": ""DESC""}}],
  ""limit"": 10
}}

Таблицы: Receipts, ReceiptItems, Items, Products, Labels, ItemLabels, ProductLabels
Операторы WHERE: =, !=, >, <, >=, <=, ILIKE, IN, NOT IN, IS NULL, BETWEEN
Функции: COUNT, SUM, AVG, MIN, MAX

=== КОНЕЦ КОНТЕКСТА ===
";
            _logger.LogInformation("[ChatService] <<< AUTO-PRIMING: контекст загружен ({Length} chars)", primingContext.Length);
            return primingContext;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatService] AUTO-PRIMING failed: {Error}", ex.Message);
            return "";
        }
    }

    private static string GetRussianMonth(int month) => month switch
    {
        1 => "января", 2 => "февраля", 3 => "марта", 4 => "апреля",
        5 => "мая", 6 => "июня", 7 => "июля", 8 => "августа",
        9 => "сентября", 10 => "октября", 11 => "ноября", 12 => "декабря",
        _ => ""
    };

    public async Task<ChatResponse> SendAsync(
        string userMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("[ChatService] ========================================");
        _logger.LogInformation("[ChatService] >>> NEW MESSAGE");
        _logger.LogInformation("[ChatService] User message ({Length} chars):\n{Message}",
            userMessage.Length, TruncateForLog(userMessage, 500));

        // AUTO-PRIMING: при первом сообщении добавляем контекст БД
        string primingContext = "";
        if (!_isPrimed)
        {
            progress?.Report("Загружаю контекст базы данных...");
            primingContext = await GetPrimingContextAsync(cancellationToken);
            _isPrimed = true;
            _logger.LogInformation("[ChatService] Priming context added ({Length} chars)", primingContext.Length);
        }

        // Добавляем сообщение пользователя
        _history.Add(new LlmChatMessage { Role = "user", Content = userMessage });
        _logger.LogDebug("[ChatService] History size: {Count} messages", _history.Count);

        // Получаем провайдер: сначала динамически установленный, потом из конфигурации
        ILlmProvider? provider = null;
        if (!string.IsNullOrEmpty(_currentProviderKey))
        {
            provider = _providerFactory.GetProvider(_currentProviderKey);
            if (provider == null)
            {
                _logger.LogWarning("[ChatService] Provider '{Key}' not found, falling back to AiOperations.Chat",
                    _currentProviderKey);
            }
        }

        // Fallback на конфигурацию AiOperations.Chat
        provider ??= _providerFactory.GetProviderForOperation(AiOperation.Chat);

        if (provider == null)
        {
            _logger.LogError("[ChatService] No provider available (tried: {Key}, AiOperations.Chat)",
                _currentProviderKey ?? "(none)");
            return new ChatResponse("", false, "AI провайдер для чата не настроен. Установите провайдер через SetProvider() или в AiOperations.Chat.");
        }

        _logger.LogInformation("[ChatService] Provider: {Provider} (SupportsTools={SupportsTools})",
            provider.Name, provider.SupportsTools);

        // Получаем определения инструментов
        var tools = _tools.GetToolDefinitions();
        _logger.LogInformation("[ChatService] Tools available: {Count}", tools.Count);
        foreach (var tool in tools)
        {
            _logger.LogDebug("[ChatService]   - {Name}: {Description}",
                tool.Name, TruncateForLog(tool.Description, 80));
        }

        // Если провайдер не поддерживает native tools, добавляем описание в системный промпт
        var effectiveSystemPrompt = _systemPrompt ?? "";
        IReadOnlyList<ToolDefinition>? effectiveTools = tools;

        // Добавляем priming context (схема БД, примеры, текущее время)
        if (!string.IsNullOrEmpty(primingContext))
        {
            effectiveSystemPrompt = effectiveSystemPrompt + "\n\n" + primingContext;
            _logger.LogInformation("[ChatService] Priming context appended to system prompt");
        }

        if (!provider.SupportsTools && tools.Count > 0)
        {
            _logger.LogInformation("[ChatService] Provider doesn't support native tools, using prompt injection");
            effectiveSystemPrompt = BuildToolsSystemPrompt(effectiveSystemPrompt, tools);
            effectiveTools = null; // Не передаём tools в API
        }

        // Подготавливаем сообщения с системным промптом
        var messages = BuildMessagesWithSystem(effectiveSystemPrompt);
        _logger.LogDebug("[ChatService] Total messages to send: {Count}", messages.Count);

        // Tool-use loop
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            var iterationStopwatch = Stopwatch.StartNew();

            _logger.LogInformation("[ChatService] --- Iteration {Iteration}/{Max} ---",
                iteration + 1, MaxIterations);
            progress?.Report($"[Iteration {iteration + 1}] Отправляю запрос к LLM...");

            // Вызов LLM
            var llmStopwatch = Stopwatch.StartNew();
            var result = await provider.ChatAsync(
                messages,
                effectiveTools,
                maxTokens: 2000,
                temperature: 0.7,
                progress: progress,
                cancellationToken: cancellationToken);
            llmStopwatch.Stop();

            // Пробуем парсить tool calls из текста (fallback для любых провайдеров)
            // Некоторые модели (например YandexAgent) выводят tool call как текст вместо native function_call
            if (!result.HasToolCalls && !string.IsNullOrEmpty(result.Response))
            {
                var parsedToolCalls = TryParseToolCallsFromText(result.Response);
                if (parsedToolCalls.Count > 0)
                {
                    // Помечаем что эти tool calls были parsed из текста (не native)
                    foreach (var tc in parsedToolCalls)
                    {
                        tc.IsParsedFromText = true;
                    }
                    result.ToolCalls = parsedToolCalls;
                    // Извлекаем текст до JSON (если был)
                    result.Response = ExtractTextBeforeToolCall(result.Response);
                    _logger.LogInformation("[ChatService] Parsed {Count} tool calls from text response (fallback, IsParsedFromText=true)", parsedToolCalls.Count);
                }
            }

            _logger.LogInformation("[ChatService] LLM response in {Time:F2}s, success={Success}",
                llmStopwatch.Elapsed.TotalSeconds, result.IsSuccess);

            if (!result.IsSuccess)
            {
                _logger.LogError("[ChatService] LLM error: {Error}", result.ErrorMessage);
                _logger.LogInformation("[ChatService] ========================================");
                return new ChatResponse("", false, result.ErrorMessage);
            }

            // Логируем ответ LLM
            if (!string.IsNullOrEmpty(result.Response))
            {
                _logger.LogDebug("[ChatService] LLM response text ({Length} chars):\n{Response}",
                    result.Response.Length, TruncateForLog(result.Response, 500));
            }

            // Если нет tool calls — это финальный ответ
            if (!result.HasToolCalls)
            {
                var response = result.Response ?? "";
                _history.Add(new LlmChatMessage
                {
                    Role = "assistant",
                    Content = response
                });

                totalStopwatch.Stop();
                _logger.LogInformation("[ChatService] <<< FINAL RESPONSE");
                _logger.LogInformation("[ChatService] Response ({Length} chars) in {Time:F2}s total, {Iterations} iteration(s)",
                    response.Length, totalStopwatch.Elapsed.TotalSeconds, iteration + 1);
                _logger.LogDebug("[ChatService] Response:\n{Response}", TruncateForLog(response, 1000));
                _logger.LogInformation("[ChatService] ========================================");

                return new ChatResponse(response, true);
            }

            // Обрабатываем tool calls
            _logger.LogInformation("[ChatService] Tool calls requested: {Count}", result.ToolCalls!.Count);
            foreach (var tc in result.ToolCalls)
            {
                _logger.LogInformation("[ChatService]   - {Name}(id={Id})", tc.Name, tc.Id);
            }

            // Добавляем assistant сообщение с tool calls
            _history.Add(new LlmChatMessage
            {
                Role = "assistant",
                Content = result.Response ?? "",
                ToolCalls = result.ToolCalls
            });

            // Обновляем messages для следующей итерации
            messages = BuildMessagesWithSystem(effectiveSystemPrompt);

            // Выполняем каждый tool call
            foreach (var call in result.ToolCalls)
            {
                _logger.LogInformation("[ChatService] >>> Executing tool: {Name}", call.Name);
                _logger.LogDebug("[ChatService] Tool arguments:\n{Args}",
                    FormatJson(call.Arguments));

                // Логируем аргументы в UI для отладки
                progress?.Report($"[DEBUG] >>> Tool: {call.Name}");
                progress?.Report($"[DEBUG] Args: {TruncateForLog(FormatJson(call.Arguments), 300)}");

                progress?.Report($"Выполняю {call.Name}...");

                var toolStopwatch = Stopwatch.StartNew();
                var toolResult = await _tools.ExecuteAsync(call.Name, call.Arguments, cancellationToken);
                toolStopwatch.Stop();

                _logger.LogInformation("[ChatService] <<< Tool {Name} completed in {Time:F2}s, success={Success}",
                    call.Name, toolStopwatch.Elapsed.TotalSeconds, toolResult.Success);

                if (!toolResult.Success)
                {
                    _logger.LogWarning("[ChatService] Tool error: {Error}", toolResult.ErrorMessage);
                }

                _logger.LogDebug("[ChatService] Tool result ({Length} chars):\n{Result}",
                    toolResult.JsonData.Length, TruncateForLog(toolResult.JsonData, 500));

                // Логируем результат в UI для отладки
                progress?.Report($"[DEBUG] <<< Tool: {call.Name} ({toolStopwatch.Elapsed.TotalSeconds:F2}s, success={toolResult.Success})");
                progress?.Report($"[DEBUG] Result ({toolResult.JsonData.Length} chars): {TruncateForLog(toolResult.JsonData, 500)}");

                // Добавляем результат tool в историю
                _history.Add(new LlmChatMessage
                {
                    Role = "tool",
                    Content = toolResult.JsonData,
                    ToolCallId = call.Id,
                    IsToolError = !toolResult.Success  // Помечаем ошибку для правильного форматирования
                });

                // Обновляем messages
                messages = BuildMessagesWithSystem(effectiveSystemPrompt);
            }

            iterationStopwatch.Stop();
            _logger.LogInformation("[ChatService] Iteration {Iteration} completed in {Time:F2}s",
                iteration + 1, iterationStopwatch.Elapsed.TotalSeconds);
        }

        totalStopwatch.Stop();
        _logger.LogWarning("[ChatService] Max iterations ({Max}) exceeded after {Time:F2}s",
            MaxIterations, totalStopwatch.Elapsed.TotalSeconds);
        _logger.LogInformation("[ChatService] ========================================");

        return new ChatResponse("", false, "Превышено максимальное количество итераций");
    }

    private List<LlmChatMessage> BuildMessagesWithSystem(string? systemPrompt = null)
    {
        var messages = new List<LlmChatMessage>();

        // Используем переданный промпт или дефолтный
        var prompt = systemPrompt ?? _systemPrompt;

        // Добавляем системный промпт первым
        if (!string.IsNullOrEmpty(prompt))
        {
            messages.Add(new LlmChatMessage
            {
                Role = "system",
                Content = prompt
            });
        }

        // Добавляем всю историю
        messages.AddRange(_history);

        return messages;
    }

    /// <summary>
    /// Создаёт системный промпт с описанием инструментов для моделей без native tool calling
    /// </summary>
    private string BuildToolsSystemPrompt(string basePrompt, IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(basePrompt))
        {
            sb.AppendLine(basePrompt);
            sb.AppendLine();
        }

        sb.AppendLine("У тебя есть доступ к следующим инструментам:");
        sb.AppendLine();

        foreach (var tool in tools)
        {
            sb.AppendLine($"### {tool.Name}");
            sb.AppendLine(tool.Description);
            if (tool.ParametersSchema != null)
            {
                sb.AppendLine("Параметры (JSON Schema):");
                sb.AppendLine($"```json");
                sb.AppendLine(JsonSerializer.Serialize(tool.ParametersSchema, new JsonSerializerOptions { WriteIndented = true }));
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }

        sb.AppendLine("ВАЖНО: Когда тебе нужно использовать инструмент, отвечай ТОЛЬКО JSON в следующем формате:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"name\": \"имя_инструмента\",");
        sb.AppendLine("  \"arguments\": { ... параметры ... }");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Не добавляй никакого текста до или после JSON блока. Если инструмент не нужен, отвечай обычным текстом.");

        return sb.ToString();
    }

    /// <summary>
    /// Попытка распарсить tool call из текстового ответа модели.
    /// Поддерживает форматы:
    /// 1. JSON в markdown code block: ```json { "name": "...", "arguments": {...} } ```
    /// 2. Голый JSON: { "name": "...", "arguments": {...} }
    /// 3. Формат function call: tool_name({"arg": "value"})
    /// 4. Обрабатывает <think>...</think> блоки (DeepSeek-R1)
    /// 5. Теги <tool_request>...</tool_request> и <tool_response>...</tool_response> (qwen)
    /// </summary>
    private List<LlmToolCall> TryParseToolCallsFromText(string text)
    {
        var result = new List<LlmToolCall>();

        try
        {
            // Удаляем <think>...</think> блоки (DeepSeek-R1 reasoning)
            text = RemoveThinkBlocks(text);

            if (string.IsNullOrWhiteSpace(text)) return result;

            // Паттерн 0: Теги <tool_request> или <tool_response> (qwen иногда так отвечает)
            var toolTagPattern = new System.Text.RegularExpressions.Regex(
                @"<tool_(?:request|response)>\s*(\{[\s\S]*?\})\s*</tool_(?:request|response)>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var toolTagMatch = toolTagPattern.Match(text);
            if (toolTagMatch.Success)
            {
                var jsonStr = toolTagMatch.Groups[1].Value.Trim();
                var toolCall = TryParseToolCallJson(jsonStr);
                if (toolCall != null)
                {
                    result.Add(toolCall);
                    _logger.LogInformation("[ChatService] Parsed tool call from <tool_*> tag: {Tool}", toolCall.Name);
                    return result;
                }
            }

            // Паттерн 1: Function call format: tool_name({"arg": "value"})
            var funcCall = TryParseFunctionCallFormat(text);
            if (funcCall != null)
            {
                result.Add(funcCall);
                return result;
            }

            // Паттерн 2: JSON в markdown code block
            var codeBlockPattern = new System.Text.RegularExpressions.Regex(
                @"```(?:json)?\s*\n?\s*(\{[\s\S]*?\})\s*\n?\s*```",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var matches = codeBlockPattern.Matches(text);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var jsonStr = match.Groups[1].Value.Trim();
                var toolCall = TryParseToolCallJson(jsonStr);
                if (toolCall != null)
                {
                    result.Add(toolCall);
                }
            }

            if (result.Count > 0) return result;

            // Паттерн 3: Голый JSON объект
            var trimmed = text.Trim();
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                var toolCall = TryParseToolCallJson(trimmed);
                if (toolCall != null)
                {
                    result.Add(toolCall);
                    return result;
                }
            }

            // Паттерн 4: JSON массив [{"name": "...", "arguments": {...}}]
            // YandexAgent иногда выводит tool call как массив в тексте
            var arrayMatch = System.Text.RegularExpressions.Regex.Match(
                text,
                @"\[\s*\{[\s\S]*?\}\s*\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (arrayMatch.Success)
            {
                var parsed = TryParseToolCallArray(arrayMatch.Value);
                if (parsed.Count > 0)
                {
                    result.AddRange(parsed);
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[ChatService] Failed to parse tool calls from text: {Error}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Парсит массив tool calls из JSON: [{"name": "...", "arguments": {...}}]
    /// </summary>
    private List<LlmToolCall> TryParseToolCallArray(string json)
    {
        var result = new List<LlmToolCall>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var toolCall = TryParseToolCallJsonElement(element);
                    if (toolCall != null)
                    {
                        result.Add(toolCall);
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("[ChatService] Failed to parse tool call array: {Error}", ex.Message);
        }
        return result;
    }

    /// <summary>
    /// Парсит один tool call из JsonElement
    /// </summary>
    private LlmToolCall? TryParseToolCallJsonElement(JsonElement element)
    {
        if (element.TryGetProperty("name", out var nameProp))
        {
            var name = nameProp.GetString();
            if (string.IsNullOrEmpty(name)) return null;

            string argsJson = "{}";
            if (element.TryGetProperty("arguments", out var argsProp))
            {
                argsJson = argsProp.GetRawText();
            }
            else if (element.TryGetProperty("parameters", out var paramsProp))
            {
                argsJson = paramsProp.GetRawText();
            }

            return new LlmToolCall
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = name,
                Arguments = argsJson
            };
        }
        return null;
    }

    /// <summary>
    /// Извлекает текст до JSON tool call (для отображения пользователю)
    /// </summary>
    private string ExtractTextBeforeToolCall(string text)
    {
        // Ищем начало JSON массива или объекта
        var jsonStart = -1;

        // Ищем начало массива [
        var arrayStart = text.IndexOf('[');
        if (arrayStart >= 0 && arrayStart < text.Length - 1)
        {
            // Проверяем что это начало JSON массива с tool call
            var afterBracket = text.Substring(arrayStart + 1).TrimStart();
            if (afterBracket.StartsWith("{"))
            {
                jsonStart = arrayStart;
            }
        }

        // Ищем начало объекта { с "name"
        if (jsonStart < 0)
        {
            var objStart = text.IndexOf('{');
            if (objStart >= 0)
            {
                jsonStart = objStart;
            }
        }

        if (jsonStart > 0)
        {
            return text.Substring(0, jsonStart).Trim();
        }

        return "";
    }

    /// <summary>
    /// Удаляет блоки &lt;think&gt;...&lt;/think&gt; из текста (DeepSeek-R1 reasoning)
    /// </summary>
    private static string RemoveThinkBlocks(string content)
    {
        var thinkStart = content.IndexOf("<think>");
        while (thinkStart >= 0)
        {
            var thinkEnd = content.IndexOf("</think>", thinkStart);
            if (thinkEnd > thinkStart)
            {
                content = content[..thinkStart] + content[(thinkEnd + 8)..];
            }
            else
            {
                // Незакрытый тег - удаляем всё до конца
                content = content[..thinkStart];
                break;
            }
            thinkStart = content.IndexOf("<think>");
        }
        return content.Trim();
    }

    /// <summary>
    /// Парсит формат function call: tool_name({"arg": "value"})
    /// Используется моделями типа DeepSeek-R1
    /// </summary>
    private LlmToolCall? TryParseFunctionCallFormat(string content)
    {
        // Ищем паттерн: слово_с_подчеркиванием( или словоСподчеркиванием (
        var funcCallPattern = new System.Text.RegularExpressions.Regex(
            @"([a-z_][a-z0-9_]*)\s*\(\s*(\{[\s\S]*?\})\s*\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var match = funcCallPattern.Match(content);
        if (!match.Success) return null;

        var toolName = match.Groups[1].Value;
        var argsJson = match.Groups[2].Value;

        try
        {
            // Проверяем что JSON валидный
            using var doc = JsonDocument.Parse(argsJson);

            _logger.LogInformation("[ChatService] Parsed function call from text: {Tool}({Args})", toolName, argsJson);

            return new LlmToolCall
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = toolName,
                Arguments = argsJson
            };
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("[ChatService] Failed to parse function call args: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Парсинг одного tool call из JSON строки.
    /// Поддерживает форматы:
    /// - { "name": "tool", "arguments": {...} }
    /// - { "name": "tool", "parameters": {...} } (DeepSeek-R1)
    /// - { "function": { "name": "tool", "arguments": {...} } }
    /// </summary>
    private LlmToolCall? TryParseToolCallJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Формат: { "name": "tool_name", "arguments": {...} } или { "name": "tool_name", "parameters": {...} }
            if (root.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (string.IsNullOrEmpty(name)) return null;

                // Пробуем "arguments" или "parameters" (DeepSeek-R1 использует parameters)
                JsonElement argsProp;
                if (!root.TryGetProperty("arguments", out argsProp))
                {
                    if (!root.TryGetProperty("parameters", out argsProp))
                    {
                        // Нет ни arguments, ни parameters - пустые аргументы
                        return new LlmToolCall
                        {
                            Id = Guid.NewGuid().ToString("N")[..8],
                            Name = name,
                            Arguments = "{}"
                        };
                    }
                }

                var argsJson = argsProp.GetRawText();

                return new LlmToolCall
                {
                    Id = Guid.NewGuid().ToString("N")[..8],
                    Name = name,
                    Arguments = argsJson
                };
            }

            // Альтернативный формат: { "function": { "name": "...", "arguments": {...} } }
            if (root.TryGetProperty("function", out var funcProp))
            {
                if (funcProp.TryGetProperty("name", out var funcNameProp))
                {
                    var name = funcNameProp.GetString();
                    if (string.IsNullOrEmpty(name)) return null;

                    // Пробуем "arguments" или "parameters"
                    JsonElement funcArgsProp;
                    if (!funcProp.TryGetProperty("arguments", out funcArgsProp))
                    {
                        if (!funcProp.TryGetProperty("parameters", out funcArgsProp))
                        {
                            return new LlmToolCall
                            {
                                Id = Guid.NewGuid().ToString("N")[..8],
                                Name = name,
                                Arguments = "{}"
                            };
                        }
                    }

                    var argsJson = funcArgsProp.GetRawText();

                    return new LlmToolCall
                    {
                        Id = Guid.NewGuid().ToString("N")[..8],
                        Name = name,
                        Arguments = argsJson
                    };
                }
            }
        }
        catch (JsonException)
        {
            // Не валидный JSON - игнорируем
        }

        return null;
    }

    private static string TruncateForLog(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + $"... ({text.Length - maxLength} more chars)";
    }

    private static readonly JsonSerializerOptions JsonLogOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string FormatJson(string json)
    {
        try
        {
            var obj = JsonSerializer.Deserialize<object>(json);
            return JsonSerializer.Serialize(obj, JsonLogOptions);
        }
        catch
        {
            return json;
        }
    }
}
