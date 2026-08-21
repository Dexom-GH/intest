using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// The real signal. A golden file proves output is stable; only a compiler proves it is valid.
/// </summary>
[TestClass]
public class CompileVerificationTests
{
    private string _root = null!;

    /// <summary>
    /// Parameterized on the spec file name (Task 10 item 7's shared-setup pattern extended
    /// here): every other project scaffold detail — namespace, base class, csproj, assembly
    /// info — is identical regardless of which spec is under test, so only the one thing that
    /// actually varies between <see cref="GeneratedProjectCompiles"/> and
    /// <see cref="GeneratedProjectWithHostileSpecTextCompiles"/> is a parameter rather than a
    /// second hand-copied method.
    /// </summary>
    private void CreateProject(string specFileName)
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-compile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Specs", specFileName), Path.Combine(_root, specFileName));

        File.WriteAllText(Path.Combine(_root, "intest.json"), $$"""
        { "schemaVersion": 1, "spec": { "source": "{{specFileName}}" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "InTest.Runtime.ApiTestBase" } }
        """);

        var runtimeProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "InTest.Runtime", "InTest.Runtime.csproj"));

        File.WriteAllText(Path.Combine(_root, "Orders.ApiTests.csproj"), $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
            <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
            <ProjectReference Include="{runtimeProject}" />
          </ItemGroup>
        </Project>
        """);

        File.WriteAllText(Path.Combine(_root, "AssemblyInfo.cs"), """
        using Microsoft.VisualStudio.TestTools.UnitTesting;

        [assembly: DoNotParallelize]
        """);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task GeneratedProjectCompiles()
    {
        CreateProject("orders.json");

        // Specs/orders.json's GET /orders/{id} has a required path parameter, so under decision
        // 1 that operation needs a fixture. This test never calls `init` — it hand-writes
        // intest.json above — but repair needs only intest.json plus the spec, so it works
        // directly here too. Without this, generate now reports drift instead of compiling
        // anything (Task 4).
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated project failed to compile:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// The real proof that spec-derived text is escaped before it lands in a generated C#
    /// string literal — a string assertion can only confirm a backslash appears somewhere;
    /// only the compiler can confirm the result is valid C#. Specs/hostile-text.json's
    /// <c>GET /widgets</c> operation is deliberately parameterless with no JSON request body, so
    /// <c>FixtureComposer.NeedsFixture</c> is false and its operationId — which contains both
    /// <c>"</c> and <c>\</c> — reaches <see cref="Rendering.TemplateRenderer"/> unvalidated by
    /// <c>FixtureDocument.TryValidateOperationKey</c> (that check only runs when
    /// <c>needsFixture</c> is true, because only then does the key become a fixture filename).
    /// That is the exact live path the reported defect travels: a fully valid OpenAPI document
    /// whose parameterless operation's operationId embeds a C#-literal-breaking character. The
    /// same spec's second operation exercises a hostile path template (a literal <c>"</c> and
    /// <c>\</c> in the URL text, still with no parameters, so it stays fixture-free too), and its
    /// third exercises a hostile query parameter name — an optional query parameter with no
    /// example or default never gets a fixture-sentinelled value (see
    /// <c>FixtureComposer.ParameterValue</c>), so it appears in
    /// <c>TestCasePlan.QueryParameterNames</c> without ever making the operation need a fixture.
    /// A hostile path *parameter* name could not be added the same way: any path parameter is
    /// unconditionally sentinelled (decision 1), so it unconditionally sets NeedsFixture — that
    /// site's escaping is covered instead by TemplateRendererTests, which does not need a real
    /// spec to reach it.
    /// </summary>
    [TestMethod]
    public async Task GeneratedProjectWithHostileSpecTextCompiles()
    {
        CreateProject("hostile-text.json");

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated project failed to compile:{Environment.NewLine}{output}");
    }
}