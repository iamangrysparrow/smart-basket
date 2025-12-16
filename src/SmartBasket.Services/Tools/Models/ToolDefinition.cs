namespace SmartBasket.Services.Tools.Models;

/// <summary>
/// Определение инструмента для LLM
/// </summary>
public record ToolDefinition(
    string Name,
    string Description,
    object ParametersSchema
);
