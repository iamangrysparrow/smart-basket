using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools.Handlers;

/// <summary>
/// Обработчик инструмента get_current_date
/// Возвращает текущую дату
/// </summary>
public class GetCurrentDateHandler : IToolHandler
{
    public string Name => "get_current_date";

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition(
            Name: Name,
            Description: "Получить текущую дату в формате YYYY-MM-DD. " +
                         "Используй когда нужно узнать сегодняшнюю дату для расчёта периодов.",
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
        var today = DateTime.Now.Date;

        return Task.FromResult(ToolResult.Ok(new
        {
            date = today.ToString("yyyy-MM-dd"),
            day_of_week = today.DayOfWeek.ToString(),
            day_of_week_ru = GetRussianDayOfWeek(today.DayOfWeek)
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
