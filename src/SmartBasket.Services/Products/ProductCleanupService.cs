using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBasket.Data;

namespace SmartBasket.Services.Products;

/// <summary>
/// Интерфейс сервиса очистки осиротевших продуктов
/// </summary>
public interface IProductCleanupService
{
    /// <summary>
    /// Удалить Products без связанных Items
    /// </summary>
    /// <returns>Количество удалённых Products</returns>
    Task<int> CleanupOrphanedProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить список осиротевших Products (без удаления)
    /// </summary>
    Task<List<OrphanedProductInfo>> GetOrphanedProductsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Информация об осиротевшем продукте
/// </summary>
public class OrphanedProductInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
}

/// <summary>
/// Сервис очистки осиротевших продуктов
/// Удаляет Products которые не имеют связанных Items (через Item.ProductId)
/// </summary>
public class ProductCleanupService : IProductCleanupService
{
    private readonly SmartBasketDbContext _dbContext;
    private readonly ILogger<ProductCleanupService> _logger;

    public ProductCleanupService(
        SmartBasketDbContext dbContext,
        ILogger<ProductCleanupService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> CleanupOrphanedProductsAsync(CancellationToken cancellationToken = default)
    {
        var orphaned = await GetOrphanedProductsAsync(cancellationToken);

        if (orphaned.Count == 0)
        {
            _logger.LogInformation("No orphaned products found");
            return 0;
        }

        _logger.LogInformation("Found {Count} orphaned products to delete", orphaned.Count);

        var orphanedIds = orphaned.Select(o => o.Id).ToList();

        var productsToDelete = await _dbContext.Products
            .Where(p => orphanedIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        _dbContext.Products.RemoveRange(productsToDelete);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted {Count} orphaned products", productsToDelete.Count);

        foreach (var p in productsToDelete)
        {
            _logger.LogDebug("Deleted orphaned product: {Name} ({Id})", p.Name, p.Id);
        }

        return productsToDelete.Count;
    }

    public async Task<List<OrphanedProductInfo>> GetOrphanedProductsAsync(CancellationToken cancellationToken = default)
    {
        // Находим Products которые не имеют связанных Items

        // Подзапрос: ID Products у которых есть Items
        var productsWithItems = _dbContext.Items
            .Select(i => i.ProductId)
            .Distinct();

        // Находим orphaned: не в множестве products с Items
        var orphanedProducts = await _dbContext.Products
            .Include(p => p.Category)
            .Where(p => !productsWithItems.Contains(p.Id))
            .Select(p => new OrphanedProductInfo
            {
                Id = p.Id,
                Name = p.Name,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : null
            })
            .ToListAsync(cancellationToken);

        return orphanedProducts;
    }
}
