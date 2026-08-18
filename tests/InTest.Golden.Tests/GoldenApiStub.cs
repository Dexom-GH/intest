using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace InTest.Golden.Tests;

/// <summary>
/// A minimal, stateful HTTP stub the golden execution tests point a generated suite's
/// <c>Api:BaseUrl</c> at, standing in for the API under test. Runs in this test process on a
/// free local port and records enough state — every request path served, how many times
/// <c>/health/ready</c> has answered — for a test to assert what actually reached the wire,
/// rather than trusting the generated suite's own "Passed!" (see
/// <c>GeneratedSuiteExecutionTests</c> for what each test checks against it and why).
/// <para>
/// Extracted out of <c>GeneratedSuiteExecutionTests</c> (M7 of Task 6's third review round) once
/// that file had doubled in size from Task 6's own additions and a further test was already
/// planned for the same file.
/// </para>
/// </summary>
internal sealed class GoldenApiStub : IDisposable
{
    /// <summary>
    /// Matches the scaffold's default <c>InTest:Readiness:ConsecutiveSuccesses</c> (see
    /// <c>InitCommand</c>'s appsettings.json template). <c>Readiness.WaitAsync</c> cannot return
    /// before this many consecutive 200s from <c>/health/ready</c>, so <c>/api/seed</c> uses it
    /// as a gate: seeding that runs before readiness has genuinely completed gets a 503, not a
    /// value that happened to work anyway. <c>GeneratedSuiteExecutionTests.PointAtStub</c> pins
    /// the scaffold's own copy of this setting to this constant (rather than trusting the two to
    /// happen to agree), so a scaffold default change fails loudly there instead of silently
    /// changing what this gate actually proves.
    /// </summary>
    public const int RequiredReadyProbes = 2;

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _serverCancellation;
    private readonly ConcurrentBag<string> _receivedPaths = [];
    private int _readyProbeCount;

    public int Port { get; }

    /// <summary>
    /// Every request path the stub has served. A <see cref="ConcurrentBag{T}"/>, so arrival
    /// order is not preserved; assertions against this must be membership-only
    /// (<c>ShouldContain</c>), never order- or index-based.
    /// </summary>
    public IReadOnlyCollection<string> ReceivedPaths => _receivedPaths;

    public GoldenApiStub()
    {
        Port = FreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();

        _serverCancellation = new CancellationTokenSource();
        _ = ServeAsync(_serverCancellation.Token);
    }

    public void Dispose()
    {
        _serverCancellation.Cancel();
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
        ((IDisposable)_listener).Dispose();
        _serverCancellation.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            _receivedPaths.Add(path);
            var (status, body) = path switch
            {
                "/health/ready" => HandleHealthCheck(),
                "/api/status" => (200, """{"state":"ok"}"""),
                // Belt-and-braces, not the primary catch: RequireFixture already throws before a
                // request carrying an unresolved sentinel is ever built (confirmed by sabotaging
                // the replace step in FixtureParameterReachesALiveRequestEndToEnd — the failure
                // surfaces as FixtureUnresolvedException, not a live 400). This exists so the
                // live proof still fails loudly, rather than hanging on a request that never
                // reaches the stub, if that call were ever removed from the template without a
                // unit test catching it first.
                "/api/status/TODO:id" => (400, """{"error":"unresolved fixture sentinel"}"""),
                // Only SeedIdFixture (APublishedFixtureKeyReachesALiveRequest) calls this. 503
                // until readiness has genuinely been satisfied — see RequiredReadyProbes' own
                // doc — so a fixture that ran before Readiness.WaitAsync returned gets a real
                // failure instead of a value that happened to work anyway.
                "/api/seed" => HandleSeed(),
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

    private (int, string) HandleHealthCheck()
    {
        Interlocked.Increment(ref _readyProbeCount);
        return (200, """{"status":"ready"}""");
    }

    private (int, string) HandleSeed() =>
        Volatile.Read(ref _readyProbeCount) >= RequiredReadyProbes
            ? (200, """{"seedValue":"seeded-42"}""")
            : (503, """{"error":"not ready for seeding yet"}""");

    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
