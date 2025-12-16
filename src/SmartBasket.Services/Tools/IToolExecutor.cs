using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools;

/// <summary>
/// Исполнитель инструментов
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Получить определения всех доступных инструментов
    /// </summary>
    IReadOnlyList<ToolDefinition> GetToolDefinitions();

    /// <summary>
    /// Выполнить инструмент по имени
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверить существование инструмента
    /// </summary>
    bool HasTool(string toolName);
}
