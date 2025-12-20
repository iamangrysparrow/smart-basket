using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Entities;
using SmartBasket.Data;

namespace SmartBasket.Services.Products;

public class ProductCategoryService : IProductCategoryService
{
    private readonly SmartBasketDbContext _db;

    public ProductCategoryService(SmartBasketDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProductCategory>> GetAllWithHierarchyAsync(CancellationToken ct = default)
    {
        return await _db.ProductCategories
            .AsNoTracking()
            .Include(c => c.Children)
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductCategory>> GetAllFlatAsync(CancellationToken ct = default)
    {
        return await _db.ProductCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.ProductCategories
            .Include(c => c.Children)
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<ProductCategory> CreateAsync(string name, Guid? parentId = null, CancellationToken ct = default)
    {
        var category = new ProductCategory
        {
            Name = name.Trim(),
            ParentId = parentId
        };

        _db.ProductCategories.Add(category);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return category;
    }

    public async Task<ProductCategory> UpdateAsync(Guid id, string name, Guid? parentId, CancellationToken ct = default)
    {
        var category = await _db.ProductCategories.FindAsync(new object[] { id }, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Category {id} not found");

        category.Name = name.Trim();
        category.ParentId = parentId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return category;
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var (canDelete, productsCount, childrenCount) = await CanDeleteAsync(id, ct).ConfigureAwait(false);

        if (!canDelete)
        {
            var errors = new List<string>();
            if (childrenCount > 0) errors.Add($"дочерних категорий: {childrenCount}");
            if (productsCount > 0) errors.Add($"продуктов: {productsCount}");
            return (false, $"Нельзя удалить: {string.Join(", ", errors)}");
        }

        var category = await _db.ProductCategories.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (category == null) return (false, "Категория не найдена");

        _db.ProductCategories.Remove(category);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return (true, null);
    }

    public async Task<(bool CanDelete, int ProductsCount, int ChildrenCount)> CanDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var productsCount = await _db.Products.CountAsync(p => p.CategoryId == id, ct).ConfigureAwait(false);
        var childrenCount = await _db.ProductCategories.CountAsync(c => c.ParentId == id, ct).ConfigureAwait(false);

        return (productsCount == 0 && childrenCount == 0, productsCount, childrenCount);
    }

    public async Task<bool> MoveAsync(Guid categoryId, Guid? newParentId, CancellationToken ct = default)
    {
        // Prevent circular reference
        if (newParentId.HasValue)
        {
            var descendants = await GetDescendantIdsAsync(categoryId, ct).ConfigureAwait(false);
            if (descendants.Contains(newParentId.Value))
                return false; // Would create circular reference
        }

        var category = await _db.ProductCategories.FindAsync(new object[] { categoryId }, ct).ConfigureAwait(false);
        if (category == null) return false;

        category.ParentId = newParentId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<Guid>> GetDescendantIdsAsync(Guid categoryId, CancellationToken ct = default)
    {
        var result = new List<Guid>();
        var toProcess = new Queue<Guid>();

        // Get direct children
        var directChildren = await _db.ProductCategories
            .Where(c => c.ParentId == categoryId)
            .Select(c => c.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var childId in directChildren)
        {
            toProcess.Enqueue(childId);
        }

        // BFS to get all descendants
        while (toProcess.Count > 0)
        {
            var currentId = toProcess.Dequeue();
            result.Add(currentId);

            var children = await _db.ProductCategories
                .Where(c => c.ParentId == currentId)
                .Select(c => c.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var childId in children)
            {
                toProcess.Enqueue(childId);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(Guid categoryId, bool includeChildren = false, CancellationToken ct = default)
    {
        IQueryable<Product> query = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Items);

        if (includeChildren)
        {
            var categoryIds = await GetDescendantIdsAsync(categoryId, ct).ConfigureAwait(false);
            var allIds = categoryIds.Append(categoryId).ToList();
            query = query.Where(p => p.CategoryId.HasValue && allIds.Contains(p.CategoryId.Value));
        }
        else
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        return await query
            .OrderBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<(int TotalCategories, int TotalProducts, int TotalItems)> GetStatisticsAsync(CancellationToken ct = default)
    {
        var categories = await _db.ProductCategories.CountAsync(ct).ConfigureAwait(false);
        var products = await _db.Products.CountAsync(ct).ConfigureAwait(false);
        var items = await _db.Items.CountAsync(ct).ConfigureAwait(false);

        return (categories, products, items);
    }
}
