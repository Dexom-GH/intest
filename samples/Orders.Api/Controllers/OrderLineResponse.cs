using Orders.Api.Domain;

namespace Orders.Api.Controllers;

public record OrderLineResponse
{
    public required int Id { get; init; }
    public required string Sku { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }

    public static OrderLineResponse From(OrderLine line)
        => new() { Id = line.Id, Sku = line.Sku, Quantity = line.Quantity, UnitPrice = line.UnitPrice };
}