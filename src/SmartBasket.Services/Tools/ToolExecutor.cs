using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools;

/// <summary>
/// Исполнитель инструментов - роутит вызовы к конкретным обработчикам
/// </summary>
public class ToolExecutor : IToolExecutor
{
    private readonly Dictionary<string, IToolHandler> _handlers;

    public ToolExecutor(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(
            h => h.Name,
            h => h,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return _handlers.Values
            .Select(h => h.GetDefinition())
            .ToList();
    }

    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            return ToolResult.Error($"Unknown tool: {toolName}");
        }

        return await handler.ExecuteAsync(argumentsJson, cancellationToken);
    }

    public bool HasTool(string toolName)
    {
        return _handlers.ContainsKey(toolName);
    }
}
