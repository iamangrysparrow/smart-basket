namespace SmartBasket.Core.Configuration;

/// <summary>
/// Тип парсера чеков
/// </summary>
public enum ParserType
{
    /// <summary>
    /// Regex-парсер для структурированных форматов
    /// </summary>
    Regex,

    /// <summary>
    /// LLM-парсер для произвольного текста
    /// </summary>
    LLM
}
