using Inventory.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

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