using System.Net;
using System.Net.Http;
using InTest.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class RunIdHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? SeenHeader;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SeenHeader = request.Headers.TryGetValues("X-Test-Run-Id", out var v) ? string.Join(",", v) : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<string?> SendAsync(string runId)
    {
        var inner = new CapturingHandler();
        var handler = new RunIdHandler(() => runId) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        await client.GetAsync("https://example.invalid/");
        return inner.SeenHeader;
    }

    [TestInitialize]
    public void Reset() => InTestAmbient.TestId.Value = null;

    [TestMethod]
    public async Task StampsTheAmbientTestIdWhenOneIsSet()
    {
        InTestAmbient.TestId.Value = "run-1-mytest";
        (await SendAsync("run-1")).ShouldBe("run-1-mytest");
    }

    [TestMethod]
    public async Task FallsBackToRunIdWhenNoTestIsInScope()
    {
        (await SendAsync("run-1")).ShouldBe("run-1");
    }

    [TestMethod]
    public async Task AmbientValueIsIsolatedPerAsyncFlow()
    {
        var first = Task.Run(async () => { InTestAmbient.TestId.Value = "run-1-a"; return await SendAsync("run-1"); });
        var second = Task.Run(async () => { InTestAmbient.TestId.Value = "run-1-b"; return await SendAsync("run-1"); });
        var results = await Task.WhenAll(first, second);
        results.ShouldBe(["run-1-a", "run-1-b"], ignoreOrder: true);
    }
}
