using SmartBasket.Services.Llm;

namespace SmartBasket.Services.Parsing;

/// <summary>
/// Интерфейс для парсеров чеков
/// </summary>
public interface IReceiptTextParser
{
    /// <summary>
    /// Уникальный идентификатор парсера для использования в конфигурации
    /// (например "InstamartParser", "LlmUniversalParser")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Список магазинов, которые поддерживает этот парсер.
    /// Используется для будущего авто-определения парсера по магазину.
    /// "*" означает универсальный парсер.
    /// </summary>
    IReadOnlyList<string> SupportedShops { get; }

    /// <summary>
    /// Проверяет, может ли этот парсер обработать данный текст чека.
    /// Используется в режиме "Auto" для автоматического выбора парсера.
    /// </summary>
    bool CanParse(string receiptText);

    /// <summary>
    /// Парсит текст чека и возвращает структурированные данные
    /// </summary>
    ParsedReceipt Parse(string receiptText, DateTime emailDate);
}
