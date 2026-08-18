using Catalog.Api.Contracts;
using Catalog.Api.Data;
using Catalog.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Controllers;

/// <summary>
/// Read and write, but deliberately <b>no DELETE</b> — products are never removed, only
/// deactivated. CategoriesController exposes DELETE, so the pair proves InTest generates
/// per declared operation rather than per assumed CRUD shape.
/// </summary>
[ApiController]
[Route("api/products")]
[Tags("Products")]
[Produces("application/json")]
public class ProductsController(CatalogDbContext database) : ControllerBase
{
    /// <summary>Lists products. Exercises query parameters in every scalar shape, plus a
    /// header parameter — §9's variation catalog is per-position, and header is a position.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<ProductResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<ProductResponse>>> List(
        [FromQuery] string? name,
        [FromQuery] decimal? minPrice,
        [FromQuery] ProductCategory? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromHeader(Name = "X-Include-Inactive")] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            return BadRequest(new ProblemDetails { Title = "page must be >= 1 and pageSize between 1 and 100." });
        }

        var query = database.Products.Include(p => p.Tags).AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(p => p.Name.Contains(name));
        }
        if (minPrice is not null)
        {
            query = query.Where(p => p.Price >= minPrice);
        }
        if (category is not null)
        {
            query = query.Where(p => p.Category == category);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(p => p.Sku)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<ProductResponse>
        {
            Items = items.Select(ProductResponse.From).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>Gets one product. The 404 branch is what makes a declared-error contract
    /// test deterministic and fixture-free.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products.Include(p => p.Tags)
                                             .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return product is null ? NotFound() : Ok(ProductResponse.From(product));
    }

    /// <summary>Nested route, so path composition is exercised beyond a single segment.</summary>
    [HttpGet("{id:guid}/tags")]
    [ProducesResponseType<IReadOnlyList<ProductTagResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProductTagResponse>>> GetTags(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products.Include(p => p.Tags)
                                             .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return product is null
            ? NotFound()
            : Ok(product.Tags.Select(ProductTagResponse.From).ToList());
    }

    /// <summary>Creates a product. Returns 201 with a Location header; a duplicate SKU
    /// returns 409 from a real unique index rather than from application code.</summary>
    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(
        [FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        if (!await database.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            return BadRequest(new ProblemDetails { Title = $"Category '{request.CategoryId}' does not exist." });
        }

        if (await database.Products.AnyAsync(p => p.Sku == request.Sku, cancellationToken))
        {
            return Conflict(new ProblemDetails { Title = $"A product with SKU '{request.Sku}' already exists." });
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = request.Sku,
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            Category = request.Category,
            CategoryId = request.CategoryId,
            AvailableFrom = request.AvailableFrom,
            SupplierEmail = request.SupplierEmail,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Dimensions = request.Dimensions is null ? null : new Dimensions
            {
                LengthCentimetres = request.Dimensions.LengthCentimetres,
                WidthCentimetres = request.Dimensions.WidthCentimetres,
                HeightCentimetres = request.Dimensions.HeightCentimetres
            },
            Tags = (request.Tags ?? []).Select(label => new ProductTag { Label = label }).ToList()
        };

        database.Products.Add(product);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ProductResponse.From(product));
    }

    /// <summary>Updates a product. 204 carries no body by definition, which is the case an
    /// earlier revision of InTest silently skipped entirely.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await database.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.IsActive = request.IsActive;

        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
