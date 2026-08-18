namespace InTest.Runtime;

/// <summary>The only implementation InTest ships. One token, one identity.</summary>
public sealed class StaticTokenProvider(string token, string identityName = "default") : ITestTokenProvider
{
    private readonly string _token = token ?? throw new ArgumentNullException(nameof(token));

    public IReadOnlyCollection<string> Identities { get; } = [identityName];

    public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default)
    {
        if (identity is not null && !Identities.Contains(identity))
        {
            throw new ArgumentException(
            $"StaticTokenProvider serves only '{string.Join(", ", Identities)}'; '{identity}' was requested. " +
            "Implement ITestTokenProvider with more than one identity to enable the 403 auth tests.",
            nameof(identity));
        }

        return Task.FromResult(_token);
    }
}
