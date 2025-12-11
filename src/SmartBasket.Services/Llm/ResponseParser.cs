using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Результат парсинга JSON из ответа LLM
/// </summary>
public class JsonExtractionResult
{
    public bool IsSuccess { get; set; }
    public string? Json { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExtractionMethod { get; set; }
}

/// <summary>
/// Результат парсинга с десериализацией
/// </summary>
public class ParseResult<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public string? RawJson { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExtractionMethod { get; set; }
}

/// <summary>
/// Интерфейс для унифицированного парсинга ответов LLM
/// </summary>
public interface IResponseParser
{
    /// <summary>
    /// Извлечь JSON объект из ответа LLM
    /// </summary>
    JsonExtractionResult ExtractJsonObject(string response, IProgress<string>? progress = null);

    /// <summary>
    /// Извлечь JSON массив из ответа LLM
    /// </summary>
    JsonExtractionResult ExtractJsonArray(string response, IProgress<string>? progress = null);

    /// <summary>
    /// Извлечь и десериализовать JSON объект
    /// </summary>
    ParseResult<T> ParseJsonObject<T>(string response, IProgress<string>? progress = null) where T : class;

    /// <summary>
    /// Извлечь и десериализовать JSON массив
    /// </summary>
    ParseResult<List<T>> ParseJsonArray<T>(string response, IProgress<string>? progress = null);
}

/// <summary>
/// Унифицированный парсер ответов LLM.
/// Поддерживает:
/// - Markdown code blocks (```json...```)
/// - Chain-of-thought (извлечение JSON из текста с рассуждениями)
/// - Несколько стратегий извлечения с fallback
/// </summary>
public class ResponseParser : IResponseParser
{
    private readonly ILogger<ResponseParser> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ResponseParser(ILogger<ResponseParser> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    public JsonExtractionResult ExtractJsonObject(string response, IProgress<string>? progress = null)
    {
        return ExtractJson(response, JsonType.Object, progress);
    }

    public JsonExtractionResult ExtractJsonArray(string response, IProgress<string>? progress = null)
    {
        return ExtractJson(response, JsonType.Array, progress);
    }

    public ParseResult<T> ParseJsonObject<T>(string response, IProgress<string>? progress = null) where T : class
    {
        var extraction = ExtractJsonObject(response, progress);
        return DeserializeResult<T>(extraction, progress);
    }

    public ParseResult<List<T>> ParseJsonArray<T>(string response, IProgress<string>? progress = null)
    {
        var extraction = ExtractJsonArray(response, progress);
        return DeserializeResult<List<T>>(extraction, progress);
    }

    private ParseResult<T> DeserializeResult<T>(JsonExtractionResult extraction, IProgress<string>? progress) where T : class
    {
        var result = new ParseResult<T>
        {
            RawJson = extraction.Json,
            ExtractionMethod = extraction.ExtractionMethod
        };

        if (!extraction.IsSuccess || string.IsNullOrEmpty(extraction.Json))
        {
            result.IsSuccess = false;
            result.ErrorMessage = extraction.ErrorMessage ?? "Failed to extract JSON";
            return result;
        }

        try
        {
            result.Data = JsonSerializer.Deserialize<T>(extraction.Json, _jsonOptions);
            result.IsSuccess = result.Data != null;
            if (!result.IsSuccess)
            {
                result.ErrorMessage = "Deserialization returned null";
            }
        }
        catch (JsonException ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"JSON deserialization failed: {ex.Message}";
            progress?.Report($"  [ResponseParser] Deserialization error: {ex.Message}");
            _logger.LogWarning(ex, "JSON deserialization failed for: {Json}", extraction.Json?.Substring(0, Math.Min(200, extraction.Json?.Length ?? 0)));
        }

        return result;
    }

    private enum JsonType { Object, Array }

    private JsonExtractionResult ExtractJson(string response, JsonType type, IProgress<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return new JsonExtractionResult
            {
                IsSuccess = false,
                ErrorMessage = "Empty response"
            };
        }

        var openBracket = type == JsonType.Object ? '{' : '[';
        var closeBracket = type == JsonType.Object ? '}' : ']';
        var typeName = type == JsonType.Object ? "object" : "array";

        progress?.Report($"  [ResponseParser] Extracting JSON {typeName} from {response.Length} chars...");

        // Strategy 1: Markdown code block with json tag
        var json = TryExtractFromMarkdownCodeBlock(response, type);
        if (json != null && IsValidJson(json, type))
        {
            progress?.Report($"  [ResponseParser] Found JSON in markdown code block ({json.Length} chars)");
            return new JsonExtractionResult
            {
                IsSuccess = true,
                Json = json,
                ExtractionMethod = "markdown_code_block"
            };
        }

        // Strategy 2: Markdown code block without language tag
        json = TryExtractFromGenericCodeBlock(response, type);
        if (json != null && IsValidJson(json, type))
        {
            progress?.Report($"  [ResponseParser] Found JSON in generic code block ({json.Length} chars)");
            return new JsonExtractionResult
            {
                IsSuccess = true,
                Json = json,
                ExtractionMethod = "generic_code_block"
            };
        }

        // Strategy 3: Direct JSON extraction with bracket matching
        json = TryExtractWithBracketMatching(response, openBracket, closeBracket);
        if (json != null && IsValidJson(json, type))
        {
            progress?.Report($"  [ResponseParser] Found JSON with bracket matching ({json.Length} chars)");
            return new JsonExtractionResult
            {
                IsSuccess = true,
                Json = json,
                ExtractionMethod = "bracket_matching"
            };
        }

        // Strategy 4: Regex fallback (greedy)
        json = TryExtractWithRegex(response, type);
        if (json != null && IsValidJson(json, type))
        {
            progress?.Report($"  [ResponseParser] Found JSON with regex ({json.Length} chars)");
            return new JsonExtractionResult
            {
                IsSuccess = true,
                Json = json,
                ExtractionMethod = "regex"
            };
        }

        // Strategy 5: Try to find JSON after common prefixes
        json = TryExtractAfterPrefix(response, type);
        if (json != null && IsValidJson(json, type))
        {
            progress?.Report($"  [ResponseParser] Found JSON after prefix ({json.Length} chars)");
            return new JsonExtractionResult
            {
                IsSuccess = true,
                Json = json,
                ExtractionMethod = "after_prefix"
            };
        }

        progress?.Report($"  [ResponseParser] No valid JSON {typeName} found in response");
        _logger.LogWarning("Failed to extract JSON {Type} from response: {Preview}...",
            typeName, response.Substring(0, Math.Min(300, response.Length)));

        return new JsonExtractionResult
        {
            IsSuccess = false,
            ErrorMessage = $"No valid JSON {typeName} found in response"
        };
    }

