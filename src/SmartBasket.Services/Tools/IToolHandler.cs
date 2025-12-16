using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools;

/// <summary>
/// Интерфейс обработчика инструмента
/// </summary>
public interface IToolHandler
{
    /// <summary>
    /// Имя инструмента
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Определение инструмента для LLM (JSON Schema)
    /// </summary>
    ToolDefinition GetDefinition();

    /// <summary>
    /// Выполнить инструмент
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default);
}
