using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Data;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.HasIndex(o => o.Reference).IsUnique();
            order.Property(o => o.TotalAmount).HasPrecision(18, 2);

            order.HasOne(o => o.Customer)
                 .WithMany(c => c.Orders)
                 .HasForeignKey(o => o.CustomerId)
                 .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderLine>(line =>
        {
            line.Property(l => l.UnitPrice).HasPrecision(18, 2);

            line.HasOne(l => l.Order)
                .WithMany(o => o.Lines)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Customer>().HasIndex(c => c.Email).IsUnique();
    }

    public static async Task SeedAsync(OrdersDbContext context, CancellationToken cancellationToken = default)
    {
        await context.Database.EnsureCreatedAsync(cancellationToken);

        if (await context.Customers.AnyAsync(cancellationToken)) return;

        var customer = new Customer
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Name = "Acme Ltd",
            Email = "orders@acme.invalid",
            PhoneNumber = "+44 20 7946 0000",
            RegisteredAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var placed = new Order
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Reference = "ORD-0001",
            CustomerId = customer.Id,
            Status = OrderStatus.Placed,
            TotalAmount = 59.97m,
            CurrencyCode = "GBP",
            PlacedAt = new DateTimeOffset(2026, 4, 2, 11, 15, 0, TimeSpan.Zero),
            RequestedDeliveryDate = new DateOnly(2026, 4, 10),
            Notes = "Leave with reception",
            Lines =
            [
                new OrderLine { Sku = "WGT-0001", Quantity = 3, UnitPrice = 19.99m }
            ]
        };

        // Already shipped, so a cancel attempt returns 409 rather than 204 — a state
        // conflict rather than a validation failure.
        var shipped = new Order
        {
            Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Reference = "ORD-0002",
            CustomerId = customer.Id,
            Status = OrderStatus.Shipped,
            TotalAmount = 5.00m,
            CurrencyCode = "GBP",
            PlacedAt = new DateTimeOffset(2026, 4, 3, 9, 0, 0, TimeSpan.Zero),
            ShippedAt = new DateTimeOffset(2026, 4, 4, 8, 0, 0, TimeSpan.Zero),
            RequestedDeliveryDate = null,
            Notes = null,
            Lines = [new OrderLine { Sku = "SPR-0002", Quantity = 1, UnitPrice = 5.00m }]
        };

        context.Customers.Add(customer);
        context.Orders.AddRange(placed, shipped);
        await context.SaveChangesAsync(cancellationToken);
    }
}
