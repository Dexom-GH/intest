using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Api.Controllers;

/// <summary>
/// No DELETE — customers are retained for audit. OrdersController exposes one, so the two
/// together prove generation follows the spec rather than an assumed CRUD shape.
/// </summary>
[ApiController]
[Route("api/customers")]
[Tags("Customers")]
[Produces("application/json")]
[Authorize(Policy = Policies.Read)]
public class CustomersController(OrdersDbContext database) : ControllerBase
{
    /// <summary>Lists customers.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CustomerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> List(CancellationToken cancellationToken)
    {
        var customers = await database.Customers.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return Ok(customers.Select(CustomerResponse.From).ToList());
    }

    /// <summary>Gets one customer.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await database.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return customer is null ? NotFound() : Ok(CustomerResponse.From(customer));
    }

    /// <summary>Registers a customer. A duplicate email returns 409 from a unique index.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Write)]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (await database.Customers.AnyAsync(c => c.Email == request.Email, cancellationToken))
        {
            return Conflict(new ProblemDetails { Title = $"A customer with email '{request.Email}' already exists." });
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        database.Customers.Add(customer);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, CustomerResponse.From(customer));
    }
}