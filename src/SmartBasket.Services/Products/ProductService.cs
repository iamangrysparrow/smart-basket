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

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Items)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Product>> GetAllSortedAsync(CancellationToken ct = default)
    {
        return await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Items)
            .OrderBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Product> CreateAsync(string name, Guid? categoryId = null, CancellationToken ct = default)
    {
        var product = new Product
        {
            Name = name.Trim(),
            CategoryId = categoryId
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return product;
    }

    public async Task<Product> UpdateAsync(Guid id, string name, Guid? categoryId, CancellationToken ct = default)
    {
        var product = await _db.Products.FindAsync(new object[] { id }, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Product {id} not found");

        product.Name = name.Trim();
        product.CategoryId = categoryId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return product;
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var (canDelete, itemsCount) = await CanDeleteAsync(id, ct).ConfigureAwait(false);

        if (!canDelete)
        {
            return (false, $"Нельзя удалить: товаров: {itemsCount}");
        }

        var product = await _db.Products.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (product == null) return (false, "Продукт не найден");

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return (true, null);
    }

    public async Task<(bool CanDelete, int ItemsCount)> CanDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var itemsCount = await _db.Items.CountAsync(i => i.ProductId == id, ct).ConfigureAwait(false);
        return (itemsCount == 0, itemsCount);
    }

    public async Task<bool> SetCategoryAsync(Guid productId, Guid? categoryId, CancellationToken ct = default)
    {
        var product = await _db.Products.FindAsync(new object[] { productId }, ct).ConfigureAwait(false);
        if (product == null) return false;

        product.CategoryId = categoryId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<Item>> GetItemsAsync(Guid productId, CancellationToken ct = default)
    {
        return await _db.Items
            .Include(i => i.Product)
            .Include(i => i.ReceiptItems)
            .Include(i => i.ItemLabels)
                .ThenInclude(il => il.Label)
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<(int TotalProducts, int TotalItems)> GetStatisticsAsync(CancellationToken ct = default)
    {
        var products = await _db.Products.CountAsync(ct).ConfigureAwait(false);
        var items = await _db.Items.CountAsync(ct).ConfigureAwait(false);

        return (products, items);
    }
}
