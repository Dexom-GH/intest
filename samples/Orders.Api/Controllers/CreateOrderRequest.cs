using System.ComponentModel.DataAnnotations;

namespace Orders.Api.Controllers;

public record CreateOrderRequest
{
    [Required, MaxLength(20)]
    public required string Reference { get; init; }

    public required Guid CustomerId { get; init; }

    [Required, MinLength(3), MaxLength(3)]
    public string CurrencyCode { get; init; } = "GBP";

    public DateOnly? RequestedDeliveryDate { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }

    [MinLength(1)]
    public required IReadOnlyList<CreateOrderLineRequest> Lines { get; init; }
}