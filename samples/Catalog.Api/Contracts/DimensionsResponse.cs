using Catalog.Api.Domain;

namespace Catalog.Api.Contracts;

public record DimensionsResponse
{
    public required double LengthCentimetres { get; init; }
    public required double WidthCentimetres { get; init; }
    public required double HeightCentimetres { get; init; }

    public static DimensionsResponse From(Dimensions dimensions) => new()
    {
        LengthCentimetres = dimensions.LengthCentimetres,
        WidthCentimetres = dimensions.WidthCentimetres,
        HeightCentimetres = dimensions.HeightCentimetres
    };
}