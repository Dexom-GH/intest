using System.ComponentModel.DataAnnotations;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// NSwag derives operationIds as {Controller}_{Action} — so these become Stock_GetAll,
/// Stock_GetBySku and so on. They look stable but churn the moment an action is renamed,
/// and NSwag ignores the Name property on the HTTP verb attribute, which is why the design
/// treats NSwag as the more dangerous producer rather than the safer one.
/// <para>Exposes DELETE; WarehousesController does not.</para>
/// </summary>
[ApiController]
[Route("api/stock")]
[Produces("application/json")]
public class StockController(InventoryDbContext database) : ControllerBase
{
    /// <summary>Lists stock, optionally filtered by warehouse or condition.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StockItemResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StockItemResponse>>> GetAll(
        [FromQuery] int? warehouseId,
        [FromQuery] StockCondition? condition,
        CancellationToken cancellationToken = default)
    {
        var query = database.StockItems.AsQueryable();
        if (warehouseId is not null) query = query.Where(s => s.WarehouseId == warehouseId);
        if (condition is not null) query = query.Where(s => s.Condition == condition);

        var items = await query.OrderBy(s => s.Sku).ToListAsync(cancellationToken);
        return Ok(items.Select(StockItemResponse.From).ToList());
    }

    /// <summary>Gets stock for a SKU. Note the route parameter is a string, not a GUID —
    /// so percent-encoding and empty-segment behaviour differ from the other samples.</summary>
    [HttpGet("{sku}")]
    [ProducesResponseType<IReadOnlyList<StockItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StockItemResponse>>> GetBySku(
        [Required] string sku, CancellationToken cancellationToken)
    {
        var items = await database.StockItems.Where(s => s.Sku == sku).ToListAsync(cancellationToken);
        return items.Count == 0 ? NotFound() : Ok(items.Select(StockItemResponse.From).ToList());
    }

    /// <summary>Adjusts quantity. Returns the updated row rather than 204, so the sample
    /// covers a mutation that does have a response body.</summary>
    [HttpPost("{sku}/adjustments")]
    [ProducesResponseType<StockItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockItemResponse>> Adjust(
        string sku, [FromBody] StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var item = await database.StockItems
            .FirstOrDefaultAsync(s => s.Sku == sku && s.WarehouseId == request.WarehouseId, cancellationToken);

        if (item is null) return NotFound();

        var updated = item.QuantityOnHand + request.Delta;
        if (updated < 0)
            return BadRequest(new ProblemDetails { Title = "Adjustment would take quantity below zero." });

        item.QuantityOnHand = updated;
        item.LastCountedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);

        return Ok(StockItemResponse.From(item));
    }

    /// <summary>
    /// Removes a stock row entirely.
    /// <para>
    /// Routed under <c>items/</c> rather than as <c>{id:int}</c> on purpose. ASP.NET
    /// routing would happily distinguish <c>{id:int}</c> from the <c>{sku}</c> route above
    /// by constraint, but OpenAPI has no notion of route constraints: both collapse to the
    /// path signature <c>/api/stock/{}</c>, which the specification requires to be unique.
    /// The resulting document is invalid and InTest rejects it with exit code 2.
    /// </para>
    /// </summary>
    [HttpDelete("items/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var item = await database.StockItems.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (item is null) return NotFound();

        database.StockItems.Remove(item);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

/// <summary>Read-only. No DELETE, no POST — warehouses are managed elsewhere.</summary>
[ApiController]
[Route("api/warehouses")]
[Produces("application/json")]
public class WarehousesController(InventoryDbContext database) : ControllerBase
{
    /// <summary>Lists warehouses.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WarehouseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WarehouseResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var warehouses = await database.Warehouses.OrderBy(w => w.Name).ToListAsync(cancellationToken);
        return Ok(warehouses.Select(WarehouseResponse.From).ToList());
    }

    /// <summary>Gets one warehouse.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var warehouse = await database.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        return warehouse is null ? NotFound() : Ok(WarehouseResponse.From(warehouse));
    }
}

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

public record StockAdjustmentRequest
{
    public required int WarehouseId { get; init; }

    [Range(-10_000, 10_000)]
    public required int Delta { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}
