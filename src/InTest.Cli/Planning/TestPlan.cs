namespace InTest.Cli.Planning;

/// <summary>Framework-neutral. No MSTest type may appear in this file (§3).</summary>
public sealed record TestPlan(string Title, IReadOnlyList<TestClassPlan> Classes, IReadOnlyList<SkippedOperation> Skipped);