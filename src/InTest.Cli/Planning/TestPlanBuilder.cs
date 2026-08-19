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

    // Decision 5: v1-c generates a declared-error case for 404 only. 400 has no deterministic
    // fixture-free trigger; 401/403 are the auth cases' territory (Task 5); everything else needs
    // conflicting state or input this plan does not create. Widening this set is a scope
    // decision for a later plan, not a constant to extend casually.
    private const int NotFoundStatus = 404;

    /// <summary>Statuses that carry no body by definition, so a missing schema is correct
    /// rather than a gap.</summary>
    private static readonly HashSet<int> BodilessStatuses = [204, 205, 304];

    public static TestPlan Build(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var skipped = new List<SkippedOperation>();
        var notes = new List<CoverageNote>();
        var draft = new List<(string Tag, TestCasePlan Case)>();
        var proposedNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, pathItem) in document.Paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var (method, operation) in (pathItem.Operations ?? []).OrderBy(o => o.Key.Method, StringComparer.Ordinal))
            {
                var key = OperationKey.Resolve(operation.OperationId, method.Method, path);

                // Delegated to the composer rather than reproduced here: it alone knows which
                // parameters it actually emits a value for (an optional query parameter with an
                // example or default still produces one), so it is the only place that can answer
                // this without risking drift from what a fixture write would really do.
                var needsFixture = FixtureComposer.NeedsFixture(operation);

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

                var pathParameterNames = PathParameters(path);
                var methodName = CSharpIdentifier.ToPascalCase(key.Value) + "_Contract";
                proposedNames[CaseIdentity(key.Value, CaseRole.Success)] = methodName;

                draft.Add((tag, new TestCasePlan(
                    MethodName: methodName,
                    DisplayName: $"Given {tag}, when {key.Value}, then {status}",
                    OperationKey: key.Value,
                    OperationKeySynthesized: key.Synthesized,
                    HttpMethod: method.Method.ToUpperInvariant(),
                    PathTemplate: path,
                    PathParameterNames: pathParameterNames,
                    ExpectedStatus: status,
                    SchemaKey: schemaKey,
                    Category: ContractCategory,
                    Role: CaseRole.Success,
                    NeedsFixture: needsFixture,
                    QueryParameterNames: QueryParameters(operation),
                    HasRequestBody: FixtureComposer.HasJsonBodyToCompose(operation))));

                // Declared-error cases come only from what the spec itself declares (decision 5)
                // — reached only once the success case above is confirmed generated, so a
                // declared-error case can never outlive an operation this method already skipped
                // (the `continue`s above it) and the two can never disagree about the operation.
                if (FindDeclaredResponse(operation, NotFoundStatus) is { } notFoundResponse)
                {
                    var requiredQueryParameters = RequiredQueryParameterNames(operation);

                    if (pathParameterNames.Count == 0)
                    {
                        // Nowhere to put an unmatchable value — telling a lookup query parameter
                        // from a filter is itself a guess. The operation's success case above
                        // still generated and runs, so this is a *note*, not a skip (§12): adding
                        // it to `skipped` would make GenerateCommand report a live, passing
                        // operation as skipped, and put it in coverage-report.json's `skipped`
                        // array instead of the artefact `--check` would actually expect it in.
                        notes.Add(new CoverageNote(key.Value,
                            $"declares {NotFoundStatus} but has no path parameter to target with an unmatchable value"));
                    }
                    else if (requiredQueryParameters.Count > 0)
                    {
                        // Decision 5's postscript: whether a missing *required* query parameter
                        // is answered with 400 or 404 depends on binding and route configuration
                        // — a measurement to take, not an assumption to ship. Sending only the
                        // unmatchable path id and omitting a required query parameter risks
                        // asserting 404 against what a compliant, correctly-routed API actually
                        // answers with 400 — the same hazard the no-path-parameter branch above
                        // exists to avoid, so it gets the same treatment: a note, not a guess
                        // shipped as a test.
                        notes.Add(new CoverageNote(key.Value,
                            $"declares {NotFoundStatus} but has required query parameter(s) " +
                            $"({string.Join(", ", requiredQueryParameters)}) that an unmatchable-id-only request would omit"));
                    }
                    else
                    {
                        var notFoundMethodName = CSharpIdentifier.ToPascalCase(key.Value) + "_NotFound";
                        proposedNames[CaseIdentity(key.Value, CaseRole.DeclaredError)] = notFoundMethodName;

                        draft.Add((tag, new TestCasePlan(
                            MethodName: notFoundMethodName,
                            DisplayName: $"Given {tag}, when {key.Value}, then {NotFoundStatus}",
                            OperationKey: key.Value,
                            OperationKeySynthesized: key.Synthesized,
                            HttpMethod: method.Method.ToUpperInvariant(),
                            PathTemplate: path,
                            PathParameterNames: pathParameterNames,
                            ExpectedStatus: NotFoundStatus,
                            SchemaKey: ResolveSchemaKey(notFoundResponse, NotFoundStatus, key.Value),
                            Category: ContractCategory,
                            Role: CaseRole.DeclaredError,
                            // Decision 6: an unmatchable generated id and no body, never a
                            // fixture value — so an unfilled fixture can never block a test that
                            // needs no data, and a broken generator can never delete or mutate
                            // real state through this case.
                            NeedsFixture: false)));
                    }
                }
            }
        }

        var deduped = CSharpIdentifier.Dedupe(proposedNames);

        var classes = draft
            .GroupBy(d => d.Tag, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TestClassPlan(
                ClassName: g.Key + "Tests",
                Tag: g.Key,
                Cases: g.Select(d => d.Case with { MethodName = deduped[CaseIdentity(d.Case.OperationKey, d.Case.Role)] })
                        .OrderBy(c => c.MethodName, StringComparer.Ordinal)
                        .ToList()))
            .ToList();

        return new TestPlan(
            document.Info?.Title ?? "Api",
            classes,
            skipped.OrderBy(s => s.OperationKey, StringComparer.Ordinal).ToList(),
            notes.OrderBy(n => n.OperationKey, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// The dedupe dictionary's key (decision 4): operation key alone collapses a success case and
    /// its declared-error sibling onto the same entry, so every case for that operation is
    /// reassigned the same final <c>MethodName</c> — CS0111 the moment an operation has more than
    /// one case. Combining role into the key keeps each case's proposed name — and, when a real
    /// collision with another operation's name exists, its hash suffix — independent of its
    /// siblings.
    /// </summary>
    private static string CaseIdentity(string operationKey, CaseRole role) => $"{operationKey}#{role}";

    private static IOpenApiResponse? FindDeclaredResponse(OpenApiOperation operation, int status)
    {
        if (operation.Responses is null)
        {
            return null;
        }

        foreach (var (code, response) in operation.Responses)
        {
            if (int.TryParse(code, out var parsed) && parsed == status)
            {
                return response;
            }
        }

        return null;
    }

    private static (int Status, IOpenApiResponse Response)? SelectSuccessResponse(OpenApiOperation operation)
    {
        if (operation.Responses is null)
        {
            return null;
        }

        foreach (var (code, response) in operation.Responses.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (int.TryParse(code, out var status) && status is >= 200 and < 400)
            {
                return (status, response);
            }
        }

        return null;
    }

    private static string? ResolveSchemaKey(IOpenApiResponse response, int status, string operationKey)
    {
        if (BodilessStatuses.Contains(status))
        {
            return null;
        }

        if (response.Content is null || !response.Content.TryGetValue(JsonMediaType, out var media) || media.Schema is null)
        {
            return null;
        }

        // A reference resolves to its component name; anything inline gets a synthesized key
        // so that contract tests never silently degrade to a status-code check.
        return media.Schema is OpenApiSchemaReference reference && reference.Reference?.Id is { Length: > 0 } id
            ? id
            : $"op:{operationKey}:{status}:{JsonMediaType}";
    }

    /// <summary>
    /// All declared <c>in: query</c> parameter names, required or not. This is a presence check
    /// only — it must not replicate <see cref="FixtureComposer"/>'s tiered precedence for which
    /// of them actually get a fixture entry (decision 1); the template only needs to know whether
    /// to look any query parameters up at runtime at all.
    /// </summary>
    private static IReadOnlyList<string> QueryParameters(OpenApiOperation operation)
        => (operation.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Query)
            .Select(p => p.Name!)
            .ToList();

    /// <summary>
    /// The subset of <see cref="QueryParameters"/> the spec marks <c>required: true</c> — the
    /// ones a declared-error case cannot simply omit without risking a 400-vs-404 mismatch (see
    /// the required-query-parameter branch above).
    /// </summary>
    private static IReadOnlyList<string> RequiredQueryParameterNames(OpenApiOperation operation)
        => (operation.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Query && p.Required)
            .Select(p => p.Name!)
            .ToList();

    private static IReadOnlyList<string> PathParameters(string path)
    {
        var names = new List<string>();
        var i = 0;

        while (i < path.Length)
        {
            var open = path.IndexOf('{', i);
            if (open < 0)
            {
                break;
            }
            var close = path.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }
            names.Add(path[(open + 1)..close]);
            i = close + 1;
        }

        return names;
    }
}
