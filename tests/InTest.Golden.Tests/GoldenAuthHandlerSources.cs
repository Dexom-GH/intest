namespace InTest.Golden.Tests;

/// <summary>
/// C# source for a <c>DelegatingHandler</c> the golden execution tests write into a scaffolded
/// project, the way an adopter's own bearer handler would arrive — see
/// <see cref="AlwaysThrowsHandler"/>'s own doc for which test uses it and why. Kept separate from
/// <see cref="GoldenFixtureSources"/>: that class's own doc scopes it to
/// <c>IAssemblyFixture</c> implementations, and this is not one.
/// </summary>
internal static class GoldenAuthHandlerSources
{
    /// <summary>
    /// For <c>GeneratedSuiteExecutionTests.ReadinessProbeSurvivesAThrowingApiHandler</c> — F10
    /// inverted. Stands in for a real bearer handler that cannot reach an unreachable identity
    /// provider: throws on every request unconditionally, the way a token fetch does when the
    /// identity server is down. Wired only onto <see cref="InTest.Runtime.InTestClients.Api"/>
    /// (see <c>GeneratedSuiteExecutionTests.AttachThrowingHandlerToApiClient</c>), never onto
    /// <see cref="InTest.Runtime.InTestClients.Readiness"/> — the whole point being that the
    /// readiness probe must survive this handler's presence on the other client.
    /// </summary>
    public const string AlwaysThrowsHandler = """
    using System.Net.Http;
    using InTest.Runtime;

    namespace Stub.ApiTests;

    public sealed class AlwaysThrowsHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("golden test: identity provider unreachable (F10 regression guard)");
        }
    }
    """;
}
