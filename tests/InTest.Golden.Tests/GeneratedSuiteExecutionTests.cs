using System.Diagnostics;
using System.Net;
using System.Text;
using InTest.Cli.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        // grows an operation that does need one. (Deliberately not done here — see Task 4a's
        // final report: the generated template still emits TestData.Require for every path
        // parameter until Task 8 rewires it to consume fixtures, so a fixture-needing operation
        // added to this spec today would fail at `dotnet test`, not prove the fixture pipeline.)
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
