using System.Reflection;

namespace InTest.Cli;

/// <summary>
/// The single source for the CLI's own version. Before this, <c>InitCommand</c>,
/// <c>FixturesRepairCommand</c> and <c>GenerateCommand</c> each hardcoded their own "0.1.0"
/// literal, and nothing kept them in step — a scaffolded project or a fixture's <c>generatedBy</c>
/// could claim a version the tool does not actually have. See "Decisions this plan encodes" §5.
/// </summary>
public static class CliVersion
{
    /// <summary>
    /// The assembly's informational version with any source-control suffix
    /// (<c>+&lt;commit-sha&gt;</c>, appended by the SDK's SourceLink integration) trimmed off, so
    /// callers get a plain "0.1.0" rather than "0.1.0+649945bcf0226d5c0c8b90f2bcbee894242a157d".
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";

        var plusIndex = informational.IndexOf('+');
        return plusIndex >= 0 ? informational[..plusIndex] : informational;
    }
}
