using Microsoft.Extensions.Logging;
using SmartBasket.Services.Ollama;

namespace SmartBasket.Services.Parsing;

/// <summary>
/// Фабрика для выбора подходящего парсера чеков.
/// Поддерживает как regex-парсеры, так и LLM-парсер.
/// </summary>
public class ReceiptTextParserFactory
{
    private readonly IEnumerable<IReceiptTextParser> _parsers;
    private readonly LlmUniversalParser _llmParser;
    private readonly ILogger<ReceiptTextParserFactory> _logger;

    public ReceiptTextParserFactory(
        IEnumerable<IReceiptTextParser> parsers,
        LlmUniversalParser llmParser,
        ILogger<ReceiptTextParserFactory> logger)
    {
        _parsers = parsers;
        _llmParser = llmParser ?? throw new ArgumentNullException(nameof(llmParser));
        _logger = logger;
    }

    /// <summary>
    /// Получить парсер по имени.
    /// </summary>
    /// <param name="parserName">Имя парсера (ShopName или "LlmUniversalParser" / "Auto")</param>
    /// <returns>Парсер или null, если не найден</returns>
    public IReceiptTextParser? GetParser(string parserName)
    {
        if (string.IsNullOrWhiteSpace(parserName))
            return null;

        // Проверяем LLM парсер
        if (parserName.Equals(LlmUniversalParser.ParserName, StringComparison.OrdinalIgnoreCase) ||
            parserName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Returning LlmUniversalParser for name: {ParserName}", parserName);
            return _llmParser;
        }

        // Ищем среди regex-парсеров
        var parser = _parsers.FirstOrDefault(p =>
            p.ShopName.Equals(parserName, StringComparison.OrdinalIgnoreCase));

        if (parser != null)
        {
            _logger.LogDebug("Found parser {ParserName}", parser.ShopName);
            return parser;
        }

        _logger.LogWarning("Parser not found: {ParserName}", parserName);
        return null;
    }

    /// <summary>
    /// Получить парсер по имени или LLM парсер по умолчанию.
    /// </summary>
    /// <param name="parserName">Имя парсера</param>
    /// <returns>Запрошенный парсер или LlmUniversalParser как fallback</returns>
    public IReceiptTextParser GetParserOrDefault(string? parserName)
    {
        if (string.IsNullOrWhiteSpace(parserName))
        {
            _logger.LogDebug("No parser name specified, returning LlmUniversalParser");
            return _llmParser;
        }

        var parser = GetParser(parserName);
        if (parser != null)
            return parser;

        _logger.LogWarning("Parser {ParserName} not found, falling back to LlmUniversalParser", parserName);
        return _llmParser;
    }

    /// <summary>
    /// Получить LLM парсер напрямую.
    /// </summary>
    public LlmUniversalParser GetLlmParser() => _llmParser;

    /// <summary>
    /// Пытается распарсить чек с помощью подходящего regex-парсера.
    /// Не включает LLM парсер в перебор.
    /// </summary>
    /// <param name="receiptText">Очищенный текст чека</param>
    /// <param name="emailDate">Дата письма</param>
    /// <returns>Результат парсинга или null, если ни один regex-парсер не подошёл</returns>
    public ParsedReceipt? TryParseWithRegex(string receiptText, DateTime emailDate)
    {
        foreach (var parser in _parsers)
        {
            // Пропускаем LLM парсер при переборе regex-парсеров
            if (parser is LlmUniversalParser)
                continue;

            if (parser.CanParse(receiptText))
            {
                _logger.LogInformation("Using {ParserName} for receipt parsing", parser.ShopName);

                var result = parser.Parse(receiptText, emailDate);

                if (result.IsSuccess)
                {
                    _logger.LogInformation(
                        "Successfully parsed {ItemCount} items from {Shop}",
                        result.Items.Count,
                        result.Shop);
                    return result;
                }

                _logger.LogWarning(
                    "Parser {ParserName} matched but failed: {Message}",
                    parser.ShopName,
                    result.Message);
            }
        }

        _logger.LogDebug("No regex parser matched the receipt text");
        return null;
    }

    /// <summary>
    /// Пытается распарсить чек с помощью подходящего regex-парсера (legacy метод).
    /// </summary>
    [Obsolete("Use TryParseWithRegex instead")]
    public ParsedReceipt? TryParse(string receiptText, DateTime emailDate)
    {
        return TryParseWithRegex(receiptText, emailDate);
    }

    /// <summary>
    /// Возвращает список зарегистрированных парсеров (включая LLM).
    /// </summary>
    public IEnumerable<string> GetRegisteredParsers()
    {
        var names = _parsers
            .Where(p => p is not LlmUniversalParser)
            .Select(p => p.ShopName)
            .ToList();

        names.Add(LlmUniversalParser.ParserName);
        return names;
    }

    /// <summary>
    /// Возвращает список только regex-парсеров.
    /// </summary>
    public IEnumerable<string> GetRegexParsers()
    {
        return _parsers
            .Where(p => p is not LlmUniversalParser)
            .Select(p => p.ShopName);
    }
}
