using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Controllers;

public record StockAdjustmentRequest
{
    public required int WarehouseId { get; init; }

    [Range(-10_000, 10_000)]
    public required int Delta { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}