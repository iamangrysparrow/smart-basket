using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools.Handlers;

/// <summary>
/// Обработчик инструмента get_current_datetime
/// Возвращает текущую дату и время
/// </summary>
public class GetCurrentDateTimeHandler : IToolHandler
{
    public string Name => "get_current_datetime";

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition(
            Name: Name,
            Description: "Получить текущую дату и время. " +
                         "Используй когда нужно знать точное время.",
            ParametersSchema: new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        );
    }

    public Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;

        return Task.FromResult(ToolResult.Ok(new
        {
            datetime = now.ToString("yyyy-MM-dd HH:mm:ss"),
            date = now.ToString("yyyy-MM-dd"),
            time = now.ToString("HH:mm:ss"),
            day_of_week = now.DayOfWeek.ToString(),
            day_of_week_ru = GetRussianDayOfWeek(now.DayOfWeek)
        }));
    }

    private static string GetRussianDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "понедельник",
        DayOfWeek.Tuesday => "вторник",
        DayOfWeek.Wednesday => "среда",
        DayOfWeek.Thursday => "четверг",
        DayOfWeek.Friday => "пятница",
        DayOfWeek.Saturday => "суббота",
        DayOfWeek.Sunday => "воскресенье",
        _ => day.ToString()
    };
}
