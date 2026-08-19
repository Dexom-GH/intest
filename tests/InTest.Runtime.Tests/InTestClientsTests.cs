using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// F10: <c>TestHost.InitializeAsync</c> registered exactly one named client
/// (<see cref="InTestClients.Api"/>) and handed that same client to
/// <see cref="Readiness.WaitAsync"/>. An adopter following the getting-started guide attaches a
/// bearer handler to <see cref="InTestClients.Api"/> via <c>ConfigureServices</c>; when the
/// identity provider is unreachable that handler throws on every request through the client —
/// including the anonymous <c>/health/ready</c> probe, which needed no token at all. The result
/// was a dead identity server reported as a dead API, after a 120-second wait. Confirmed live:
/// before <see cref="InTestClients.Readiness"/> existed, this test's own probe (against
/// <see cref="InTestClients.Api"/>, the only client there was) reproduced exactly that —
/// <c>ReadinessTimeoutException</c> with "last response: HttpRequestException" — rather than the
/// assertion below ever running.
/// <para>
/// This does not exercise <c>TestHost.InitializeAsync</c> directly — this repo deliberately does
/// not build an in-process harness for it (see <c>TestHostTests</c>'s note on
/// <c>ContextTextWriter</c>: it needs <c>AppContext.BaseDirectory</c>, a real <c>TestContext</c>,
/// and live HTTP). It pins the two-client shape and the "ConfigureServices attaches to Api only"
/// contract that fix relies on. <c>GeneratedSuiteExecutionTests</c>'s Golden test proves
/// <c>TestHost.InitializeAsync</c> itself resolves <see cref="InTestClients.Readiness"/> for the
/// real probe, end to end, against a real generated suite.
/// </para>
/// </summary>
[TestClass]
public class InTestClientsTests
{
    /// <summary>Always throws — stands in for a bearer handler that cannot reach an unreachable
    /// identity provider. Records whether it ran at all, which is the only thing this test
    /// needs: the readiness probe must never reach it.</summary>
    private sealed class ThrowingHandler : DelegatingHandler
    {
        public bool Ran { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ran = true;
            throw new HttpRequestException("identity provider unreachable");
        }
    }

    /// <summary>Stands in for the live health endpoint so this test sends no real network
    /// traffic — always answers 200 immediately.</summary>
    private sealed class AlwaysReadyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static ReadinessOptions Options() => new()
    {
        Enabled = true, Path = "health/ready", ExpectStatus = 200,
        ConsecutiveSuccesses = 1, TimeoutSeconds = 5, IntervalSeconds = 0
    };

    private static CancellationToken TestContextCancellation() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [TestMethod]
    public async Task ReadinessProbeDoesNotRunApiClientHandlers()
    {
        var throwing = new ThrowingHandler();

        var services = new ServiceCollection();
        services.AddTransient(_ => new RunIdHandler(() => "run-1"));

        services.AddHttpClient(InTestClients.Api, client => client.BaseAddress = new Uri("https://h.invalid/api/"))
            .AddHttpMessageHandler<RunIdHandler>();
        services.AddHttpClient(InTestClients.Readiness, client => client.BaseAddress = new Uri("https://h.invalid/api/"))
            .AddHttpMessageHandler<RunIdHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new AlwaysReadyHandler());

        // Where TestHost.InitializeAsync's own ConfigureServices?.Invoke(services, Configuration)
        // runs: after InTest's registrations, attaching to InTestClients.Api specifically. This
        // is how the bug reaches the readiness probe in the first place (decision 1).
        services.AddHttpClient(InTestClients.Api).AddHttpMessageHandler(() => throwing);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Readiness);

        await Readiness.WaitAsync(client, Options(), TestContextCancellation());

        throwing.Ran.ShouldBeFalse("a handler attached to InTestClients.Api must never run for the readiness probe");
    }
}
