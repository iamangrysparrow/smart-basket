using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartBasket.Services.Shopping;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools.Handlers;

/// <summary>
/// Обработчик инструмента update_basket.
/// Добавляет, удаляет или изменяет товары в текущем списке покупок.
/// </summary>
public class UpdateBasketHandler : IToolHandler
{
    private readonly IShoppingSessionService _sessionService;
    private readonly ILogger<UpdateBasketHandler> _logger;

    public string Name => "update_basket";

    public UpdateBasketHandler(
        IShoppingSessionService sessionService,
        ILogger<UpdateBasketHandler> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition(
            Name,
            "Добавить, удалить или изменить товары в текущем списке покупок",
            new
            {
                type = "object",
                properties = new
                {
                    operations = new
                    {
                        type = "array",
                        description = "Список операций над корзиной",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                action = new
                                {
                                    type = "string",
                                    @enum = new[] { "add", "remove", "update" },
                                    description = "Тип действия: add - добавить товар, remove - удалить товар, update - изменить количество"
                                },
                                name = new
                                {
                                    type = "string",
                                    description = "Название товара"
                                },
                                quantity = new
                                {
                                    type = "number",
                                    description = "Количество (для add и update). По умолчанию 1"
                                },
                                unit = new
                                {
                                    type = "string",
                                    @enum = new[] { "шт", "кг", "л", "г", "мл" },
                                    description = "Единица измерения. По умолчанию 'шт'"
                                },
                                category = new
                                {
                                    type = "string",
                                    description = "Категория товара для группировки (например: Молочные продукты, Овощи, Хлеб)"
                                }
                            },
                            required = new[] { "action", "name" }
                        }
                    }
                },
                required = new[] { "operations" }
            });
    }

    public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[UpdateBasketHandler] Arguments: {Args}", argumentsJson);

        try
        {
            var args = JsonSerializer.Deserialize<UpdateBasketArgs>(argumentsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (args?.Operations == null || args.Operations.Count == 0)
            {
                return Task.FromResult(ToolResult.Error("Не указаны операции"));
            }

            // Проверяем наличие активной сессии
            if (_sessionService.CurrentSession == null)
            {
                _logger.LogWarning("[UpdateBasketHandler] No active session, operations will be logged but not applied");
            }

            var results = new List<string>();

            foreach (var op in args.Operations)
            {
                var action = op.Action?.ToLower();
                var name = op.Name;
                var quantity = op.Quantity ?? 1;
                var unit = op.Unit ?? "шт";
                var category = op.Category;

                switch (action)
                {
                    case "add":
                        if (_sessionService.CurrentSession != null)
                        {
                            _sessionService.AddItem(name, quantity, unit, category);
                        }
                        results.Add($"✓ Добавлено: {name} {quantity} {unit}" + (category != null ? $" [{category}]" : ""));
                        _logger.LogInformation("[UpdateBasketHandler] ADD: {Name} {Qty} {Unit} [{Category}]",
                            name, quantity, unit, category ?? "без категории");
                        break;

                    case "remove":
                        if (_sessionService.CurrentSession != null)
                        {
                            if (_sessionService.RemoveItem(name))
                            {
                                results.Add($"✓ Удалено: {name}");
                            }
                            else
                            {
                                results.Add($"✗ Не найдено: {name}");
                            }
                        }
                        else
                        {
                            results.Add($"✓ Удалено: {name}");
                        }
                        _logger.LogInformation("[UpdateBasketHandler] REMOVE: {Name}", name);
                        break;

                    case "update":
                        if (_sessionService.CurrentSession != null)
                        {
                            if (_sessionService.UpdateItem(name, quantity, unit))
                            {
                                results.Add($"✓ Изменено: {name} → {quantity} {unit}");
                            }
                            else
                            {
                                results.Add($"✗ Не найдено: {name}");
                            }
                        }
                        else
                        {
                            results.Add($"✓ Изменено: {name} → {quantity} {unit}");
                        }
                        _logger.LogInformation("[UpdateBasketHandler] UPDATE: {Name} → {Qty} {Unit}",
                            name, quantity, unit);
                        break;

                    default:
                        results.Add($"✗ Неизвестное действие: {action}");
                        _logger.LogWarning("[UpdateBasketHandler] Unknown action: {Action}", action);
                        break;
                }
            }

            _logger.LogInformation("[UpdateBasketHandler] Processed {Count} operations", args.Operations.Count);

            // Получаем текущий список товаров
            var items = _sessionService.GetCurrentItems();

            return Task.FromResult(ToolResult.Ok(new
            {
                success = true,
                results,
                message = $"Обработано операций: {args.Operations.Count}",
                item_count = items.Count,
                items = items.Select(i => new
                {
                    name = i.Name,
                    quantity = i.Quantity,
                    unit = i.Unit,
                    category = i.Category
                }).ToList()
            }));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[UpdateBasketHandler] JSON parsing error");
            return Task.FromResult(ToolResult.Error($"Ошибка разбора аргументов: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpdateBasketHandler] Execution error");
            return Task.FromResult(ToolResult.Error($"Ошибка выполнения: {ex.Message}"));
        }
    }
}
