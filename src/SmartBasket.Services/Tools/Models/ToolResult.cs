using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmartBasket.Services.Tools.Models;

/// <summary>
/// Результат выполнения инструмента
/// </summary>
public record ToolResult(
    bool Success,
    string JsonData,
    string? ErrorMessage = null
)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Не кодировать кириллицу
    };

    public static ToolResult Ok(object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        return new ToolResult(true, json);
    }

    public static ToolResult Error(string message)
        => new(false, $"{{\"error\": \"{message}\"}}", message);
}
