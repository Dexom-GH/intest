using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class StaticTokenProviderTests
{
    [TestMethod]
    public async Task ReturnsTheConfiguredToken()
    {
        var provider = new StaticTokenProvider("tok-123");
        (await provider.GetTokenAsync("api://orders")).ShouldBe("tok-123");
    }

    [TestMethod]
    public void AdvertisesExactlyOneIdentitySo403TestsGateOff()
    {
        new StaticTokenProvider("tok-123").Identities.Count.ShouldBe(1);
    }

    [TestMethod]
    public void AdvertisesTheDefaultIdentityWithNoScopesDeclared()
    {
        var identity = new StaticTokenProvider("tok-123").Identities[0];

        identity.Name.ShouldBe("default");
        // StaticTokenProvider never learns what scopes its one identity holds — null, not [],
        // reports that honestly (TestIdentity.Scopes' null-vs-empty distinction).
        identity.Scopes.ShouldBeNull();
    }

    [TestMethod]
    public async Task RejectsAnIdentityItCannotServe()
    {
        var provider = new StaticTokenProvider("tok-123");
        var ex = await Should.ThrowAsync<ArgumentException>(() => provider.GetTokenAsync("api://orders", "wrong-scope"));

        ex.Message.ShouldContain("'default'");
        ex.Message.ShouldNotContain("TestIdentity");
    }
}
