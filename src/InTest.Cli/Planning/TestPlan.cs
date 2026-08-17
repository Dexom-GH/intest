namespace InTest.Cli.Planning;

/// <summary>Framework-neutral. No MSTest type may appear in this file (§3).</summary>
public sealed record TestPlan(string Title, IReadOnlyList<TestClassPlan> Classes, IReadOnlyList<SkippedOperation> Skipped);

public sealed record TestClassPlan(string ClassName, string Tag, IReadOnlyList<TestCasePlan> Cases);

public sealed record TestCasePlan(
    string MethodName,
    string DisplayName,
    string OperationKey,
    bool OperationKeySynthesized,
    string HttpMethod,
    string PathTemplate,
    IReadOnlyList<string> PathParameterNames,
    int ExpectedStatus,
    string? SchemaKey,
    string Category,
    // Carries FixtureComposer.NeedsFixture's verdict for this operation so that no other caller
    // (fixtures repair, chiefly) ever has to recompute or restate it — a divergence between a
    // second copy of this logic and the composer's own is a defect this branch already fixed
    // twice. Defaults to true so call sites outside fixture handling, which never asked for a
    // NeedsFixture opinion, are unaffected.
    bool NeedsFixture = true);

public sealed record SkippedOperation(string OperationKey, string Reason);
