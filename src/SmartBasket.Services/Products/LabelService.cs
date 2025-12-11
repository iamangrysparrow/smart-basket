using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Entities;
using SmartBasket.Data;

namespace SmartBasket.Services.Products;

public class LabelService : ILabelService
{
    private readonly SmartBasketDbContext _db;

    public LabelService(SmartBasketDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LabelWithCount>> GetAllWithCountsAsync(CancellationToken ct = default)
    {
        var labels = await _db.Labels
            .Include(l => l.ItemLabels)
            .OrderBy(l => l.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return labels.Select(l => new LabelWithCount(l, l.ItemLabels.Count)).ToList();
    }

    public async Task<Label?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Labels
            .Include(l => l.ItemLabels)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Label> CreateAsync(string name, string color, CancellationToken ct = default)
    {
        var label = new Label
        {
            Name = name.Trim(),
            Color = color
        };

        _db.Labels.Add(label);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return label;
    }

    public async Task<Label> UpdateAsync(Guid id, string name, string color, CancellationToken ct = default)
    {
        var label = await _db.Labels.FindAsync(new object[] { id }, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Label {id} not found");

        label.Name = name.Trim();
        label.Color = color;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return label;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var label = await _db.Labels
            .Include(l => l.ItemLabels)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            .ConfigureAwait(false);

        if (label == null) return false;

        // Remove all item-label associations first
        _db.ItemLabels.RemoveRange(label.ItemLabels);

        // Remove label
        _db.Labels.Remove(label);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<Item>> GetItemsWithLabelAsync(Guid labelId, CancellationToken ct = default)
    {
        return await _db.Items
            .Include(i => i.Product)
            .Include(i => i.ReceiptItems)
            .Include(i => i.ItemLabels)
                .ThenInclude(il => il.Label)
            .Where(i => i.ItemLabels.Any(il => il.LabelId == labelId))
            .OrderBy(i => i.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Item>> GetItemsWithoutLabelsAsync(CancellationToken ct = default)
    {
        return await _db.Items
            .Include(i => i.Product)
            .Include(i => i.ReceiptItems)
            .Where(i => !i.ItemLabels.Any())
            .OrderBy(i => i.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AssignLabelToItemAsync(Guid itemId, Guid labelId, CancellationToken ct = default)
    {
        var exists = await _db.ItemLabels
            .AnyAsync(il => il.ItemId == itemId && il.LabelId == labelId, ct)
            .ConfigureAwait(false);

        if (exists) return;

        _db.ItemLabels.Add(new ItemLabel { ItemId = itemId, LabelId = labelId });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveLabelFromItemAsync(Guid itemId, Guid labelId, CancellationToken ct = default)
    {
        var itemLabel = await _db.ItemLabels
            .FirstOrDefaultAsync(il => il.ItemId == itemId && il.LabelId == labelId, ct)
            .ConfigureAwait(false);

        if (itemLabel == null) return;

        _db.ItemLabels.Remove(itemLabel);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAllLabelsFromItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var itemLabels = await _db.ItemLabels
            .Where(il => il.ItemId == itemId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _db.ItemLabels.RemoveRange(itemLabels);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AssignLabelToItemsAsync(IEnumerable<Guid> itemIds, Guid labelId, CancellationToken ct = default)
    {
        var itemIdList = itemIds.ToList();

        var existingItemIds = await _db.ItemLabels
            .Where(il => itemIdList.Contains(il.ItemId) && il.LabelId == labelId)
            .Select(il => il.ItemId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var newItemIds = itemIdList.Except(existingItemIds);

        foreach (var itemId in newItemIds)
        {
            _db.ItemLabels.Add(new ItemLabel { ItemId = itemId, LabelId = labelId });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveLabelFromItemsAsync(IEnumerable<Guid> itemIds, Guid labelId, CancellationToken ct = default)
    {
        var itemIdList = itemIds.ToList();

        var itemLabels = await _db.ItemLabels
            .Where(il => itemIdList.Contains(il.ItemId) && il.LabelId == labelId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _db.ItemLabels.RemoveRange(itemLabels);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Label>> GetLabelsForItemAsync(Guid itemId, CancellationToken ct = default)
    {
        return await _db.ItemLabels
            .Where(il => il.ItemId == itemId)
            .Select(il => il.Label!)
            .OrderBy(l => l.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<(int TotalLabels, int ItemsWithLabels, int ItemsWithoutLabels)> GetStatisticsAsync(CancellationToken ct = default)
    {
        var totalLabels = await _db.Labels.CountAsync(ct).ConfigureAwait(false);

        var itemsWithLabels = await _db.Items
            .CountAsync(i => i.ItemLabels.Any(), ct)
            .ConfigureAwait(false);

        var totalItems = await _db.Items.CountAsync(ct).ConfigureAwait(false);

        return (totalLabels, itemsWithLabels, totalItems - itemsWithLabels);
    }

    public async Task<int> SyncFromFileAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return 0;

        var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
        var labelsFromFile = lines
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (labelsFromFile.Count == 0)
            return 0;

        var existingLabels = await _db.Labels
            .Select(l => l.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var existingSet = new HashSet<string>(existingLabels, StringComparer.OrdinalIgnoreCase);

        var newLabels = labelsFromFile
            .Where(name => !existingSet.Contains(name))
            .Select(name => new Label
            {
                Name = name,
                Color = GenerateColor(name)
            })
            .ToList();

        if (newLabels.Count > 0)
        {
            _db.Labels.AddRange(newLabels);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return newLabels.Count;
    }

    private static string GenerateColor(string name)
    {
        // Генерируем цвет на основе хеша имени для консистентности
        var hash = name.GetHashCode();
        var r = (hash & 0xFF0000) >> 16;
        var g = (hash & 0x00FF00) >> 8;
        var b = hash & 0x0000FF;

        // Делаем цвет более пастельным (ближе к белому)
        r = 128 + r / 2;
        g = 128 + g / 2;
        b = 128 + b / 2;

        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
