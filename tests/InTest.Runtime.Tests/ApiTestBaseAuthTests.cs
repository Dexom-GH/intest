using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// v1-c Task 5: the runtime guard that replaces <c>MemberCondition</c> (decision 3 — measured to
/// be evaluated before <c>[AssemblyInitialize]</c>, so it cannot see anything the DI container
/// built), and <see cref="ApiTestBase.UseIdentity"/>, the override point a generated auth case
/// calls before building its request (decision 7).
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
    private sealed class FakeTokenProvider(params string[] identities) : ITestTokenProvider
    {
        public IReadOnlyList<string> Identities { get; } = identities;

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

        ex.Message.ShouldContain("identit");   // names the capability, not just "skipped"
        ex.Message.ShouldContain("403");
    }

    [TestMethod]
    public void NoRegisteredProviderAlsoSkipsTheForbiddenTestAndSaysWhy()
    {
        // TestHost.TokenProvider is null for every spec that declares no security — the same
        // zero-identity state ResolveDefaultIdentity already treats as ordinary, not an error.
        TestHost.TokenProvider = null;

        var ex = Should.Throw<AssertInconclusiveException>(ApiTestBase.RequireMultipleIdentities);

        ex.Message.ShouldContain("identit");
        ex.Message.ShouldContain("403");
    }

    [TestMethod]
    public void ATwoIdentityProviderLetsTheForbiddenTestRun()
    {
        TestHost.TokenProvider = new FakeTokenProvider("default", "wrong-scope");

        Should.NotThrow(ApiTestBase.RequireMultipleIdentities);
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
}
