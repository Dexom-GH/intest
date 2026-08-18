using System.ComponentModel.DataAnnotations;

namespace Orders.Api.Controllers;

public record CreateCustomerRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }

    [MaxLength(30)]
    public string? PhoneNumber { get; init; }
}