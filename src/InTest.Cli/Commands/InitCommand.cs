namespace InTest.Cli.Commands;

public static class InitCommand
{
    public const int ExitOk = 0;
    public const int ExitAlreadyInitialised = 3;

    public static int Run(string projectRoot, string projectName, string specSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(specSource);

        if (File.Exists(Path.Combine(projectRoot, "intest.json")))
        {
            Console.Error.WriteLine("intest.json already exists. `init` never overwrites; edit it or delete it first.");
            return ExitAlreadyInitialised;
        }

        var baseClassName = projectName.Split('.')[0] + "TestBase";
        Directory.CreateDirectory(Path.Combine(projectRoot, ".config"));

        Write(projectRoot, "intest.json", $$"""
        {
          "schemaVersion": 1,
          "intestVersion": "{{CliVersion.Current}}",
          "spec": { "source": "{{specSource.Replace("\\", "/")}}", "producer": "auto" },
          "project": {
            "name": "{{projectName}}",
            "rootNamespace": "{{projectName}}",
            "framework": "mstest",
            "assertions": ["shouldly"],
            "testBaseClass": "{{projectName}}.{{baseClassName}}"
          }
        }
        """);

        Write(projectRoot, $"{projectName}.csproj", $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <IsPackable>false</IsPackable>
            <RunSettingsFilePath>$(MSBuildProjectDirectory)/{projectName}.runsettings</RunSettingsFilePath>
            <InTestSpecSource>{specSource.Replace("\\", "/")}</InTestSpecSource>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
            <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
            <PackageReference Include="MSTest.Analyzers" Version="4.3.3" />
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
            <PackageReference Include="Shouldly" Version="4.3.0" />
            <PackageReference Include="InTest.Runtime" Version="0.1.0" />
          </ItemGroup>
          <ItemGroup>
            <Content Include="Generated/spec-schemas.json" Link="spec-schemas.json" CopyToOutputDirectory="PreserveNewest" />
            <Content Include="Generated/spec-paths.json" Link="spec-paths.json" CopyToOutputDirectory="PreserveNewest" />
            <!-- TestHost resolves configuration from AppContext.BaseDirectory, so these must
                 travel to the output directory. Without them every generated project fails at
                 AssemblyInitialize with a FileNotFoundException for appsettings.json. -->
            <Content Include="appsettings*.json" CopyToOutputDirectory="PreserveNewest" />
            <!-- FixtureStore also loads from AppContext.BaseDirectory. Without this every
                 fixture is invisible at runtime — every operation that needs one 400s or sends
                 literal "TODO:..." sentinels, and nothing at compile time catches it. -->
            <Content Include="fixtures/**/*.json" CopyToOutputDirectory="PreserveNewest" />
          </ItemGroup>
          <!-- Parallelization intent lives in AssemblyInfo.cs. The MSBuild properties below
               generate a second assembly attribute, which fails as CS0579 inside obj/. -->
          <Target Name="InTestGuardParallelizeProperties" BeforeTargets="BeforeBuild"
                  Condition="'$(MSTestParallelizeScope)' != '' or '$(MSTestParallelizeWorkers)' != ''">
            <Error Code="INTEST0001"
                   Text="Parallelization intent is declared in AssemblyInfo.cs. Remove MSTestParallelizeScope/MSTestParallelizeWorkers from the project file and edit [assembly: Parallelize] or [assembly: DoNotParallelize] instead." />
          </Target>
        </Project>
        """);

        Write(projectRoot, "AssemblyInfo.cs", """
        using Microsoft.VisualStudio.TestTools.UnitTesting;

        // The single authoritative declaration of parallelization intent.
        // Do NOT set MSTestParallelizeScope in the .csproj — it generates this attribute,
        // and two of them is a build error.
        [assembly: DoNotParallelize]
        """);

        Write(projectRoot, ".editorconfig", """
        root = true

        [*.cs]
        dotnet_diagnostic.CA1707.severity = none
        """);

        Write(projectRoot, "TestStartup.cs", $$"""
        using InTest.Runtime;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.VisualStudio.TestTools.UnitTesting;

        namespace {{projectName}};

        [TestClass]
        public static class TestStartup
        {
            [AssemblyInitialize]
            public static async Task AssemblyInit(TestContext context)
            {
                TestHost.ConfigureServices = Register;
                await TestHost.InitializeAsync(context, context.CancellationToken);
            }

            /// <summary>Team-owned registrations. Add configuration providers and an
            /// ITestTokenProvider implementation here.</summary>
            private static void Register(IServiceCollection services, IConfiguration configuration)
            {
                // Path and query parameters live in fixtures/, not here — each operation that
                // needs one has a fixture with a "TODO:" sentinel for every value it requires.
                // Fill those in by hand, or run `intest fixtures repair` after a spec change to
                // add sentinels for anything newly required.
            }
        }
        """);

        Write(projectRoot, $"{baseClassName}.cs", $$"""
        using InTest.Runtime;

        namespace {{projectName}};

        /// <summary>Your shared helpers. Generated classes derive from this.</summary>
        public abstract class {{baseClassName}} : ApiTestBase
        {
        }
        """);

        Write(projectRoot, "appsettings.json", """
        {
          "InTest": {
            "DefaultProfile": "local",
            "Readiness": {
              "Enabled": true,
              "Path": "/health/ready",
              "ExpectStatus": 200,
              "ConsecutiveSuccesses": 2,
              "TimeoutSeconds": 120,
              "IntervalSeconds": 3
            }
          },
          // BaseUrl substitutes for the spec's servers[0].url: the spec's paths are appended
          // to it. If those paths already begin with a prefix such as /api, this value must
          // NOT repeat it, or every request 404s against configuration that looks correct.
          "Api": { "BaseUrl": "https://localhost:5001/" }
        }
        """);

        Write(projectRoot, "appsettings.staging.json", """
        { "Api": { "BaseUrl": "https://REPLACE-ME.example.com/" } }
        """);

        Write(projectRoot, $"{projectName}.runsettings", """
        <?xml version="1.0" encoding="utf-8"?>
        <RunSettings>
          <TestRunParameters>
            <!-- Uncommenting this PINS the profile and makes INTEST_PROFILE unreachable.
                 Leave commented unless this file is environment-specific. -->
            <!-- <Parameter name="profile" value="staging" /> -->
          </TestRunParameters>
          <MSTest>
            <TestTimeout>60000</TestTimeout>
          </MSTest>
        </RunSettings>
        """);

        Write(projectRoot, Path.Combine(".config", "dotnet-tools.json"), $$"""
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "intest.cli": { "version": "{{CliVersion.Current}}", "commands": ["intest"] }
          }
        }
        """);

        Console.WriteLine($"Initialised {projectName}. Next: `intest generate`.");
        return ExitOk;
    }

    private static void Write(string root, string relativePath, string content)
        => File.WriteAllText(Path.Combine(root, relativePath), content.ReplaceLineEndings("\n") + "\n");
}
