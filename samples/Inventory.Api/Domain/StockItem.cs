using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Domain;

public class Warehouse
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(2)]
    public string CountryCode { get; set; } = "GB";

    public bool IsOperational { get; set; }

    public List<StockItem> StockItems { get; set; } = [];
}

public class StockItem
{
    public int Id { get; set; }

    [Required, MaxLength(32)]
    public string Sku { get; set; } = string.Empty;

    public int WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    public int QuantityOnHand { get; set; }

    public int QuantityReserved { get; set; }

    public StockCondition Condition { get; set; }

    public DateTimeOffset LastCountedAt { get; set; }

    public decimal? UnitCost { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public enum StockCondition
{
    New = 0,
    Refurbished = 1,
    Damaged = 2
}
