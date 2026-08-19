using System.Text.Json;
using System.Text.Json.Nodes;
using InTest.Cli.Planning;

namespace InTest.Cli.Coverage;

/// <summary>
/// Everything InTest did not cover, or covered less thoroughly than a full contract test.
/// Committed and compared by `--check`, because it is the one generated artefact whose
/// content tracks the shape of the spec rather than the templates.
/// </summary>
public static class CoverageReport
{
    public static string ToJson(TestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var cases = plan.Classes.SelectMany(c => c.Cases).ToList();

        var skipped = new JsonArray();
        foreach (var s in plan.Skipped)
            skipped.Add(new JsonObject { ["operation"] = s.OperationKey, ["reason"] = s.Reason });

        var report = new JsonObject
        {
            ["title"] = plan.Title,
            ["generated"] = cases.Count,
            ["skipped"] = skipped,
            ["notes"] = new JsonObject
            {
                // Both metrics name *operations*, not cases. An operation can emit more than one
                // case since declared-error cases arrived (decision 5) — counting cases here
                // double-counts every operation that also gets a 404 case, and every sample spec
                // in the repo declares one, so this is a Distinct rather than a Count/Sum.
                ["untaggedOperations"] = plan.Classes.Where(c => c.Tag == "Default")
                    .SelectMany(c => c.Cases).Select(c => c.OperationKey)
                    .Distinct(StringComparer.Ordinal).Count(),
                ["synthesizedOperationIds"] = cases.Where(c => c.OperationKeySynthesized)
                    .Select(c => c.OperationKey).Distinct(StringComparer.Ordinal).Count(),
                ["statusOnlyContractTests"] = cases.Count(c => c.SchemaKey is null),
                ["inlineResponseSchemas"] = cases.Count(c => c.SchemaKey?.StartsWith("op:", StringComparison.Ordinal) == true)
            }
        };

        return report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
