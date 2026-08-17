using System.ComponentModel.DataAnnotations;
using Catalog.Api.Data;
using Catalog.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Controllers;

/// <summary>
/// Unlike ProductsController, this one <b>does</b> expose DELETE. The pair is deliberate:
/// InTest must generate from what the spec declares, never from an assumed CRUD shape.
/// </summary>
[ApiController]
[Route("api/categories")]
[Tags("Categories")]
[Produces("application/json")]
public class CategoriesController(CatalogDbContext database) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> List(CancellationToken cancellationToken)
    {
        var categories = await database.Categories.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return Ok(categories.Select(CategoryResponse.From).ToList());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var category = await database.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return category is null ? NotFound() : Ok(CategoryResponse.From(category));
    }

    [HttpPost]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> Create(
        [FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        if (await database.Categories.AnyAsync(c => c.Name == request.Name, cancellationToken))
            return Conflict(new ProblemDetails { Title = $"A category named '{request.Name}' already exists." });

        var category = new Category { Id = Guid.NewGuid(), Name = request.Name, Notes = request.Notes };

        database.Categories.Add(category);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = category.Id }, CategoryResponse.From(category));
    }

    /// <summary>
    /// Deletes a category. A category still referenced by a product returns 409 because the
    /// foreign key is Restrict — a real relational constraint, not an application check.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var category = await database.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null) return NotFound();

        if (await database.Products.AnyAsync(p => p.CategoryId == id, cancellationToken))
            return Conflict(new ProblemDetails { Title = "Category is referenced by one or more products." });

        database.Categories.Remove(category);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public record CategoryResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Notes { get; init; }

    public static CategoryResponse From(Category category)
        => new() { Id = category.Id, Name = category.Name, Notes = category.Notes };
}

public record CreateCategoryRequest
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}
