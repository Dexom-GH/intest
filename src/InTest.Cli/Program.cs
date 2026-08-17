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
var specOption = new Option<string>("--spec") { Description = "Path or URL of the OpenAPI document.", Required = true };

var init = new Command("init", "Scaffold a test project.");
init.Options.Add(projectOption);
init.Options.Add(nameOption);
init.Options.Add(specOption);
init.SetAction(parseResult => InitCommand.Run(
    parseResult.GetValue(projectOption)!,
    parseResult.GetValue(nameOption)!,
    parseResult.GetValue(specOption)!));

var root = new RootCommand("InTest — generate API integration tests from an OpenAPI document.");
root.Subcommands.Add(generate);
root.Subcommands.Add(init);

return await root.Parse(args).InvokeAsync();
