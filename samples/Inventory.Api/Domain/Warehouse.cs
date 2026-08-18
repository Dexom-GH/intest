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