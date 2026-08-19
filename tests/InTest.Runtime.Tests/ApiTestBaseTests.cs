using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// <see cref="ApiTestBase"/> as a whole is not given an in-process harness — its
/// <c>[TestInitialize]</c> depends on <c>TestHost.Root</c>, which only exists after the full,
/// heavy <c>TestHost.InitializeAsync</c> has run (see <c>TestHostTests</c>'s own note on why that
/// method gets no harness either). <see cref="ApiTestBase.ResolveDefaultIdentity"/> is pulled out
/// as an internal, dependency-free seam specifically so the one genuinely new decision this task
/// adds — which identity a test defaults to — has a real test rather than shipping unverified
/// alongside a mechanical field-set.
/// </summary>
[TestClass]
public class ApiTestBaseTests
{
    private sealed class FakeProvider(IReadOnlyList<string> identities) : ITestTokenProvider
    {
        public IReadOnlyList<string> Identities { get; } = identities;

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by this test");
    }

    [TestMethod]
    public void ResolvesToTheFirstIdentityWhenTheProviderHasOne()
    {
        var provider = new FakeProvider(["default", "secondary"]);

        ApiTestBase.ResolveDefaultIdentity(provider).ShouldBe("default");
    }

    [TestMethod]
    public void ResolvesToTheNoTokenSentinelWhenTheProviderHasZeroIdentities()
    {
        // ITestTokenProvider.cs already documents this as an explicitly contemplated state, not
        // an error: indexing Identities[0] blind here would throw ArgumentOutOfRangeException in
        // [TestInitialize], before a single request is built, for every test in the suite —
        // turning a gating state into a suite-wide crash (decision 7).
        var provider = new FakeProvider([]);

        ApiTestBase.ResolveDefaultIdentity(provider).ShouldBe(InTestIdentities.None);
    }

    [TestMethod]
    public void ResolvesToTheNoTokenSentinelWhenNoProviderIsRegistered()
    {
        // Catalog and Inventory declare no security and register no provider at all — the
        // majority case. This must behave exactly as an empty Identities list would.
        ApiTestBase.ResolveDefaultIdentity(null).ShouldBe(InTestIdentities.None);
    }
}
