using InTest.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class TokenResolverTests
{
    private static TokenResolver Resolver(params (string Key, string Value)[] configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)))
            .Build();
        return new TokenResolver(configuration, runId: "run-fixed-1");
    }

    [TestMethod]
    public void ConfigTokenReadsConfiguration()
    {
        var resolver = Resolver(("Orders:ApiKey", "the-value"));

        resolver.Resolve("{{config:Orders:ApiKey}}", "create-order.json").ShouldBe("the-value");
    }

    [TestMethod]
    public void SecretTokenResolvesTheSameWayAsConfig()
    {
        var resolver = Resolver(("Orders:ApiKey", "super-secret-value"));

        resolver.Resolve("{{secret:Orders:ApiKey}}", "create-order.json").ShouldBe("super-secret-value");
    }

    [TestMethod]
    public void SecretValuesNeverAppearInAnErrorMessage()
    {
        var resolver = Resolver(("Orders:ApiKey", "super-secret-value"));

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{secret:Orders:Missing}}", "create-order.json"));

        ex.Message.ShouldNotContain("super-secret-value");
        ex.Message.ShouldContain("Orders:Missing");
    }

    [TestMethod]
    public void ASecretResolvedElsewhereInTheSameFixtureNeverLeaksIntoAnUnrelatedFailure()
    {
        // The value from an earlier, successfully-resolved {{secret:}} token must not survive
        // into the exception thrown by a later token failing in the same fixture.
        var resolver = Resolver(("Orders:ApiKey", "super-secret-value"));

        resolver.Resolve("{{secret:Orders:ApiKey}}", "create-order.json").ShouldBe("super-secret-value");

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("prefix {{secret:Orders:ApiKey}} suffix {{bogus}}", "create-order.json"));

        ex.Message.ShouldNotContain("super-secret-value");
    }

    [TestMethod]
    public void RunIdTokenIsIdenticalAcrossTwoResolutions()
    {
        var resolver = Resolver();

        var first = resolver.Resolve("{{runId}}", "f.json");
        var second = resolver.Resolve("{{runId}}", "f.json");

        first.ShouldBe("run-fixed-1");
        second.ShouldBe(first);
    }

    [TestMethod]
    public void UtcNowDiffersBetweenResolutionsBecauseItIsPerRequestNotCached()
    {
        var tick = 0;
        var configuration = new ConfigurationBuilder().Build();
        var resolver = new TokenResolver(configuration, "run-1", () => DateTimeOffset.UnixEpoch.AddSeconds(tick++));

        var first = resolver.Resolve("{{utcNow}}", "f.json");
        var second = resolver.Resolve("{{utcNow}}", "f.json");

        second.ShouldNotBe(first, "{{utcNow}} must be evaluated per call, not cached");
    }

    [TestMethod]
    public void AnUnknownTokenFailsNamingTheTokenAndListingTheSupportedOnes()
    {
        var resolver = Resolver();

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{bogus}}", "create-order.json"));

        ex.Message.ShouldContain("bogus");
        ex.Message.ShouldContain("config:");
        ex.Message.ShouldContain("secret:");
        ex.Message.ShouldContain("runId");
        ex.Message.ShouldContain("utcNow");
    }

    [TestMethod]
    public void AFixtureTokenFailsAsNotSupportedUntilV1BRatherThanBeingLeftLiteral()
    {
        var resolver = Resolver();

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{fixture:seededCustomer.id}}", "create-order.json"));

        ex.Message.ShouldContain("fixture:seededCustomer.id");
        ex.Message.ShouldContain("v1-b");
    }

    [TestMethod]
    public void AMissingConfigKeyFailsNamingTheKey()
    {
        var resolver = Resolver();

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{config:Orders:ApiKey}}", "create-order.json"));

        ex.Message.ShouldContain("Orders:ApiKey");
    }

    [TestMethod]
    public void AValueContainingNoTokenIsReturnedUnchanged()
    {
        var resolver = Resolver();

        resolver.Resolve("plain string, no tokens here", "f.json").ShouldBe("plain string, no tokens here");
    }

    [TestMethod]
    public void TheFileNameAppearsInResolutionErrorsSoAReaderKnowsWhichFixtureFailed()
    {
        var resolver = Resolver();

        Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{config:Missing:Key}}", "update-order.json"))
              .Message.ShouldContain("update-order.json");
    }
}
