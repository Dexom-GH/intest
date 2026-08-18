using System.ComponentModel.DataAnnotations;

namespace Catalog.Api.Contracts;

public record DimensionsRequest
{
    [Range(0, 10_000)] public required double LengthCentimetres { get; init; }
    [Range(0, 10_000)] public required double WidthCentimetres { get; init; }
    [Range(0, 10_000)] public required double HeightCentimetres { get; init; }
}