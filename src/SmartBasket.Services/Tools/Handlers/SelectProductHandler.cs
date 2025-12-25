using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools.Handlers;

/// <summary>
/// Результат выбора товара AI
/// </summary>
public record ProductSelection(
    string DraftItemId,
    string? SelectedProductId,
    int Quantity,
    string Reasoning
);

/// <summary>
/// Обработчик инструмента select_product.
/// AI вызывает этот инструмент для выбора лучшего товара из результатов поиска.
/// </summary>
public class SelectProductHandler : IToolHandler
{
    private readonly ILogger<SelectProductHandler> _logger;

    /// <summary>
    /// Последний выбор AI. Используется для получения результата после вызова.
    /// </summary>
    public ProductSelection? LastSelection { get; private set; }

    public string Name => "select_product";

    public SelectProductHandler(ILogger<SelectProductHandler> logger)
    {
        _logger = logger;
    }

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition(
            Name,
            "Выбрать лучший товар из результатов поиска в магазине",
            new
            {
                type = "object",
                properties = new
                {
                    draft_item_id = new
                    {
                        type = "string",
                        description = "ID товара из списка покупок"
                    },
                    selected_product_id = new
                    {
                        type = "string",
                        nullable = true,
                        description = "ID выбранного товара из результатов поиска. null если ничего не подходит"
                    },
                    quantity = new
                    {
                        type = "integer",
                        description = "Количество единиц товара для покупки (с учётом фасовки)"
                    },
                    reasoning = new
                    {
                        type = "string",
                        description = "Краткое обоснование выбора (1-2 предложения)"
                    }
                },
                required = new[] { "draft_item_id", "quantity", "reasoning" }
            });
    }

    public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SelectProductHandler] Arguments: {Args}", argumentsJson);

        try
        {
            var args = JsonSerializer.Deserialize<SelectProductArgs>(argumentsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (args == null)
            {
                return Task.FromResult(ToolResult.Error("Не удалось разобрать аргументы"));
            }

            if (string.IsNullOrWhiteSpace(args.DraftItemId))
            {
                return Task.FromResult(ToolResult.Error("Не указан draft_item_id"));
            }

            // Сохраняем выбор
            LastSelection = new ProductSelection(
                args.DraftItemId,
                args.SelectedProductId,
                args.Quantity ?? 1,
                args.Reasoning ?? "Не указано"
            );

            _logger.LogInformation(
                "[SelectProductHandler] Selection: DraftItem={DraftItemId}, Product={ProductId}, Qty={Qty}, Reason={Reason}",
                LastSelection.DraftItemId,
                LastSelection.SelectedProductId ?? "null",
                LastSelection.Quantity,
                LastSelection.Reasoning);

            var responseMessage = LastSelection.SelectedProductId != null
                ? $"Выбран товар: {LastSelection.SelectedProductId}, количество: {LastSelection.Quantity}"
                : $"Подходящий товар не найден: {LastSelection.Reasoning}";

            return Task.FromResult(ToolResult.Ok(new
            {
                success = true,
                message = responseMessage,
                draft_item_id = LastSelection.DraftItemId,
                selected_product_id = LastSelection.SelectedProductId,
                quantity = LastSelection.Quantity,
                reasoning = LastSelection.Reasoning
            }));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[SelectProductHandler] JSON parsing error");
            return Task.FromResult(ToolResult.Error($"Ошибка разбора аргументов: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SelectProductHandler] Execution error");
            return Task.FromResult(ToolResult.Error($"Ошибка выполнения: {ex.Message}"));
        }
    }

    /// <summary>
    /// Очистить последний выбор
    /// </summary>
    public void ClearLastSelection()
    {
        LastSelection = null;
    }
}

/// <summary>
/// Аргументы инструмента select_product
/// </summary>
internal class SelectProductArgs
{
    public string? DraftItemId { get; set; }
    public string? SelectedProductId { get; set; }
    public int? Quantity { get; set; }
    public string? Reasoning { get; set; }
}
