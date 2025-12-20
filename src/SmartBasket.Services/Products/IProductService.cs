using SmartBasket.Core.Entities;

namespace SmartBasket.Services.Products;

/// <summary>
/// Service for managing products (flat list with optional category reference)
/// </summary>
public interface IProductService
{
    /// <summary>
    /// Get all products with category info
    /// </summary>
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all products sorted by name (for UI list)
    /// </summary>
    Task<IReadOnlyList<Product>> GetAllSortedAsync(CancellationToken ct = default);

    /// <summary>
    /// Get product by ID
    /// </summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create new product
    /// </summary>
    Task<Product> CreateAsync(string name, Guid? categoryId = null, CancellationToken ct = default);

    /// <summary>
    /// Update product (name and/or category)
    /// </summary>
    Task<Product> UpdateAsync(Guid id, string name, Guid? categoryId, CancellationToken ct = default);

    /// <summary>
    /// Delete product (only if no items)
    /// </summary>
    Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Check if product can be deleted
    /// </summary>
    Task<(bool CanDelete, int ItemsCount)> CanDeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Move product to category
    /// </summary>
    Task<bool> SetCategoryAsync(Guid productId, Guid? categoryId, CancellationToken ct = default);

    /// <summary>
    /// Get items for product
    /// </summary>
    Task<IReadOnlyList<Item>> GetItemsAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Get statistics
    /// </summary>
    Task<(int TotalProducts, int TotalItems)> GetStatisticsAsync(CancellationToken ct = default);
}
