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

        // Review finding on Task 4: TestPlan.Notes was populated by TestPlanBuilder's three
        // withholding branches and read by nothing — a withheld declared-error case was a
        // completely silent omission, exactly what §12 legislates against ("skips remove tests.
        // Notes do not" only holds if something reports them). Same {operation, reason} shape as
        // `skipped` above, deliberately: a note is "the operation's other cases still generated"
        // rather than "nothing generated", not a lesser amount of detail about it.
        var withheld = new JsonArray();
        foreach (var n in plan.Notes)
            withheld.Add(new JsonObject { ["operation"] = n.OperationKey, ["reason"] = n.Reason });

        // Task 6: an operation can now emit more than one case (declared-error and auth cases,
        // decisions 5 and 3), so a per-operation count is no longer the same number as a
        // per-case count. authCases is also reused below by both the generated and gated counts.
        var authCases = cases.Where(c => c.Role == CaseRole.Auth).ToList();

        var report = new JsonObject
        {
            ["title"] = plan.Title,
            // Left as a case count, deliberately: GenerateCommand.cs's own console line
            // ("Generated N test(s)") already fixes what this field means, so redefining it here
            // would contradict output the same run just printed. operationsGenerated, below, is
            // the field that now carries what "generated" only meant by coincidence before
            // declared-error and auth cases existed — §12's own example ("Operations in spec: 148
            // / Generated: 113") is that older, 1:1 meaning.
            ["generated"] = cases.Count,
            ["operationsGenerated"] = cases.Select(c => c.OperationKey).Distinct(StringComparer.Ordinal).Count(),
            ["skipped"] = skipped,
            ["notes"] = new JsonObject
            {
                ["withheld"] = withheld,
                // Both metrics name *operations*, not cases. An operation can emit more than one
                // case since declared-error and auth cases arrived (decisions 5 and 3) —
                // counting cases here double-counts every operation that also gets a non-success
                // case, and every sample spec in the repo declares a 404, so this is a Distinct
                // over Role.Success cases only, not a Count/Sum or a Distinct over every role.
                // Filtering to Success is what actually enforces "one entry per operation" —
                // TestPlanBuilder only ever emits a non-success case for an operation whose
                // success case already generated (TestPlanBuilder.cs:100-103), so a role filter
                // and a bare Distinct happen to produce the same number today, but only the
                // filter says so structurally rather than leaning on that cross-file invariant.
                ["untaggedOperations"] = plan.Classes.Where(c => c.Tag == "Default")
                    .SelectMany(c => c.Cases).Where(c => c.Role == CaseRole.Success)
                    .Select(c => c.OperationKey).Distinct(StringComparer.Ordinal).Count(),
                ["synthesizedOperationIds"] = cases.Where(c => c.Role == CaseRole.Success && c.OperationKeySynthesized)
                    .Select(c => c.OperationKey).Distinct(StringComparer.Ordinal).Count(),
                // Role.Success only: a declared-error case's SchemaKey is null because decision 5
                // never asks a 404 response for a schema, and an auth case's is null because
                // decision 3's fixed 401/403 pair never reads a declared response at all (see
                // TestCasePlan.SchemaKey's own doc on the Auth cases). Counting either here
                // inflated a note whose stated meaning is "no response schema declared — fixable
                // in the spec" with cases that never had a schema question to begin with — the
                // same bodiless-204 mistake §12 already names, recurring under a new role.
                ["statusOnlyContractTests"] = cases.Count(c => c.SchemaKey is null && c.Role == CaseRole.Success),
                ["inlineResponseSchemas"] = cases.Count(c => c.SchemaKey?.StartsWith("op:", StringComparison.Ordinal) == true),
                ["declaredErrorTestsGenerated"] = cases.Count(c => c.Role == CaseRole.DeclaredError),
                ["authTestsGenerated"] = authCases.Count,
                // Named "gated on", not "skipped for want of": whether a generated case actually
                // gets skipped is decided at runtime by RequireMultipleIdentities against whatever
                // ITestTokenProvider a project registers (decision 3) — the CLI generates this
                // report long before any provider exists (decision 7) and cannot know that number.
                // What it can say honestly is how many generated cases *require* a second identity
                // to run at all: only the wrong-scope 403 case (IdentitySlot.Secondary) does: the
                // no-token 401 case always runs regardless of how many identities a provider has.
                ["authTestsGatedOnSecondIdentity"] = authCases.Count(c => c.Slot == IdentitySlot.Secondary),
                // Matched against TestPlanBuilder.NoPathParameterNoteReason — the constant the
                // builder's no-path-parameter branch builds its note text from — rather than a
                // second hand-copied literal here. A reword of that constant changes both sides
                // at once, since there is only one string, not a restatement of it: this count
                // cannot drift from the message a reader of `withheld` actually sees, because
                // both are the same object in memory, not two copies that happen to agree today.
                ["notFoundWithoutPathParameter"] = plan.Notes.Count(n =>
                    n.Reason.Contains(TestPlanBuilder.NoPathParameterNoteReason, StringComparison.Ordinal))
            }
        };

        return report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
