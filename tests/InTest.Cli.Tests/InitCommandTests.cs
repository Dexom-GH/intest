using InTest.Cli.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class InitCommandTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-init-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveDirectory()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void ScaffoldsEveryTeamOwnedFile()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "../Orders/bin/Debug/net10.0/orders.json").ShouldBe(0);

        foreach (var file in new[]
        {
            "intest.json", "Orders.ApiTests.csproj", ".editorconfig", "AssemblyInfo.cs",
            "TestStartup.cs", "OrdersTestBase.cs", "appsettings.json", "Orders.ApiTests.runsettings",
            ".config/dotnet-tools.json"
        })
            File.Exists(Path.Combine(_root, file)).ShouldBeTrue($"{file} was not scaffolded.");
    }

    [TestMethod]
    public void DeclaresParallelizationOnlyInAssemblyInfo()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "AssemblyInfo.cs")).ShouldContain("[assembly: DoNotParallelize]");
        // The element form, not the bare name: the INTEST0001 guard target must *name* both
        // properties in order to detect them, so what matters is that neither is ever *set*.
        var csproj = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        csproj.ShouldNotContain("<MSTestParallelizeScope>");
        csproj.ShouldNotContain("<MSTestParallelizeWorkers>");
    }

    [TestMethod]
    public void GuardsAgainstTheDuplicateAttributeBuildBreak()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj")).ShouldContain("INTEST0001");
    }

    [TestMethod]
    public void LeavesTheProfileParameterCommentedOut()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var runsettings = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.runsettings"));
        runsettings.ShouldContain("<!-- <Parameter name=\"profile\"");
    }

    [TestMethod]
    public void RefusesToOverwriteAnExistingProject()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(0);
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(3);
    }

    [TestMethod]
    public void CsprojCopiesFixturesToTheOutputDirectory()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"))
            .ShouldContain("fixtures/**/*.json",
                customMessage: "FixtureStore loads from AppContext.BaseDirectory — this is the F1 defect repeating");
    }

    [TestMethod]
    public void TestStartupDoesNotReferenceTheDeletedTestDataType()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "TestStartup.cs"))
            .ShouldNotContain("TestData", customMessage: "Task 8 deletes it; a scaffold must not teach a dead API");
    }

    [TestMethod]
    public void RegisterCommentPointsAtTheAuthMechanismThatActuallyWorks()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        // ITestTokenProvider has no consumers (F8): nothing calls GetTokenAsync, so telling an
        // adopter to implement it teaches a dead extension point. The comment must instead
        // point at the DelegatingHandler-on-InTestClients.Api mechanism that getting-started's
        // Phase 3 "Auth" section documents as working today.
        var scaffold = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        scaffold.ShouldContain("DelegatingHandler",
            customMessage: "a scaffold must not teach a dead API; it must point at the auth mechanism that works");
        scaffold.ShouldContain("InTestClients.Api",
            customMessage: "a scaffold must not teach a dead API; it must point at the auth mechanism that works");
    }
}
