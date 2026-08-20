using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class RunIdHandlerTests
{
    private static async Task<string?> SendAsync(string runId)
    {
        var inner = new TestSupport.CapturingHandler();
        var handler = new RunIdHandler(() => runId) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        await client.GetAsync("https://example.invalid/");
        return inner.SeenRunIdHeader;
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
