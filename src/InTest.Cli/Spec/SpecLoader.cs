using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace InTest.Cli.Spec;

/// <summary>
/// Loads an OpenAPI document. Microsoft.OpenApi 3.10.0 reads Swagger 2.0 and OpenAPI 3.0,
/// 3.1 and 3.2, normalizing dialect differences into one object model — which is what makes
/// a single downstream schema path possible.
/// </summary>
public static class SpecLoader
{
    public static async Task<LoadedSpec> LoadFromTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        ReadResult result;
        try
        {
            result = OpenApiDocument.Parse(text, "json", new OpenApiReaderSettings());
        }
        catch (Exception ex)
        {
            throw new SpecLoadException($"The OpenAPI document could not be parsed: {ex.Message}", ex);
        }

        var document = result.Document
            ?? throw new SpecLoadException("The OpenAPI document could not be parsed: no document was produced.");

        var errors = result.Diagnostic?.Errors;
        if (errors is { Count: > 0 })
        {
            throw new SpecLoadException(
            "The OpenAPI document could not be parsed:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e => "  " + e.Message)));
        }

        if (document.Paths is null || document.Paths.Count == 0)
        {
            throw new SpecLoadException("The OpenAPI document declares no operations, so there is nothing to generate.");
        }

        await Task.CompletedTask;
        return new LoadedSpec(document, result.Diagnostic?.SpecificationVersion ?? OpenApiSpecVersion.OpenApi3_0, text);
    }

    /// <summary>
    /// Whether a spec source names something this loader cannot read. Lives here, on the type
    /// that has the limitation: <see cref="SpecLoader"/> reads a spec from text and from a file,
    /// and from nothing else. When URL support lands — §9's <c>spec.json</c> snapshot, which
    /// gives a URL-sourced spec a reviewable diff — it lands as a sibling of
    /// <see cref="LoadFromFileAsync"/>, and this member and <see cref="UrlReason"/> go away with
    /// the limitation they describe.
    /// <para>
    /// The prefix test is deliberately narrow, and a general "is this an absolute URI" check is
    /// deliberately <i>not</i> used: <c>Uri.TryCreate("C:/specs/orders.json", UriKind.Absolute, …)</c>
    /// succeeds with scheme <c>file</c>, so the general check refuses the single most ordinary
    /// <c>spec.source</c> value on Windows. Only <c>http</c> and <c>https</c> are refused because
    /// only they were ever promised — the help text said "Path or URL" and getting started's
    /// Phase 1 pointed at a Swagger endpoint. Anything else with a scheme still fails as a path,
    /// which is what it is.
    /// </para>
    /// <para>
    /// Note what this is <i>not</i>. It is not a fifth member of the four value-safety rules
    /// <see cref="Configuration.ConfigLoader"/> maps. Those govern text reaching a grammar that
    /// could misread it, and the fix is escaping or refusal. This governs <i>capability</i>: the
    /// value is perfectly well-formed and means exactly what it says — InTest just cannot read
    /// that kind of source yet.
    /// </para>
    /// </summary>
    public static bool IsUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one sentence InTest says about a URL spec source, in the shape every other refusal in
    /// this repository uses (see <see cref="Naming.CSharpIdentifier.EmptyValueReason"/>): name
    /// the setting, quote what was written, say what is wrong with it, then the caller's rule and
    /// remedy. One constant rather than two literals that agree today — <c>InitCommand</c> and
    /// <see cref="Configuration.ConfigLoader"/> both say it, about <c>--spec</c> and about
    /// <c>spec.source</c>, and the adopter's next move is the same either way.
    /// <para>
    /// It names the roadmap because the alternative is worse: an adopter who read "Path or URL"
    /// and followed getting started's URL branch is not making a mistake they can diagnose from
    /// "unsupported" alone. What went wrong is that the documentation described a capability
    /// ahead of the build.
    /// </para>
    /// </summary>
    public static string UrlReason(string setting, string source, string rule) =>
        $"{setting} '{source}' is a URL, and InTest reads the OpenAPI document from a local file. " +
        "Reading the spec from a URL — snapshotting it to a committed spec.json so it still " +
        $"arrives as a reviewable diff — is designed but not built. {rule}";

    public static Task<LoadedSpec> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new SpecLoadException($"Spec file not found: {path}");
        }

        return LoadFromTextAsync(File.ReadAllText(path), cancellationToken);
    }
}
