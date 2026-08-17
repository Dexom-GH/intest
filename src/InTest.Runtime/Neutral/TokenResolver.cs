using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace InTest.Runtime;

/// <summary>
/// Raised when a <c>{{...}}</c> token in a fixture cannot be resolved — an unknown token name, a
/// <c>{{fixture:...}}</c> token before v1-b (decision 4), or a <c>{{config:}}</c>/<c>{{secret:}}</c>
/// key with no configured value. Every message is built from the token's <em>name</em> only, never
/// a resolved value, so a secret that resolved successfully earlier in the same fixture cannot
/// leak into the message for a different, later failure in that same fixture.
/// </summary>
public sealed class FixtureResolutionException(string message) : Exception(message);

/// <summary>
/// Resolves <c>{{...}}</c> runtime tokens inside one fixture value, per §10's resolution-timing
/// table. <c>{{config:...}}</c> and <c>{{secret:...}}</c> read <see cref="IConfiguration"/>, which
/// <c>TestHost</c> already builds once at <c>AssemblyInitialize</c> — resolving through it needs no
/// extra caching here, since the read itself is already "once per run, after configuration build".
/// <c>{{runId}}</c> is a fixed string handed in at construction, so it is identical for the life of
/// this resolver. Only <c>{{utcNow}}</c> must vary per call: it invokes the clock (real time in
/// production, injectable for tests) every time <see cref="Resolve"/> runs, never once and reused —
/// see <c>FixtureStore.ResolvedBody</c>, which relies on that to differ between requests.
/// <c>{{fixture:...}}</c> is out of scope for v1-a (decision 4) and always fails, naming the token
/// rather than being left as literal text.
/// </summary>
public sealed class TokenResolver(IConfiguration configuration, string runId, Func<DateTimeOffset>? utcNowProvider = null)
{
    private const string SupportedTokens = "{{config:...}}, {{secret:...}}, {{runId}}, {{utcNow}}";

    private static readonly Regex TokenPattern = new(@"\{\{(?<token>[^{}]+)\}\}", RegexOptions.Compiled);

    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly string _runId = runId ?? throw new ArgumentNullException(nameof(runId));
    private readonly Func<DateTimeOffset> _utcNow = utcNowProvider ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// Resolves every <c>{{...}}</c> token in <paramref name="value"/>. <paramref name="fileName"/>
    /// is used only to identify the fixture in an error message — it never becomes part of a
    /// resolved value.
    /// </summary>
    public string Resolve(string value, string fileName)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(fileName);

        return TokenPattern.Replace(value, match => ResolveToken(match.Groups["token"].Value, fileName));
    }

    private string ResolveToken(string token, string fileName)
    {
        if (token == "runId") return _runId;
        if (token == "utcNow") return _utcNow().ToString("O");

        if (token.StartsWith("config:", StringComparison.Ordinal))
            return ResolveConfig(token["config:".Length..], fileName);
        if (token.StartsWith("secret:", StringComparison.Ordinal))
            return ResolveConfig(token["secret:".Length..], fileName);

        if (token.StartsWith("fixture:", StringComparison.Ordinal))
            throw new FixtureResolutionException(
                $"'{{{{{token}}}}}' in '{fileName}' is not supported until v1-b.");

        throw new FixtureResolutionException(
            $"Unknown token '{{{{{token}}}}}' in '{fileName}'. Supported tokens: {SupportedTokens}.");
    }

    private string ResolveConfig(string key, string fileName)
    {
        var value = _configuration[key];
        if (value is null)
            throw new FixtureResolutionException(
                $"Configuration key '{key}' required by '{fileName}' is not set.");
        return value;
    }
}
