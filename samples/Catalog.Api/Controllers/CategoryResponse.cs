using Catalog.Api.Domain;

namespace Catalog.Api.Controllers;

public record CategoryResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Notes { get; init; }

    public static CategoryResponse From(Category category)
        => new() { Id = category.Id, Name = category.Name, Notes = category.Notes };
}