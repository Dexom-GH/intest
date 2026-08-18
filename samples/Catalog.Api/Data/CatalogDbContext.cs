using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            // A real unique index, so a duplicate SKU produces a 409 rather than a silent
            // second row. The EF Core InMemory provider would not enforce this.
            product.HasIndex(p => p.Sku).IsUnique();

            product.OwnsOne(p => p.Dimensions);

            product.HasOne(p => p.ParentProduct)
                   .WithMany()
                   .HasForeignKey(p => p.ParentProductId)
                   .OnDelete(DeleteBehavior.Restrict);

            product.HasOne(p => p.CategoryEntity)
                   .WithMany(c => c.Products)
                   .HasForeignKey(p => p.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict);

            product.Property(p => p.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ProductTag>()
                    .HasOne(t => t.Product)
                    .WithMany(p => p.Tags)
                    .HasForeignKey(t => t.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Category>().HasIndex(c => c.Name).IsUnique();
    }

    /// <summary>
    /// Deterministic seed. Fixed GUIDs mean generated tests can reference a known id without
    /// a fixture, which is what makes the v0 acceptance run possible before fixtures exist.
    /// </summary>
    public static async Task SeedAsync(CatalogDbContext context, CancellationToken cancellationToken = default)
    {
        await context.Database.EnsureCreatedAsync(cancellationToken);

        if (await context.Categories.AnyAsync(cancellationToken))
        {
            return;
        }

        var hardware = new Category
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Hardware",
            Notes = "Physical goods"
        };

        var software = new Category
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Software",
            Notes = null
        };

        // Referenced by no product, so it can be deleted — exercises the 204 path.
        var deletable = new Category
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Deprecated",
            Notes = "Unused, safe to delete"
        };

        var widget = new Product
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Sku = "WGT-0001",
            Name = "Widget",
            Description = "A standard widget",
            Price = 19.99m,
            StockQuantity = 42,
            ViewCount = 1_200_000L,
            WeightKilograms = 1.25,
            RatingAverage = 4.5f,
            IsActive = true,
            Category = ProductCategory.Hardware,
            CreatedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero),
            AvailableFrom = new DateOnly(2026, 2, 1),
            DailyCutoff = new TimeOnly(16, 0),
            LeadTime = TimeSpan.FromHours(48),
            Thumbnail = [0x01, 0x02, 0x03],
            ProductUrl = "https://example.invalid/products/wgt-0001",
            SupplierEmail = "supplier@example.invalid",
            CategoryId = hardware.Id,
            Dimensions = new Dimensions { LengthCentimetres = 10, WidthCentimetres = 5, HeightCentimetres = 2 },
            Tags = [new ProductTag { Label = "featured" }, new ProductTag { Label = "in-stock" }]
        };

        // Every nullable left null, so a generated contract test proves null is accepted
        // where the schema says it may be.
        var sparse = new Product
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Sku = "SPR-0002",
            Name = "Sparse",
            Description = null,
            Price = 5.00m,
            StockQuantity = 0,
            ViewCount = 0,
            WeightKilograms = 0.1,
            RatingAverage = null,
            IsActive = false,
            Category = ProductCategory.Unknown,
            CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            DiscontinuedAt = null,
            AvailableFrom = null,
            DailyCutoff = null,
            LeadTime = null,
            Thumbnail = null,
            ProductUrl = null,
            SupplierEmail = null,
            ParentProductId = widget.Id,
            CategoryId = software.Id,
            Dimensions = null
        };

        context.Categories.AddRange(hardware, software, deletable);
        context.Products.AddRange(widget, sparse);
        await context.SaveChangesAsync(cancellationToken);
    }
}
