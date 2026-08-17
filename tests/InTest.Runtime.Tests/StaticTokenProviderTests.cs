using InTest.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    public async Task RejectsAnIdentityItCannotServe()
    {
        var provider = new StaticTokenProvider("tok-123");
        await Should.ThrowAsync<ArgumentException>(() => provider.GetTokenAsync("api://orders", "wrong-scope"));
    }
}
