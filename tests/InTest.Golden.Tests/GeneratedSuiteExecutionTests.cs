using System.Diagnostics;
using System.Xml.Linq;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// Scaffolds a project, generates into it, builds it, and <b>runs</b> it against a live stub
/// (<see cref="GoldenApiStub"/>).
/// <para>
/// This exists because of a defect the v0 acceptance run found: <c>init</c> scaffolded
/// appsettings.json but never copied it to the output directory, so every generated project
/// died at AssemblyInitialize. Compile verification passed throughout — it proves generated
/// code builds, never that it runs. Those are different gates and only the first was covered.
/// </para>
/// </summary>
[TestClass]
public class GeneratedSuiteExecutionTests
{
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Stub", "version": "1.0" },
      "paths": {
        "/api/status": {
          "get": {
            "operationId": "getStatus",
            "tags": ["Status"],
            "responses": {
              "200": {
                "description": "ok",
                "content": {
                  "application/json": {
                    "schema": { "$ref": "#/components/schemas/Status" }
                  }
                }
              }
            }
          }
        }
      },
      "components": {
        "schemas": {
          "Status": {
            "type": "object",
            "required": ["state"],
            "properties": { "state": { "type": "string" } }
          }
        }
      }
    }
    """;

    /// <summary>
    /// <see cref="Spec"/> plus a path-parameter operation, used only by
    /// <see cref="FixtureParameterReachesALiveRequestEndToEnd"/>. This is the F1 live proof Task
    /// 4a deferred here (its report, lines 1176-1196): a bare GET with no parameters composes no
    /// fixture at all (decision 1), so it can never prove a fixture is loaded and consumed by a
    /// running test — only an operation with a required parameter can. Kept as a separate spec
    /// rather than folded into <see cref="Spec"/> so the two existing tests below, which build
    /// and run the suite without ever touching <c>fixtures/getStatusById.json</c>, are unaffected
    /// by this addition.
    /// </summary>
    private const string SpecWithPathParameter = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Stub", "version": "1.0" },
      "paths": {
        "/api/status": {
          "get": {
            "operationId": "getStatus",
            "tags": ["Status"],
            "responses": {
              "200": {
                "description": "ok",
                "content": {
                  "application/json": {
                    "schema": { "$ref": "#/components/schemas/Status" }
                  }
                }
              }
            }
          }
        },
        "/api/status/{id}": {
          "get": {
            "operationId": "getStatusById",
            "tags": ["Status"],
            "parameters": [
              { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
            ],
            "responses": {
              "200": {
                "description": "ok",
                "content": {
                  "application/json": {
                    "schema": { "$ref": "#/components/schemas/Status" }
                  }
                }
              }
            }
          }
        }
      },
      "components": {
        "schemas": {
          "Status": {
            "type": "object",
            "required": ["state"],
            "properties": { "state": { "type": "string" } }
          }
        }
      }
    }
    """;

    private string _root = null!;
    private GoldenApiStub _stub = null!;

    [TestInitialize]
    public void StartStubAndScaffold()
    {
        _stub = new GoldenApiStub();

        _root = Path.Combine(Path.GetTempPath(), "intest-run-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec);
    }

    [TestCleanup]
    public void StopStub()
    {
        _stub.Dispose();

        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public async Task GeneratedSuiteBuildsAndPassesAgainstALiveService()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        // This spec's only operation is a bare GET with no body and no parameters, so today it
        // composes no fixture at all (decision 1) and this call is a no-op — but it mirrors what
        // an adopter actually runs, and it is what keeps this test realistic if the spec ever
        // grows an operation that does need one. The fixture pipeline itself — a required
        // parameter actually loaded from a fixture and sent on a live request — is proved by
        // FixtureParameterReachesALiveRequestEndToEnd below, against SpecWithPathParameter.
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var build = await RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var test = await RunAsync("dotnet", $"test \"{_root}\" --no-build --nologo");

        // The assertion that matters: the suite ran and passed. A FileNotFoundException for
        // appsettings.json, an unresolvable schema bundle, or a broken base URL all fail here
        // and none of them fail a compile check.
        test.Output.ShouldContain("Passed!", customMessage: test.Output);
        test.ExitCode.ShouldBe(0, test.Output);
    }

    [TestMethod]
    public async Task ScaffoldedConfigurationTravelsToTheOutputDirectory()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        (await RunAsync("dotnet", $"build \"{_root}\" --nologo -v q")).ExitCode.ShouldBe(0);

        var output = Path.Combine(_root, "bin", "Debug", "net10.0");
        foreach (var required in new[] { "appsettings.json", "spec-schemas.json", "spec-paths.json" })
            File.Exists(Path.Combine(output, required)).ShouldBeTrue($"{required} did not reach the output directory.");
    }

    /// <summary>
    /// The F1 live proof (plan Task 8, Step 2a). Everything else in this file proves a generated
    /// suite builds and runs; nothing yet proves a fixture is actually <i>loaded and used</i> by
    /// a running test rather than merely declared for copying (Task 4a proved only the latter).
    /// Runs exactly the sequence an adopter does — generate, repair, hand-fill the sentinel,
    /// build, run — against an operation whose only way to succeed is a fixture value reaching a
    /// live HTTP request.
    /// </summary>
    [TestMethod]
    public async Task FixtureParameterReachesALiveRequestEndToEnd()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithPathParameter);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        // `generate` is read-only under fixtures/ and refuses to run at all while one is
        // missing (it exits with "no fixture found", the drift check working as intended) — so
        // `repair` must create the fixture first, exactly as it does in the two tests above.
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Guard against the generated suite silently missing the operation entirely (the plan's
        // second failure mode) as early as possible: if getStatusById were never generated, the
        // fixture `repair` just created for it would be orphaned and this assertion would catch
        // it directly, rather than only inferring it later from a shorter trx.
        var generatedFile = Directory.GetFiles(_root, "StatusTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one StatusTests.g.cs");
        File.ReadAllText(generatedFile).ShouldContain("GetStatusById_Contract",
            customMessage: "the operation this test exists to prove must actually be generated");

        var fixturePath = Path.Combine(_root, "fixtures", "getStatusById.json");
        File.Exists(fixturePath).ShouldBeTrue("`fixtures repair` should have composed one fixture for the required path parameter");
        var beforeReplace = File.ReadAllText(fixturePath);
        beforeReplace.ShouldContain("\"TODO:id\"", customMessage: "a required path parameter always gets a sentinel (decision 1)");

        // The step a human adopter performs by hand: fill in the sentinel with a value the
        // service actually accepts.
        File.WriteAllText(fixturePath, beforeReplace.Replace("\"TODO:id\"", "\"42\"", StringComparison.Ordinal));

        // Guard against the first failure mode directly, rather than only inferring it from the
        // live request's outcome below: re-reads the file from disk (not the in-memory string
        // just written) so a no-op caused by the wrong path, the wrong key, or writing to the
        // wrong file is caught here rather than only by RequireFixture further down.
        File.ReadAllText(fixturePath).ShouldNotContain("TODO:id",
            customMessage: "the sentinel replacement must actually take effect on disk");

        var build = await RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await RunAsync("dotnet",
            $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var statusByIdResult = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatusById_Contract", StringComparison.Ordinal));

        // The assertion that closes the F1 loop: the specific test this fixture exists for was
        // both present (guards the second failure mode — the suite cannot quietly pass one test
        // short with nothing noticing) and passed (guards the first — an unresolved sentinel
        // makes RequireFixture throw before any request is built, and the stub itself rejects
        // the literal sentinel too, so a no-op replace fails here even if the direct on-disk
        // check above were somehow fooled).
        statusByIdResult.ShouldNotBeNull(
            $"GetStatusById_Contract did not appear in the trx at all — the suite ran one test short and nothing noticed:{Environment.NewLine}{test.Output}");
        statusByIdResult!.Attribute("outcome")?.Value.ShouldBe("Passed",
            $"GetStatusById_Contract ran but did not pass — the fixture value likely never reached the live request:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);
    }

    /// <summary>
    /// Plan Task 6, Step 1 — the crux of the v1-b fixture lifecycle. A first draft of this test
    /// claimed to discriminate three orderings ("services before seeding", "seeding before
    /// resolution", "resolution before validation") but only the last two actually failed under
    /// any wrong implementation, and both failed the <em>same</em> way (an unresolved
    /// <c>{{fixture:...}}</c> token) — "services before seeding" was true by construction the
    /// fixture could not even compile without it. This version tests two independently
    /// falsifiable orderings instead:
    /// <list type="bullet">
    /// <item><description>Seeding after readiness. <c>GoldenFixtureSources.SeedIdFixture</c>
    /// takes a real <c>IHttpClientFactory</c> constructor dependency (proving fixtures can
    /// consume anything <c>ConfigureServices</c> registered) and calls the stub's
    /// <c>/api/seed</c>, which only answers once <see cref="GoldenApiStub"/> has seen as many
    /// <c>/health/ready</c> probes as <c>Readiness.WaitAsync</c> requires to return
    /// (<see cref="GoldenApiStub.RequiredReadyProbes"/>). If seeding ran before readiness, this
    /// call gets a 503 and the fixture throws.</description></item>
    /// <item><description>Resolution after seeding, seeding after services. The fixture publishes
    /// the value <em>it received back from that live call</em>, and the fixture value under test
    /// points at <c>{{fixture:seededId}}</c>. Validation would flag that token as unresolved, and
    /// <c>RequireFixture</c> would throw before any request is built, unless
    /// <c>TokenResolver</c> was built with the published key already in hand.</description></item>
    /// </list>
    /// <para>
    /// Asserting <c>test.Output.ShouldContain("Passed!")</c> alone would be too weak — a suite
    /// that resolved the token to the wrong value, or somehow ran with the wrong order but still
    /// happened to satisfy the stub (which answers 200 for almost anything under
    /// <c>/api/status/</c>), could still print it. The assertion that actually closes the loop is
    /// on <see cref="GoldenApiStub.ReceivedPaths"/>: the stub, running in this process, records
    /// every path it served, so this test can confirm the exact value
    /// <c>GoldenFixtureSources.SeedIdFixture</c> published — not merely "some" value — reached
    /// the wire.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task APublishedFixtureKeyReachesALiveRequest()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithPathParameter);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Point the required path parameter at a fixture token instead of a literal value, so the
        // live request can only carry the right id if TokenResolver was built with the published
        // key already in hand.
        var fixturePath = Path.Combine(_root, "fixtures", "getStatusById.json");
        File.Exists(fixturePath).ShouldBeTrue("`fixtures repair` should have composed one fixture for the required path parameter");
        var beforeReplace = File.ReadAllText(fixturePath);
        beforeReplace.ShouldContain("\"TODO:id\"", customMessage: "a required path parameter always gets a sentinel (decision 1)");
        File.WriteAllText(fixturePath, beforeReplace.Replace("\"TODO:id\"", "\"{{fixture:seededId}}\"", StringComparison.Ordinal));

        // Register a fake assembly fixture the way an adopter would: a class implementing
        // IAssemblyFixture, added to the project, and wired into TestStartup.cs's Register hook.
        File.WriteAllText(Path.Combine(_root, "SeedIdFixture.cs"), GoldenFixtureSources.SeedIdFixture);
        RegisterFixture("SeedIdFixture");

        var build = await RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await RunAsync("dotnet",
            $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var statusByIdResult = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatusById_Contract", StringComparison.Ordinal));

        statusByIdResult.ShouldNotBeNull(
            $"GetStatusById_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        statusByIdResult!.Attribute("outcome")?.Value.ShouldBe("Passed",
            $"GetStatusById_Contract ran but did not pass — a published fixture key likely never reached " +
            $"TokenResolver:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);

        // The assertion that actually proves the order, not just that the suite reported success:
        // the exact value SeedIdFixture published — "seeded-42", not "TODO:id" and not anything
        // else — must have reached the stub on the wire.
        _stub.ReceivedPaths.ShouldContain("/api/status/seeded-42",
            $"the published fixture value never reached the live request. Paths actually served: " +
            $"{string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// Proves <c>AppliesTo</c>-based skipping — <c>FixtureRunner.RunAsync</c>'s own logic,
    /// already unit-tested against a bare <see cref="StringWriter"/> in
    /// <c>FixtureRunnerTests</c> — actually threads correctly through <c>TestHost</c>'s real
    /// profile resolution and real DI-resolved fixture list in a live, generated, built, and run
    /// suite. <c>GoldenFixtureSources.SkippedFixture</c>'s <c>AppliesTo</c> excludes the
    /// scaffold's default profile ("local"); if it ran anyway, it throws, which fails
    /// [AssemblyInitialize] and every test in the suite — the actual strong signal this test
    /// relies on (<c>test.ExitCode.ShouldBe(0)</c>). The marker file it also writes first is
    /// belt-and-braces only: absence of a file degrades to a vacuous pass if its path is ever
    /// wrong, and there is no positive control proving the mechanism itself works.
    /// <para>
    /// Also asserts the skip line itself reached real process output — see
    /// <c>TestHost.ContextTextWriter</c>'s own doc for why that is
    /// <c>TestContext.DisplayMessage(Warning, ...)</c>, not <c>WriteLine</c>, and for the
    /// confirmed VSTest behaviour behind that choice.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task SkippedFixtureIsNotRunByALiveGeneratedSuite()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        File.WriteAllText(Path.Combine(_root, "SkippedFixture.cs"), GoldenFixtureSources.SkippedFixture);
        RegisterFixture("SkippedFixture");

        var build = await RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var test = await RunAsync("dotnet", $"test \"{_root}\" --no-build --nologo");

        // If SkippedFixture ran instead of being skipped, it throws and AssemblyInitialize fails
        // every test — the real signal. The suite must still pass.
        test.ExitCode.ShouldBe(0, test.Output);

        // Belt-and-braces: SkippedFixture writes this file to the output directory as the very
        // first thing it does if it ever runs at all, before it throws.
        var markerPath = Path.Combine(_root, "bin", "Debug", "net10.0", "skipped-fixture-ran.marker");
        File.Exists(markerPath).ShouldBeFalse(
            "SkippedFixture ran even though its AppliesTo excludes the active profile ('local') — " +
            "FixtureRunner's skip logic did not apply inside a live TestHost.InitializeAsync run.");

        // The seam DisplayMessage opened up: FixtureRunner's own skip line, verbatim, reaching
        // real process stdout on a passing run.
        test.Output.ShouldContain(
            "Skipping fixture 'Stub.ApiTests.SkippedFixture': its AppliesTo does not include profile 'local'.",
            customMessage: $"the skip line never reached process output:{Environment.NewLine}{test.Output}");
    }

    /// <summary>
    /// I1 (Task 6's third review round): the aggregated fixture-validation report must surface
    /// even when nothing fails — decision 2's whole point is that a non-blocking fixture problem
    /// stays visible while the run still succeeds. Uses <c>--filter</c> to run only
    /// <c>GetStatus_Contract</c>, the operation with no fixture, while
    /// <c>fixtures/getStatusById.json</c> keeps its unresolved <c>"TODO:id"</c> sentinel: nothing
    /// calls <c>RequireFixture("getStatusById")</c>, so nothing fails, and the run passes with a
    /// real problem sitting in the report. Before <c>TestHost</c> used
    /// <c>TestContext.DisplayMessage</c>, this report existed only as a <c>WriteLine</c> call
    /// that VSTest silently drops on exactly this kind of passing run (see
    /// <c>TestHost.ContextTextWriter</c>'s doc for the confirmed mechanism) — so this test would
    /// have passed against that bug: nothing here checks that the report exists, only that it
    /// reached somewhere a human or CI system would actually see it.
    /// </summary>
    [TestMethod]
    public async Task ValidationReportWithAProblemSurfacesOnAPassingRun()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithPathParameter);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var fixturePath = Path.Combine(_root, "fixtures", "getStatusById.json");
        File.ReadAllText(fixturePath).ShouldContain("\"TODO:id\"",
            customMessage: "left unresolved on purpose — this test needs a genuine, standing validation problem");

        var build = await RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        // "GetStatus_Contract" (no "By") does not match "GetStatusById_Contract" as a substring,
        // so this filter runs only the fixture-free operation and never touches the one with the
        // still-unresolved sentinel.
        var test = await RunAsync("dotnet",
            $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_Contract\"");

        test.ExitCode.ShouldBe(0,
            $"the filtered run should pass — nothing calls RequireFixture for the one operation with a " +
            $"problem:{Environment.NewLine}{test.Output}");

        test.Output.ShouldContain("getStatusById:",
            customMessage: $"the aggregated report never reached process output on this passing run:{Environment.NewLine}{test.Output}");
        test.Output.ShouldContain("is still unfilled (TODO:id)",
            customMessage: $"the report reached output but not with the expected problem detail:{Environment.NewLine}{test.Output}");
    }

    private void PointAtStub()
    {
        var path = Path.Combine(_root, "appsettings.json");
        var original = File.ReadAllText(path);

        // Pinned, not merely replaced: if the scaffold's own default ever changes, this
        // assertion catches the drift here — loudly, at the one place that must stay in sync
        // with GoldenApiStub.RequiredReadyProbes — rather than a correct TestHost silently
        // failing a seeding-vs-readiness golden test with a bare, seemingly-unrelated 503 (M4).
        const string consecutiveSuccessesMarker = "\"ConsecutiveSuccesses\": 2";
        original.ShouldContain(consecutiveSuccessesMarker,
            customMessage: "the scaffold's default InTest:Readiness:ConsecutiveSuccesses changed — " +
                "update GoldenApiStub.RequiredReadyProbes and this replacement together");

        var json = original
            .Replace("https://localhost:5001/", $"http://localhost:{_stub.Port}/", StringComparison.Ordinal)
            .Replace("\"TimeoutSeconds\": 120", "\"TimeoutSeconds\": 20", StringComparison.Ordinal)
            .Replace(consecutiveSuccessesMarker, $"\"ConsecutiveSuccesses\": {GoldenApiStub.RequiredReadyProbes}", StringComparison.Ordinal);

        File.WriteAllText(path, json);
    }

    /// <summary>The scaffold references InTest.Runtime from NuGet, which is not published.</summary>
    private void UseProjectReferenceInsteadOfPackage()
    {
        var runtimeProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "InTest.Runtime", "InTest.Runtime.csproj"));

        var path = Path.Combine(_root, "Stub.ApiTests.csproj");
        var csproj = File.ReadAllText(path).Replace(
            """<PackageReference Include="InTest.Runtime" Version="0.1.0" />""",
            $"""<ProjectReference Include="{runtimeProject}" />""",
            StringComparison.Ordinal);

        File.WriteAllText(path, csproj);
    }

    /// <summary>
    /// Wires a fixture class already written into <c>_root</c> into <c>TestStartup.cs</c>'s
    /// <c>Register</c> hook, the way an adopter would — replacing the scaffold's own
    /// placeholder comment, which must still be present or this is silently a no-op.
    /// </summary>
    private void RegisterFixture(string typeName)
    {
        var testStartupPath = Path.Combine(_root, "TestStartup.cs");
        var testStartup = File.ReadAllText(testStartupPath);
        const string placeholder = "// services.AddSingleton<IAssemblyFixture, YourFixture>();";
        testStartup.ShouldContain(placeholder,
            customMessage: "the scaffolded registration placeholder must still be present to replace");

        File.WriteAllText(testStartupPath, testStartup.Replace(
            placeholder,
            $"services.AddSingleton<IAssemblyFixture, {typeName}>();",
            StringComparison.Ordinal));
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }
}
