using Orders.Api.Domain;

namespace Orders.Api.Controllers;

public record CustomerResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? PhoneNumber { get; init; }
    public required DateTimeOffset RegisteredAt { get; init; }

    public static CustomerResponse From(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Email = customer.Email,
        PhoneNumber = customer.PhoneNumber,
        RegisteredAt = customer.RegisteredAt
    };
}