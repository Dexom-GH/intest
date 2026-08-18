using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Api.Controllers;

/// <summary>
/// Every action requires a token. Reads need the read scope, writes need the write scope —
/// so a read-only client receives 403 on writes, which is what the generated wrong-scope
/// auth tests assert. This controller <b>does</b> expose DELETE; CustomersController does not.
/// </summary>
[ApiController]
[Route("api/orders")]
[Tags("Orders")]
[Produces("application/json")]
[Authorize(Policy = Policies.Read)]
public class OrdersController(OrdersDbContext database) : ControllerBase
{
    /// <summary>Lists orders, optionally filtered by status.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] string? customerEmail,
        CancellationToken cancellationToken = default)
    {
        var query = database.Orders.Include(o => o.Lines).Include(o => o.Customer).AsQueryable();

        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            query = query.Where(o => o.Customer!.Email == customerEmail);
        }

        var orders = await query.OrderBy(o => o.Reference).ToListAsync(cancellationToken);
        return Ok(orders.Select(OrderResponse.From).ToList());
    }

    /// <summary>Gets one order.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await database.Orders.Include(o => o.Lines)
                                         .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null ? NotFound() : Ok(OrderResponse.From(order));
    }

    /// <summary>Creates an order. Requires the write scope, so a read-only token gets 403.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Write)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (!await database.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken))
        {
            return BadRequest(new ProblemDetails { Title = $"Customer '{request.CustomerId}' does not exist." });
        }

        if (await database.Orders.AnyAsync(o => o.Reference == request.Reference, cancellationToken))
        {
            return Conflict(new ProblemDetails { Title = $"Order '{request.Reference}' already exists." });
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Reference = request.Reference,
            CustomerId = request.CustomerId,
            Status = OrderStatus.Draft,
            CurrencyCode = request.CurrencyCode,
            PlacedAt = DateTimeOffset.UtcNow,
            RequestedDeliveryDate = request.RequestedDeliveryDate,
            Notes = request.Notes,
            TestRunId = Request.Headers["X-Test-Run-Id"].FirstOrDefault(),
            Lines = request.Lines.Select(l => new OrderLine
            {
                Sku = l.Sku,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice
            }).ToList()
        };

        order.TotalAmount = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        database.Orders.Add(order);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, OrderResponse.From(order));
    }

    /// <summary>Cancels an order. Already-shipped orders return 409 — a state conflict.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var order = await database.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered)
        {
            return Conflict(new ProblemDetails { Title = $"Order in status '{order.Status}' cannot be cancelled." });
        }

        order.Status = OrderStatus.Cancelled;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}