using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Entities;

namespace SmartBasket.Data;

public class SmartBasketDbContext : DbContext
{
    public SmartBasketDbContext(DbContextOptions<SmartBasketDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Good> Goods => Set<Good>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<RawReceiptItem> RawReceiptItems => Set<RawReceiptItem>();
    public DbSet<ConsumptionHistory> ConsumptionHistory => Set<ConsumptionHistory>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<CategorizationCache> CategorizationCache => Set<CategorizationCache>();
    public DbSet<EmailHistory> EmailHistory => Set<EmailHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Product
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Unit).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Threshold).HasPrecision(10, 3);
            entity.Property(e => e.AvgDailyConsumption).HasPrecision(10, 3);
            entity.HasIndex(e => e.Name);
        });

        // Item
        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.UnitRatio).HasPrecision(10, 3);
            entity.Property(e => e.Shop).HasMaxLength(255);
            entity.HasOne(e => e.Product)
                  .WithMany(p => p.Items)
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ProductId);
        });

        // Good
        modelBuilder.Entity<Good>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Quantity).HasPrecision(10, 3);
            entity.Property(e => e.Price).HasPrecision(10, 2);
            entity.HasOne(e => e.Item)
                  .WithMany(i => i.Goods)
                  .HasForeignKey(e => e.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Receipt)
                  .WithMany(r => r.Goods)
                  .HasForeignKey(e => e.ReceiptId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ItemId);
            entity.HasIndex(e => e.ReceiptId);
        });

        // Receipt
        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReceiptNumber).HasMaxLength(255);
            entity.Property(e => e.Shop).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EmailId).HasMaxLength(255);
            entity.Property(e => e.Total).HasPrecision(10, 2);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(e => e.EmailId).IsUnique();
            entity.HasIndex(e => e.ReceiptDate);
        });

        // RawReceiptItem
        modelBuilder.Entity<RawReceiptItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RawName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.RawVolume).HasMaxLength(100);
            entity.Property(e => e.RawPrice).HasMaxLength(50);
            entity.Property(e => e.Unit).HasMaxLength(50);
            entity.Property(e => e.Quantity).HasPrecision(10, 3);
            entity.Property(e => e.CategorizationStatus).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.Receipt)
                  .WithMany(r => r.RawItems)
                  .HasForeignKey(e => e.ReceiptId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Item)
                  .WithMany()
                  .HasForeignKey(e => e.ItemId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.ReceiptId);
            entity.HasIndex(e => e.CategorizationStatus);
        });

        // ConsumptionHistory
        modelBuilder.Entity<ConsumptionHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.QuantityConsumed).HasPrecision(10, 3);
            entity.Property(e => e.Source).HasMaxLength(50);
            entity.HasOne(e => e.Product)
                  .WithMany(p => p.ConsumptionHistory)
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ProductId, e.Date });
        });

        // Alert
        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.Product)
                  .WithMany(p => p.Alerts)
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ProductId, e.Status });
        });

        // CategorizationCache
        modelBuilder.Entity<CategorizationCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RawItemName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Confidence).HasPrecision(5, 2);
            entity.Property(e => e.CategorizedBy).HasMaxLength(50);
            entity.HasOne(e => e.Product)
                  .WithMany()
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.RawItemName).IsUnique();
        });

        // EmailHistory
        modelBuilder.Entity<EmailHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EmailId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Sender).HasMaxLength(255);
            entity.Property(e => e.Subject).HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.EmailId).IsUnique();
        });
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
