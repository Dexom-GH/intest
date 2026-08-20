namespace InTest.Cli.Planning;

/// <summary>
/// What a case's <see cref="TestCasePlan.ExpectedStatus"/> represents. Declared errors are never
/// inferred (decision 5) — a case exists in this role only because the spec itself declared the
/// response InTest generated it from. <see cref="Auth"/> (Task 5) is different again: it is never
/// read off a declared response either — it exists because the operation declares `security`, and
/// its expected status (401 or 403) comes from decision 3's fixed pair, not from anything the spec
/// enumerates in `responses`. v1-c generates only these three.
/// </summary>
public enum CaseRole
{
    Success,
    DeclaredError,

    /// <summary>
    /// A no-token 401 case or a wrong-scope 403 case (decision 3), generated once each for every
    /// operation that declares `security` — independent of whether the operation's own
    /// `responses` enumerate 401 or 403 at all. Carries an <see cref="TestCasePlan.Slot"/>
    /// (decision 7) selecting which identity the generated case authenticates as:
    /// <see cref="IdentitySlot.None"/> for the 401 case, <see cref="IdentitySlot.Secondary"/> for
    /// the 403 case. Like <see cref="DeclaredError"/>, always fixture-free and pointed at an
    /// unmatchable id (decision 6) — see <see cref="TestCasePlan.NeedsFixture"/> and
    /// <see cref="TestCasePlan.PathParameterKinds"/> on this role's cases.
    /// </summary>
    Auth
}

/// <summary>
/// Which identity a generated auth case authenticates as (decision 7) — never a literal identity
/// name, since the CLI generates this plan long before any adopter has written an
/// <c>ITestTokenProvider</c> and cannot know one. Mirrors <c>InTest.Runtime.IdentitySlot</c> by
/// name only: this project does not, and must not, reference <c>InTest.Runtime</c> (the CLI
/// generates code for a project that references it, it does not consume it), so
/// <see cref="Rendering.TemplateRenderer"/> is what turns a value here into the literal
/// <c>IdentitySlot.Whatever</c> text the rendered method body names — the generated code's own
/// <c>using InTest.Runtime;</c> is what makes that symbol resolve there.
/// </summary>
public enum IdentitySlot
{
    /// <summary>No override: the ambient identity <c>ApiTestBase.ApiTestInitialize</c> already
    /// set. Every case that is not <see cref="CaseRole.Auth"/> carries this by default and the
    /// template emits nothing for it — the reason every existing success case stays
    /// byte-identical in the golden file once <see cref="CaseRole.Auth"/> exists.</summary>
    Default,

    /// <summary>Some other identity than <see cref="Default"/> — the wrong-scope 403 case.</summary>
    Secondary,

    /// <summary>Send no token at all — the no-token 401 case.</summary>
    None
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
    // Parallel to PathParameterNames — same order, same length when set. TestPlanBuilder's
    // declared-error and auth branches both populate this (the only roles that ever render an
    // unmatchable value from it); every other call site, including every one that predates this
    // field, is read as "kind unknown", which TemplateRenderer treats identically to String — the
    // same GUID it always rendered, so no existing behaviour changes silently.
    IReadOnlyList<PathParameterKind>? PathParameterKinds = null,
    // Decision 7: which identity a CaseRole.Auth case authenticates as. Defaults to Default, the
    // no-override slot, so every call site that predates Task 5 — none of which had a slot to
    // state, including every Success and DeclaredError case — renders exactly as it always did:
    // TemplateRenderer emits nothing for Default.
    IdentitySlot Slot = IdentitySlot.Default,
    // The distinct union of OAuth scopes the operation's `security` declares, across every
    // requirement and every scheme within it — carried, not recomputed, for the same reason
    // Role above is: a later task's template/render phase needs to pass these scopes to a
    // runtime guard for the wrong-scope 403 case, and it must not have to re-parse
    // OpenApiOperation.Security itself to get them. This plan is the single source of truth for
    // what the spec declared; a render-time re-derivation is a second copy of that logic that
    // could drift from this one, the same class of defect Role's comment already warns about for
    // NeedsFixture. Defaults to an empty array, never null, so every call site that predates
    // this member — every Success and DeclaredError case, and the 401 case, none of which have a
    // scope requirement to state — reads as "nothing required" rather than an absent value a
    // consumer would have to null-check. TestPlanBuilder.PlanAuthCases is the only site that
    // assigns a non-default value, and it always assigns the result of a Distinct() projection
    // (never a nullable expression), so this invariant holds end to end, not just at the default.
    IReadOnlyList<string>? RequiredScopes = null)
{
    // Collection-typed record parameters cannot default to a non-constant expression
    // (Array.Empty<string>() is not a compile-time constant) directly in the parameter list, so
    // the primary constructor parameter above stays nullable and this explicitly-declared
    // property — which overrides the compiler-generated one for the same-named positional
    // parameter — normalizes it to an empty array instead. Every call site that never states
    // RequiredScopes, and every call site that passes null outright, reads back a non-null empty
    // collection, never a null reference.
    public IReadOnlyList<string> RequiredScopes { get; init; } = RequiredScopes ?? Array.Empty<string>();
}