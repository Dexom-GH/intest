using System.Reflection;
using InTest.Cli.Planning;
using Scriban;

namespace InTest.Cli.Rendering;

public sealed class TemplateRenderer
{
    private readonly Template _classTemplate = Template.Parse(LoadEmbedded("mstest-class.scriban"));

    public string RenderClass(TestClassPlan plan, string @namespace, string baseClass)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var model = new
        {
            @namespace,
            class_name = plan.ClassName,
            base_class = baseClass,
            cases = plan.Cases.Select(c => new
            {
                method_name = c.MethodName,
                display_name = c.DisplayName,
                category = c.Category,
                operation_key = c.OperationKey,
                http_method_pascal = ToPascalMethod(c.HttpMethod),
                path_template = c.PathTemplate,
                path_argument_list = PathArguments(c),
                query_expression = QueryExpression(c),
                has_body = c.HasRequestBody,
                expected_status = c.ExpectedStatus,
                schema_key = c.SchemaKey,
                mutates = c.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE",
                // Decision 6: a declared-error case shares its operation key with the success
                // case beside it, so calling RequireFixture here would let that sibling's unfilled
                // or unresolved fixture block a case that needs no data at all — the exact failure
                // mode decision 6 exists to prevent.
                //
                // Deliberately phrased as "== Success" rather than "!= DeclaredError": Task 5 adds
                // CaseRole.Auth (see that enum's own doc comment), and decision 6 applies to auth
                // cases too — a wrong-scope 403 pointed at a real id via FixtureParameter succeeds
                // when auth is broken, deleting real data at exactly the moment something is
                // already wrong. Testing positively for the one safe role means any role this code
                // has not been told about yet — Auth included — takes the fixture-free arm by
                // default, rather than the destructive one. Not TestCasePlan.NeedsFixture, which
                // answers a different question: whether the operation gets a fixture *file* at all
                // (FixtureComposer's verdict). That is already false for parameterless success
                // cases like listOrders, which must still emit RequireFixture — using it here would
                // silently change success-case output.
                emits_fixture_lookup = c.Role == CaseRole.Success
            }).ToList()
        };

        var rendered = _classTemplate.Render(model, member => member.Name);
        return Normalize(rendered);
    }

    /// <summary>Normalizes line endings so golden files compare identically on every OS.</summary>
    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private static string ToPascalMethod(string httpMethod)
        => httpMethod.Length == 0 ? "Get" : char.ToUpperInvariant(httpMethod[0]) + httpMethod[1..].ToLowerInvariant();

    /// <summary>
    /// Every path parameter on a success case is unconditionally required (decision 1), so its
    /// value always comes from the fixture via <c>FixtureParameter</c> — never a sentinel
    /// constant, never TestData. Every other role is the deliberate exception (decision 6): it
    /// sends a fresh, generated id no seeded row can match, precisely so an unfilled fixture can
    /// never block it and a broken generator can never point a mutating non-success case at real
    /// data.
    ///
    /// The condition tests for Success, not "!= DeclaredError", for the same fail-safe reason as
    /// <c>emits_fixture_lookup</c> above: Task 5's Auth role must default to this same
    /// fixture-free arm the instant it exists, without anyone having to remember to add it here.
    /// </summary>
    private static string PathArguments(TestCasePlan plan)
    {
        if (plan.PathParameterNames.Count == 0)
        {
            return string.Empty;
        }

        var values = plan.Role != CaseRole.Success
            ? plan.PathParameterNames.Select(_ => "Guid.NewGuid().ToString()")
            : plan.PathParameterNames.Select(n => $"FixtureParameter(\"{plan.OperationKey}\", \"{n}\")");

        return ", " + string.Join(", ", values);
    }

    /// <summary>
    /// Appended to the built path so the query string comes entirely from whichever declared
    /// query parameters the fixture actually supplies (decision 1) — never baked into the
    /// template, since an optional parameter with no example or default is never sent at all and
    /// the template has no way to know at generation time which ones a hand-filled fixture will
    /// end up carrying.
    /// </summary>
    private static string QueryExpression(TestCasePlan plan)
    {
        var names = plan.QueryParameterNames ?? [];
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var nameArgs = string.Join(", ", names.Select(n => $"\"{n}\""));
        return $" + InTestUrl.BuildQuery(FixtureQueryParameters(\"{plan.OperationKey}\", {nameArgs}))";
    }

    private static string LoadEmbedded(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded template '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
