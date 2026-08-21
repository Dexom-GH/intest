namespace InTest.Cli;

/// <summary>
/// The single source for the process exit codes defined by the design spec's §5 exit-code
/// convention. Before this, <c>InitCommand</c>, <c>GenerateCommand</c> and
/// <c>FixturesRepairCommand</c> each declared their own subset — eight declarations of four
/// numbers — and <c>Program</c> needed the same numbers again, using a literal with a §5 citation
/// rather than becoming a fourth copy. Nothing kept the copies in step but discipline, which is
/// the arrangement <c>CONTRIBUTING.md</c>'s "One canonical explanation" rule exists to replace:
/// one copy authoritative, the rest pointing at it.
/// <para>
/// Same shape and same reason as <see cref="CliVersion"/>, which collapsed the same three
/// commands' hardcoded version literals.
/// </para>
/// <para>
/// §5 is the contract; this type only transcribes it. Changing a value here does not change the
/// convention, it breaks it. §5's code <c>4</c> — tool/config version mismatch — is deliberately
/// absent: it belongs to <c>generate --check</c>, which is not shipped, so declaring it now would
/// add a constant no code path can return. The gap is the unshipped flag, not an oversight.
/// </para>
/// </summary>
public static class ExitCode
{
    /// <summary>
    /// The requested state was reached, <b>including when no work was needed</b> — a PR script
    /// running <c>fixtures repair</c> unconditionally must not fail on a clean tree.
    /// </summary>
    public const int Ok = 0;

    /// <summary>
    /// Real work is outstanding that a human must do: fixture drift, validation failures,
    /// <c>--check</c> differences. Kept separate from <see cref="ToolError"/> deliberately —
    /// folding a crash or an unreadable spec into this code would leave CI unable to tell "the
    /// fixtures drifted, fix them" from "the tool blew up", two failures with entirely different
    /// responses and only one of them the developer's to act on.
    /// </summary>
    public const int WorkOutstanding = 1;

    /// <summary>
    /// Tool error — the tool did not do the work it was asked to do, and nothing was written: the
    /// command line could not be parsed, the spec is unparseable, <c>spec.source</c> is missing,
    /// <c>intest.json</c> is malformed, or an exception went unhandled. Returned by <b>any</b>
    /// command; §5 lists it per-command only where it is likely.
    /// </summary>
    public const int ToolError = 2;

    /// <summary>
    /// The command declined because proceeding would destroy or duplicate existing state.
    /// </summary>
    public const int AlreadyInitialised = 3;
}
