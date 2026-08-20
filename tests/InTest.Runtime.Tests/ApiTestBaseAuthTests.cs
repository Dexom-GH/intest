using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// v1-c Task 5: the runtime guard that replaces <c>MemberCondition</c> (decision 3 — measured to
/// be evaluated before <c>[AssemblyInitialize]</c>, so it cannot see anything the DI container
/// built), and <see cref="ApiTestBase.UseIdentity"/>, the override point a generated auth case
/// calls before building its request (decision 7). Also <see cref="ApiTestBase.ResolveIdentitySlot"/>,
/// the slot-to-identity resolution <c>UseIdentity</c> defers to, and Task 2's
/// <see cref="ApiTestBase.RequireSecondaryIdentityLacks"/>, the guard that skips a wrong-scope
/// 403 the secondary identity is actually authorized for.
/// <para>
/// <see cref="TestHost.TokenProvider"/> is process-wide static state, the same shape
/// <c>TestHostTests</c> already hand-rolls for <c>TestHost.RetainedFixtureContext</c>: reset
/// before and after every test here so no test is at the mercy of what its predecessor left
/// behind, and so this class never leaks into whatever runs after it.
/// </para>
/// </summary>
[TestClass]
public class ApiTestBaseAuthTests
{
    private sealed class FakeTokenProvider : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; }

        public FakeTokenProvider(params string[] identityNames) =>
            Identities = identityNames.Select(n => new TestIdentity(n)).ToArray();

        // Widened for Task 2 (RequireSecondaryIdentityLacks): those tests need identities that
        // carry TestIdentity.Scopes, not just names.
        public FakeTokenProvider(params TestIdentity[] identities) => Identities = identities;

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by these tests");
    }

    /// <summary>
    /// Gives the tests below a way to call the <c>protected static</c>
    /// <see cref="ApiTestBase.UseIdentity"/> — the same reason <c>FixtureValidationTests</c>
    /// tests <c>FixtureValidation</c> directly rather than through <c>ApiTestBase.RequireFixture</c>
    /// wherever possible, except <c>UseIdentity</c>'s scope-restore behaviour is new enough this
    /// task that it earns a direct test rather than only the golden execution suite's live proof.
    /// </summary>
    private sealed class TestableApiTestBase : ApiTestBase
    {
        public static IDisposable ExposeUseIdentity(IdentitySlot slot) => UseIdentity(slot);
    }

    [TestInitialize]
    public void Reset()
    {
        TestHost.TokenProvider = null;
        InTestAmbient.Identity.Value = null;
    }

    [TestCleanup]
    public void ResetAfter()
    {
        TestHost.TokenProvider = null;
        InTestAmbient.Identity.Value = null;
    }

    // --- RequireMultipleIdentities (decision 3) ---

    [TestMethod]
    public void AOneIdentityProviderSkipsTheForbiddenTestAndSaysWhy()
    {
        // Must fail if the guard stops throwing OR stops explaining. A bare ShouldBeFalse on
        // some condition property would pass just as well with nothing registered at all —
        // asserting through the guard itself, on a provider deliberately built one-identity, is
        // the point.
        TestHost.TokenProvider = new FakeTokenProvider("only-one");

        var ex = Should.Throw<AssertInconclusiveException>(ApiTestBase.RequireMultipleIdentities);

        // Task 10 item 4: this must name the count a *registered* provider advertised — the
        // phrase that distinguishes this case from NoRegisteredProviderAlsoSkips... below, which
        // has no provider at all. Asserting only "identit"/"403" (as both tests did before this
        // task) passes equally on either message and would not have caught the wording bug that
        // motivated the branch.
        ex.Message.ShouldContain("advertises 1 identity");
        ex.Message.ShouldContain("403");
    }

    [TestMethod]
    public void NoRegisteredProviderAlsoSkipsTheForbiddenTestAndSaysWhy()
    {
        // TestHost.TokenProvider is null for every spec that declares no security — the same
        // zero-identity state ResolveDefaultIdentity already treats as ordinary, not an error.
        TestHost.TokenProvider = null;

        var ex = Should.Throw<AssertInconclusiveException>(ApiTestBase.RequireMultipleIdentities);

        // Task 10 item 4: must say no provider is registered, not "advertises 0 identities" —
        // that older wording reads as if a provider *is* registered and simply advertises none,
        // sending a reader hunting for a bug in code they never wrote.
        ex.Message.ShouldContain("no ITestTokenProvider is registered");
        ex.Message.ShouldContain("403");
    }

    [TestMethod]
    public void ATwoIdentityProviderLetsTheForbiddenTestRun()
    {
        TestHost.TokenProvider = new FakeTokenProvider("default", "wrong-scope");

        Should.NotThrow(ApiTestBase.RequireMultipleIdentities);
    }

    /// <summary>Returns a null <c>Identities</c> despite the interface's non-nullable
    /// annotation — nothing at compile time stops a misbehaving <see cref="ITestTokenProvider"/>
    /// implementation from doing this, and <see cref="ApiTestBase.ResolveDefaultIdentity"/>
    /// already guards against exactly this shape.</summary>
    private sealed class NullIdentitiesProvider : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities => null!;

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by this test");
    }

    [TestMethod]
    public void AProviderWithANullIdentitiesListSkipsTheForbiddenTestRatherThanThrowing()
    {
        // Task 10 item 3: RequireMultipleIdentities' neighbour, ResolveDefaultIdentity, guards
        // both "no provider" and "provider whose Identities is itself null" — deliberately,
        // since ITestTokenProvider.Identities is non-nullable only by annotation, not by
        // anything the runtime enforces. `TestHost.TokenProvider?.Identities.Count` chains `?.`
        // through the first access only, so a provider with a null Identities list threw
        // NullReferenceException here instead of the same Inconclusive skip every other
        // zero/one-identity state produces.
        TestHost.TokenProvider = new NullIdentitiesProvider();

        var ex = Should.Throw<AssertInconclusiveException>(ApiTestBase.RequireMultipleIdentities);

        ex.Message.ShouldContain("identit");
        ex.Message.ShouldContain("403");
    }

    // --- ResolveIdentitySlot (decision 7's resolution, pulled out the same way ResolveDefaultIdentity was) ---

    [TestMethod]
    public void NoneSlotIsAlwaysTheSentinelRegardlessOfTheProvider()
    {
        // The 401 case's whole mechanism: it does not matter what the provider advertises, None
        // must never be resolved to an actual identity.
        var provider = new FakeTokenProvider("default", "secondary");

        ApiTestBase.ResolveIdentitySlot(IdentitySlot.None, provider).ShouldBe(InTestIdentities.None);
    }

    [TestMethod]
    public void SecondarySlotResolvesToTheSecondIdentity()
    {
        var provider = new FakeTokenProvider("default", "wrong-scope");

        ApiTestBase.ResolveIdentitySlot(IdentitySlot.Secondary, provider).ShouldBe("wrong-scope");
    }

    [TestMethod]
    public void DefaultSlotResolvesTheSameWayResolveDefaultIdentityAlreadyDoes()
    {
        var provider = new FakeTokenProvider("default", "secondary");

        ApiTestBase.ResolveIdentitySlot(IdentitySlot.Default, provider).ShouldBe("default");
        ApiTestBase.ResolveIdentitySlot(IdentitySlot.Default, null).ShouldBe(InTestIdentities.None);
    }

    // --- UseIdentity (the generated auth case's override point) ---

    [TestMethod]
    public void UseIdentityOverridesTheAmbientIdentityForTheScope()
    {
        TestHost.TokenProvider = new FakeTokenProvider("default", "secondary");
        InTestAmbient.Identity.Value = "default";

        using (TestableApiTestBase.ExposeUseIdentity(IdentitySlot.Secondary))
        {
            InTestAmbient.Identity.Value.ShouldBe("secondary");
        }
    }

    [TestMethod]
    public void UseIdentityRestoresWhateverWasAmbientBeforeItOnDispose()
    {
        // Scoped rather than assigned outright (decision from Task 5's own plan text): a test
        // that throws mid-body must not leave a secondary identity set for whatever runs next.
        // [TestCleanup] clearing InTestAmbient.Identity is not the only thing standing between
        // one test and the next — the using-scope's own Dispose must restore it independently.
        TestHost.TokenProvider = new FakeTokenProvider("default", "secondary");
        InTestAmbient.Identity.Value = "default";

        using (TestableApiTestBase.ExposeUseIdentity(IdentitySlot.Secondary))
        {
        }

        InTestAmbient.Identity.Value.ShouldBe("default");
    }

    [TestMethod]
    public void UseIdentityWithTheNoneSlotSendsTheSentinel()
    {
        InTestAmbient.Identity.Value = "default";

        using (TestableApiTestBase.ExposeUseIdentity(IdentitySlot.None))
        {
            InTestAmbient.Identity.Value.ShouldBe(InTestIdentities.None);
        }

        InTestAmbient.Identity.Value.ShouldBe("default");
    }

    // --- RequireSecondaryIdentityLacks (Task 2: the runtime guard for a wrong-scope 403) ---

    [TestMethod]
    public void SecondaryWithNullScopesAlwaysRuns()
    {
        // null = not declared / unknown (Task 1). Unknown-means-run is deliberate: treating it
        // as a skip would switch auth testing off by default for anyone who never declares
        // scopes on their secondary identity.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("secondary"));

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));
    }

    [TestMethod]
    public void SecondaryHoldingTheRequiredScopeSkipsAndNamesTheIdentityAndScope()
    {
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", ["orders.read"]));

        var ex = Should.Throw<AssertInconclusiveException>(() =>
            ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));

        ex.Message.ShouldContain("readonly");
        ex.Message.ShouldContain("orders.read");
        ex.Message.ShouldContain("403");
        ex.Message.ShouldNotContain("including");
    }

    [TestMethod]
    public void SecondaryLackingTheRequiredScopeRuns()
    {
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", ["orders.read"]));

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.write"));
    }

    [TestMethod]
    public void PartialScopeOverlapStillRunsTheTest()
    {
        // Holding one of two required scopes does not authorize the operation, so a 403 is still
        // provable. Must fail against an `Any` implementation — the easy wrong version of this.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", ["orders.read"]));

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.read", "orders.write"));
    }

    [TestMethod]
    public void SecondaryHoldingAStrictSupersetSkipsWithoutClaimingTheExtraScopeIsRequired()
    {
        // The guard skips on superset, not equality — the ordinary shape of a read-only identity
        // that holds several read scopes. A message that joins only the held scopes under "which
        // this operation requires" states something false the moment the identity holds more
        // than the operation needs, and gives the reader no clue which scope to remove.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", ["orders.read", "products.read"]));

        var ex = Should.Throw<AssertInconclusiveException>(() =>
            ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));

        ex.Message.ShouldContain("readonly");
        ex.Message.ShouldContain("orders.read");
        ex.Message.ShouldContain("products.read");
        // The identity holds products.read too, but the operation never asked for it — the
        // message must not claim otherwise.
        ex.Message.ShouldNotContain("products.read, which this operation requires");
    }

    [TestMethod]
    public void ScopeComparisonIsOrdinalAndCaseSensitive()
    {
        // RFC 6749 scope tokens are case-sensitive, so EqualityComparer<string>.Default (what
        // scopes.Contains binds to) is the correct comparer. Pins the behaviour so a future
        // switch to OrdinalIgnoreCase would be caught rather than passing every existing test.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", ["ORDERS.READ"]));

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));
    }

    [TestMethod]
    public void SecondaryHoldingEveryRequiredScopeSkips()
    {
        // Containment is over the whole set: holding both required scopes really does authorize
        // the operation, so the 403 genuinely cannot happen.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", ["orders.read", "orders.write"]));

        Should.Throw<AssertInconclusiveException>(() =>
            ApiTestBase.RequireSecondaryIdentityLacks("orders.read", "orders.write"));
    }

    [TestMethod]
    public void SecondaryWithAnEmptyScopesDeclarationRuns()
    {
        // [] is a real declaration — "holds no scopes" — not the same as null, but it still can
        // never be a superset of a non-empty requirement, so the test runs either way.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", []));

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.write"));
    }

    [TestMethod]
    public void ZeroRequiredScopesRunsEvenWhenSecondaryHoldsScopes()
    {
        // A zero-argument call means the operation declares no scopes at all — it can still 403
        // on other grounds (tenant, role, resource ownership), so this must never skip.
        // `requiredScopes.All(scopes.Contains)` is vacuously true over an empty requiredScopes,
        // which read as "the secondary already holds everything required" and skipped; that is
        // the bug this test exists to catch.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", ["orders.read"]));

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks());
    }

    [TestMethod]
    public void ZeroRequiredScopesRunsEvenWhenSecondaryScopesIsEmpty()
    {
        // Same bug, [] variant: an empty requiredScopes is still vacuously "All" over an empty
        // Scopes, and Scopes being non-null (even though empty) meant the guard's `is not { }
        // scopes` half didn't save it either — this must run regardless.
        TestHost.TokenProvider = new FakeTokenProvider(
            new TestIdentity("default"),
            new TestIdentity("readonly", []));

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks());
    }

    [TestMethod]
    public void NoRegisteredProviderRunsRatherThanSkippingASecondTime()
    {
        // RequireMultipleIdentities already owns this skip; never skip twice for one reason.
        TestHost.TokenProvider = null;

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));
    }

    [TestMethod]
    public void OnlyOneRegisteredIdentityRuns()
    {
        // Same reason: RequireMultipleIdentities owns the "fewer than two identities" skip.
        TestHost.TokenProvider = new FakeTokenProvider("only-one");

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));
    }

    [TestMethod]
    public void ANullIdentitiesListRunsRatherThanThrowing()
    {
        // Task 2 step 2: this guard reaches further than RequireMultipleIdentities
        // (Identities[1], not just Identities.Count), so it must guard the same
        // provider-registered-but-Identities-itself-null shape that guard already does — v1-c's
        // live NullReferenceException on exactly this shape is why.
        TestHost.TokenProvider = new NullIdentitiesProvider();

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));
    }

    /// <summary>A provider whose second identity is itself null despite the non-nullable element
    /// type — nothing at compile time stops a misbehaving <see cref="ITestTokenProvider"/> from
    /// doing this, the same reasoning <see cref="NullIdentitiesProvider"/> already covers one
    /// level up.</summary>
    private sealed class NullSecondaryIdentityProvider : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; } = [new TestIdentity("default"), null!];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by this test");
    }

    [TestMethod]
    public void ANullSecondaryIdentityElementRunsRatherThanThrowing()
    {
        TestHost.TokenProvider = new NullSecondaryIdentityProvider();

        Should.NotThrow(() => ApiTestBase.RequireSecondaryIdentityLacks("orders.read"));
    }
}
