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
        InTestAmbient.Identity.Value = ResolveDefaultIdentity(_scope.ServiceProvider.GetService<ITestTokenProvider>());

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
        provider?.Identities is { Count: > 0 } identities ? identities[0] : InTestIdentities.None;

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
        var count = TestHost.TokenProvider?.Identities.Count ?? 0;
        if (count >= 2)
        {
            return;
        }

        Assert.Inconclusive(
            $"Skipped: the registered ITestTokenProvider advertises {count} identit{(count == 1 ? "y" : "ies")}; " +
            "a wrong-scope 403 test needs at least 2.");
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
    /// thrown before reaching this if fewer than two identities were registered.
    /// </summary>
    internal static string ResolveIdentitySlot(IdentitySlot slot, ITestTokenProvider? provider) => slot switch
    {
        IdentitySlot.None => InTestIdentities.None,
        IdentitySlot.Secondary => provider!.Identities[1],
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
