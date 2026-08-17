using System.ComponentModel.DataAnnotations;

namespace Orders.Api.Domain;

public class Customer
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }

    public List<Order> Orders { get; set; } = [];
}

public class Order
{
    public Guid Id { get; set; }

    /// <summary>Human-readable, unique — a duplicate produces a real 409.</summary>
    [Required, MaxLength(20)]
    public string Reference { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public OrderStatus Status { get; set; }

    public decimal TotalAmount { get; set; }

    [Required, MaxLength(3)]
    public string CurrencyCode { get; set; } = "GBP";

    public DateTimeOffset PlacedAt { get; set; }

    public DateTimeOffset? ShippedAt { get; set; }

    public DateOnly? RequestedDeliveryDate { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Correlation id InTest stamps on every request, persisted so a test run's
    /// rows can be identified and swept later.</summary>
    [MaxLength(120)]
    public string? TestRunId { get; set; }

    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    [Required, MaxLength(32)]
    public string Sku { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

public enum OrderStatus
{
    Draft = 0,
    Placed = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}
