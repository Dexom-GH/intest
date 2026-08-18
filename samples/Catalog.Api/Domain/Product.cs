using System.ComponentModel.DataAnnotations;

namespace Catalog.Api.Domain;

/// <summary>
/// Deliberately exercises every primitive the OpenAPI type system distinguishes, plus the
/// nullable variant of each. `null` vs. omitted vs. empty is the distinction a DTO-level unit
/// test cannot make, which is why the generated suite has to cross a real HTTP boundary.
/// </summary>
public class Product
{
    public Guid Id { get; set; }

    /// <summary>Unique. A second product with the same SKU produces a 409 from a real
    /// unique index, which is why the samples use SQLite rather than the InMemory provider.</summary>
    [Required, MaxLength(32), RegularExpression("^[A-Z]{3}-[0-9]{4}$")]
    public string Sku { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Nullable string — distinguishes null from omitted from empty.</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    [Range(0.01, 1_000_000)]
    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public long ViewCount { get; set; }

    public double WeightKilograms { get; set; }

    public float? RatingAverage { get; set; }

    public bool IsActive { get; set; }

    public ProductCategory Category { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTime? DiscontinuedAt { get; set; }

    public DateOnly? AvailableFrom { get; set; }

    public TimeOnly? DailyCutoff { get; set; }

    public TimeSpan? LeadTime { get; set; }

    /// <summary>Binary content — serializes as a base64 string with format byte.</summary>
    public byte[]? Thumbnail { get; set; }

    [MaxLength(500)]
    public string? ProductUrl { get; set; }

    [EmailAddress, MaxLength(320)]
    public string? SupplierEmail { get; set; }

    /// <summary>Self-referencing. Bundling schemas under `definitions` terminates here;
    /// inlining them would not.</summary>
    public Guid? ParentProductId { get; set; }

    public Product? ParentProduct { get; set; }

    /// <summary>Array of references.</summary>
    public List<ProductTag> Tags { get; set; } = [];

    /// <summary>Nested object, nullable — an owned entity rather than a relation.</summary>
    public Dimensions? Dimensions { get; set; }

    public Guid CategoryId { get; set; }

    public Category? CategoryEntity { get; set; }
}