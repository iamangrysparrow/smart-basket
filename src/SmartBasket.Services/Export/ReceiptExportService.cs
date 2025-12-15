using System.Text.Json;
using SmartBasket.Core.Entities;

namespace SmartBasket.Services.Export;

/// <summary>
/// Сервис экспорта чеков в JSON
/// </summary>
public interface IReceiptExportService
{
    /// <summary>
    /// Экспорт чека в полный формат JSON
    /// </summary>
    string ExportToFullJson(Receipt receipt);

    /// <summary>
    /// Экспорт чека в минимальный формат JSON
    /// </summary>
    string ExportToMinimalJson(Receipt receipt);

    /// <summary>
    /// Сохранить чек в файл (полный формат)
    /// </summary>
    Task SaveToFileFullAsync(Receipt receipt, string filePath);

    /// <summary>
    /// Сохранить чек в файл (минимальный формат)
    /// </summary>
    Task SaveToFileMinimalAsync(Receipt receipt, string filePath);
}

public class ReceiptExportService : IReceiptExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ExportToFullJson(Receipt receipt)
    {
        var export = MapToFullExport(receipt);
        return JsonSerializer.Serialize(export, JsonOptions);
    }

    public string ExportToMinimalJson(Receipt receipt)
    {
        var export = MapToMinimalExport(receipt);
        return JsonSerializer.Serialize(export, JsonOptions);
    }

    public async Task SaveToFileFullAsync(Receipt receipt, string filePath)
    {
        var json = ExportToFullJson(receipt);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task SaveToFileMinimalAsync(Receipt receipt, string filePath)
    {
        var json = ExportToMinimalJson(receipt);
        await File.WriteAllTextAsync(filePath, json);
    }

    private static ReceiptExportFull MapToFullExport(Receipt receipt)
    {
        return new ReceiptExportFull
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            Receipt = new ReceiptDataFull
            {
                Id = receipt.Id,
                ShopName = receipt.Shop,
                ReceiptNumber = receipt.ReceiptNumber,
                Date = receipt.ReceiptDate.ToString("yyyy-MM-dd"),
                Time = receipt.ReceiptDate.ToString("HH:mm:ss"),
                TotalAmount = receipt.Total ?? 0,
                Currency = "RUB",
                Items = receipt.Items.Select(MapToFullItem).ToList()
            },
            Metadata = new ExportMetadata
            {
                EmailId = receipt.EmailId
            }
        };
    }

    private static ReceiptExportMinimal MapToMinimalExport(Receipt receipt)
    {
        return new ReceiptExportMinimal
        {
            ShopName = receipt.Shop,
            Date = receipt.ReceiptDate.ToString("yyyy-MM-dd"),
            TotalAmount = receipt.Total ?? 0,
            Items = receipt.Items.Select(MapToMinimalItem).ToList()
        };
    }

    private static ReceiptItemFull MapToFullItem(ReceiptItem item)
    {
        var result = new ReceiptItemFull
        {
            Name = item.Item?.Name ?? "Unknown",
            Quantity = item.Quantity,
            Unit = item.Item?.UnitOfMeasure ?? "шт",
            PricePerUnit = item.Price ?? 0,
            TotalPrice = item.Amount ?? 0
        };

        // Product info
        if (item.Item?.Product != null)
        {
            result.Product = new ProductInfo
            {
                Id = item.Item.Product.Id,
                Name = item.Item.Product.Name,
                Category = item.Item.Product.Parent?.Name
            };
        }

        // Labels
        if (item.Item?.ItemLabels != null && item.Item.ItemLabels.Count > 0)
        {
            result.Labels = item.Item.ItemLabels
                .Where(il => il.Label != null)
                .Select(il => il.Label!.Name)
                .ToList();

            if (result.Labels.Count == 0)
                result.Labels = null;
        }

        return result;
    }

    private static ReceiptItemMinimal MapToMinimalItem(ReceiptItem item)
    {
        return new ReceiptItemMinimal
        {
            Name = item.Item?.Name ?? "Unknown",
            Quantity = item.Quantity,
            PricePerUnit = item.Price ?? 0,
            TotalPrice = item.Amount ?? 0
        };
    }
}
