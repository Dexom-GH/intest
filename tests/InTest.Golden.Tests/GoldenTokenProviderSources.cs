namespace InTest.Golden.Tests;

/// <summary>
/// C# source for an <c>ITestTokenProvider</c> the golden execution tests write into a scaffolded
/// project, the way an adopter's own provider would arrive — see
/// <see cref="TwoIdentityTokenProvider"/>'s own doc for which test uses it and why. Kept
/// separate from <see cref="GoldenFixtureSources"/> and <see cref="GoldenAuthHandlerSources"/>
/// for the same reason those two are separate from each other: a distinct concern, in its own file.
/// </summary>
internal static class GoldenTokenProviderSources
{
    /// <summary>
    /// For <c>GeneratedSuiteExecutionTests.AuthCasesReceiveRealStatusesOverTheWire</c> — Task 5
    /// Step 2's live wire proof. Advertises two identities so the wrong-scope 403 case's guard
    /// (<c>RequireMultipleIdentities</c>) passes, and issues a token that names which identity it
    /// was issued for — <c>"token-for-{identity}"</c> — rather than an opaque value, so
    /// <see cref="GoldenApiStub"/> can map a request's <c>Authorization</c> header back to a
    /// scope without needing to parse a real JWT or run any identity protocol at all. The
    /// generated 401 case never reaches <c>GetTokenAsync</c> — <c>AuthHandler</c> short-circuits
    /// on the no-token sentinel before ever asking a provider for anything.
    /// </summary>
    public const string TwoIdentityTokenProvider = """
    using InTest.Runtime;

    namespace Stub.ApiTests;

    public sealed class TwoIdentityTokenProvider : ITestTokenProvider
    {
        public IReadOnlyList<string> Identities { get; } = ["default", "secondary"];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            Task.FromResult($"token-for-{identity ?? Identities[0]}");
    }
    """;
}
