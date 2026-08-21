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

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-compile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Specs", "orders.json"), Path.Combine(_root, "orders.json"));

        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
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
}