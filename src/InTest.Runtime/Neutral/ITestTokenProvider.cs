namespace InTest.Runtime;

/// <summary>
/// Supplies bearer tokens to generated tests. InTest ships only <see cref="StaticTokenProvider"/>;
/// everything else is the adopter's, so that no identity or cloud library is imposed.
/// </summary>
public interface ITestTokenProvider
{
    /// <summary>
    /// Identities this provider can issue tokens for. A count of one or zero gates the
    /// wrong-scope and wrong-tenant auth tests off, and is the source of the coverage
    /// report's gated-test count. A declared capability, never a probe.
    /// </summary>
    IReadOnlyCollection<string> Identities { get; }

    Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default);
}
