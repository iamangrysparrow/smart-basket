using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Entities;
using SmartBasket.Data;

namespace SmartBasket.Services.Products;

public class ItemService : IItemService
{
    private readonly SmartBasketDbContext _db;

    public ItemService(SmartBasketDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Item>> GetItemsAsync(ItemFilter filter, CancellationToken ct = default)
    {
        IQueryable<Item> query = _db.Items
            .Include(i => i.Product)
            .Include(i => i.ReceiptItems)
            .Include(i => i.ItemLabels)
                .ThenInclude(il => il.Label);

        // Filter by product IDs (for hierarchy)
        if (filter.ProductIds != null && filter.ProductIds.Count > 0)
        {
            query = query.Where(i => filter.ProductIds.Contains(i.ProductId));
        }
        else if (filter.ProductId.HasValue)
        {
            query = query.Where(i => i.ProductId == filter.ProductId.Value);
        }

        // Filter by label
        if (filter.LabelId.HasValue)
        {
            query = query.Where(i => i.ItemLabels.Any(il => il.LabelId == filter.LabelId.Value));
        }

        // Filter items without labels
        if (filter.WithoutLabels == true)
        {
            query = query.Where(i => !i.ItemLabels.Any());
        }

        // Filter by shop
        if (!string.IsNullOrWhiteSpace(filter.Shop) && filter.Shop != "Все")
        {
            query = query.Where(i => i.Shop == filter.Shop);
        }

        // Search by name
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var search = filter.SearchText.ToLowerInvariant();
            query = query.Where(i => i.Name.ToLower().Contains(search));
        }

        return await query
            .OrderBy(i => i.Name)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Items
            .Include(i => i.Product)
            .Include(i => i.ReceiptItems)
            .Include(i => i.ItemLabels)
                .ThenInclude(il => il.Label)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> MoveToProductAsync(Guid itemId, Guid productId, CancellationToken ct = default)
    {
        var item = await _db.Items.FindAsync(new object[] { itemId }, ct).ConfigureAwait(false);
        if (item == null) return false;

        var productExists = await _db.Products.AnyAsync(p => p.Id == productId, ct).ConfigureAwait(false);
        if (!productExists) return false;

        item.ProductId = productId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task<int> MoveItemsToProductAsync(IEnumerable<Guid> itemIds, Guid productId, CancellationToken ct = default)
    {
        var productExists = await _db.Products.AnyAsync(p => p.Id == productId, ct).ConfigureAwait(false);
        if (!productExists) return 0;

        var itemIdList = itemIds.ToList();
        var items = await _db.Items
            .Where(i => itemIdList.Contains(i.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var item in items)
        {
            item.ProductId = productId;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return items.Count;
    }

    public async Task<IReadOnlyList<string>> GetShopsAsync(CancellationToken ct = default)
    {
        return await _db.Items
            .Where(i => i.Shop != null)
            .Select(i => i.Shop!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        return await _db.Items.CountAsync(ct).ConfigureAwait(false);
    }
}
