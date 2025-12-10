using SmartBasket.Core.Entities;

namespace SmartBasket.Services.Products;

/// <summary>
/// Service for managing items (individual products from receipts)
/// </summary>
public interface IItemService
{
    /// <summary>
    /// Get items with filtering and paging
    /// </summary>
    Task<IReadOnlyList<Item>> GetItemsAsync(ItemFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Get item by ID with related data
    /// </summary>
    Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Move item to different product
    /// </summary>
    Task<bool> MoveToProductAsync(Guid itemId, Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Move multiple items to product
    /// </summary>
    Task<int> MoveItemsToProductAsync(IEnumerable<Guid> itemIds, Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Get unique shop names for filtering
    /// </summary>
    Task<IReadOnlyList<string>> GetShopsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get total items count
    /// </summary>
    Task<int> GetCountAsync(CancellationToken ct = default);
}

/// <summary>
/// Filter for items query
/// </summary>
public class ItemFilter
{
    /// <summary>
    /// Filter by product ID
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Include items from child products
    /// </summary>
    public bool IncludeChildProducts { get; set; }

    /// <summary>
    /// Product IDs to include (for child products)
    /// </summary>
    public IReadOnlyList<Guid>? ProductIds { get; set; }

    /// <summary>
    /// Filter by label ID
    /// </summary>
    public Guid? LabelId { get; set; }

    /// <summary>
    /// Filter items without labels
    /// </summary>
    public bool? WithoutLabels { get; set; }

    /// <summary>
    /// Filter by shop name
    /// </summary>
    public string? Shop { get; set; }

    /// <summary>
    /// Search by item name
    /// </summary>
    public string? SearchText { get; set; }

    /// <summary>
    /// Max items to return
    /// </summary>
    public int Take { get; set; } = 200;

    /// <summary>
    /// Skip items for paging
    /// </summary>
    public int Skip { get; set; } = 0;
}
