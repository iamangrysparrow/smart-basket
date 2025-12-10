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
    /// Удалить Products без связанных Items и без дочерних Products
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
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }
}

/// <summary>
/// Сервис очистки осиротевших продуктов
/// Удаляет Products которые:
/// - Не имеют связанных Items (через Item.ProductId)
/// - Не являются родителями других Products (через Product.ParentId)
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

        // Удаляем в порядке: сначала дочерние (без ParentId среди orphaned), потом родительские
        // Но так как мы уже отфильтровали только те, у которых нет дочерних - порядок не важен
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
        // Находим Products которые:
        // 1. Не имеют связанных Items
        // 2. Не являются родителями других Products

        // Подзапрос: ID Products у которых есть Items
        var productsWithItems = _dbContext.Items
            .Select(i => i.ProductId)
            .Distinct();

        // Подзапрос: ID Products которые являются родителями
        var parentProducts = _dbContext.Products
            .Where(p => p.ParentId != null)
            .Select(p => p.ParentId!.Value)
            .Distinct();

        // Находим orphaned: не в первом и не во втором множестве
        var orphanedProducts = await _dbContext.Products
            .Include(p => p.Parent)
            .Where(p => !productsWithItems.Contains(p.Id))
            .Where(p => !parentProducts.Contains(p.Id))
            .Select(p => new OrphanedProductInfo
            {
                Id = p.Id,
                Name = p.Name,
                ParentId = p.ParentId,
                ParentName = p.Parent != null ? p.Parent.Name : null
            })
            .ToListAsync(cancellationToken);

        return orphanedProducts;
    }
}
