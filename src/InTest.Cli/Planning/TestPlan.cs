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
    string Category);

public sealed record SkippedOperation(string OperationKey, string Reason);
