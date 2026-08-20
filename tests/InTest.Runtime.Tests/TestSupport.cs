using System.Net;

namespace InTest.Runtime.Tests;

/// <summary>
/// Test-support helpers three agents each wrote their own copy of before Task 10 item 6
/// consolidated them: <see cref="CapturingHandler"/> lived, byte-identical but for a parameter
/// name, in <c>InTestClientsTests</c> and <c>AuthHandlerTests</c>, plus a header-only variant in
/// <c>RunIdHandlerTests</c>; <see cref="Options"/> was copied verbatim from
/// <c>ReadinessTests</c> into <c>InTestClientsTests</c>; <see cref="TimeoutToken"/> replaces a
/// <c>TestContextCancellation()</c> duplicated the same way, which leaked its
/// <see cref="CancellationTokenSource"/> on every call.
/// </summary>
internal static class TestSupport
{
    /// <summary>
    /// Records the request it saw — including the run-id header <c>RunIdHandler</c> sets — and
    /// answers 200 without any real network traffic. Subsumes the three near-duplicate handlers
    /// this replaces: <c>AuthHandlerTests</c> and <c>InTestClientsTests</c> only ever read
    /// <see cref="SeenRequest"/>; <c>RunIdHandlerTests</c> only ever read the run-id header,
    /// available here as <see cref="SeenRunIdHeader"/> rather than a second handler recomputing
    /// it.
    /// </summary>
    internal sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? SeenRequest;
        public string? SeenRunIdHeader;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SeenRequest = request;
            SeenRunIdHeader = request.Headers.TryGetValues("X-Test-Run-Id", out var v) ? string.Join(",", v) : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    internal static ReadinessOptions Options(int consecutiveSuccesses = 2) => new()
    {
        Enabled = true, Path = "health/ready", ExpectStatus = 200,
        ConsecutiveSuccesses = consecutiveSuccesses, TimeoutSeconds = 5, IntervalSeconds = 0
    };

    /// <summary>
    /// A 30-second safety net against a genuine hang in the code under test — not a stand-in for
    /// <c>TestContext.CancellationToken</c>, which reflects the runner's own timeout policy (none,
    /// by default, for these tests) and would let a real bug spin forever instead of failing this
    /// test fast. Returns the <see cref="CancellationTokenSource"/> itself, not just its
    /// <c>.Token</c>, so the caller can dispose it in a <c>using</c> — the copy this replaces
    /// created one per call and disposed none of them.
    /// </summary>
    internal static CancellationTokenSource TimeoutToken() => new(TimeSpan.FromSeconds(30));
}
