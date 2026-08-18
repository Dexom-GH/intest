using System.ComponentModel.DataAnnotations;
using Catalog.Api.Domain;

namespace Catalog.Api.Contracts;

public record CreateProductRequest
{
    [Required, MaxLength(32), RegularExpression("^[A-Z]{3}-[0-9]{4}$")]
    public required string Sku { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    [Range(0.01, 1_000_000)]
    public required decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public required int StockQuantity { get; init; }

    public required Guid CategoryId { get; init; }

    public ProductCategory Category { get; init; } = ProductCategory.Unknown;

    public DateOnly? AvailableFrom { get; init; }

    [EmailAddress, MaxLength(320)]
    public string? SupplierEmail { get; init; }

    public DimensionsRequest? Dimensions { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }
}