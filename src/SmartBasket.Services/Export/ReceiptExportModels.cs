using System.Text.Json.Serialization;

namespace SmartBasket.Services.Export;

/// <summary>
/// Полная модель экспорта чека (с метаданными, категориями, метками)
/// </summary>
public class ReceiptExportFull
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("receipt")]
    public ReceiptDataFull Receipt { get; set; } = null!;

    [JsonPropertyName("metadata")]
    public ExportMetadata? Metadata { get; set; }
}

/// <summary>
/// Минимальная модель экспорта чека (только основные данные)
/// </summary>
public class ReceiptExportMinimal
{
    [JsonPropertyName("shopName")]
    public string ShopName { get; set; } = null!;

    [JsonPropertyName("date")]
    public string Date { get; set; } = null!;

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("items")]
    public List<ReceiptItemMinimal> Items { get; set; } = new();
}

/// <summary>
/// Полные данные чека
/// </summary>
public class ReceiptDataFull
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("shopName")]
    public string ShopName { get; set; } = null!;

    [JsonPropertyName("receiptNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReceiptNumber { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = null!;

    [JsonPropertyName("time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Time { get; set; }

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "RUB";

    [JsonPropertyName("items")]
    public List<ReceiptItemFull> Items { get; set; } = new();
}

/// <summary>
/// Полные данные позиции чека
/// </summary>
public class ReceiptItemFull
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; set; }

    [JsonPropertyName("pricePerUnit")]
    public decimal PricePerUnit { get; set; }

    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get; set; }

    [JsonPropertyName("product")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductInfo? Product { get; set; }

    [JsonPropertyName("labels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Labels { get; set; }
}

/// <summary>
/// Минимальные данные позиции чека
/// </summary>
public class ReceiptItemMinimal
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("pricePerUnit")]
    public decimal PricePerUnit { get; set; }

    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get; set; }
}

/// <summary>
/// Информация о продукте (категории)
/// </summary>
public class ProductInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }
}

/// <summary>
/// Метаданные экспорта
/// </summary>
public class ExportMetadata
{
    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonPropertyName("emailId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EmailId { get; set; }
}
