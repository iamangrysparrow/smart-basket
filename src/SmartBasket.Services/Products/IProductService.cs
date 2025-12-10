using SmartBasket.Core.Entities;

namespace SmartBasket.Services.Products;

/// <summary>
/// Service for managing products (categories) hierarchy
/// </summary>
public interface IProductService
{
    /// <summary>
    /// Get all products with hierarchy (includes Children and Items count)
    /// </summary>
    Task<IReadOnlyList<Product>> GetAllWithHierarchyAsync(CancellationToken ct = default);

    /// <summary>
    /// Get products as flat list (for ComboBox)
    /// </summary>
    Task<IReadOnlyList<Product>> GetAllFlatAsync(CancellationToken ct = default);

    /// <summary>
    /// Get product by ID with children
    /// </summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create new product
    /// </summary>
    Task<Product> CreateAsync(string name, Guid? parentId = null, CancellationToken ct = default);

    /// <summary>
    /// Update product (name and/or parent)
    /// </summary>
    Task<Product> UpdateAsync(Guid id, string name, Guid? parentId, CancellationToken ct = default);

    /// <summary>
    /// Delete product (only if no children and no items)
    /// </summary>
    /// <returns>True if deleted, false if has dependencies</returns>
    Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Check if product can be deleted
    /// </summary>
    Task<(bool CanDelete, int ItemsCount, int ChildrenCount)> CanDeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Move product to new parent
    /// </summary>
    Task<bool> MoveAsync(Guid productId, Guid? newParentId, CancellationToken ct = default);

    /// <summary>
    /// Get items for product (optionally including children's items)
    /// </summary>
    Task<IReadOnlyList<Item>> GetItemsAsync(Guid productId, bool includeChildren = false, CancellationToken ct = default);

    /// <summary>
    /// Get all descendant product IDs (for filtering)
    /// </summary>
    Task<IReadOnlyList<Guid>> GetDescendantIdsAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Get statistics
    /// </summary>
    Task<(int TotalProducts, int TotalItems)> GetStatisticsAsync(CancellationToken ct = default);
}
