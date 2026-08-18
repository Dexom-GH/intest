using System.ComponentModel.DataAnnotations;

namespace Catalog.Api.Domain;

/// <summary>Referenced by Product and deletable, unlike Product itself — the samples
/// deliberately differ in which controllers expose DELETE.</summary>
public class Category
{
    public Guid Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public List<Product> Products { get; set; } = [];
}