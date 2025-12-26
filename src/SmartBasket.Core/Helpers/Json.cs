using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartBasket.Core.Helpers;

/// <summary>
/// ГЛОБАЛЬНЫЕ настройки JSON сериализации для ВСЕГО проекта.
///
/// ВАЖНО: Всегда используй Json.Serialize() вместо JsonSerializer.Serialize()!
/// Это гарантирует корректное отображение кириллицы (без \uXXXX).
/// </summary>
public static class Json
{
    /// <summary>
    /// Дефолтные опции: кириллица без экранирования, null игнорируется
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Для логов и отладки: pretty print + кириллица без экранирования
    /// </summary>
    public static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Для парсинга: case insensitive
    /// </summary>
    public static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Сериализация с дефолтными опциями (кириллица читаема!)
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, DefaultOptions);
    }

    /// <summary>
    /// Сериализация с pretty print (для логов)
    /// </summary>
    public static string SerializePretty<T>(T value)
    {
        return JsonSerializer.Serialize(value, PrettyOptions);
    }

    /// <summary>
    /// Десериализация с дефолтными опциями
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, ParseOptions);
    }

    /// <summary>
    /// Десериализация с дефолтными опциями (non-nullable)
    /// </summary>
    public static T DeserializeRequired<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, ParseOptions)
            ?? throw new JsonException($"Failed to deserialize JSON to {typeof(T).Name}");
    }
}
