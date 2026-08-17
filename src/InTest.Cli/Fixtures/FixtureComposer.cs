using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace InTest.Cli.Fixtures;

/// <summary>
/// Implements §10's four-tier precedence for composing a fixture from an operation: a
/// media-type example wins outright over per-property examples, which win over declared
/// defaults, which win over a schema-shaped skeleton of <c>TODO:</c> sentinels. The recorded
/// <see cref="FixtureMeta.Tier"/> is the worst of those sources used anywhere in the document —
/// one unresolved property is enough to mark the whole fixture as needing attention.
/// </summary>
public static class FixtureComposer
{
    private const string JsonMediaType = "application/json";

    public static FixtureDocument Compose(
        OpenApiDocument document, string path, string httpMethod, string operationKey, string generatedBy)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operation = document.Paths[path].Operations![new HttpMethod(httpMethod)];
        var tier = new TierTracker();

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in operation.Parameters ?? [])
        {
            if (parameter.In is not (ParameterLocation.Path or ParameterLocation.Query)) continue;

            var value = ParameterValue(parameter, tier);
            if (value is not null) parameters[parameter.Name!] = value;
        }

        JsonNode? body = null;
        if (operation.RequestBody?.Content?.TryGetValue(JsonMediaType, out var media) is true && media.Schema is not null)
            body = ComposeBody(media, tier);

        return new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = tier.Value, OperationId = operationKey, GeneratedBy = generatedBy },
            Parameters = parameters,
            Body = body
        };
    }

    /// <summary>
    /// A path parameter is always sentinelled, whatever the document claims about its
    /// <c>required</c> flag — see decision 1. A query parameter is sentinelled only when it is
    /// genuinely required; an optional one is surfaced solely when the spec gives it a real
    /// value (an <c>example</c> or a <c>default</c>), and is omitted (returns <see langword="null"/>)
    /// otherwise so it is never sent.
    /// </summary>
    private static string? ParameterValue(IOpenApiParameter parameter, TierTracker tier)
    {
        var alwaysSentinelled = parameter.In is ParameterLocation.Path
            || (parameter.Required && parameter.In is ParameterLocation.Query);

        if (alwaysSentinelled)
        {
            tier.Record(4);
            return $"TODO:{parameter.Name}";
        }

        if (FirstExample(parameter.Schema) is { } example)
        {
            tier.Record(2);
            return ParameterScalarToString(example);
        }

        if (parameter.Schema?.Default is { } defaultValue)
        {
            tier.Record(3);
            return ParameterScalarToString(defaultValue);
        }

        return null;
    }

    private static string ParameterScalarToString(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();

    /// <summary>
    /// Microsoft.OpenApi 3.10.0 marks the singular <see cref="IOpenApiSchema.Example"/> obsolete
    /// in favor of the plural <see cref="IOpenApiSchema.Examples"/> — but for an OpenAPI 3.0.x
    /// document's singular <c>example</c> keyword, <c>Examples</c> is left empty; <c>Example</c>
    /// is the one actually populated. Confirmed against the installed package rather than
    /// assumed. <c>Example</c> is read deliberately, so the suppression is scoped to this line.
    /// </summary>
    private static JsonNode? FirstExample(IOpenApiSchema? schema)
    {
#pragma warning disable CS0618
        return schema?.Example;
#pragma warning restore CS0618
    }

    /// <summary>
    /// Tier 1: the media type's own example is used verbatim, with no per-property composition
    /// at all. Anything else falls to <see cref="ComposeFromSchema"/> for tiers 2 through 4.
    /// </summary>
    private static JsonNode? ComposeBody(IOpenApiMediaType media, TierTracker tier)
    {
        if (media.Example is not null)
        {
            tier.Record(1);
            return media.Example.DeepClone();
        }

        return ComposeFromSchema(media.Schema, "body", tier, []);
    }

    /// <summary>
    /// Recursively composes a value for one schema. <paramref name="propertyName"/> names the
    /// property this schema belongs to, used only if composition bottoms out at a sentinel.
    /// <paramref name="visitedRefs"/> tracks component schema ids currently on the recursion
    /// path; revisiting one (a self- or mutually-referencing schema) emits <see langword="null"/>
    /// and stops instead of recursing forever.
    /// </summary>
    private static JsonNode? ComposeFromSchema(
        IOpenApiSchema? schema, string propertyName, TierTracker tier, HashSet<string> visitedRefs)
    {
        if (schema is null) return null;

        if (schema is OpenApiSchemaReference reference)
        {
            var id = reference.Reference?.Id ?? string.Empty;
            if (!visitedRefs.Add(id)) return null;
            try { return ComposeFromSchema(reference.Target, propertyName, tier, visitedRefs); }
            finally { visitedRefs.Remove(id); }
        }

        if (FirstExample(schema) is { } example)
        {
            tier.Record(2);
            return example.DeepClone();
        }

        if (schema.Default is not null)
        {
            tier.Record(3);
            return schema.Default.DeepClone();
        }

        if (schema.Type?.HasFlag(JsonSchemaType.Object) is true)
        {
            var obj = new JsonObject();
            foreach (var (name, propertySchema) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
                obj[name] = ComposeFromSchema(propertySchema, name, tier, visitedRefs);
            return obj;
        }

        if (schema.Type?.HasFlag(JsonSchemaType.Array) is true && schema.Items is not null)
            return new JsonArray(ComposeFromSchema(schema.Items, propertyName, tier, visitedRefs));

        tier.Record(4);
        return JsonValue.Create($"TODO:{propertyName}");
    }

    /// <summary>Tracks the worst (highest-numbered) tier used anywhere while composing a fixture.</summary>
    private sealed class TierTracker
    {
        public int Value { get; private set; } = 1;

        public void Record(int candidateTier)
        {
            if (candidateTier > Value) Value = candidateTier;
        }
    }
}
