namespace Catalog.Api.Contracts;

/// <summary>Paged envelope — a wrapper type rather than a bare array, so the generated
/// contract test asserts against a named schema in both shapes across the samples.</summary>
public record PagedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}