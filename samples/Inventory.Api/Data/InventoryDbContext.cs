using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockItem> StockItems => Set<StockItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(item =>
        {
            // Composite unique key — one row per SKU per warehouse. A duplicate is a 409.
            item.HasIndex(i => new { i.Sku, i.WarehouseId }).IsUnique();
            item.Property(i => i.UnitCost).HasPrecision(18, 2);

            item.HasOne(i => i.Warehouse)
                .WithMany(w => w.StockItems)
                .HasForeignKey(i => i.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Warehouse>().HasIndex(w => w.Name).IsUnique();
    }

    public static async Task SeedAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        await context.Database.EnsureCreatedAsync(cancellationToken);

        if (await context.Warehouses.AnyAsync(cancellationToken)) return;

        var london = new Warehouse { Id = 1, Name = "London", CountryCode = "GB", IsOperational = true };
        var leeds = new Warehouse { Id = 2, Name = "Leeds", CountryCode = "GB", IsOperational = false };

        context.Warehouses.AddRange(london, leeds);
        context.StockItems.AddRange(
            new StockItem
            {
                Id = 1, Sku = "WGT-0001", WarehouseId = 1, QuantityOnHand = 120, QuantityReserved = 5,
                Condition = StockCondition.New, UnitCost = 12.50m,
                LastCountedAt = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero), Notes = "Primary stock"
            },
            new StockItem
            {
                Id = 2, Sku = "SPR-0002", WarehouseId = 1, QuantityOnHand = 0, QuantityReserved = 0,
                Condition = StockCondition.Damaged, UnitCost = null,
                LastCountedAt = new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero), Notes = null
            });

        await context.SaveChangesAsync(cancellationToken);
    }
}
