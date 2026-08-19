namespace InTest.Cli.Planning;

/// <summary>
/// What a case's <see cref="TestCasePlan.ExpectedStatus"/> represents. Declared errors are never
/// inferred (decision 5) — a case exists in this role only because the spec itself declared the
/// response InTest generated it from. Task 5 adds <c>Auth</c>; v1-c generates only these two.
/// </summary>
public enum CaseRole
{
    Success,
    DeclaredError
}

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
    // Part of the case's identity, not a derived property computed at render time — decision 4.
    // TestPlanBuilder's dedupe machinery keys its proposed-name dictionary on operation key
    // *and* role together: two cases for the same operation (a success and a declared error)
    // deliberately get different method names, and only get the same *hash suffix* input when
    // they also share a role — collapsing role into the operation key alone reassigns every
    // case for an operation the same deduped name, which is CS0111 the moment an operation
    // emits more than one case. Defaults to Success so every call site that predates decision 5
    // — none of which had a role to state — is read as what it always was.
    CaseRole Role = CaseRole.Success,
    // Carries FixtureComposer.NeedsFixture's verdict for this operation so that no other caller
    // (fixtures repair, chiefly) ever has to recompute or restate it — a divergence between a
    // second copy of this logic and the composer's own is a defect this branch already fixed
    // twice. Defaults to true so call sites outside fixture handling, which never asked for a
    // NeedsFixture opinion, are unaffected.
    bool NeedsFixture = true,
    // All declared `in: query` parameter names, whether or not the composer ends up emitting a
    // fixture entry for each one (decision 1). The template only needs this to decide whether an
    // operation has query parameters at all, so it knows whether to look any up at runtime — it
    // is not a restatement of FixtureComposer's tiered precedence, just a presence check. Null
    // (the default) means "not computed", read as empty by every consumer.
    IReadOnlyList<string>? QueryParameterNames = null,
    // Whether the operation has an `application/json` request body with a schema to compose from
    // — FixtureComposer.HasJsonBodyToCompose is the sole authority on this (same reasoning as
    // NeedsFixture above), so this is set from that method directly rather than re-derived here.
    bool HasRequestBody = false);