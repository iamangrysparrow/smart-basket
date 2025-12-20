using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartBasket.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartBasketDbContext>
{
    public SmartBasketDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SmartBasketDbContext>();

        // Use SQLite for design-time operations (migrations)
        optionsBuilder.UseSqlite("Data Source=:memory:");

        return new SmartBasketDbContext(optionsBuilder.Options);
    }
}
