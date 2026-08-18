namespace InTest.Cli.Fixtures;

/// <summary>
/// The result of comparing a committed fixture against what <see cref="FixtureComposer"/> would
/// compose from the current schema. Never mutates either document — <see cref="Commands.FixturesRepairCommand"/>
/// decides what to do with each list.
/// </summary>
public sealed record FixtureDriftResult(
    IReadOnlyList<string> MissingProperties,
    IReadOnlyList<string> StaleProperties,
    IReadOnlyList<string> MissingParameters);