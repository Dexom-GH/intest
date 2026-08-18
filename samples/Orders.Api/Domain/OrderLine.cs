using System.ComponentModel.DataAnnotations;

namespace Orders.Api.Domain;

public class OrderLine
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    [Required, MaxLength(32)]
    public string Sku { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}