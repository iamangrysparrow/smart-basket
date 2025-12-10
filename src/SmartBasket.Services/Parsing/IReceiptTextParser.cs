using SmartBasket.Services.Ollama;

namespace SmartBasket.Services.Parsing;

/// <summary>
/// Интерфейс для regex-парсеров чеков конкретных магазинов
/// </summary>
public interface IReceiptTextParser
{
    /// <summary>
    /// Название магазина, который обрабатывает этот парсер
    /// </summary>
    string ShopName { get; }

    /// <summary>
    /// Проверяет, может ли этот парсер обработать данный текст чека
    /// </summary>
    bool CanParse(string receiptText);

    /// <summary>
    /// Парсит текст чека и возвращает структурированные данные
    /// </summary>
    ParsedReceipt Parse(string receiptText, DateTime emailDate);
}
