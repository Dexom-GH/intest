using System.Diagnostics;

namespace InTest.Golden.Tests;

/// <summary>
/// Runs an external process and captures its combined stdout+stderr. Task 9's whole-branch
/// review found this exact block duplicated in three places — <c>InitCommandTests</c> (in
/// <c>InTest.Cli.Tests</c>), <see cref="GeneratedSuiteExecutionTests"/>'s own private
/// <c>RunAsync</c>, and inline in <see cref="CompileVerificationTests"/>. Task 10 item 7 moves
/// the first of those into this assembly (next to <see cref="CompileVerificationTests"/>, which
/// already owns "does the scaffolded project build"), which turns what was a cross-assembly
/// duplication into a same-assembly one with an obvious single home — this class (item 6).
/// </summary>
internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }
}
