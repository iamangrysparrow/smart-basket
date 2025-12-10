using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Entities;
using SmartBasket.Data;

namespace SmartBasket.Services.Products;

public class ProductService : IProductService
{
    private readonly SmartBasketDbContext _db;

    public ProductService(SmartBasketDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Product>> GetAllWithHierarchyAsync(CancellationToken ct = default)
    {
        return await _db.Products
            .Include(p => p.Children)
            .Include(p => p.Items)
            .OrderBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Product>> GetAllFlatAsync(CancellationToken ct = default)
    {
        return await _db.Products
            .OrderBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Products
            .Include(p => p.Children)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Product> CreateAsync(string name, Guid? parentId = null, CancellationToken ct = default)
    {
        var product = new Product
        {
            Name = name.Trim(),
            ParentId = parentId
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return product;
    }

    public async Task<Product> UpdateAsync(Guid id, string name, Guid? parentId, CancellationToken ct = default)
    {
        var product = await _db.Products.FindAsync(new object[] { id }, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Product {id} not found");

        product.Name = name.Trim();
        product.ParentId = parentId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return product;
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var (canDelete, itemsCount, childrenCount) = await CanDeleteAsync(id, ct).ConfigureAwait(false);

        if (!canDelete)
        {
            var errors = new List<string>();
            if (childrenCount > 0) errors.Add($"дочерних продуктов: {childrenCount}");
            if (itemsCount > 0) errors.Add($"товаров: {itemsCount}");
            return (false, $"Нельзя удалить: {string.Join(", ", errors)}");
        }

        var product = await _db.Products.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (product == null) return (false, "Продукт не найден");

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return (true, null);
    }

    public async Task<(bool CanDelete, int ItemsCount, int ChildrenCount)> CanDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var itemsCount = await _db.Items.CountAsync(i => i.ProductId == id, ct).ConfigureAwait(false);
        var childrenCount = await _db.Products.CountAsync(p => p.ParentId == id, ct).ConfigureAwait(false);

        return (itemsCount == 0 && childrenCount == 0, itemsCount, childrenCount);
    }

    public async Task<bool> MoveAsync(Guid productId, Guid? newParentId, CancellationToken ct = default)
    {
        // Prevent circular reference
        if (newParentId.HasValue)
        {
            var descendants = await GetDescendantIdsAsync(productId, ct).ConfigureAwait(false);
            if (descendants.Contains(newParentId.Value))
                return false; // Would create circular reference
        }

        var product = await _db.Products.FindAsync(new object[] { productId }, ct).ConfigureAwait(false);
        if (product == null) return false;

        product.ParentId = newParentId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<Item>> GetItemsAsync(Guid productId, bool includeChildren = false, CancellationToken ct = default)
    {
        IQueryable<Item> query = _db.Items
            .Include(i => i.Product)
            .Include(i => i.ReceiptItems)
            .Include(i => i.ItemLabels)
                .ThenInclude(il => il.Label);

        if (includeChildren)
        {
            var productIds = await GetDescendantIdsAsync(productId, ct).ConfigureAwait(false);
            var allIds = productIds.Append(productId).ToList();
            query = query.Where(i => allIds.Contains(i.ProductId));
        }
        else
        {
            query = query.Where(i => i.ProductId == productId);
        }

        return await query
            .OrderBy(i => i.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> GetDescendantIdsAsync(Guid productId, CancellationToken ct = default)
    {
        var result = new List<Guid>();
        var toProcess = new Queue<Guid>();

        // Get direct children
        var directChildren = await _db.Products
            .Where(p => p.ParentId == productId)
            .Select(p => p.Id)
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

            var children = await _db.Products
                .Where(p => p.ParentId == currentId)
                .Select(p => p.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var childId in children)
            {
                toProcess.Enqueue(childId);
            }
        }

        return result;
    }

    public async Task<(int TotalProducts, int TotalItems)> GetStatisticsAsync(CancellationToken ct = default)
    {
        var products = await _db.Products.CountAsync(ct).ConfigureAwait(false);
        var items = await _db.Items.CountAsync(ct).ConfigureAwait(false);

        return (products, items);
    }
}
