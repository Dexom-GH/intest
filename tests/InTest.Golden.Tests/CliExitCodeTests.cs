using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// §5's exit-code convention at the one layer no command owns: the command line itself.
/// <para>
/// These invoke the built <c>InTest.Cli</c> assembly as a real process and assert on the code it
/// hands the shell. Nothing shorter would do. The defect under test lived in <c>Program</c>'s
/// final expression, above every command and below every test that calls a <c>Command.Run</c>
/// directly — <c>InTest.Cli.Tests</c> could not have observed it, because a test that starts at
/// <c>InitCommand.Run</c> has already skipped the parse.
/// </para>
/// <para>
/// They live in this assembly and not in <c>InTest.Cli.Tests</c> because
/// <see cref="ProcessRunner"/> does — item 6 of Task 10 gave out-of-process invocation a single
/// home here precisely so a second copy would not appear in the other assembly.
/// </para>
/// </summary>
[TestClass]
public class CliExitCodeTests
{
    private static string Cli => Path.Combine(AppContext.BaseDirectory, "InTest.Cli.dll");

    private static Task<(int ExitCode, string Output)> RunCliAsync(string arguments) =>
        ProcessRunner.RunAsync("dotnet", $"\"{Cli}\" {arguments}".TrimEnd());

    [TestMethod]
    public async Task MissingRequiredOptionExitsToolError()
    {
        // The defect, stated: `--name ""` and `--name` absent are the same mistake one keystroke
        // apart. The first reached CommandArguments and exited 2; the second never reached a
        // command at all and exited 1 — the code §5 reserves for work a human must go and do.
        var (exitCode, output) = await RunCliAsync("init --spec orders.json");

        exitCode.ShouldBe(2, $"a command line that could not be parsed is a tool error:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task UnrecognisedFlagExitsToolError()
    {
        var (exitCode, output) = await RunCliAsync("init --name Orders.ApiTests --spec orders.json --bogus");

        exitCode.ShouldBe(2, $"an unrecognised flag is a tool error:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task NoCommandNamedExitsToolError()
    {
        // The root command has subcommands and no action of its own, so bare `intest` is a parse
        // failure like any other. Named here because the fix sits above every command: this one
        // belongs to no command, which is the point.
        var (exitCode, output) = await RunCliAsync(string.Empty);

        exitCode.ShouldBe(2, $"naming no command is a tool error:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task ParseFailureKeepsSystemCommandLineDiagnostics()
    {
        // The exit code was the defect, not the text. Asserting on the interpolated token rather
        // than on System.CommandLine's sentence keeps this from failing under a non-English UI
        // culture, where the sentence around the token is localised and the token is not.
        var (_, output) = await RunCliAsync("init --spec orders.json");

        output.ShouldContain("--name");
    }

    [TestMethod]
    public async Task HelpExitsOk()
    {
        var (exitCode, output) = await RunCliAsync("--help");

        exitCode.ShouldBe(0, $"--help is not a failure:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task HelpOnACommandWithAnUnsuppliedRequiredOptionExitsOk()
    {
        // The carve-out that makes the fix non-trivial. `init --help` parses with errors present
        // — `--name` and `--spec` are both required and neither was given — and HelpAction
        // declares ClearsParseErrors, so the errors are gone by the time it has run. Read
        // ParseResult.Errors before invoking and this exits 2 and help stops working.
        var (exitCode, output) = await RunCliAsync("init --help");

        exitCode.ShouldBe(0, $"asking a command for help is not a failure:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task VersionExitsOk()
    {
        var (exitCode, output) = await RunCliAsync("--version");

        exitCode.ShouldBe(0, $"--version is not a failure:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task ACommandsOwnExitCodeSurvives()
    {
        // The override must be reachable only from the parse layer. A command line that parses
        // cleanly and then declines still reports why it declined — 3, not 2 and not 0.
        var root = Path.Combine(Path.GetTempPath(), "intest-exitcode-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var first = await RunCliAsync($"init --project \"{root}\" --name Orders.ApiTests --spec orders.json");
            first.ExitCode.ShouldBe(InitCommand.ExitOk, first.Output);

            var (exitCode, output) = await RunCliAsync(
                $"init --project \"{root}\" --name Orders.ApiTests --spec orders.json");

            exitCode.ShouldBe(InitCommand.ExitAlreadyInitialised, output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
