using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InTest.Runtime;

/// <summary>
/// Ambient context for generated tests: configuration, services, client, identifiers and
/// scope lifecycle. Deliberately nothing domain-specific — base classes in test projects
/// become dumping grounds, so helpers belong in the team's own base class.
/// </summary>
public abstract class ApiTestBase
{
    private IServiceScope _scope = null!;

    public TestContext TestContext { get; set; } = null!;

    protected IConfiguration Config => TestHost.Configuration;
    protected IServiceProvider Services => _scope.ServiceProvider;
    protected SchemaBundle Schemas => TestHost.Schemas;
    protected string RunId => TestHost.RunIdValue;

    /// <summary>
    /// Derived from TestDisplayName, never TestName: TestName returns the bare method name
    /// for every [DataRow] row, so all variations of an operation would share one id.
    /// </summary>
    protected string TestId => InTestId.ForTest(TestHost.RunIdValue, TestContext.TestDisplayName);

    protected HttpClient Client { get; private set; } = null!;

    [TestInitialize]
    public void ApiTestInitialize()
    {
        _scope = TestHost.Root.CreateScope();
        InTestAmbient.TestId.Value = TestId;

        // The Default slot, resolved (v1-c decision 7): every test authenticates as this unless
        // a generated auth case overrides it before sending its request. Resolved here, once per
        // test, from whatever ITestTokenProvider the generated project registered — never a
        // literal identity name, since the CLI that generated this suite could not have known one.
        //
        // Reads TestHost.TokenProvider rather than resolving a fresh instance from _scope
        // (Task 10 item 1): RequireMultipleIdentities and UseIdentity already read that same
        // static, and under the scaffold's documented AddSingleton registration the two are the
        // same object — but under any other lifetime they would not be, so a provider whose
        // Identities is computed per instance could gate the 403 case on one object while this
        // Default identity came from another. Reading the static here removes that lifetime
        // question entirely, since it is already resolved from the same container.
        InTestAmbient.Identity.Value = ResolveDefaultIdentity(TestHost.TokenProvider);

        Client = _scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
    }

    [TestCleanup]
    public void ApiTestCleanup()
    {
        InTestAmbient.TestId.Value = null;
        InTestAmbient.Identity.Value = null;
        _scope.Dispose();
    }

    /// <summary>
    /// The Default slot resolved to a concrete identity (v1-c decision 7): <c>Identities[0]</c>
    /// when the provider advertises at least one, otherwise <see cref="InTestIdentities.None"/> —
    /// including when <paramref name="provider"/> itself is null, which is the ordinary state for
    /// every spec that declares no <c>security</c> (question (b), and Catalog/Inventory's own
    /// scaffolds). <c>ITestTokenProvider.Identities</c>'s own doc already documents a count of
    /// zero as contemplated, not an error; indexing <c>Identities[0]</c> without this check would
    /// throw <see cref="ArgumentOutOfRangeException"/> here, in <c>[TestInitialize]</c>, before a
    /// single request is built — for every test in the suite, not just the auth ones.
    /// <para>
    /// Pulled out as an internal, dependency-free seam — rather than left inline in
    /// <see cref="ApiTestInitialize"/> — because that method needs a live <c>TestHost.Root</c> to
    /// exercise at all, and this decision deserves its own test independent of that weight.
    /// </para>
    /// </summary>
    internal static string ResolveDefaultIdentity(ITestTokenProvider? provider) =>
        provider?.Identities is { Count: > 0 } identities ? identities[0].Name : InTestIdentities.None;

