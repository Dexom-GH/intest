using System.Net;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class ReadinessTests
{
    private sealed class ScriptedHandler(params HttpStatusCode[] script) : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }
        public string? LastRequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            LastRequestUri = r.RequestUri?.ToString();
            var status = script[Math.Min(_index++, script.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static ReadinessOptions Options(int consecutive = 2) => new()
    {
        Enabled = true, Path = "health/ready", ExpectStatus = 200,
        ConsecutiveSuccesses = consecutive, TimeoutSeconds = 5, IntervalSeconds = 0
    };

    [TestMethod]
    public async Task RequiresConsecutiveSuccessesSoAnOldInstanceCannotSatisfyIt()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };

        await Readiness.WaitAsync(client, Options(), TestContextCancellation());

        handler.Calls.ShouldBe(4);
    }

    [TestMethod]
    public async Task ThrowsWithTheLastResponseWhenItNeverBecomesReady()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };

        var ex = await Should.ThrowAsync<ReadinessTimeoutException>(
            () => Readiness.WaitAsync(client, Options(), TestContextCancellation()));

        ex.Message.ShouldContain("did not become ready");
        ex.Message.ShouldContain("503");
    }

    [TestMethod]
    public async Task DisabledReadinessIssuesNoRequests()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };

        var options = Options();
        options.Enabled = false;

        await Readiness.WaitAsync(client, options, TestContextCancellation());
        handler.Calls.ShouldBe(0);
    }

    [TestMethod]
    public async Task FailsImmediatelyOnAStatusThatWillNeverChange()
    {
        // A 404 means the probe path is wrong, not that the service is starting. Waiting the
        // full timeout for one turns a three-second diagnosis into a two-minute one.
        var handler = new ScriptedHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };

        var options = Options();
        options.TimeoutSeconds = 120;

        var ex = await Should.ThrowAsync<ReadinessTimeoutException>(
            () => Readiness.WaitAsync(client, options, TestContextCancellation()));

        handler.Calls.ShouldBe(1, "it must not keep polling a terminal status");
        ex.Message.ShouldContain("will not change by waiting");
        ex.Message.ShouldContain("leading slash");
    }

    [TestMethod]
    [DataRow("health/ready", "https://h/api/health/ready", DisplayName = "no leading slash resolves under the base URL")]
    [DataRow("/health/ready", "https://h/health/ready", DisplayName = "leading slash resolves against the host root")]
    public async Task ProbePathResolutionFollowsStandardUriSemantics(string path, string expected)
    {
        // Health endpoints conventionally sit at the host root while the API sits under a
        // prefix, so the scaffold ships "/health/ready". Both forms must remain available.
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };

        var options = Options();
        options.Path = path;

        await Readiness.WaitAsync(client, options, TestContextCancellation());

        handler.LastRequestUri.ShouldBe(expected);
    }

    private static CancellationToken TestContextCancellation() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
}
