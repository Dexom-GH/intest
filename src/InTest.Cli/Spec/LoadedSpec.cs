using Microsoft.OpenApi;

namespace InTest.Cli.Spec;

public sealed record LoadedSpec(OpenApiDocument Document, OpenApiSpecVersion Version, string RawJson);