    /// <summary>
    /// Generated 403 (wrong-scope) cases call this first, before anything else in the method
    /// body — decision 3's replacement for <c>MemberCondition</c>, which was measured to be
    /// evaluated 15ms before <c>[AssemblyInitialize]</c> on MSTest 4.3.3 and so can never see
    /// anything the DI container built. <see cref="Assert.Inconclusive(string)"/> runs inside the
    /// test body instead, after <see cref="TestHost.InitializeAsync"/> has genuinely finished, so
    /// it can consult the real, registered <see cref="ITestTokenProvider"/> rather than a config
    /// flag that can drift from it.
    /// <para>
    /// <c>protected internal</c>: <c>protected</c> so the generated suite — a different assembly
    /// deriving from <see cref="ApiTestBase"/> — can call it exactly like its <c>protected
    /// static</c> neighbours <see cref="RequireFixture"/> and <see cref="FixtureBody"/>;
    /// <c>internal</c> so <c>InTest.Runtime.Tests</c> can call it directly, without a test-only
    /// subclass, via the <c>InternalsVisibleTo</c> already in <c>InTest.Runtime.csproj</c>. Plain
    /// <c>protected static</c> would match the neighbours but leave those tests unable to reach
    /// it at all.
    /// </para>
    /// <para>
    /// The message passed to <c>Assert.Inconclusive</c> is what makes this decision 3's actual
    /// argument rather than a quieter skip: confirmed on MSTest 4.3.3 / .NET 10 to survive
    /// verbatim into the .trx's <c>&lt;Message&gt;</c>, prefixed only with
    /// "Assert.Inconclusive. " — and the .trx spells the outcome <c>NotExecuted</c>, not the
    /// console summary's "Skipped".
    /// </para>
    /// </summary>
    protected internal static void RequireMultipleIdentities()
    {
        var provider = TestHost.TokenProvider;

        // `?.Identities?.Count` — not `?.Identities.Count` (Task 10 item 3): the first `?.`
        // guards only "no provider registered"; a registered provider whose Identities is
        // itself null (the property is non-nullable only by annotation, not by anything the
        // runtime enforces) would still throw NullReferenceException on an unguarded
        // `.Count`. ResolveDefaultIdentity's own `provider?.Identities is { Count: > 0 }`
        // already guards both cases — this must match its neighbour.
        var count = provider?.Identities?.Count ?? 0;
        if (count >= 2)
        {
            return;
        }

        // Task 10 item 4: branched on whether a provider is registered at all, not just its
        // count. "The registered ITestTokenProvider advertises 0 identities" sends a reader
        // hunting for a bug in code they never wrote when the true state — the day-one
        // scaffold, and every spec declaring no `security` — is that there is no registered
        // provider at all. Decision 3's whole argument for Assert.Inconclusive over a quieter
        // skip is that the reason stays visible *and correct*.
        Assert.Inconclusive(provider is null
            ? "Skipped: no ITestTokenProvider is registered; a wrong-scope 403 test needs at least 2 identities."
            : $"Skipped: the registered ITestTokenProvider advertises {count} identit{(count == 1 ? "y" : "ies")}; " +
              "a wrong-scope 403 test needs at least 2.");
    }

