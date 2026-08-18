namespace Catalog.Api.Domain;

/// <summary>Owned type — renders as a nested inline object rather than a `$ref`.</summary>
public class Dimensions
{
    public double LengthCentimetres { get; set; }
    public double WidthCentimetres { get; set; }
    public double HeightCentimetres { get; set; }
}