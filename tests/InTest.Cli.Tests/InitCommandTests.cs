using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using InTest.Cli.Commands;
using InTest.Cli.Configuration;
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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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
        {
            File.Exists(Path.Combine(_root, file)).ShouldBeTrue($"{file} was not scaffolded.");
        }
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
    public void RefusesAnInvalidNameAndWritesNothing()
    {
        // --name seeds project.rootNamespace, project.testBaseClass, baseClassName, and the
        // `namespace` declaration of two scaffolded files — an invalid value here is invalid
        // regardless of what is (or is not) already on disk, so this must be checked before the
        // intest.json-already-exists check and before anything is written.
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = InitCommand.Run(_root, "My Project", "orders.json");
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty();

        var message = capturedError.ToString();
        message.ShouldContain("--name");
        message.ShouldContain("My Project");
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
    public void RegisterCommentPointsAtImplementingITestTokenProviderNowThatAuthHandlerConsumesIt()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        // Task 2 question (e): AuthHandler now ships attached to InTestClients.Api, so telling
        // an adopter to append their own DelegatingHandler there produces two handlers both
        // setting Authorization, where the last one registered silently wins. The comment must
        // say AuthHandler is already attached and that only ITestTokenProvider needs
        // implementing — the instruction this same comment told people NOT to follow before
        // AuthHandler existed to consume it.
        var scaffold = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        scaffold.ShouldContain("AuthHandler",
            customMessage: "the scaffold must say AuthHandler is already attached, not send an adopter to write their own");
        scaffold.ShouldContain("ITestTokenProvider",
            customMessage: "the scaffold must point at the extension point that now actually works");
        scaffold.ShouldContain("InTestClients.Api",
            customMessage: "the scaffold must still name the client AuthHandler is attached to");
    }

    [TestMethod]
    public void ScaffoldedStartupDrainsFixtureCleanup()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Task 5: without an [AssemblyCleanup] calling TestHost.CleanupAsync, DrainAsync ships
        // with no caller and a fixture's teardown never runs in a generated project. One regex
        // ties the attribute, the method signature, and the call together as a single unit,
        // rather than two independent ShouldContain checks: independent checks would still pass
        // if the call were moved into AssemblyInit and AssemblyCleanup were left empty, which is
        // exactly the failure mode this test exists to catch. The call is pinned with its
        // parenthesised invocation, "TestHost.CleanupAsync(context)", not the bare
        // "TestHost.CleanupAsync" substring: that bare form also appears in the method's own doc
        // comment, so it would stay present even if the method body were gutted.
        Regex.IsMatch(
                startup,
                @"\[AssemblyCleanup\]\s+public\s+static\s+async\s+Task\s+AssemblyCleanup\(TestContext\s+context\)" +
                @"\s*\{\s*await\s+TestHost\.CleanupAsync\(context\);\s*\}",
                RegexOptions.Singleline)
            .ShouldBeTrue("expected [AssemblyCleanup] to directly wrap a call to TestHost.CleanupAsync(context)");
    }

    [TestMethod]
    public void RegisterMethodShowsACommentedFixtureRegistrationExample()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Commented, not live: `init` never discovers fixtures by reflection (v1-b decision 2), and a
        // live call here would reference a fixture type that does not exist yet, breaking every
        // fresh scaffold's build before a team has written one.
        startup.ShouldContain("// services.AddSingleton<IAssemblyFixture,");
    }

    [TestMethod]
    public void RegisterMethodShowsACommentedTokenProviderRegistrationExample()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Task 6: same precedent as the IAssemblyFixture example above — commented, not live.
        // StaticTokenProvider needs a real token neither Catalog nor Inventory has a source for,
        // so a live registration here would either fail to construct or issue a token that
        // authenticates nothing. AuthHandler already no-ops when no provider is registered (Task
        // 2(b)), which is exactly the state this scaffold must ship in.
        startup.ShouldContain("// services.AddSingleton<ITestTokenProvider",
            customMessage: "the scaffold must show the registration, but only as a comment");
    }

    [TestMethod]
    public void EscapesAmpersandSoTheGeneratedCsprojActuallyParses()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "../R&D/orders.json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        // The real parse, not a string check: an unescaped '&' is not well-formed XML and
        // XDocument.Parse throws on it rather than silently accepting it.
        var doc = XDocument.Parse(csprojText);

        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("../R&D/orders.json");
    }

    [TestMethod]
    public void EscapesDollarParenSoItSurvivesAsLiteralTextNotAnMSBuildExpansion()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders$(Configuration).json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        var doc = XDocument.Parse(csprojText);

        // %24, not a bare $( — a bare $(Configuration) would expand as an MSBuild property
        // reference rather than surviving as the literal text the adopter typed.
        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("orders%24(Configuration).json");
    }

    [TestMethod]
    public void EscapesQuestionMarkSoTheIncludeGlobCannotResolveToADifferentFile()
    {
        // Confirmed by real `dotnet build` (see MSBuildPropertyValue's doc comment): with
        // specs/orders.json and specs/ordersX.json both on disk, an unescaped
        // Include="$(InTestSpecSource)" for "specs/orders?.json" silently resolved to
        // ordersX.json — the wrong file — instead of failing loudly.
        InitCommand.Run(_root, "Orders.ApiTests", "orders?.json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        var doc = XDocument.Parse(csprojText);

        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("orders%3F.json");
    }

    [TestMethod]
    public void EscapesQuoteSoTheGeneratedIntestJsonActuallyParses()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders\".json").ShouldBe(0);

        var jsonText = File.ReadAllText(Path.Combine(_root, "intest.json"));
        // The real parse: an unescaped '"' inside the JSON string value truncates it and leaves
        // the rest of the document malformed, which JsonDocument.Parse throws on.
        using var doc = JsonDocument.Parse(jsonText);

        doc.RootElement.GetProperty("spec").GetProperty("source").GetString().ShouldBe("orders\".json");
    }

    [TestMethod]
    public void WritesAmpersandAndNonAsciiCharactersLiterallyIntoIntestJson()
    {
        // Pins the choice of JavaScriptEncoder.UnsafeRelaxedJsonEscaping over the default
        // encoder — a choice round-tripping cannot prove, since both produce valid JSON encoding
        // the same string. The default encoder would render '&' as \u0026 and 'é' as \u00e9:
        // still correct JSON, but unreadable by an adopter who opens the file by hand.
        InitCommand.Run(_root, "Orders.ApiTests", "../R&D/café.json").ShouldBe(0);

        var jsonText = File.ReadAllText(Path.Combine(_root, "intest.json"));
        jsonText.ShouldContain("R&D");
        jsonText.ShouldContain("café");
    }

    [TestMethod]
    public void RoundTripsAHazardousSpecSourcePastConfigLoad()
    {
        // The strongest test on this surface: proves the value survives write (InitCommand) then
        // read (ConfigLoader) intact, through both escaping layers at once.
        var hazardous = "../R&D/orders?\"$(x).json";
        InitCommand.Run(_root, "Orders.ApiTests", hazardous).ShouldBe(0);

        ConfigLoader.Load(_root).SpecSource.ShouldBe(hazardous.Replace("\\", "/"));
    }

    [TestMethod]
    public void RefusesACharacterXmlCannotRepresentAndWritesNothing()
    {
        // U+0001 is a C0 control character XML 1.0's Char production excludes — no MSBuild or
        // XML escape sequence represents it, so this must refuse rather than escape.
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = InitCommand.Run(_root, "Orders.ApiTests", "orders\u0001.json");
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty();

        var message = capturedError.ToString();
        message.ShouldContain("--spec");
        // Pins that the diagnosis itself — not just the boilerplate sentence appended in
        // InitCommand — reached the message: MSBuildPropertyValue renders the offending
        // character as U+0001 rather than pasting the raw control character into the terminal.
        message.ShouldContain("U+0001");
    }

    // ScaffoldStillBuildsWithNoTokenProviderRegistered moved to InTest.Golden.Tests, next to
    // CompileVerificationTests (Task 10 item 7): it is the only out-of-process build that lived
    // in this assembly, and under a solution-level `dotnet test` this assembly's ~6s run fully
    // overlaps InTest.Golden.Tests' ~1m40s one, so two independent MSBuild invocations could
    // build scaffolded projects that both ProjectReference the same InTest.Runtime.csproj
    // simultaneously — a known source of intermittent obj/ file-lock failures. The assertion
    // itself is unchanged; see ScaffoldCompileVerificationTests there.
}
