using System.Text.Json;
using SmartBasket.Core.Helpers;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Настройки JSON для LLM провайдеров.
/// Использует глобальные опции из SmartBasket.Core.Helpers.Json
/// </summary>
public static class LlmJsonOptions
{
    /// <summary>
    /// Для логирования - pretty print, кириллица без экранирования
    /// </summary>
    public static JsonSerializerOptions ForLogging => Json.PrettyOptions;

    /// <summary>
    /// Для парсинга ответов - case insensitive
    /// </summary>
    public static JsonSerializerOptions ForParsing => Json.ParseOptions;

    /// <summary>
    /// Компактный JSON без pretty print
    /// </summary>
    public static JsonSerializerOptions Compact => Json.DefaultOptions;
}
