namespace InTest.Cli.Planning;

/// <summary>
/// One thing InTest chose not to generate a case for, while every other case the operation
/// earned still generated and runs — decision 5's 404-declared-with-no-path-parameter case is
/// the first occupant. Kept apart from <see cref="SkippedOperation"/> because §12
/// (docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md:1460) draws that line
/// deliberately: "skips remove tests. Notes do not." A consumer that folded both into one list
/// could not tell an operation with a passing generated test from one with none at all —
/// GenerateCommand's "Skipped N operation(s)" line and coverage-report.json's `skipped` array
/// both read <c>TestPlan.Skipped</c> alone, so an operation belongs here instead whenever its
/// other cases still generated.
/// </summary>
public sealed record CoverageNote(string OperationKey, string Reason);
