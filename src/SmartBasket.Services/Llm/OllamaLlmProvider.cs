using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Llm;

/// <summary>
/// LLM провайдер для Ollama (локальные модели)
/// </summary>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaLlmProvider> _logger;
    private readonly AiProviderConfig _config;

    public string Name => $"Ollama/{_config.Model}";

    public bool SupportsConversationReset => false;

    public bool SupportsTools => true; // Ollama API поддерживает tools

    public void ResetConversation()
    {
        // Ollama не хранит состояние — ничего не делаем
    }

    public OllamaLlmProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaLlmProvider> logger,
        AiProviderConfig config)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _config = config;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var baseUrl = _config.BaseUrl ?? "http://localhost:11434";
            var response = await client.GetAsync($"{baseUrl}/api/tags", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (true, $"Ollama connected: {baseUrl}, model: {_config.Model}");
            }

            return (false, $"Ollama returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Ollama connection failed: {ex.Message}");
        }
    }

    public async Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        int maxTokens = 2000,
        double temperature = 0.1,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LlmGenerationResult();

        try
        {
            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var actualMaxTokens = _config.MaxTokens ?? maxTokens;
            var actualTemperature = _config.Temperature > 0 ? _config.Temperature : temperature;
            var baseUrl = _config.BaseUrl ?? "http://localhost:11434";

            var request = new OllamaRequest
            {
                Model = _config.Model,
                Prompt = prompt,
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = actualTemperature,
                    NumPredict = actualMaxTokens
                }
            };

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
            var requestUrl = $"{baseUrl}/api/generate";

            _logger.LogInformation("[Ollama] ========================================");
            _logger.LogInformation("[Ollama] >>> ЗАПРОС К OLLAMA");
            _logger.LogInformation("[Ollama] Config.Key: {Key}", _config.Key);
            _logger.LogInformation("[Ollama] Config.Model: {Model}", _config.Model);
            _logger.LogInformation("[Ollama] Config.BaseUrl: {BaseUrl}", _config.BaseUrl);
            _logger.LogInformation("[Ollama] URL: {Url}", requestUrl);
            _logger.LogInformation("[Ollama] Timeout: {Timeout}s", timeoutSeconds);
            _logger.LogInformation("[Ollama] Request JSON:\n{Json}", requestJson);

            progress?.Report($"[Ollama] >>> ЗАПРОС К OLLAMA");
            progress?.Report($"[Ollama] Config.Key: {_config.Key}");
            progress?.Report($"[Ollama] Config.Model: {_config.Model}");
            progress?.Report($"[Ollama] URL: {requestUrl}");
            progress?.Report($"[Ollama] Timeout: {timeoutSeconds}s");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response;
            try
            {
                _logger.LogInformation("[Ollama] Отправляю HTTP запрос...");
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                _logger.LogInformation("[Ollama] HTTP ответ: {StatusCode}", response.StatusCode);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("[Ollama] Timeout после {Timeout}s", timeoutSeconds);
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("[Ollama] Ошибка HTTP: {StatusCode}, Body: {Body}", response.StatusCode, errorContent);
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama returned {response.StatusCode}: {errorContent}";
                return result;
            }

            var fullResponse = new StringBuilder();
            var lineBuffer = new StringBuilder();

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk?.Response != null)
                    {
                        fullResponse.Append(chunk.Response);

                        foreach (var ch in chunk.Response)
                        {
                            if (ch == '\n')
                            {
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

            result.IsSuccess = true;
            result.Response = fullResponse.ToString();

            _logger.LogInformation("[Ollama] <<< ОТВЕТ ПОЛУЧЕН");
            _logger.LogInformation("[Ollama] Response length: {Length} chars", result.Response?.Length ?? 0);
            var responsePreview = result.Response?.Length > 500
                ? result.Response.Substring(0, 500) + "..."
                : result.Response;
            _logger.LogInformation("[Ollama] Response preview:\n{Preview}", responsePreview);
            _logger.LogInformation("[Ollama] ========================================");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[Ollama] Операция отменена пользователем");
            result.IsSuccess = false;
            result.ErrorMessage = "Operation cancelled by user";
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Ollama] Таймаут генерации");
            result.IsSuccess = false;
            result.ErrorMessage = "Generation timed out";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Ollama] Исключение: {Message}", ex.Message);
            result.IsSuccess = false;
            result.ErrorMessage = $"Error: {ex.Message}";
        }

        return result;
    }

    public async Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        int maxTokens = 2000,
        double temperature = 0.7,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LlmGenerationResult();

        try
        {
            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = Math.Max(_config.TimeoutSeconds, 60);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var actualMaxTokens = _config.MaxTokens ?? maxTokens;
            var actualTemperature = _config.Temperature > 0 ? _config.Temperature : temperature;
            var baseUrl = _config.BaseUrl ?? "http://localhost:11434";

            // Конвертируем сообщения в формат Ollama
            var ollamaMessages = ConvertMessages(messages);

            // Конвертируем tools в формат Ollama
            var ollamaTools = tools != null ? ConvertTools(tools) : null;

            var request = new OllamaChatRequest
            {
                Model = _config.Model,
                Messages = ollamaMessages,
                Tools = ollamaTools,
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = actualTemperature,
                    NumPredict = actualMaxTokens
                }
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var requestJson = JsonSerializer.Serialize(request, jsonOptions);
            var requestUrl = $"{baseUrl}/api/chat";

            _logger.LogInformation("[Ollama Chat] ========================================");
            _logger.LogInformation("[Ollama Chat] >>> ЗАПРОС К OLLAMA /api/chat");
            _logger.LogInformation("[Ollama Chat] Config.Model: {Model}", _config.Model);
            _logger.LogInformation("[Ollama Chat] Messages count: {Count}", ollamaMessages.Length);
            _logger.LogInformation("[Ollama Chat] Tools count: {Count}", ollamaTools?.Length ?? 0);
            _logger.LogInformation("[Ollama Chat] URL: {Url}", requestUrl);

            progress?.Report($"[Ollama Chat] >>> ЗАПРОС К /api/chat");
            progress?.Report($"[Ollama Chat] Model: {_config.Model}, Messages: {ollamaMessages.Length}, Tools: {ollamaTools?.Length ?? 0}");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama timeout after {timeoutSeconds}s";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("[Ollama Chat] Ошибка HTTP: {StatusCode}, Body: {Body}", response.StatusCode, errorContent);
                result.IsSuccess = false;
                result.ErrorMessage = $"Ollama returned {response.StatusCode}: {errorContent}";
                return result;
            }

            // Read streaming response
            var fullResponse = new StringBuilder();
            var lineBuffer = new StringBuilder();
            var collectedToolCalls = new List<LlmToolCall>();

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<OllamaChatStreamChunk>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // Обработка текстового контента
                    if (chunk?.Message?.Content != null)
                    {
                        fullResponse.Append(chunk.Message.Content);

                        foreach (var ch in chunk.Message.Content)
                        {
                            if (ch == '\n')
                            {
                                progress?.Report($"  {lineBuffer}");
                                lineBuffer.Clear();
                            }
                            else
                            {
                                lineBuffer.Append(ch);
                            }
                        }
                    }

                    // Обработка tool calls
                    if (chunk?.Message?.ToolCalls != null)
                    {
                        foreach (var toolCall in chunk.Message.ToolCalls)
                        {
                            if (toolCall?.Function != null)
                            {
                                var argsJson = toolCall.Function.Arguments != null
                                    ? JsonSerializer.Serialize(toolCall.Function.Arguments)
                                    : "{}";

                                // Используем ID из Ollama если есть, иначе генерируем свой
                                var toolCallId = !string.IsNullOrEmpty(toolCall.Id)
                                    ? toolCall.Id
                                    : $"call_{Guid.NewGuid().ToString("N")[..8]}";

                                collectedToolCalls.Add(new LlmToolCall
                                {
                                    Id = toolCallId,
                                    Name = toolCall.Function.Name ?? "",
                                    Arguments = argsJson
                                });

                                _logger.LogInformation("[Ollama Chat] Tool call: id={Id}, {Name}({Args})",
                                    toolCallId, toolCall.Function.Name, argsJson);
                                progress?.Report($"  [Tool Call] {toolCall.Function.Name} (id={toolCallId})");
                            }
                        }
                    }

                    if (chunk?.Done == true)
                    {
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

            result.IsSuccess = true;
            result.Response = fullResponse.ToString();

            if (collectedToolCalls.Count > 0)
            {
                result.ToolCalls = collectedToolCalls;
                _logger.LogInformation("[Ollama Chat] <<< TOOL CALLS (native): {Count}", collectedToolCalls.Count);
            }
            else
            {
                // Fallback: попробуем распарсить tool call из текста (для моделей без native tool calling)
                var parsedToolCalls = TryParseToolCallsFromText(result.Response);
                if (parsedToolCalls.Count > 0)
                {
                    result.ToolCalls = parsedToolCalls;
                    result.Response = ""; // Очищаем текст, т.к. это был tool call
                    _logger.LogInformation("[Ollama Chat] <<< TOOL CALLS (parsed from text): {Count}", parsedToolCalls.Count);
                    foreach (var tc in parsedToolCalls)
                    {
                        _logger.LogInformation("[Ollama Chat]   - {Name}: {Args}", tc.Name, tc.Arguments);
                        progress?.Report($"  [Tool Call parsed] {tc.Name}");
                    }
                }
                else
                {
                    _logger.LogInformation("[Ollama Chat] <<< ОТВЕТ ПОЛУЧЕН");
                }
            }

            _logger.LogInformation("[Ollama Chat] Response length: {Length} chars", result.Response?.Length ?? 0);
            _logger.LogInformation("[Ollama Chat] ========================================");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Operation cancelled by user";
            throw;
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Generation timed out";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Ollama Chat] Исключение: {Message}", ex.Message);
            result.IsSuccess = false;
            result.ErrorMessage = $"Error: {ex.Message}";
        }

        return result;
    }

    private OllamaChatMessage[] ConvertMessages(IEnumerable<LlmChatMessage> messages)
    {
        var result = new List<OllamaChatMessage>();

        foreach (var msg in messages)
        {
            var ollamaMsg = new OllamaChatMessage
            {
                Role = msg.Role,
                Content = msg.Content
            };

            // Если это assistant сообщение с tool calls
            if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                // Assistant message с tool calls должен иметь content = null или пустую строку
                ollamaMsg.Content = string.IsNullOrEmpty(msg.Content) ? null : msg.Content;
                ollamaMsg.ToolCalls = msg.ToolCalls.Select(tc => new OllamaToolCall
                {
                    Id = tc.Id, // Передаём ID для связи с tool result
                    Function = new OllamaFunctionCall
                    {
                        Name = tc.Name,
                        Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Arguments)
                    }
                }).ToArray();
            }
            // Если это tool результат - передаём tool_call_id для связи
            else if (msg.Role == "tool")
            {
                ollamaMsg.ToolCallId = msg.ToolCallId;
            }

            result.Add(ollamaMsg);
        }

        return result.ToArray();
    }

    private OllamaTool[] ConvertTools(IEnumerable<ToolDefinition> tools)
    {
        return tools.Select(t => new OllamaTool
        {
            Type = "function",
            Function = new OllamaToolFunction
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParametersSchema
            }
        }).ToArray();
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
    private List<LlmToolCall> TryParseToolCallsFromText(string? text)
    {
        var result = new List<LlmToolCall>();
        if (string.IsNullOrWhiteSpace(text)) return result;

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
                    _logger.LogInformation("[Ollama Chat] Parsed tool call from <tool_*> tag: {Tool}", toolCall.Name);
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

            // Паттерн 2: JSON в markdown code block (```json ... ``` или ``` ... ```)
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

            // Если нашли tool calls в code blocks - возвращаем
            if (result.Count > 0) return result;

            // Паттерн 3: Голый JSON (начинается с { и заканчивается })
            var trimmed = text.Trim();
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                var toolCall = TryParseToolCallJson(trimmed);
                if (toolCall != null)
                {
                    result.Add(toolCall);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[Ollama Chat] Failed to parse tool calls from text: {Error}", ex.Message);
        }

        return result;
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
        // Список известных инструментов будем проверять по паттерну имени
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

            _logger.LogInformation("[Ollama Chat] Parsed function call from text: {Tool}({Args})", toolName, argsJson);

            return new LlmToolCall
            {
                Id = $"call_{Guid.NewGuid().ToString("N")[..8]}",
                Name = toolName,
                Arguments = argsJson
            };
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("[Ollama Chat] Failed to parse function call args: {Error}", ex.Message);
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

    // DTOs for /api/generate
    private class OllamaRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; } = 2000;
    }

    private class OllamaStreamChunk
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    // DTOs for /api/chat
    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public OllamaChatMessage[] Messages { get; set; } = Array.Empty<OllamaChatMessage>();

        [JsonPropertyName("tools")]
        public OllamaTool[]? Tools { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public OllamaToolCall[]? ToolCalls { get; set; }

        /// <summary>
        /// ID вызова инструмента (для role="tool" - связь с assistant tool call)
        /// </summary>
        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }
    }

    private class OllamaChatStreamChunk
    {
        [JsonPropertyName("message")]
        public OllamaChatStreamMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    private class OllamaChatStreamMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public OllamaToolCall[]? ToolCalls { get; set; }
    }

    // Tool DTOs
    private class OllamaTool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public OllamaToolFunction? Function { get; set; }
    }

    private class OllamaToolFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; }
    }

    private class OllamaToolCall
    {
        /// <summary>
        /// ID вызова инструмента (для связи с tool result)
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public OllamaFunctionCall? Function { get; set; }
    }

    private class OllamaFunctionCall
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public object? Arguments { get; set; }
    }
}