    /// <summary>
    /// Generated wrong-scope 403 cases call this — after <see cref="RequireMultipleIdentities"/>,
    /// before building their request — because a second identity existing is not enough to make a
    /// 403 provable: if the secondary identity's own declared <see cref="TestIdentity.Scopes"/>
    /// already cover everything <paramref name="requiredScopes"/> lists, it is authorized for the
    /// operation and a 403 assertion would fail against a correct API. A read-only identity is
    /// never "wrong scope" for a read it actually holds.
    /// <para>
    /// Containment is over the whole set: the secondary identity must hold <em>every</em> scope in
    /// <paramref name="requiredScopes"/> before the test is skipped. Holding only some of several
    /// required scopes does not authorize the operation, so the 403 is still real and the test
    /// still runs — <c>All</c>, not <c>Any</c>.
    /// </para>
    /// <para>
    /// Guarded the same way <see cref="RequireMultipleIdentities"/> is, not the way
    /// <see cref="ResolveIdentitySlot"/> is (they are deliberately opposite) — but not because
    /// nothing runs before this one. Task 4 emits <see cref="RequireMultipleIdentities"/> first
    /// and this member second, in the same generated method body, so in generated code its own
    /// provider/<c>Identities</c>/count checks are strictly redundant. It guards anyway for two
    /// reasons: its wrong answer is silent — <see cref="ResolveIdentitySlot"/> failing throws,
    /// loud and immediate, while this one failing the guard *skips a test*, which looks like
    /// success — and it is <c>protected internal</c> on a shipped base class, so an adopter's
    /// hand-written 403 test can call it directly, with nothing having run before it. This gate
    /// reaches further than <see cref="ResolveIdentitySlot"/> does — all the way to
    /// <c>Identities[1]</c> and its <see cref="TestIdentity.Scopes"/> — so every one of "no
    /// provider", "<c>Identities</c> itself null", and "fewer than two identities" must fall
    /// through to this method returning without a skip. v1-c shipped a live
    /// <see cref="NullReferenceException"/> on exactly this shape (a provider guarded, but not
    /// its <c>Identities</c>) in <see cref="RequireMultipleIdentities"/> itself; this guard
    /// exists precisely so that mistake is not repeated one index further in. A <c>null</c>
    /// <see cref="TestIdentity.Scopes"/> also falls through here — not declared / unknown means
    /// run and allow the test to fail, never skip.
    /// </para>
    /// <para>
    /// "The second element itself is null" also falls through this method without a skip — but
    /// that is narrower than it sounds. This method only ever guarantees it will not itself
    /// <em>skip</em> the test on that shape; it does not guarantee the test goes on to run.
    /// A provider whose second element is null violates <see cref="ITestTokenProvider.Identities"/>'s
    /// non-null annotation, and the generated case's very next call, <see cref="UseIdentity"/>,
    /// resolves through <see cref="ResolveIdentitySlot"/>, which indexes
    /// <c>Identities[1].Name</c> unguarded and throws <see cref="NullReferenceException"/> on
    /// exactly that shape. That is intended: failing loudly on a provider that breaks its own
    /// contract is preferable to this method inventing a defensive skip for a state it has no
    /// principled reason to call "not a 403".
    /// </para>
    /// <para>
    /// <c>protected internal</c> for the same two reasons as <see cref="RequireMultipleIdentities"/>.
    /// </para>
    /// </summary>
    protected internal static void RequireSecondaryIdentityLacks(params string[] requiredScopes)
    {
        var provider = TestHost.TokenProvider;
        if (provider?.Identities is not { Count: >= 2 } identities || identities[1] is not { } secondary)
        {
            return;
        }

        if (secondary.Scopes is not { } scopes) return;      // undeclared: unknown means run
        if (requiredScopes is not { Length: > 0 }) return;   // no requirement to compare against
        // requiredScopes.All(...) is vacuously true over an empty requiredScopes, which is why
        // the line above must be its own check rather than falling through to this one: a
        // scope-free operation can still 403 on other grounds (tenant, role, resource
        // ownership), and skipping would assert something this code has no basis for.
        if (!requiredScopes.All(s => scopes.Contains(s, StringComparer.Ordinal))) return;    // lacks at least one: the 403 is real

        // The comparer above is explicit, not incidental: Enumerable.Contains(source, value) has
        // an ICollection<T> fast path that delegates to the collection's own Contains, so
        // `scopes.Contains` (the two-argument form) would use *whatever comparer `scopes` itself
        // was built with* — e.g. OrdinalIgnoreCase, if the adopter's TestIdentity used a
        // case-insensitive HashSet<string> — rather than a comparer this method controls. The
        // three-argument overload used above has no such fast path; it always enumerates and
        // compares with the comparer passed to it. RFC 6749 scope tokens are case-sensitive, so
        // "ORDERS.READ" must not satisfy a requirement for "orders.read" regardless of how the
        // secondary identity's Scopes collection happens to compare equality internally.
        //
        // `Except` below has no such fast path to worry about either way: it always builds its
        // own set with the default comparer, which is ordinal for `string`.
        var extra = scopes.Except(requiredScopes).Any();
        Assert.Inconclusive(extra
            ? $"Skipped: the secondary identity '{secondary.Name}' holds {string.Join(", ", scopes)} — " +
              $"including {string.Join(", ", requiredScopes)}, which this operation requires — so it " +
              "cannot produce a 403. Declare different scopes on that identity, or leave Scopes null " +
              "to run this test anyway."
            : $"Skipped: the secondary identity '{secondary.Name}' holds {string.Join(", ", scopes)}, " +
              "which this operation requires, so it cannot produce a 403. Declare different scopes on " +
              "that identity, or leave Scopes null to run this test anyway.");
    }

