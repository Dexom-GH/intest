using System.ComponentModel.DataAnnotations;

namespace Orders.Api.Controllers;

public record CreateOrderLineRequest
{
    [Required, MaxLength(32)]
    public required string Sku { get; init; }

    [Range(1, 1000)]
    public required int Quantity { get; init; }

    [Range(0.01, 1_000_000)]
    public required decimal UnitPrice { get; init; }
}