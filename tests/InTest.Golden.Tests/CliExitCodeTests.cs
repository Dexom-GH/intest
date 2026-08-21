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

    /// <summary>
    /// The help text is a promise the tool makes in its own voice, and it was the one the tool
    /// could not keep: <c>--spec</c> read "Path or URL of the OpenAPI document", while both
    /// commands that consume the value hand <c>Path.Combine(projectRoot, source)</c> to
    /// <c>SpecLoader.LoadFromFileAsync</c>, which opens files. Pinned here for the same reason
    /// every other test in this class is: <c>Program</c>'s option definitions are above every
    /// command, so <c>InTest.Cli.Tests</c> — which calls <c>Command.Run</c> methods directly —
    /// never executes them and could not observe this.
    /// <para>
    /// Asserted on the <c>--spec</c> line alone rather than on the whole help output, because
    /// <c>--project</c>'s line carries the current directory as its default and a checkout path
    /// is not something a test gets to make claims about.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task SpecHelpPromisesAPathAndNotAUrl()
    {
        var (_, output) = await RunCliAsync("init --help");

        var specLine = output.Split('\n').SingleOrDefault(line => line.Contains("--spec <spec>"));
        specLine.ShouldNotBeNull($"init --help must document --spec:{Environment.NewLine}{output}");

        specLine.ShouldContain("Path of the OpenAPI document");
        specLine.ShouldNotContain("URL",
            customMessage: "the help text must not promise an input the tool cannot accept — " +
                           "URL support is designed (the spec.json snapshot) and not built");
    }

    /// <summary>
    /// The refusal that replaced a success. Measured before it existed: this exact command line
    /// printed "Initialised Orders.ApiTests. Next: `intest generate`." and exited <b>0</b>,
    /// writing the whole scaffold; `generate` then failed with
    /// <c>Spec file not found: &lt;projectRoot&gt;\https://example.com/openapi.json</c>, exit 2.
    /// So the tool accepted the value its help had promised, and contradicted itself one command
    /// later in the vocabulary of a missing file.
    /// <para>
    /// Out of process rather than in <c>InitCommandTests</c>, which pins the same refusal:
    /// <c>init</c> is the command that <i>takes</i> <c>--spec</c>, so its exit code is what a
    /// pipeline sees, and §5 separates 2 from 1 precisely so a mistyped argument cannot report
    /// itself as fixture drift. Exit 0 was worse than either.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AUrlSpecExitsToolErrorAndScaffoldsNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "intest-urlspec-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var (exitCode, output) = await RunCliAsync(
                $"init --project \"{root}\" --name Orders.ApiTests --spec https://example.com/openapi.json");

            exitCode.ShouldBe(InitCommand.ExitToolError, output);
            output.ShouldContain("--spec",
                customMessage: "a refusal leads with the setting the adopter got wrong");
            output.ShouldContain("URL",
                customMessage: "a refusal names the kind of value it is refusing, so the adopter " +
                               "is not sent looking for a file");
            output.ShouldNotContain("Initialised",
                customMessage: "the defect was `init` confirming the belief the help text created");
            Directory.GetFileSystemEntries(root).ShouldBeEmpty(
                "§5's exit 2 is \"nothing was written\"");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
