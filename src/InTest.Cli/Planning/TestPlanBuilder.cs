using InTest.Cli.Fixtures;
using InTest.Cli.Naming;
using InTest.Cli.Spec;
using Microsoft.OpenApi;

namespace InTest.Cli.Planning;

public static class TestPlanBuilder
{
    private const string JsonMediaType = "application/json";
    private const string DefaultTag = "Default";
    private const string ContractCategory = "Contract";

    /// <summary>Statuses that carry no body by definition, so a missing schema is correct
    /// rather than a gap.</summary>
    private static readonly HashSet<int> BodilessStatuses = [204, 205, 304];

    public static TestPlan Build(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var skipped = new List<SkippedOperation>();
        var draft = new List<(string Tag, TestCasePlan Case)>();
        var proposedNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, pathItem) in document.Paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var (method, operation) in (pathItem.Operations ?? []).OrderBy(o => o.Key.Method, StringComparer.Ordinal))
            {
                var key = OperationKey.Resolve(operation.OperationId, method.Method, path);

                var needsFixture =
                    operation.RequestBody?.Content?.ContainsKey(JsonMediaType) is true ||
                    (operation.Parameters ?? []).Any(p =>
                        // Same predicate as Task 2's composer — a path parameter is required whether or not
                        // the document says so, because it cannot be omitted from the URL.
                        p.In is ParameterLocation.Path || (p.Required && p.In is ParameterLocation.Query));

                if (needsFixture && !FixtureDocument.TryValidateOperationKey(key.Value, out var reason))
                {
                    skipped.Add(new SkippedOperation(key.Value, reason));
                    continue;
                }

                if (operation.RequestBody?.Content is { Count: > 0 } requestContent &&
                    !requestContent.ContainsKey(JsonMediaType))
                {
                    skipped.Add(new SkippedOperation(key.Value,
                        $"request body media type(s) {string.Join(", ", requestContent.Keys.Order(StringComparer.Ordinal))} not supported in v0"));
                    continue;
                }

                var success = SelectSuccessResponse(operation);
                if (success is null)
                {
                    skipped.Add(new SkippedOperation(key.Value, "no 2xx or 3xx response declared"));
                    continue;
                }

                var (status, response) = success.Value;
                var schemaKey = ResolveSchemaKey(response, status, key.Value);

                var tag = operation.Tags?.FirstOrDefault()?.Name is { Length: > 0 } t
                    ? CSharpIdentifier.ToPascalCase(t)
                    : DefaultTag;

                var methodName = CSharpIdentifier.ToPascalCase(key.Value) + "_Contract";
                proposedNames[key.Value] = methodName;

                draft.Add((tag, new TestCasePlan(
                    MethodName: methodName,
                    DisplayName: $"Given {tag}, when {key.Value}, then {status}",
                    OperationKey: key.Value,
                    OperationKeySynthesized: key.Synthesized,
                    HttpMethod: method.Method.ToUpperInvariant(),
                    PathTemplate: path,
                    PathParameterNames: PathParameters(path),
                    ExpectedStatus: status,
                    SchemaKey: schemaKey,
                    Category: ContractCategory)));
            }
        }

        var deduped = CSharpIdentifier.Dedupe(proposedNames);

        var classes = draft
            .GroupBy(d => d.Tag, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TestClassPlan(
                ClassName: g.Key + "Tests",
                Tag: g.Key,
                Cases: g.Select(d => d.Case with { MethodName = deduped[d.Case.OperationKey] })
                        .OrderBy(c => c.MethodName, StringComparer.Ordinal)
                        .ToList()))
            .ToList();

        return new TestPlan(
            document.Info?.Title ?? "Api",
            classes,
            skipped.OrderBy(s => s.OperationKey, StringComparer.Ordinal).ToList());
    }

    private static (int Status, IOpenApiResponse Response)? SelectSuccessResponse(OpenApiOperation operation)
    {
        if (operation.Responses is null) return null;

        foreach (var (code, response) in operation.Responses.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (int.TryParse(code, out var status) && status is >= 200 and < 400)
                return (status, response);
        }

        return null;
    }

    private static string? ResolveSchemaKey(IOpenApiResponse response, int status, string operationKey)
    {
        if (BodilessStatuses.Contains(status)) return null;

        if (response.Content is null || !response.Content.TryGetValue(JsonMediaType, out var media) || media.Schema is null)
            return null;

        // A reference resolves to its component name; anything inline gets a synthesized key
        // so that contract tests never silently degrade to a status-code check.
        return media.Schema is OpenApiSchemaReference reference && reference.Reference?.Id is { Length: > 0 } id
            ? id
            : $"op:{operationKey}:{status}:{JsonMediaType}";
    }

    private static IReadOnlyList<string> PathParameters(string path)
    {
        var names = new List<string>();
        var i = 0;

        while (i < path.Length)
        {
            var open = path.IndexOf('{', i);
            if (open < 0) break;
            var close = path.IndexOf('}', open);
            if (close < 0) break;
            names.Add(path[(open + 1)..close]);
            i = close + 1;
        }

        return names;
    }
}
