using System.ComponentModel.DataAnnotations;

namespace Orders.Api.Domain;

public class Customer
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }

    public List<Order> Orders { get; set; } = [];
}