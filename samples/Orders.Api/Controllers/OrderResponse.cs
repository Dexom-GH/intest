using Orders.Api.Domain;

namespace Orders.Api.Controllers;

public record OrderResponse
{
    public required Guid Id { get; init; }
    public required string Reference { get; init; }
    public required Guid CustomerId { get; init; }
    public required OrderStatus Status { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string CurrencyCode { get; init; }
    public required DateTimeOffset PlacedAt { get; init; }
    public DateTimeOffset? ShippedAt { get; init; }
    public DateOnly? RequestedDeliveryDate { get; init; }
    public string? Notes { get; init; }
    public required IReadOnlyList<OrderLineResponse> Lines { get; init; }

    public static OrderResponse From(Order order) => new()
    {
        Id = order.Id,
        Reference = order.Reference,
        CustomerId = order.CustomerId,
        Status = order.Status,
        TotalAmount = order.TotalAmount,
        CurrencyCode = order.CurrencyCode,
        PlacedAt = order.PlacedAt,
        ShippedAt = order.ShippedAt,
        RequestedDeliveryDate = order.RequestedDeliveryDate,
        Notes = order.Notes,
        Lines = order.Lines.Select(OrderLineResponse.From).ToList()
    };
}