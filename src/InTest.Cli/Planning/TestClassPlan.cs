namespace InTest.Cli.Planning;

public sealed record TestClassPlan(string ClassName, string Tag, IReadOnlyList<TestCasePlan> Cases);