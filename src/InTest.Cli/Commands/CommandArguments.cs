using System.Diagnostics.CodeAnalysis;
using InTest.Cli.Naming;

namespace InTest.Cli.Commands;

/// <summary>
/// How a command refuses an argument the adopter got wrong: name the setting, say what is wrong
/// with the value, state the rule, end with something copyable — then exit 2, having written
/// nothing. One shape for the whole command surface.
/// <para>
/// This exists because the surface had two shapes and the split fell along which argument the
/// adopter mistyped rather than along anything they could reason about. <c>--name</c> was
/// refused through <see cref="CSharpIdentifier.TryValidateDottedName"/> — setting named, value
/// quoted, rule stated, remedy appended, exit 2. <c>--project</c> and <c>--spec</c> hit
/// <c>ArgumentException.ThrowIfNullOrWhiteSpace</c> and escaped unhandled, which
/// <c>System.CommandLine</c> reports as exit <b>1</b>. That is a contract violation, not an
/// untidy message: §5 reserves 1 for "real work is outstanding that a human must do" — fixture
/// drift, validation failures — and separates it from 2 so "CI can tell a crash from fixture
/// drift". A mistyped <c>--spec</c> announced itself to a pipeline as fixture drift.
/// </para>
/// <para>
/// Note what this is not. It is not the rule for what makes a value <i>unsafe</i> — this
/// repository keeps four of those, deliberately distinct (see <see cref="Configuration.ConfigLoader"/>
/// for the map). This governs only <i>how a command refuses</i>, and it applies identically
/// whichever of those rules did the rejecting.
/// </para>
/// </summary>
internal static class CommandArguments
{
    /// <summary>
    /// <c>--project</c> is the one argument every command takes, so its rule is stated once here
    /// rather than three times. Command-neutral by necessity: `init`, `generate` and
    /// `fixtures repair` all quote it verbatim, so it must not name any one of them.
    /// </summary>
    public const string ProjectRule =
        "It must be the directory of the test project — for example \"tests/Orders.ApiTests\". " +
        "Omit --project entirely to use the current directory.";

    /// <summary>
    /// Refuses an argument left empty or all-whitespace. Returns <see langword="false"/> with
    /// <paramref name="reason"/> set to the refusal, in the shape
    /// <see cref="CSharpIdentifier.EmptyValueReason"/> fixes.
    /// <para>
    /// A blank argument is worth refusing rather than tolerating because it does not reliably
    /// fail on its own. <c>Path.Combine("", "intest.json")</c> is <c>"intest.json"</c>, so a
    /// blank <c>--project</c> silently retargets every read and write at the process's current
    /// directory instead of erroring — the same shape of quiet wrong-thing that
    /// <see cref="Configuration.ConfigLoader"/> calls out for an empty <c>spec.source</c>.
    /// </para>
    /// </summary>
    public static bool TryRequireValue(
        [NotNullWhen(true)] string? value, string setting, string rule, out string reason)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reason = CSharpIdentifier.EmptyValueReason(setting, rule);
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
