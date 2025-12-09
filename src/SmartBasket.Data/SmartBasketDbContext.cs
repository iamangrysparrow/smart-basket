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
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<ProductLabel> ProductLabels => Set<ProductLabel>();
    public DbSet<ItemLabel> ItemLabels => Set<ItemLabel>();
    public DbSet<EmailHistory> EmailHistory => Set<EmailHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Product (с иерархией)
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.ParentId);

            // Self-referencing для иерархии
            entity.HasOne(e => e.Parent)
                  .WithMany(p => p.Children)
                  .HasForeignKey(e => e.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Item (справочник товаров)
        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.UnitOfMeasure).HasMaxLength(50);
            entity.Property(e => e.UnitQuantity).HasPrecision(10, 3);
            entity.Property(e => e.Shop).HasMaxLength(255);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.Shop);

            entity.HasOne(e => e.Product)
                  .WithMany(p => p.Items)
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Receipt (чек)
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
            entity.HasIndex(e => e.Shop);
        });

        // ReceiptItem (товарная позиция в чеке)
        modelBuilder.Entity<ReceiptItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Quantity).HasPrecision(10, 3);
            entity.Property(e => e.Price).HasPrecision(10, 2);
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.HasIndex(e => e.ItemId);
            entity.HasIndex(e => e.ReceiptId);

            entity.HasOne(e => e.Item)
                  .WithMany(i => i.ReceiptItems)
                  .HasForeignKey(e => e.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Receipt)
                  .WithMany(r => r.Items)
                  .HasForeignKey(e => e.ReceiptId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Label (метки)
        modelBuilder.Entity<Label>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // ProductLabel (many-to-many)
        modelBuilder.Entity<ProductLabel>(entity =>
        {
            entity.HasKey(e => new { e.ProductId, e.LabelId });

            entity.HasOne(e => e.Product)
                  .WithMany(p => p.ProductLabels)
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Label)
                  .WithMany(l => l.ProductLabels)
                  .HasForeignKey(e => e.LabelId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ItemLabel (many-to-many)
        modelBuilder.Entity<ItemLabel>(entity =>
        {
            entity.HasKey(e => new { e.ItemId, e.LabelId });

            entity.HasOne(e => e.Item)
                  .WithMany(i => i.ItemLabels)
                  .HasForeignKey(e => e.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Label)
                  .WithMany(l => l.ItemLabels)
                  .HasForeignKey(e => e.LabelId)
                  .OnDelete(DeleteBehavior.Cascade);
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
