using Inventory.Api.Domain;

namespace Inventory.Api.Controllers;

public record WarehouseResponse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string CountryCode { get; init; }
    public required bool IsOperational { get; init; }

    public static WarehouseResponse From(Warehouse warehouse) => new()
    {
        Id = warehouse.Id,
        Name = warehouse.Name,
        CountryCode = warehouse.CountryCode,
        IsOperational = warehouse.IsOperational
    };
}