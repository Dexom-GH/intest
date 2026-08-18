namespace InTest.Cli.Fixtures;

public sealed class FixtureMeta
{
    public required int Tier { get; init; }
    public required string OperationId { get; init; }
    public required string GeneratedBy { get; init; }
}