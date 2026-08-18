using System.ComponentModel.DataAnnotations;

namespace Catalog.Api.Contracts;

public record UpdateProductRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    [Range(0.01, 1_000_000)]
    public required decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public required int StockQuantity { get; init; }

    public required bool IsActive { get; init; }
}