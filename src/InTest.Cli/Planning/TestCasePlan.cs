namespace InTest.Cli.Planning;

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