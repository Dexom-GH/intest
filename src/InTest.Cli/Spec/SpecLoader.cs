using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace InTest.Cli.Spec;

public sealed class SpecLoadException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record LoadedSpec(OpenApiDocument Document, OpenApiSpecVersion Version, string RawJson);

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
            throw new SpecLoadException(
                "The OpenAPI document could not be parsed:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  " + e.Message)));

        if (document.Paths is null || document.Paths.Count == 0)
            throw new SpecLoadException("The OpenAPI document declares no operations, so there is nothing to generate.");

        await Task.CompletedTask;
        return new LoadedSpec(document, result.Diagnostic?.SpecificationVersion ?? OpenApiSpecVersion.OpenApi3_0, text);
    }

    public static Task<LoadedSpec> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new SpecLoadException($"Spec file not found: {path}");

        return LoadFromTextAsync(File.ReadAllText(path), cancellationToken);
    }
}
