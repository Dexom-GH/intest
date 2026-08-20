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

    [TestMethod]
    public async Task RequiresConsecutiveSuccessesSoAnOldInstanceCannotSatisfyIt()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        await Readiness.WaitAsync(client, TestSupport.Options(consecutiveSuccesses: 2), cts.Token);

        handler.Calls.ShouldBe(4);
    }

    [TestMethod]
    public async Task ThrowsWithTheLastResponseWhenItNeverBecomesReady()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        var ex = await Should.ThrowAsync<ReadinessTimeoutException>(
            () => Readiness.WaitAsync(client, TestSupport.Options(consecutiveSuccesses: 2), cts.Token));

        ex.Message.ShouldContain("did not become ready");
        ex.Message.ShouldContain("503");
    }

    [TestMethod]
    public async Task DisabledReadinessIssuesNoRequests()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        var options = TestSupport.Options(consecutiveSuccesses: 2);
        options.Enabled = false;

        await Readiness.WaitAsync(client, options, cts.Token);
        handler.Calls.ShouldBe(0);
    }

    [TestMethod]
    public async Task FailsImmediatelyOnAStatusThatWillNeverChange()
    {
        // A 404 means the probe path is wrong, not that the service is starting. Waiting the
        // full timeout for one turns a three-second diagnosis into a two-minute one.
        var handler = new ScriptedHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        var options = TestSupport.Options(consecutiveSuccesses: 2);
        options.TimeoutSeconds = 120;

        var ex = await Should.ThrowAsync<ReadinessTimeoutException>(
            () => Readiness.WaitAsync(client, options, cts.Token));

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
        using var cts = TestSupport.TimeoutToken();

        var options = TestSupport.Options(consecutiveSuccesses: 2);
        options.Path = path;

        await Readiness.WaitAsync(client, options, cts.Token);

        handler.LastRequestUri.ShouldBe(expected);
    }
}
