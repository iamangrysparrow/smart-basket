using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Общие настройки JSON сериализации для всех LLM провайдеров.
/// Кириллица и спецсимволы НЕ экранируются как \uXXXX - читаемо в логах!
/// </summary>
public static class LlmJsonOptions
{
    /// <summary>
    /// Для логирования и отправки запросов - pretty print, кириллица без экранирования
    /// </summary>
    public static readonly JsonSerializerOptions ForLogging = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Для парсинга ответов - case insensitive property names
    /// </summary>
    public static readonly JsonSerializerOptions ForParsing = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Минимальные настройки для компактного JSON без pretty print
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
