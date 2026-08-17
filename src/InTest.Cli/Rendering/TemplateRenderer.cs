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
                mutates = c.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE"
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
    /// Every path parameter is unconditionally required (decision 1), so its value always comes
    /// from the fixture via <c>FixtureParameter</c> — never a sentinel constant, never TestData.
    /// </summary>
    private static string PathArguments(TestCasePlan plan)
        => plan.PathParameterNames.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", plan.PathParameterNames.Select(n => $"FixtureParameter(\"{plan.OperationKey}\", \"{n}\")"));

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
        if (names.Count == 0) return string.Empty;

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
