using System.ComponentModel.DataAnnotations;

namespace Catalog.Api.Controllers;

public record CreateCategoryRequest
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}