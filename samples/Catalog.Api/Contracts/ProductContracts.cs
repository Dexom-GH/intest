using System.ComponentModel.DataAnnotations;
using Catalog.Api.Domain;

namespace Catalog.Api.Contracts;

/// <summary>Full product representation returned by read endpoints.</summary>
public record ProductResponse
{
    public required Guid Id { get; init; }
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required decimal Price { get; init; }
    public required int StockQuantity { get; init; }
    public required long ViewCount { get; init; }
    public required double WeightKilograms { get; init; }
    public float? RatingAverage { get; init; }
    public required bool IsActive { get; init; }
    public required ProductCategory Category { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTime? DiscontinuedAt { get; init; }
    public DateOnly? AvailableFrom { get; init; }
    public TimeOnly? DailyCutoff { get; init; }
    public TimeSpan? LeadTime { get; init; }
    public byte[]? Thumbnail { get; init; }
    public string? ProductUrl { get; init; }
    public string? SupplierEmail { get; init; }
    public Guid? ParentProductId { get; init; }
    public required Guid CategoryId { get; init; }
    public DimensionsResponse? Dimensions { get; init; }
    public required IReadOnlyList<ProductTagResponse> Tags { get; init; }

    public static ProductResponse From(Product product) => new()
    {
        Id = product.Id,
        Sku = product.Sku,
        Name = product.Name,
        Description = product.Description,
        Price = product.Price,
        StockQuantity = product.StockQuantity,
        ViewCount = product.ViewCount,
        WeightKilograms = product.WeightKilograms,
        RatingAverage = product.RatingAverage,
        IsActive = product.IsActive,
        Category = product.Category,
        CreatedAt = product.CreatedAt,
        DiscontinuedAt = product.DiscontinuedAt,
        AvailableFrom = product.AvailableFrom,
        DailyCutoff = product.DailyCutoff,
        LeadTime = product.LeadTime,
        Thumbnail = product.Thumbnail,
        ProductUrl = product.ProductUrl,
        SupplierEmail = product.SupplierEmail,
        ParentProductId = product.ParentProductId,
        CategoryId = product.CategoryId,
        Dimensions = product.Dimensions is null ? null : DimensionsResponse.From(product.Dimensions),
        Tags = product.Tags.Select(ProductTagResponse.From).ToList()
    };
}

public record DimensionsResponse
{
    public required double LengthCentimetres { get; init; }
    public required double WidthCentimetres { get; init; }
    public required double HeightCentimetres { get; init; }

    public static DimensionsResponse From(Dimensions dimensions) => new()
    {
        LengthCentimetres = dimensions.LengthCentimetres,
        WidthCentimetres = dimensions.WidthCentimetres,
        HeightCentimetres = dimensions.HeightCentimetres
    };
}

public record ProductTagResponse
{
    public required int Id { get; init; }
    public required string Label { get; init; }

    public static ProductTagResponse From(ProductTag tag) => new() { Id = tag.Id, Label = tag.Label };
}

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

public record DimensionsRequest
{
    [Range(0, 10_000)] public required double LengthCentimetres { get; init; }
    [Range(0, 10_000)] public required double WidthCentimetres { get; init; }
    [Range(0, 10_000)] public required double HeightCentimetres { get; init; }
}

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

/// <summary>Paged envelope — a wrapper type rather than a bare array, so the generated
/// contract test asserts against a named schema in both shapes across the samples.</summary>
public record PagedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
