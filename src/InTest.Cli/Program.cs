using System.CommandLine;
using InTest.Cli.Commands;

var projectOption = new Option<string>("--project")
{
    Description = "Test project directory containing intest.json.",
    DefaultValueFactory = _ => Directory.GetCurrentDirectory()
};

var generate = new Command("generate", "Generate tests from the configured OpenAPI document.");
generate.Options.Add(projectOption);
generate.SetAction((parseResult, cancellationToken) =>
    GenerateCommand.RunAsync(parseResult.GetValue(projectOption)!, cancellationToken));

var nameOption = new Option<string>("--name") { Description = "Test project name.", Required = true };
var specOption = new Option<string>("--spec") { Description = "Path of the OpenAPI document, relative to the test project directory.", Required = true };

var init = new Command("init", "Scaffold a test project.");
init.Options.Add(projectOption);
init.Options.Add(nameOption);
init.Options.Add(specOption);
init.SetAction(parseResult => InitCommand.Run(
    parseResult.GetValue(projectOption)!,
    parseResult.GetValue(nameOption)!,
    parseResult.GetValue(specOption)!));

var fixtures = new Command("fixtures", "Fixture maintenance.");
var repair = new Command("repair", "Create missing fixtures and add sentinels for new required properties.");
repair.Options.Add(projectOption);
repair.SetAction((parseResult, cancellationToken) =>
    FixturesRepairCommand.RunAsync(parseResult.GetValue(projectOption)!, cancellationToken));
fixtures.Subcommands.Add(repair);

var root = new RootCommand("InTest — generate API integration tests from an OpenAPI document.");
root.Subcommands.Add(generate);
root.Subcommands.Add(init);
root.Subcommands.Add(fixtures);

// §5's exit-code convention, applied at the one layer no command owns. `System.CommandLine`
// returns 1 when it cannot parse the command line, and 1 is reserved for real work outstanding
// that a human must do — fixture drift, validation failures, `--check` differences. A command
// line that could not be parsed is none of those: nothing ran. That left `intest init --name ""`
// exiting 2 and `intest init` — the same mistake one keystroke apart — exiting 1, so CI could not
// tell a mistyped invocation from fixture drift, which is the single confusion the 1/2 split
// exists to prevent. The literal is deliberate: §5 owns these numbers and three commands already
// each declare their own copy, so a fourth would deepen a duplication rather than resolve it.
//
// This sits above every command, so it holds for commands not yet written. It is not a widening:
// bare `intest` and bare `intest fixtures` are parse failures of the same kind and exit 2 too.
// Exempting them would mean adding a branch that asserts some parse failures mean outstanding
// work, which §5 denies.
//
// Invoke first, then read. `InvokeAsync` is what prints `System.CommandLine`'s own diagnostics,
// and those are not the defect — the code is, so the text is left exactly as the library wrote
// it. Reading `Errors` afterwards is safe rather than merely convenient: a terminating action
// that declares `ClearsParseErrors` — `--help`, `--version` — suppresses the errors at *parse*
// time, so they never enter `Errors` to begin with instead of being cleared out of it mid-call.
// Measured against 2.0.11, not inferred from the property name.
var parseResult = root.Parse(args);
var exitCode = await parseResult.InvokeAsync();

return parseResult.Errors.Count > 0 ? 2 : exitCode;
