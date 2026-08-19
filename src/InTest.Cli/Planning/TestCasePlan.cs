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

/// <summary>
/// The OpenAPI-declared shape of a path parameter, as far as <see cref="TemplateRenderer"/>
/// needs to know it to pick an unmatchable-but-well-typed value for a non-success case (decision
/// 6). Review finding on Task 4: rendering <c>Guid.NewGuid().ToString()</c> for every path
/// parameter regardless of declared type sends an ill-typed value against a `type: integer`
/// parameter — an ASP.NET Core `[ApiController]` binding `int id` without a route constraint
/// answers 400 from model binding before the action's <c>NotFound()</c> path ever runs, so the
/// generated 404 case fails on every run. Only <see cref="Integer"/> gets special treatment;
/// every other declared type (string, its formats included, or nothing declared at all) still
/// takes a fresh GUID, which was already a well-typed unmatchable value for those.
/// </summary>
public enum PathParameterKind
{
    String,
    Integer
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
    bool HasRequestBody = false,
    // Parallel to PathParameterNames — same order, same length when set. Only TestPlanBuilder's
    // declared-error branch populates this (the only role that ever renders an unmatchable
    // value from it); every other call site, including every one that predates this field, is
    // read as "kind unknown", which TemplateRenderer treats identically to String — the same
    // GUID it always rendered, so no existing behaviour changes silently.
    IReadOnlyList<PathParameterKind>? PathParameterKinds = null);