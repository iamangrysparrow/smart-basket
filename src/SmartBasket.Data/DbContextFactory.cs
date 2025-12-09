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
        services.AddDbContext<SmartBasketDbContext>(options =>
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
