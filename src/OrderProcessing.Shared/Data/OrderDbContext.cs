using Microsoft.EntityFrameworkCore;
using OrderProcessing.Shared.Domain;

namespace OrderProcessing.Shared.Data;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<InventoryItem> Inventory => Set<InventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerId).IsRequired().HasMaxLength(128);
            e.Property(x => x.RequestedTotalAmount).HasPrecision(18, 2);
            e.Property(x => x.FinalTotalAmount).HasPrecision(18, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.FailureReason).HasMaxLength(512);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CustomerId);
            e.HasMany(x => x.Items)
                .WithOne(x => x.Order!)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).IsRequired().HasMaxLength(64);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory");
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).IsRequired().HasMaxLength(64);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasIndex(x => x.Sku).IsUnique();
        });
    }
}