    /// <summary>
    /// Extract from ```json ... ``` block
    /// </summary>
    private static string? TryExtractFromMarkdownCodeBlock(string response, JsonType type)
    {
        // Match ```json ... ``` or ```JSON ... ```
        var match = Regex.Match(response, @"```(?:json|JSON)\s*([\s\S]*?)```", RegexOptions.Multiline);
        if (match.Success)
        {
            var content = match.Groups[1].Value.Trim();
            if (StartsWithCorrectBracket(content, type))
            {
                return content;
            }
        }
        return null;
    }

    /// <summary>
    /// Extract from ``` ... ``` block (no language tag)
    /// </summary>
    private static string? TryExtractFromGenericCodeBlock(string response, JsonType type)
    {
        var match = Regex.Match(response, @"```\s*([\s\S]*?)```", RegexOptions.Multiline);
        if (match.Success)
        {
            var content = match.Groups[1].Value.Trim();
            if (StartsWithCorrectBracket(content, type))
            {
                return content;
            }
        }
        return null;
    }

    /// <summary>
    /// Extract JSON using bracket matching (handles nested structures)
    /// </summary>
    private static string? TryExtractWithBracketMatching(string response, char openBracket, char closeBracket)
    {
        var startIndex = response.IndexOf(openBracket);
        if (startIndex < 0) return null;

        var depth = 0;
        var inString = false;
        var escapeNext = false;

        for (var i = startIndex; i < response.Length; i++)
        {
            var c = response[i];

            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escapeNext = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == openBracket)
            {
                depth++;
            }
            else if (c == closeBracket)
            {
                depth--;
                if (depth == 0)
                {
                    return response.Substring(startIndex, i - startIndex + 1);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extract with simple regex (fallback)
    /// </summary>
    private static string? TryExtractWithRegex(string response, JsonType type)
    {
        var pattern = type == JsonType.Object
            ? @"\{[\s\S]*\}"
            : @"\[[\s\S]*\]";

        var match = Regex.Match(response, pattern);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Try to find JSON after common prefixes like "JSON:", "Result:", etc.
    /// </summary>
    private static string? TryExtractAfterPrefix(string response, JsonType type)
    {
        var prefixes = new[]
        {
            "JSON:",
            "json:",
            "Result:",
            "result:",
            "Output:",
            "output:",
            "Ответ:",
            "ответ:",
            "Результат:",
            "результат:"
        };

        foreach (var prefix in prefixes)
        {
            var idx = response.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var afterPrefix = response.Substring(idx + prefix.Length).TrimStart();
                var openBracket = type == JsonType.Object ? '{' : '[';
                var closeBracket = type == JsonType.Object ? '}' : ']';

                var json = TryExtractWithBracketMatching(afterPrefix, openBracket, closeBracket);
                if (json != null) return json;
            }
        }

        return null;
    }

    private static bool StartsWithCorrectBracket(string content, JsonType type)
    {
        var expectedBracket = type == JsonType.Object ? '{' : '[';
        return content.Length > 0 && content[0] == expectedBracket;
    }

    private bool IsValidJson(string json, JsonType type)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var valueKind = doc.RootElement.ValueKind;

            return type switch
            {
                JsonType.Object => valueKind == JsonValueKind.Object,
                JsonType.Array => valueKind == JsonValueKind.Array,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
