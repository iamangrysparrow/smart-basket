using SmartBasket.Core.Entities;

namespace SmartBasket.Services.Products;

/// <summary>
/// Service for managing product categories hierarchy
/// </summary>
public interface IProductCategoryService
{
    /// <summary>
    /// Get all categories with hierarchy (includes Children and Products count)
    /// </summary>
    Task<IReadOnlyList<ProductCategory>> GetAllWithHierarchyAsync(CancellationToken ct = default);

    /// <summary>
    /// Get categories as flat list (for ComboBox)
    /// </summary>
    Task<IReadOnlyList<ProductCategory>> GetAllFlatAsync(CancellationToken ct = default);

    /// <summary>
    /// Get category by ID with children
    /// </summary>
    Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create new category
    /// </summary>
    Task<ProductCategory> CreateAsync(string name, Guid? parentId = null, CancellationToken ct = default);

    /// <summary>
    /// Update category (name and/or parent)
    /// </summary>
    Task<ProductCategory> UpdateAsync(Guid id, string name, Guid? parentId, CancellationToken ct = default);

    /// <summary>
    /// Delete category (only if no children and no products)
    /// </summary>
    Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Check if category can be deleted
    /// </summary>
    Task<(bool CanDelete, int ProductsCount, int ChildrenCount)> CanDeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Move category to new parent
    /// </summary>
    Task<bool> MoveAsync(Guid categoryId, Guid? newParentId, CancellationToken ct = default);

    /// <summary>
    /// Get all descendant category IDs (for filtering)
    /// </summary>
    Task<IReadOnlyList<Guid>> GetDescendantIdsAsync(Guid categoryId, CancellationToken ct = default);

    /// <summary>
    /// Get products for category (optionally including children's products)
    /// </summary>
    Task<IReadOnlyList<Product>> GetProductsAsync(Guid categoryId, bool includeChildren = false, CancellationToken ct = default);

    /// <summary>
    /// Get statistics
    /// </summary>
    Task<(int TotalCategories, int TotalProducts, int TotalItems)> GetStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// Получить полный путь категории от корня до листа.
    /// Формат: "Корневая \ Родительская \ Текущая"
    /// </summary>
    Task<string?> GetCategoryPathAsync(Guid categoryId, CancellationToken ct = default);

    /// <summary>
    /// Получить полный путь категории для продукта.
    /// </summary>
    Task<string?> GetProductCategoryPathAsync(Guid productId, CancellationToken ct = default);
}
