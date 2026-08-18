using System.ComponentModel.DataAnnotations;

namespace Catalog.Api.Domain;

public class ProductTag
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Label { get; set; } = string.Empty;

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }
}