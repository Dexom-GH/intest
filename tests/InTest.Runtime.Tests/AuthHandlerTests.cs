using System.Net;
using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// F8's remaining half: <see cref="ITestTokenProvider"/>, <see cref="StaticTokenProvider"/> and
/// <c>Identities</c> have shipped since v1-b with nothing calling <c>GetTokenAsync</c>.
/// <see cref="AuthHandler"/> is that caller.
/// </summary>
[TestClass]
public class AuthHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? SeenRequest;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SeenRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>Records exactly which identity it was asked for, so a test can assert on the
    /// ambient value AuthHandler actually forwarded rather than merely on the resulting header.</summary>
    private sealed class RecordingProvider(string token, IReadOnlyList<string>? identities = null) : ITestTokenProvider
    {
        public string? LastAudience;
        public string? LastIdentity;

        public IReadOnlyList<string> Identities { get; } = identities ?? ["default", "secondary"];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default)
        {
            LastAudience = audience;
            LastIdentity = identity;
            return Task.FromResult(token);
        }
    }

    private sealed class ThrowingProvider : ITestTokenProvider
    {
        public IReadOnlyList<string> Identities { get; } = ["default"];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("identity server unreachable");
    }

    private static async Task<HttpRequestMessage> SendThroughHandler(ITestTokenProvider? provider, string audience = "api://orders")
    {
        var inner = new CapturingHandler();
        var handler = new AuthHandler(provider, audience) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        await client.GetAsync("https://example.invalid/");
        return inner.SeenRequest!;
    }

    [TestInitialize]
    public void Reset() => InTestAmbient.Identity.Value = null;

    [TestMethod]
    public async Task SetsAuthorizationHeaderFromTheProvider()
    {
        InTestAmbient.Identity.Value = "default";
        var provider = new RecordingProvider("tok-abc");

        var request = await SendThroughHandler(provider);

        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization!.Parameter.ShouldBe("tok-abc");
    }

    [TestMethod]
    public async Task RequestsTheTokenForTheAmbientIdentityNotAlwaysTheDefault()
    {
        InTestAmbient.Identity.Value = "secondary";
        var provider = new RecordingProvider("tok-xyz");

        await SendThroughHandler(provider);

        provider.LastIdentity.ShouldBe("secondary");
    }

    /// <summary>
    /// Makes the previously-dead <see cref="RecordingProvider.LastAudience"/> field load-bearing.
    /// Question (c)'s audience resolution lives in <c>TestHost.ResolveAudience</c> (covered
    /// separately in <c>TestHostTests</c>) and is passed into <see cref="AuthHandler"/>'s
    /// constructor; this pins the second half — that whatever audience the handler was
    /// constructed with is the one that actually reaches the provider, not a value hardcoded
    /// somewhere in between.
    /// </summary>
    [TestMethod]
    public async Task RequestsTheTokenForTheAudienceItWasConstructedWith()
    {
        InTestAmbient.Identity.Value = "default";
        var provider = new RecordingProvider("tok-abc");

        await SendThroughHandler(provider, audience: "api://custom-audience");

        provider.LastAudience.ShouldBe("api://custom-audience");
    }

    [TestMethod]
    public async Task SendsNoAuthorizationHeaderForTheNoTokenIdentity()
    {
        // The 401 test does not "use a bad token" — it sends none. A handler that always sets a
        // header would make that test unwritable.
        InTestAmbient.Identity.Value = InTestIdentities.None;
        var provider = new RecordingProvider("tok-abc");

        var request = await SendThroughHandler(provider);

        request.Headers.Authorization.ShouldBeNull();
        provider.LastIdentity.ShouldBeNull("the sentinel must short-circuit before the provider is ever asked for a token");
    }

    [TestMethod]
    public async Task NoOpsWhenNoProviderIsRegistered()
    {
        InTestAmbient.Identity.Value = "default";

        var request = await SendThroughHandler(provider: null);

        request.Headers.Authorization.ShouldBeNull();
    }

    [TestMethod]
    public async Task AProviderThatThrowsNamesTheProviderAndTheIdentity()
    {
        // Deliberately not "default": the implementation's catch clause falls back to the
        // literal string "(default)" when identity is null, so asserting on "default" is
        // satisfied by that fallback whether or not the identity is ever actually interpolated
        // into the message. A distinctive identity that cannot collide with the fallback is the
        // only way this assertion discriminates.
        InTestAmbient.Identity.Value = "identity-under-test";

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => SendThroughHandler(new ThrowingProvider()));

        ex.Message.ShouldContain(nameof(ThrowingProvider),
            customMessage: "a bare HttpRequestException doesn't say which provider or identity failed");
        ex.Message.ShouldContain("identity-under-test");
    }

    [TestMethod]
    public async Task AmbientIdentityIsIsolatedPerAsyncFlow()
    {
        async Task<string?> RunWith(string identity)
        {
            InTestAmbient.Identity.Value = identity;
            var handlerProvider = new RecordingProvider("tok");
            await SendThroughHandler(handlerProvider);
            return handlerProvider.LastIdentity;
        }

        var first = Task.Run(() => RunWith("identity-a"));
        var second = Task.Run(() => RunWith("identity-b"));
        var results = await Task.WhenAll(first, second);

        results.ShouldBe(["identity-a", "identity-b"], ignoreOrder: true);
    }
}
