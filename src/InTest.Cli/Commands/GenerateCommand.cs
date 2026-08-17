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