    /// <summary>
    /// Overrides the ambient identity for the remainder of the calling scope — the auth cases'
    /// override point (decision 7). A generated 401 or 403 case calls this after
    /// <see cref="RequireMultipleIdentities"/> (403 only) and before building its request, since
    /// <see cref="ApiTestInitialize"/> has already set the <c>Default</c> slot by the time any
    /// test body runs.
    /// <para>
    /// Scoped rather than assigned outright: returning an <see cref="IDisposable"/> that restores
    /// whatever was ambient before it, rather than leaving the override in place until
    /// <see cref="ApiTestCleanup"/> runs, means a test that throws mid-body still restores it —
    /// <c>[TestCleanup]</c> clearing <see cref="InTestAmbient.Identity"/> to null is not the only
    /// thing standing between one test and a leaked <see cref="IdentitySlot.Secondary"/> reaching
    /// whatever runs after it inside the same scope (a fixture's own cleanup closure, say).
    /// </para>
    /// </summary>
    protected static IDisposable UseIdentity(IdentitySlot slot)
    {
        var previous = InTestAmbient.Identity.Value;
        InTestAmbient.Identity.Value = ResolveIdentitySlot(slot, TestHost.TokenProvider);
        return new IdentityScope(previous);
    }

    /// <summary>
    /// Resolves a slot to a concrete identity (decision 7), pulled out as an internal,
    /// dependency-free seam for the same reason <see cref="ResolveDefaultIdentity"/> is one:
    /// <see cref="IdentitySlot.Default"/> defers to it entirely, including its zero-identity handling;
    /// <see cref="IdentitySlot.None"/> is always the sentinel, independent of what
    /// <paramref name="provider"/> advertises; <see cref="IdentitySlot.Secondary"/> indexes
    /// <c>Identities[1]</c> directly rather than defensively, because the only caller that ever
    /// selects it — a generated 403 case — has already called
    /// <see cref="RequireMultipleIdentities"/> first, in the same method body, which would have
    /// thrown before reaching this if fewer than two identities were registered. That prior call
    /// says nothing about the element itself being non-null, though: a provider whose
    /// <c>Identities[1]</c> is itself null — violating <see cref="ITestTokenProvider.Identities"/>'s
    /// non-null annotation, which nothing at compile time or in <see cref="RequireMultipleIdentities"/>
    /// enforces — reaches <c>Identities[1].Name</c> here unguarded and throws
    /// <see cref="NullReferenceException"/>, deliberately: failing loudly on a provider that
    /// breaks its own contract is the intended treatment, not a gap this method should paper over.
    /// </summary>
    internal static string ResolveIdentitySlot(IdentitySlot slot, ITestTokenProvider? provider) => slot switch
    {
        IdentitySlot.None => InTestIdentities.None,
        IdentitySlot.Secondary => provider!.Identities[1].Name,
        _ => ResolveDefaultIdentity(provider)
    };

    private sealed class IdentityScope(string? previous) : IDisposable
    {
        public void Dispose() => InTestAmbient.Identity.Value = previous;
    }

    /// <summary>
    /// Generated tests call this before building a request. Consults the aggregated validation
    /// report built once at <c>AssemblyInitialize</c> — never <c>TestHost.Fixtures.Get</c>
    /// directly — so an operation with no fixture at all (the majority case) is a no-op rather
    /// than the <see cref="FixtureNotFoundException"/> a direct <c>Get</c> would throw. Only an
    /// operation whose fixture has an unresolved sentinel or token throws, naming its file and
    /// property (Task 7 / decision 2).
    /// </summary>
    protected static void RequireFixture(string operationKey) =>
        TestHost.FixtureValidationReport.ThrowIfBlocked(operationKey);

    /// <summary>
    /// The fixture's resolved request body as a compact JSON string, or null when it carries
    /// none. Generated mutating methods call this after <see cref="RequireFixture"/> has already
    /// guaranteed nothing in it is unresolved.
    /// </summary>
    protected static string? FixtureBody(string operationKey) =>
        TestHost.Fixtures.ResolvedBody(operationKey, TestHost.FixtureTokens)?.ToJsonString();

    /// <summary>A single resolved path parameter value, sourced from the fixture rather than
    /// the deleted <c>TestData</c> (decision 1).</summary>
    protected static string FixtureParameter(string operationKey, string name) =>
        TestHost.Fixtures.ResolvedParameter(operationKey, name, TestHost.FixtureTokens);

    /// <summary>Resolved values for whichever of <paramref name="names"/> the fixture actually
    /// supplies — an optional query parameter with no value is simply absent (decision 1).</summary>
    protected static IReadOnlyDictionary<string, string> FixtureQueryParameters(string operationKey, params string[] names) =>
        TestHost.Fixtures.ResolvedQueryParameters(operationKey, names, TestHost.FixtureTokens);
}
