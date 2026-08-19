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
/// was a dead identity server reported as a dead API, after a 120-second wait.
/// <para>
/// This exercises <see cref="TestHost.RegisterInTestClients"/> directly rather than
/// hand-duplicating its registrations — the seam <c>TestHost.InitializeAsync</c> itself calls —
/// so this proves something about <c>TestHost</c>'s own code, not merely about
/// <c>Microsoft.Extensions.Http</c>'s named-client isolation (a review of the first version of
/// this test found exactly that gap and it was deleted rather than fixed; this replaces it).
/// <c>InitializeAsync</c> as a whole still gets no in-process harness — see
/// <c>TestHostTests</c>'s note on <c>ContextTextWriter</c> for why — but
/// <see cref="TestHost.RegisterInTestClients"/> needs none of what makes that true: no
/// <c>AppContext.BaseDirectory</c>, no real <c>TestContext</c>, no live HTTP.
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

        // The exact registration TestHost.InitializeAsync performs, via the seam it calls — not
        // a hand-duplicated copy of it.
        TestHost.RegisterInTestClients(services, new Uri("https://h.invalid/api/"));

        // Stand in for the live probe so this test sends no real network traffic. Additive to
        // whatever RegisterInTestClients already configured for this name (named-HttpClient
        // configuration composes rather than replaces).
        services.AddHttpClient(InTestClients.Readiness).ConfigurePrimaryHttpMessageHandler(() => new AlwaysReadyHandler());

        // Where an adopter's ConfigureServices attaches an auth handler, per the getting-started
        // guide: to InTestClients.Api specifically. This is how F10's bug reached the readiness
        // probe when both roles shared one client.
        services.AddHttpClient(InTestClients.Api).AddHttpMessageHandler(() => throwing);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Readiness);

        await Readiness.WaitAsync(client, Options(), TestContextCancellation());

        throwing.Ran.ShouldBeFalse("a handler attached to InTestClients.Api must never run for the readiness probe");
    }
}
