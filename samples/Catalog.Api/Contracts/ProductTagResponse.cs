using Catalog.Api.Domain;

namespace Catalog.Api.Contracts;

public record ProductTagResponse
{
    public required int Id { get; init; }
    public required string Label { get; init; }

    public static ProductTagResponse From(ProductTag tag) => new() { Id = tag.Id, Label = tag.Label };
}