using Microsoft.Extensions.Logging;
using SmartBasket.Services.Ollama;

namespace SmartBasket.Services.Parsing;

/// <summary>
/// Фабрика для выбора подходящего regex-парсера чеков
/// </summary>
public class ReceiptTextParserFactory
{
    private readonly IEnumerable<IReceiptTextParser> _parsers;
    private readonly ILogger<ReceiptTextParserFactory> _logger;

    public ReceiptTextParserFactory(
        IEnumerable<IReceiptTextParser> parsers,
        ILogger<ReceiptTextParserFactory> logger)
    {
        _parsers = parsers;
        _logger = logger;
    }

    /// <summary>
    /// Пытается распарсить чек с помощью подходящего regex-парсера
    /// </summary>
    /// <param name="receiptText">Очищенный текст чека</param>
    /// <param name="emailDate">Дата письма</param>
    /// <returns>Результат парсинга или null, если ни один парсер не подошёл</returns>
    public ParsedReceipt? TryParse(string receiptText, DateTime emailDate)
    {
        foreach (var parser in _parsers)
        {
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
    /// Возвращает список зарегистрированных парсеров
    /// </summary>
    public IEnumerable<string> GetRegisteredParsers()
    {
        return _parsers.Select(p => p.ShopName);
    }
}
