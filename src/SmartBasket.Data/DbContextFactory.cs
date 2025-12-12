using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartBasket.Data;

public static class DbContextFactory
{
    public static IServiceCollection AddSmartBasketDbContext(
        this IServiceCollection services,
        DatabaseProviderType providerType,
        string connectionString)
    {
        // Use DbContextFactory for WPF apps to avoid concurrency issues
        // Each service will create its own DbContext instance via Transient registration
        services.AddDbContextFactory<SmartBasketDbContext>(options =>
        {
            switch (providerType)
            {
                case DatabaseProviderType.PostgreSQL:
                    options.UseNpgsql(connectionString);
                    break;
                case DatabaseProviderType.SQLite:
                    options.UseSqlite(connectionString);
                    break;
                default:
                    throw new ArgumentException($"Unsupported database provider: {providerType}");
            }
        });

        // Register DbContext as Transient - each injection gets a new instance
        // This prevents concurrency issues in WPF where services may run in parallel
        services.AddTransient(sp => sp.GetRequiredService<IDbContextFactory<SmartBasketDbContext>>().CreateDbContext());

        return services;
    }

    public static SmartBasketDbContext CreateDbContext(
        DatabaseProviderType providerType,
        string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SmartBasketDbContext>();

        switch (providerType)
        {
            case DatabaseProviderType.PostgreSQL:
                optionsBuilder.UseNpgsql(connectionString);
                break;
            case DatabaseProviderType.SQLite:
                optionsBuilder.UseSqlite(connectionString);
                break;
            default:
                throw new ArgumentException($"Unsupported database provider: {providerType}");
        }

        return new SmartBasketDbContext(optionsBuilder.Options);
    }
}
