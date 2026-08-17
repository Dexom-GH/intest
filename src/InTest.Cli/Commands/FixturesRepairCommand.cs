using System.Text.Json;
using System.Text.Json.Nodes;
using InTest.Cli.Fixtures;
using InTest.Cli.Planning;
using InTest.Cli.Spec;

namespace InTest.Cli.Commands;

/// <summary>
/// The only command that writes under <c>fixtures/</c> (Task 3's plan section, "owning creation,
/// sentinel addition and stale flagging"). It creates a fixture for every operation the test plan
/// covers but has none yet, adds properties and parameters a schema change made required since a
/// fixture was last written, and reports — without touching — properties a fixture still carries
/// that the schema no longer declares. It never overwrites a value already present: that is the
/// one invariant a hand-edited, committed fixture depends on.
/// </summary>
public static class FixturesRepairCommand
{
    public const int ExitOk = 0;
    public const int ExitToolError = 2;

    // Hardcoded to match InitCommand's current scaffolding (both will read one source once
    // Task 4a lands — see "Decisions this plan encodes" §5's note in the plan document).
    private const string CliVersion = "0.1.0";

    public static async Task<int> RunAsync(
        string projectRoot, CancellationToken cancellationToken, TextWriter? report = null)
    {
        report ??= Console.Out;

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

            var spec = await SpecLoader.LoadFromFileAsync(Path.Combine(projectRoot, specRelative), cancellationToken)
                                       .ConfigureAwait(false);

            // Iterates the test plan, not the raw document: TestPlanBuilder is the sole authority
            // on which operations exist (it already applies FixtureComposer.NeedsFixture and skips
            // non-JSON bodies and responses with no 2xx/3xx). Iterating the document directly would
            // create fixtures `generate`'s drift check disagrees with — see the plan's Task 3 note.
            var plan = TestPlanBuilder.Build(spec.Document);
            var fixturesDir = Path.Combine(projectRoot, "fixtures");
            var generatedBy = $"intest {CliVersion}";

            var created = 0;
            var updated = 0;
            var failed = 0;

            foreach (var testCase in plan.Classes.SelectMany(c => c.Cases)
                                                  .Where(c => c.NeedsFixture)
                                                  .OrderBy(c => c.OperationKey, StringComparer.Ordinal))
            {
                // NeedsFixture is FixtureComposer's own verdict, carried on the plan by
                // TestPlanBuilder — restating that decision here (e.g. inspecting Compose's
                // output for emptiness) is exactly the second copy that has drifted from the
                // composer twice before. An operation that doesn't need one is left alone
                // entirely, whether or not a fixture already happens to exist for it.

                // Every key reaching here already passed FixtureDocument.TryValidateOperationKey
                // inside TestPlanBuilder (an operation with an unusable key is recorded as skipped
                // and never produces a TestCasePlan). FileNameFor throwing here means that
                // invariant broke — a bug to surface, not a condition to defensively swallow.
                var fixturePath = Path.Combine(fixturesDir, FixtureDocument.FileNameFor(testCase.OperationKey));

                try
                {
                    var composed = FixtureComposer.Compose(
                        spec.Document, testCase.PathTemplate, testCase.HttpMethod, testCase.OperationKey, generatedBy);

                    if (!File.Exists(fixturePath))
                    {
                        Directory.CreateDirectory(fixturesDir);
                        await File.WriteAllTextAsync(fixturePath, composed.ToJson(), cancellationToken).ConfigureAwait(false);
                        created++;
                        continue;
                    }

                    var existingText = await File.ReadAllTextAsync(fixturePath, cancellationToken).ConfigureAwait(false);
                    var existing = FixtureDocument.Parse(existingText);
                    var drift = FixtureDrift.Compare(existing, composed);

                    var changed = false;

                    if (drift.MissingProperties.Count > 0)
                    {
                        var body = existing.Body as JsonObject ?? new JsonObject();
                        var composedBody = (JsonObject)composed.Body!;
                        foreach (var name in drift.MissingProperties)
                            body[name] = composedBody[name]?.DeepClone();
                        existing.Body = body;
                        changed = true;
                    }

                    foreach (var name in drift.MissingParameters)
                    {
                        existing.Parameters[name] = composed.Parameters[name];
                        changed = true;
                    }

                    // Stale properties are reported, never deleted (§10) — a property no longer in
                    // the schema may be deliberate, and silent deletion is how that intent is lost.
                    foreach (var name in drift.StaleProperties)
                        report.WriteLine(
                            $"{testCase.OperationKey}: '{name}' is no longer in schema (kept — remove by hand if it was not intentional).");

                    if (changed)
                    {
                        await File.WriteAllTextAsync(fixturePath, existing.ToJson(), cancellationToken).ConfigureAwait(false);
                        updated++;
                    }
                }
                catch (FixtureFormatException ex)
                {
                    // One bad committed fixture is that operation's problem, not the whole run's:
                    // every other operation's legitimate repair — creation or sentinel addition —
                    // must still happen. The run as a whole still reports a tool error (below),
                    // since the malformed fixture itself is unresolved.
                    failed++;
                    report.WriteLine($"{testCase.OperationKey}: {ex.Message}");
                }
            }

            report.WriteLine(created + updated == 0
                ? "Nothing to repair."
                : $"Created {created} fixture(s), updated {updated} fixture(s).");

            return failed == 0 ? ExitOk : ExitToolError;
        }
        catch (SpecLoadException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitToolError;
        }
        catch (FixtureFormatException ex)
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
