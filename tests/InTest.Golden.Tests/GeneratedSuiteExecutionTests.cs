using System.Diagnostics;
using System.Net;
using System.Text;
using System.Xml.Linq;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// Scaffolds a project, generates into it, builds it, and <b>runs</b> it against a live stub.
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
    private HttpListener _listener = null!;
    private int _port;
    private CancellationTokenSource _serverCancellation = null!;

    [TestInitialize]
    public void StartStubAndScaffold()
    {
        _port = FreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();

        _serverCancellation = new CancellationTokenSource();
        _ = ServeAsync(_serverCancellation.Token);

        _root = Path.Combine(Path.GetTempPath(), "intest-run-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec);
    }

    [TestCleanup]
    public void StopStub()
    {
        _serverCancellation.Cancel();
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
        ((IDisposable)_listener).Dispose();
        _serverCancellation.Dispose();

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

    private void PointAtStub()
    {
        var path = Path.Combine(_root, "appsettings.json");
        var json = File.ReadAllText(path)
            .Replace("https://localhost:5001/", $"http://localhost:{_port}/", StringComparison.Ordinal)
            .Replace("\"TimeoutSeconds\": 120", "\"TimeoutSeconds\": 20", StringComparison.Ordinal);
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

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            var (status, body) = path switch
            {
                "/health/ready" => (200, """{"status":"ready"}"""),
                "/api/status" => (200, """{"state":"ok"}"""),
                // Belt-and-braces, not the primary catch: RequireFixture already throws before a
                // request carrying an unresolved sentinel is ever built (confirmed by sabotaging
                // the replace step below — the failure surfaces as FixtureUnresolvedException,
                // not a live 400). This exists so the live proof still fails loudly, rather than
                // hanging on a request that never reaches the stub, if that call were ever
                // removed from the template without the Step 1 unit test catching it first.
                "/api/status/TODO:id" => (400, """{"error":"unresolved fixture sentinel"}"""),
                _ when path.StartsWith("/api/status/", StringComparison.Ordinal) => (200, """{"state":"ok"}"""),
                _ => (404, """{"error":"not found"}""")
            };

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
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
