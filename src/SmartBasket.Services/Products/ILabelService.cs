using SmartBasket.Core.Entities;

namespace SmartBasket.Services.Products;

/// <summary>
/// Service for managing labels and their assignments
/// </summary>
public interface ILabelService
{
    /// <summary>
    /// Get all labels with item counts
    /// </summary>
    Task<IReadOnlyList<LabelWithCount>> GetAllWithCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get label by ID
    /// </summary>
    Task<Label?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create new label
    /// </summary>
    Task<Label> CreateAsync(string name, string color, CancellationToken ct = default);

    /// <summary>
    /// Update label
    /// </summary>
    Task<Label> UpdateAsync(Guid id, string name, string color, CancellationToken ct = default);

    /// <summary>
    /// Delete label (removes from all items)
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get items with specific label
    /// </summary>
    Task<IReadOnlyList<Item>> GetItemsWithLabelAsync(Guid labelId, CancellationToken ct = default);

    /// <summary>
    /// Get items without any labels
    /// </summary>
    Task<IReadOnlyList<Item>> GetItemsWithoutLabelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Assign label to item
    /// </summary>
    Task AssignLabelToItemAsync(Guid itemId, Guid labelId, CancellationToken ct = default);

    /// <summary>
    /// Remove label from item
    /// </summary>
    Task RemoveLabelFromItemAsync(Guid itemId, Guid labelId, CancellationToken ct = default);

    /// <summary>
    /// Remove all labels from item
    /// </summary>
    Task RemoveAllLabelsFromItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Assign label to multiple items
    /// </summary>
    Task AssignLabelToItemsAsync(IEnumerable<Guid> itemIds, Guid labelId, CancellationToken ct = default);

    /// <summary>
    /// Remove label from multiple items
    /// </summary>
    Task RemoveLabelFromItemsAsync(IEnumerable<Guid> itemIds, Guid labelId, CancellationToken ct = default);

    /// <summary>
    /// Get labels assigned to item
    /// </summary>
    Task<IReadOnlyList<Label>> GetLabelsForItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Get statistics
    /// </summary>
    Task<(int TotalLabels, int ItemsWithLabels, int ItemsWithoutLabels)> GetStatisticsAsync(CancellationToken ct = default);
}

/// <summary>
/// Label with item count for display
/// </summary>
public record LabelWithCount(Label Label, int ItemCount);
