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