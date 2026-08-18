using System.Text.RegularExpressions;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class RunIdTests
{
    private static readonly DateTimeOffset Stamp =
        new(2026, 8, 17, 14, 22, 33, TimeSpan.Zero);

    private static RunIdEnvironment Env(params (string Key, string Value)[] vars)
        => new(vars.ToDictionary(v => v.Key, v => v.Value), UserName: "tjay");

    [TestMethod]
    public void Create_UsesConfiguredPrefixAboveEverythingElse()
    {
        var id = RunId.Create(Env(("TF_BUILD", "True"), ("BUILD_BUILDID", "4471")), Stamp, "nightly");
        id.ShouldStartWith("nightly-");
    }

    [TestMethod]
    public void Create_DerivesAzureDevOpsPrefixFromBuildId()
    {
        RunId.Create(Env(("TF_BUILD", "True"), ("BUILD_BUILDID", "4471")), Stamp, null)
             .ShouldStartWith("ci4471-");
    }

    [TestMethod]
    public void Create_DerivesGitHubActionsPrefixFromRunId()
    {
        RunId.Create(Env(("GITHUB_ACTIONS", "true"), ("GITHUB_RUN_ID", "99123")), Stamp, null)
             .ShouldStartWith("ci99123-");
    }

    [TestMethod]
    public void Create_FallsBackToGenericCi()
    {
        RunId.Create(Env(("CI", "true")), Stamp, null).ShouldStartWith("ci-");
    }

    [TestMethod]
    public void Create_FallsBackToUserNameLocally()
    {
        RunId.Create(Env(), Stamp, null).ShouldStartWith("tjay-");
    }

    [TestMethod]
    public void Create_UsesUtcTimestampSoAgeIsDerivable()
    {
        RunId.Create(Env(), Stamp, null).ShouldContain("-20260817T142233Z-");
    }

    [TestMethod]
    public void Create_MatchesTheDocumentedShape()
    {
        Regex.IsMatch(RunId.Create(Env(), Stamp, null), "^[a-z0-9-]+-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{8}$")
             .ShouldBeTrue();
    }

    [TestMethod]
    public void Create_SanitizesAndCapsThePrefix()
    {
        var id = RunId.Create(Env(), Stamp, "Some Very Long Prefix_With!Junk");
        id.Length.ShouldBeLessThanOrEqualTo(RunId.MaxLength);
        // Comparisons, not an 'is' pattern: ShouldAllBe takes an Expression<Func<char, bool>>,
        // and CS8122 forbids pattern matching inside an expression tree.
        id.ShouldAllBe(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == 'T' || c == 'Z');
    }
}
