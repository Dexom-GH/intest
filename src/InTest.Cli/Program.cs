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

var root = new RootCommand("InTest — generate API integration tests from an OpenAPI document.");
root.Subcommands.Add(generate);

return await root.Parse(args).InvokeAsync();
