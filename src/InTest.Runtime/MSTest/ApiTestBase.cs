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
