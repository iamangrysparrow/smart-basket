using Microsoft.Extensions.Logging;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools;

/// <summary>
/// Исполнитель инструментов - роутит вызовы к конкретным обработчикам
/// </summary>
public class ToolExecutor : IToolExecutor
{
    private readonly Dictionary<string, IToolHandler> _handlers;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(IEnumerable<IToolHandler> handlers, ILogger<ToolExecutor> logger)
    {
        _logger = logger;
        _handlers = handlers.ToDictionary(
            h => h.Name,
            h => h,
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("[ToolExecutor] Initialized with {Count} handlers: {Names}",
            _handlers.Count, string.Join(", ", _handlers.Keys));
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
        _logger.LogInformation("[ToolExecutor] ExecuteAsync called: tool={Tool}, args={ArgsLength} chars",
            toolName, argumentsJson?.Length ?? 0);

        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            _logger.LogError("[ToolExecutor] Unknown tool: {Tool}. Available: {Available}",
                toolName, string.Join(", ", _handlers.Keys));
            return ToolResult.Error($"Unknown tool: {toolName}");
        }

        _logger.LogInformation("[ToolExecutor] Found handler: {Handler} for tool {Tool}",
            handler.GetType().Name, toolName);

        var result = await handler.ExecuteAsync(argumentsJson, cancellationToken);

        _logger.LogInformation("[ToolExecutor] Handler returned: Success={Success}, ResultLength={Length}",
            result.Success, result.JsonData?.Length ?? 0);

        return result;
    }

    public bool HasTool(string toolName)
    {
        return _handlers.ContainsKey(toolName);
    }
}
