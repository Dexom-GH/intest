using Inventory.Api.Domain;

namespace Inventory.Api.Controllers;

public record StockItemResponse
{
    public required int Id { get; init; }
    public required string Sku { get; init; }
    public required int WarehouseId { get; init; }
    public required int QuantityOnHand { get; init; }
    public required int QuantityReserved { get; init; }
    public required StockCondition Condition { get; init; }
    public required DateTimeOffset LastCountedAt { get; init; }
    public decimal? UnitCost { get; init; }
    public string? Notes { get; init; }

    public static StockItemResponse From(StockItem item) => new()
    {
        Id = item.Id,
        Sku = item.Sku,
        WarehouseId = item.WarehouseId,
        QuantityOnHand = item.QuantityOnHand,
        QuantityReserved = item.QuantityReserved,
        Condition = item.Condition,
        LastCountedAt = item.LastCountedAt,
        UnitCost = item.UnitCost,
        Notes = item.Notes
    };
}