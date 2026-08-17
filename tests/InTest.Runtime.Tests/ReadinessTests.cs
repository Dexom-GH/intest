using System.Net;
using System.Net.Http;
using InTest.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class ReadinessTests
{
    private sealed class ScriptedHandler(params HttpStatusCode[] script) : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
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

    private static CancellationToken TestContextCancellation() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
}
