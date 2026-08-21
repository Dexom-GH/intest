using System.Text.Encodings.Web;
using System.Text.Json;
using InTest.Cli.Configuration;
using InTest.Cli.Naming;

namespace InTest.Cli.Commands;

public static class InitCommand
{
    public const int ExitOk = 0;
    public const int ExitToolError = 2;
    public const int ExitAlreadyInitialised = 3;

    // See JsonSpecSource below for why this uses the relaxed encoder.
    private static readonly JsonSerializerOptions SpecSourceJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(string projectRoot, string projectName, string specSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(specSource);

        // Normalised once, before either escaping step, and reused at both sites below — the
        // intest.json JSON string and the csproj's <InTestSpecSource> element must agree on the
        // same slash-normalised value, or ConfigLoader.Load and the built project would disagree
        // on what "the spec" is.
        var normalizedSpecSource = specSource.Replace("\\", "/");

        // projectName seeds project.rootNamespace, project.testBaseClass, baseClassName, and the
        // `namespace` declaration of two scaffolded files (TestStartup.cs and
        // <Name>TestBase.cs) — refusing an invalid --name here is what stops a scaffold that
        // cannot compile from ever being written. Checked before the intest.json-already-exists
        // check below: an invalid name is invalid regardless of what is already on disk.
        if (!CSharpIdentifier.TryValidateDottedName(projectName, "--name", out var nameReason))
        {
            Console.Error.WriteLine($"{nameReason} Pass a valid C# name to `intest init --name` — for example \"Orders.ApiTests\".");
            return ExitToolError;
        }

        // specSource reaches the generated .csproj as bare XML inside <InTestSpecSource> — a
        // value XML 1.0 cannot represent in any form would fail the build before any InTest code
        // runs, with an MSBuild error that has no visible connection to `--spec`. Same reasoning
        // as --name above: an invalid value is invalid regardless of what is already on disk, so
        // this is checked before the intest.json-already-exists check too.
        //
        // This guard refuses before *both* writes below, even though a C0 control character is
        // representable in a JSON string (intest.json alone could carry it). Two reasons the
        // csproj alone doesn't cover: (1) `init` writes an atomic scaffold — a valid intest.json
        // paired with a .csproj that cannot load is not a partial success, it is a project that
        // cannot build, with the failure displaced away from the flag that caused it; (2) the
        // same guard is what stops an unpaired surrogate reaching JsonSerializer below, which
        // would otherwise silently substitute U+FFFD and hand the adopter a *different* path with
        // no error at all.
        if (!MSBuildPropertyValue.TryEscape(normalizedSpecSource, "--spec", out var specSourceEscaped, out var specReason))
        {
            Console.Error.WriteLine($"{specReason} Pass the path to the OpenAPI document to `intest init --spec` — for example \"../Orders/bin/Debug/net10.0/orders.json\".");
            return ExitToolError;
        }

        if (File.Exists(Path.Combine(projectRoot, "intest.json")))
        {
            Console.Error.WriteLine("intest.json already exists. `init` never overwrites; edit it or delete it first.");
            return ExitAlreadyInitialised;
        }

        var baseClassName = projectName.Split('.')[0] + "TestBase";
        Directory.CreateDirectory(Path.Combine(projectRoot, ".config"));

        Write(projectRoot, "intest.json", $$"""
        {
          "schemaVersion": {{ConfigLoader.SupportedSchemaVersion}},
          "intestVersion": "{{CliVersion.Current}}",
          "spec": { "source": {{JsonSpecSource(normalizedSpecSource)}}, "producer": "auto" },
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
            <InTestSpecSource>{specSourceEscaped}</InTestSpecSource>
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

            /// <summary>Drains any fixture teardown registered during AssemblyInit — runs even
            /// when AssemblyInit itself failed, and never fails the run: see
            /// TestHost.CleanupAsync for why a drain failure is written to the test log instead
            /// of thrown.</summary>
            [AssemblyCleanup]
            public static async Task AssemblyCleanup(TestContext context)
            {
                await TestHost.CleanupAsync(context);
            }

            /// <summary>Team-owned registrations. Add configuration providers here. AuthHandler
            /// is already attached to InTestClients.Api; a secured API needs only an
            /// ITestTokenProvider registered below — do not also append a DelegatingHandler of
            /// your own, or two handlers will set Authorization and the last one registered
            /// silently wins. See "Auth" in Phase 3 of getting-started.md for a worked
            /// example.</summary>
            private static void Register(IServiceCollection services, IConfiguration configuration)
            {
                // StaticTokenProvider ships as the one-identity, one-token implementation; write
                // your own (like YourTokenProvider below) for more than one identity, which the
                // wrong-scope 403 cases need — and declare each identity's Scopes, or a read-only
                // identity's own read operations can never produce a provable 403. Catalog and
                // Inventory declare no `security` and register nothing at all — they cannot,
                // since StaticTokenProvider needs a real token neither has a source for — so this
                // stays commented for the same reason the IAssemblyFixture example below does: a
                // live registration here would reference a type that does not exist yet, breaking
                // every fresh scaffold's build before a team has written one. See "Auth" in Phase
                // 3 of getting-started.md for a worked example.
                // services.AddSingleton<ITestTokenProvider, YourTokenProvider>();

                // Per-request fixtures: path and query parameter values live in fixtures/, not
                // here — each operation that needs one has a fixture file with a "TODO:"
                // sentinel for every value it requires. Fill those in by hand, or run
                // `intest fixtures repair` after a spec change to add sentinels for anything
                // newly required.

                // A different kind of fixture: assembly fixtures seed data once before any test
                // runs, registered here rather than under fixtures/. Order is resolved
                // automatically from DependsOn; profile-restrict with AppliesTo. See "fixtures"
                // in Phase 5 of getting-started.md for a worked example.
                // services.AddSingleton<IAssemblyFixture, YourFixture>();
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

    /// <summary>
    /// Renders <paramref name="value"/> as a JSON string literal, quotes included, for splicing
    /// directly into the intest.json template in place of a hand-written <c>"..."</c> pair. Uses
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> rather than the default encoder:
    /// verified against both for the ordinary path
    /// <c>../Orders/bin/Debug/net10.0/orders.json</c>, the output is byte-identical either way, so
    /// the choice cannot be proven by round-tripping a hazardous value through
    /// <c>ConfigLoader.Load</c> — both encoders produce valid JSON encoding the same string. It
    /// buys readability instead: the default encoder is markedly more aggressive about escaping
    /// ordinary path and URL characters into <c>\uXXXX</c>, which serves no purpose in a file
    /// adopters read by hand, since JSON string escaping already makes every character losslessly
    /// representable without it. See
    /// <c>InitCommandTests.WritesAmpersandAndNonAsciiCharactersLiterallyIntoIntestJson</c> for a
    /// character each encoder renders differently, and for what reverting this encoder choice
    /// breaks.
    /// </summary>
    private static string JsonSpecSource(string value) => JsonSerializer.Serialize(value, SpecSourceJsonOptions);
}
