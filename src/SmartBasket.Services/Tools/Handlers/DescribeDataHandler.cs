using Microsoft.EntityFrameworkCore;
using SmartBasket.Data;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Tools.Handlers;

/// <summary>
/// Обработчик инструмента describe_data
/// Возвращает схему БД и примеры данных с богатыми связями
/// </summary>
public class DescribeDataHandler : IToolHandler
{
    private readonly SmartBasketDbContext _db;

    public DescribeDataHandler(SmartBasketDbContext db)
    {
        _db = db;
    }

    public string Name => "describe_data";

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition(
            Name: Name,
            Description: "Получить схему базы данных и примеры данных с полными связями. " +
                         "Вызови ОДИН РАЗ в начале диалога чтобы понять структуру данных." +
                         "Функция вернет описание и ПРИМЕР данных. Внимание! Данные ПРИМЕРНЫЕ! Для получения реальных данных необходимо использовать инструмент query!",
            ParametersSchema: new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        );
    }

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var schema = GetSchema();
            var statistics = await GetStatisticsAsync(cancellationToken);
            var examples = await GetExamplesAsync(cancellationToken);

            var result = new
            {
                schema,
                statistics,
                examples
            };

            return ToolResult.Ok(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Ошибка получения схемы: {ex.Message}");
        }
    }

    private object GetSchema()
    {
        return new
        {
            note = "Все имена таблиц и колонок в PascalCase",
            tables = new[]
            {
                new
                {
                    name = "Receipts",
                    description = "Чеки из магазинов",
                    columns = new[]
                    {
                        new { name = "Id", type = "uuid", description = "PK" },
                        new { name = "ReceiptDate", type = "timestamp", description = "Дата чека" },
                        new { name = "Shop", type = "string", description = "Название магазина" },
                        new { name = "Total", type = "decimal", description = "Сумма чека" },
                        new { name = "ReceiptNumber", type = "string", description = "Номер чека" },
                        new { name = "EmailId", type = "string", description = "ID письма (дедупликация)" },
                        new { name = "Status", type = "string", description = "Статус: Parsed, Archived, Error" },
                        new { name = "CreatedAt", type = "timestamp", description = "Дата создания записи" },
                        new { name = "UpdatedAt", type = "timestamp", description = "Дата обновления" }
                    }
                },
                new
                {
                    name = "ReceiptItems",
                    description = "Товарные позиции в чеках",
                    columns = new[]
                    {
                        new { name = "Id", type = "uuid", description = "PK" },
                        new { name = "ReceiptId", type = "uuid", description = "FK → Receipts.Id" },
                        new { name = "ItemId", type = "uuid", description = "FK → Items.Id" },
                        new { name = "Quantity", type = "decimal", description = "Количество" },
                        new { name = "Price", type = "decimal", description = "Цена за единицу" },
                        new { name = "Amount", type = "decimal", description = "Сумма позиции" },
                        new { name = "CreatedAt", type = "timestamp", description = "Дата создания записи" },
                        new { name = "UpdatedAt", type = "timestamp", description = "Дата обновления" }
                    }
                },
                new
                {
                    name = "Items",
                    description = "Справочник уникальных товаров",
                    columns = new[]
                    {
                        new { name = "Id", type = "uuid", description = "PK" },
                        new { name = "Name", type = "string", description = "Название товара" },
                        new { name = "ProductId", type = "uuid", description = "FK → Products.Id (категория)" },
                        new { name = "Shop", type = "string", description = "Магазин" },
                        new { name = "UnitOfMeasure", type = "string", description = "Единица: шт, кг, л" },
                        new { name = "UnitQuantity", type = "decimal", description = "Количество в единице" },
                        new { name = "CreatedAt", type = "timestamp", description = "Дата создания записи" },
                        new { name = "UpdatedAt", type = "timestamp", description = "Дата обновления" }
                    }
                },
                new
                {
                    name = "Products",
                    description = "Категории товаров (иерархия)",
                    columns = new[]
                    {
                        new { name = "Id", type = "uuid", description = "PK" },
                        new { name = "Name", type = "string", description = "Название категории" },
                        new { name = "ParentId", type = "uuid?", description = "FK → Products.Id (родитель), null = корень" },
                        new { name = "CreatedAt", type = "timestamp", description = "Дата создания записи" },
                        new { name = "UpdatedAt", type = "timestamp", description = "Дата обновления" }
                    }
                },
                new
                {
                    name = "Labels",
                    description = "Пользовательские метки",
                    columns = new[]
                    {
                        new { name = "Id", type = "uuid", description = "PK" },
                        new { name = "Name", type = "string", description = "Название метки" },
                        new { name = "Color", type = "string", description = "Цвет метки в HEX" },
                        new { name = "CreatedAt", type = "timestamp", description = "Дата создания записи" },
                        new { name = "UpdatedAt", type = "timestamp", description = "Дата обновления" }
                    }
                },
                new
                {
                    name = "ItemLabels",
                    description = "Связь товаров с метками (M:N)",
                    columns = new[]
                    {
                        new { name = "ItemId", type = "uuid", description = "FK → Items.Id" },
                        new { name = "LabelId", type = "uuid", description = "FK → Labels.Id" }
                    }
                },
                new
                {
                    name = "ProductLabels",
                    description = "Связь категорий с метками (M:N)",
                    columns = new[]
                    {
                        new { name = "ProductId", type = "uuid", description = "FK → Products.Id" },
                        new { name = "LabelId", type = "uuid", description = "FK → Labels.Id" }
                    }
                }
            },
            relationships = new[]
            {
                "Receipts.Id ← ReceiptItems.ReceiptId (1:N) — чек содержит позиции",
                "Items.Id ← ReceiptItems.ItemId (1:N) — товар в позициях",
                "Products.Id ← Items.ProductId (1:N) — категория товара",
                "Products.Id ← Products.ParentId (self-reference) — иерархия категорий",
                "Items ↔ Labels через ItemLabels (M:N) — метки товаров",
                "Products ↔ Labels через ProductLabels (M:N) — метки категорий"
            },
            join_examples = new[]
            {
                "Товары с суммами: ReceiptItems JOIN Items ON Items.Id = ReceiptItems.ItemId",
                "Чеки с товарами: Receipts JOIN ReceiptItems ON Receipts.Id = ReceiptItems.ReceiptId",
                "Товары по категориям: Items JOIN Products ON Products.Id = Items.ProductId"
            }
        };
    }

    private async Task<object> GetStatisticsAsync(CancellationToken ct)
    {
        var totalReceipts = await _db.Receipts.CountAsync(ct);
        var totalItems = await _db.Items.CountAsync(ct);
        var totalProducts = await _db.Products.CountAsync(ct);
        var totalLabels = await _db.Labels.CountAsync(ct);

        string dateRange = "нет данных";
        if (totalReceipts > 0)
        {
            var minDate = await _db.Receipts.MinAsync(r => r.ReceiptDate, ct);
            var maxDate = await _db.Receipts.MaxAsync(r => r.ReceiptDate, ct);
            dateRange = $"{minDate:yyyy-MM-dd} — {maxDate:yyyy-MM-dd}";
        }

        return new
        {
            total_receipts = totalReceipts,
            total_items = totalItems,
            total_products = totalProducts,
            total_labels = totalLabels,
            date_range = dateRange
        };
    }

    private async Task<List<object>> GetExamplesAsync(CancellationToken ct)
    {
        var examples = new List<object>();

        // Находим товары с богатыми связями:
        // 1. Есть метки (item_labels)
        // 2. Есть категория (product_id != null)
        // 3. Есть в нескольких чеках (receipt_items)
        var candidateItems = await _db.Items
            .Where(i => i.ProductId != Guid.Empty)
            .Where(i => i.ItemLabels.Any()) // Только с метками
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.ProductId,
                LabelsCount = i.ItemLabels.Count,
                ReceiptsCount = i.ReceiptItems.Select(ri => ri.ReceiptId).Distinct().Count()
            })
            .OrderByDescending(i => i.ReceiptsCount)
            .ThenByDescending(i => i.LabelsCount)
            .Take(3)
            .ToListAsync(ct);

        // Если нет товаров с метками — берём просто товары с категориями
        if (candidateItems.Count == 0)
        {
            candidateItems = await _db.Items
                .Where(i => i.ProductId != Guid.Empty)
                .Select(i => new
                {
                    i.Id,
                    i.Name,
                    i.ProductId,
                    LabelsCount = i.ItemLabels.Count,
                    ReceiptsCount = i.ReceiptItems.Select(ri => ri.ReceiptId).Distinct().Count()
                })
                .OrderByDescending(i => i.ReceiptsCount)
                .Take(3)
                .ToListAsync(ct);
        }

        // Для каждого товара строим полный пример
        foreach (var candidate in candidateItems)
        {
            var example = await BuildItemExampleAsync(candidate.Id, ct);
            if (example != null)
            {
                examples.Add(example);
            }
        }

        return examples;
    }

    private async Task<object?> BuildItemExampleAsync(Guid itemId, CancellationToken ct)
    {
        var item = await _db.Items
            .Where(i => i.Id == itemId)
            .Select(i => new { i.Id, i.Name, i.ProductId })
            .FirstOrDefaultAsync(ct);

        if (item == null) return null;

        // Получаем иерархию категорий
        var productHierarchy = await GetProductHierarchyAsync(item.ProductId, ct);

        // Получаем метки
        var labels = await _db.ItemLabels
            .Where(il => il.ItemId == itemId)
            .Select(il => new
            {
                id = il.Label!.Id,
                name = il.Label.Name,
                color = il.Label.Color
            })
            .ToListAsync(ct);

        // Получаем покупки (receipt_items с информацией о чеке)
        var receiptItems = await _db.ReceiptItems
            .Where(ri => ri.ItemId == itemId)
            .OrderByDescending(ri => ri.Receipt!.ReceiptDate)
            .Take(5)
            .Select(ri => new
            {
                receipt_id = ri.ReceiptId,
                receipt_date = ri.Receipt!.ReceiptDate.ToString("yyyy-MM-dd"),
                receipt_shop = ri.Receipt.Shop,
                quantity = ri.Quantity,
                price = ri.Price ?? 0,
                amount = ri.Amount ?? 0
            })
            .ToListAsync(ct);

        return new
        {
            description = "Товар с полной цепочкой связей",
            item = new { id = item.Id, name = item.Name },
            product_hierarchy = productHierarchy,
            labels,
            receipt_items = receiptItems
        };
    }

    private async Task<List<object>> GetProductHierarchyAsync(Guid productId, CancellationToken ct)
    {
        var hierarchy = new List<object>();

        if (productId == Guid.Empty) return hierarchy;

        // Получаем продукт и его категорию
        var product = await _db.Products
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Name, p.CategoryId })
            .FirstOrDefaultAsync(ct);

        if (product == null) return hierarchy;

        // Добавляем продукт
        hierarchy.Add(new
        {
            id = product.Id,
            name = product.Name,
            type = "product",
            level = 1
        });

        // Строим иерархию категорий если есть
        if (product.CategoryId.HasValue)
        {
            var categoryHierarchy = await GetCategoryHierarchyAsync(product.CategoryId.Value, ct);

            // Пересчитываем уровни и добавляем в начало
            int categoryLevel = 1;
            foreach (var cat in categoryHierarchy)
            {
                hierarchy.Insert(0, new
                {
                    id = ((dynamic)cat).id,
                    name = ((dynamic)cat).name,
                    type = "category",
                    level = categoryLevel++
                });
            }

            // Обновляем уровень продукта
            hierarchy[hierarchy.Count - 1] = new
            {
                id = product.Id,
                name = product.Name,
                type = "product",
                level = categoryHierarchy.Count + 1
            };
        }

        return hierarchy;
    }

    private async Task<List<object>> GetCategoryHierarchyAsync(Guid categoryId, CancellationToken ct)
    {
        var hierarchy = new List<object>();
        var currentId = categoryId;
        var visited = new HashSet<Guid>();

        while (currentId != Guid.Empty && !visited.Contains(currentId))
        {
            visited.Add(currentId);

            var category = await _db.ProductCategories
                .Where(c => c.Id == currentId)
                .Select(c => new { c.Id, c.Name, c.ParentId })
                .FirstOrDefaultAsync(ct);

            if (category == null) break;

            hierarchy.Insert(0, new
            {
                id = category.Id,
                name = category.Name
            });

            currentId = category.ParentId ?? Guid.Empty;
        }

        return hierarchy;
    }
}
