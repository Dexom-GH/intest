namespace InTest.Cli.Planning;

/// <summary>Framework-neutral. No MSTest type may appear in this file (§3).</summary>
public sealed record TestPlan(
    string Title,
    IReadOnlyList<TestClassPlan> Classes,
    IReadOnlyList<SkippedOperation> Skipped,
    // Skips remove tests; notes do not (§12). See CoverageNote for why this is a separate list
    // rather than folded into Skipped.
    IReadOnlyList<CoverageNote> Notes);