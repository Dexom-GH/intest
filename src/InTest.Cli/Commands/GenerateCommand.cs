using System.Text.Json.Nodes;
using System.Text.Json;
using InTest.Cli.Coverage;
using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using InTest.Cli.Schemas;
using InTest.Cli.Spec;

namespace InTest.Cli.Commands;

public static class GenerateCommand
{
    public const int ExitOk = 0;
    public const int ExitWorkOutstanding = 1;
    public const int ExitToolError = 2;

    /// <summary>Longest leading run of path segments shared by every generated operation.</summary>
    internal static string CommonPathPrefix(Planning.TestPlan plan)
    {
        var paths = plan.Classes.SelectMany(c => c.Cases).Select(c => c.PathTemplate).ToList();
        if (paths.Count == 0) return string.Empty;

        var segmentLists = paths
            .Select(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var shortest = segmentLists.Min(s => s.Length);
        var shared = new List<string>();

        for (var i = 0; i < shortest; i++)
        {
            var candidate = segmentLists[0][i];
            if (candidate.StartsWith('{')) break;
            if (!segmentLists.All(s => string.Equals(s[i], candidate, StringComparison.OrdinalIgnoreCase))) break;
            shared.Add(candidate);
        }

        return shared.Count == 0 ? string.Empty : "/" + string.Join("/", shared);
    }

    public static async Task<int> RunAsync(string projectRoot, CancellationToken cancellationToken)
    {
        try
        {
            var configPath = Path.Combine(projectRoot, "intest.json");
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"No intest.json found in '{projectRoot}'. Run `intest init` first.");
                return ExitToolError;
            }

            using var config = JsonDocument.Parse(File.ReadAllText(configPath));
            var specRelative = config.RootElement.GetProperty("spec").GetProperty("source").GetString()!;
            var project = config.RootElement.GetProperty("project");
            var rootNamespace = project.GetProperty("rootNamespace").GetString()!;
            var baseClass = project.GetProperty("testBaseClass").GetString()!;

            var spec = await SpecLoader.LoadFromFileAsync(Path.Combine(projectRoot, specRelative), cancellationToken)
                                       .ConfigureAwait(false);

            var plan = TestPlanBuilder.Build(spec.Document);
            var generated = Path.Combine(projectRoot, "Generated");

            if (Directory.Exists(generated)) Directory.Delete(generated, recursive: true);
            Directory.CreateDirectory(generated);

            var renderer = new TemplateRenderer();
            foreach (var testClass in plan.Classes)
            {
                var source = renderer.RenderClass(testClass, rootNamespace, baseClass);
                await File.WriteAllTextAsync(Path.Combine(generated, testClass.ClassName + ".g.cs"), source, cancellationToken)
                          .ConfigureAwait(false);
            }

            await File.WriteAllTextAsync(Path.Combine(generated, "spec-schemas.json"),
                SchemaBundleBuilder.Build(spec.Document, plan), cancellationToken).ConfigureAwait(false);

            // The prefix every operation path shares, if any. TestHost uses it to detect a
            // base URL that repeats it; otherwise every request 404s and nothing says why.
            var pathManifest = new JsonObject { ["operationPathPrefix"] = CommonPathPrefix(plan) };
            await File.WriteAllTextAsync(
                Path.Combine(generated, "spec-paths.json"),
                pathManifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(Path.Combine(projectRoot, "coverage-report.json"),
                CoverageReport.ToJson(plan), cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"Generated {plan.Classes.Sum(c => c.Cases.Count)} test(s) across {plan.Classes.Count} class(es).");
            if (plan.Skipped.Count > 0)
                Console.WriteLine($"Skipped {plan.Skipped.Count} operation(s) — see coverage-report.json.");

            return ExitOk;
        }
        catch (SpecLoadException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitToolError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"intest: unexpected failure: {ex.GetType().Name}: {ex.Message}");
            return ExitToolError;
        }
    }
}
