using System.Text;
using System.Xml;

namespace InTest.Cli.Naming;

/// <summary>
/// Escapes text for embedding as an MSBuild property value inside a generated project file —
/// the text pasted directly between <c>&lt;Prop&gt;</c> and <c>&lt;/Prop&gt;</c>. Two grammars
/// apply, because the outer file is an MSBuild project written as XML: the property value is
/// read through MSBuild's own <c>%XX</c> escaping, and the file that carries it is read through
/// XML's. A build undoes them in the opposite order it takes to apply them — the XML parser
/// unescapes <c>&amp;amp;</c> back to <c>&amp;</c> while loading the file, and only afterward
/// does MSBuild evaluate <c>%XX</c> escapes in the resulting property value — so this method must
/// escape MSBuild first and XML second for its output to survive that round trip.
/// <para>
/// This is an escape rule, not a refuse rule, the same distinction <see cref="CSharpLiteral"/>
/// draws: an MSBuild property value and an XML text node can, between them, carry every character
/// a filesystem path contains, losslessly — unlike a dotted C# name in declaration position (see
/// <see cref="CSharpIdentifier.TryValidateDottedName"/>), where no escaping construct makes an
/// invalid value resolve, so refusing is the only fix there. Here escaping is the fix, and the
/// adopter's <c>--spec</c> path is exactly the kind of value that needs it: it is text the
/// adopter frequently cannot rename (<c>C:/Work/R&amp;D/orders.json</c>), unlike an identifier,
/// which the adopter chose and can simply choose differently.
/// </para>
/// <para>
/// Only the residue XML 1.0 cannot represent in <i>any</i> form falls back to refusal (see
/// <see cref="TryEscape"/>), for exactly the reason <c>TryValidateDottedName</c> refuses: there
/// is nothing to escape it into.
/// </para>
/// <para>
/// Unlike <see cref="CSharpLiteral"/>, whose doc deliberately keeps its caller out of the
/// picture and points at <c>CompileVerificationTests</c> instead, this type's doc names
/// <c>--spec</c>, <c>$(InTestSpecSource)</c>, <c>Include=</c> and a concrete file pair. That is a
/// deliberate departure, not an oversight: the refusal message here genuinely needs a
/// caller-supplied <c>setting</c> name to be useful, and <c>CSharpLiteral</c> has no refusal
/// path to justify the same way.
/// </para>
/// </summary>
public static class MSBuildPropertyValue
{
    /// <summary>
    /// Escapes <paramref name="value"/> in two layers, in this fixed order, and refuses values
    /// XML cannot represent at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Layer 1 (MSBuild), <c>%</c> first:</b> <c>%</c> must be escaped before any of the other
    /// MSBuild special characters this call site can receive, for the same reason
    /// <see cref="CSharpLiteral.Escape"/> escapes backslash first: every other replacement's
    /// <c>%XX</c> output introduces a literal <c>%</c>, so escaping <c>%</c> afterward would
    /// re-escape the <c>%</c> those steps just emitted. See the <c>.Replace</c> chain below, and
    /// the comment directly above it, for the exact mappings and their order.
    /// </para>
    /// <para>
    /// <c>?</c> and <c>*</c> earn their place in this set the same way the other four do: MSBuild
    /// gives them a second, unrelated meaning wherever a property value is consumed as a file
    /// path — <c>Include="$(InTestSpecSource)"</c> globs it, so an unescaped <c>?</c> or <c>*</c>
    /// there does not fail to resolve, it silently resolves to a <i>different file</i>. This is
    /// not theoretical for this call site: <c>?</c> and <c>*</c> are legal characters in a POSIX
    /// filename, and <c>--spec</c> names a file the adopter frequently cannot rename — the same
    /// premise that makes this an escape rule rather than a refuse rule. Confirmed against a real
    /// <c>dotnet build</c>: with <c>specs/orders.json</c> and <c>specs/ordersX.json</c> both on
    /// disk and the property set to <c>specs/orders?.json</c>, an unescaped <c>Include</c> glob
    /// resolved to <c>ordersX.json</c> — the wrong file, silently — while <c>%3F</c> kept it
    /// literal and resolved correctly. The escaped and unescaped property values evaluate
    /// identically either way (<c>specs/orders?.json</c>); only the <c>Include</c> glob
    /// distinguishes them.
    /// </para>
    /// <para>
    /// <c>'</c> is in MSBuild's special-character set too, but is deliberately excluded here: it
    /// is inert in property-value text and only becomes special inside an MSBuild
    /// <i>condition</i> (<c>Condition="'$(Prop)'=='x'"</c>), a context this call site never
    /// writes into. Escaping it here would make every scaffolded project file uglier for a case
    /// that cannot arise — the same shape <see cref="CSharpLiteral.Escape"/>'s doc comment already
    /// uses to justify its own escaped set: the set is defensible as a rule tied to where the
    /// value is actually consumed, not as a list of characters that happened to look risky.
    /// </para>
    /// <para>
    /// <b>Layer 2 (XML):</b> <c>&amp;</c> → <c>&amp;amp;</c>, then <c>&lt;</c> → <c>&amp;lt;</c>.
    /// <c>&gt;</c> and <c>'</c> are deliberately left raw — both are legal unescaped in XML
    /// character data, and escaping them would only make the generated project file harder for
    /// an adopter to read, for no gain in safety.
    /// </para>
    /// <para>
    /// <b>Layer 1 before layer 2</b> is required, not stylistic. Escaping XML first would turn a
    /// raw <c>&amp;</c> into <c>&amp;amp;</c>; the MSBuild pass would then see the <c>;</c> that
    /// introduces and mangle it into <c>&amp;amp%3B</c> — corrupting the very escape it just
    /// produced. Running MSBuild first avoids this: layer 2's own output, <c>&amp;amp;</c> and
    /// <c>&amp;lt;</c>, contains none of the characters layer 1 targets (<c>%24</c>, <c>%40</c>,
    /// <c>%3B</c>, <c>%3F</c>, <c>%2A</c>, <c>%25</c> contain neither <c>&amp;</c> nor
    /// <c>&lt;</c>), so it survives layer 2 untouched.
    /// </para>
    /// <para>
    /// Refused, with <paramref name="reason"/> set and this method returning <c>false</c>: any
    /// character <see cref="XmlConvert.IsXmlChar"/> rejects — the C0 control characters excluded
    /// from XML 1.0's <c>Char</c> production (tab, LF and CR are part of that production and pass
    /// through unescaped; most other C0 controls are not), and the two noncharacters U+FFFE and
    /// U+FFFF, which the same production excludes for an unrelated reason but are just as
    /// unrepresentable — plus any unpaired UTF-16 surrogate, which <c>IsXmlChar</c> also rejects
    /// but this method reports with its own "unpaired surrogate" message, a more useful diagnosis
    /// than "not an XML character". No MSBuild or XML escape sequence represents any of these;
    /// the only fix is to remove them from the value before it reaches this method.
    /// <paramref name="reason"/> leads with <paramref name="setting"/>, names the offending
    /// character by its code point rather than the raw glyph (which for most of this set is
    /// itself invisible or corrupting when printed to a terminal), and <paramref name="escaped"/>
    /// is set to <see cref="string.Empty"/> rather than left undefined.
    /// </para>
    /// </remarks>
    public static bool TryEscape(string value, string setting, out string escaped, out string reason)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(setting);

        escaped = string.Empty;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            // Surrogates are checked before XmlConvert.IsXmlChar, not after: IsXmlChar rejects
            // every surrogate code unit individually (the Char production has no D800-DFFF
            // range), so checking it first would misreport a valid surrogate pair — an emoji,
            // say — as unrepresentable, instead of the "unpaired surrogate" diagnosis this method
            // gives an actually-invalid one.
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++; // valid pair — skip its low surrogate, it is not a character on its own
                    continue;
                }

                reason = $"{setting} '{Display(value)}' contains an unpaired surrogate " +
                         $"{CodePoint(c)}, which XML 1.0 cannot represent in any form. Remove or fix it.";
                return false;
            }

            if (char.IsLowSurrogate(c))
            {
                reason = $"{setting} '{Display(value)}' contains an unpaired surrogate " +
                         $"{CodePoint(c)}, which XML 1.0 cannot represent in any form. Remove or fix it.";
                return false;
            }

            if (!XmlConvert.IsXmlChar(c))
            {
                reason = $"{setting} '{Display(value)}' contains {CodePoint(c)}, which XML 1.0 " +
                         "cannot represent in any form. Remove it.";
                return false;
            }
        }

        // The order below is load-bearing: '%' first, then the rest of the MSBuild layer, then
        // the XML layer (the final two replacements) — see this type's remarks for why both
        // orderings matter. Every per-character expectation in MSBuildPropertyValueTests contains
        // a '%', so reordering any step past '%' re-escapes it and fails a test rather than
        // corrupting output silently.
        escaped = value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("$", "%24", StringComparison.Ordinal)
            .Replace("@", "%40", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal)
            .Replace("*", "%2A", StringComparison.Ordinal)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);

        reason = string.Empty;
        return true;
    }

    private static string CodePoint(char c) => $"U+{(int)c:X4}";

    /// <summary>
    /// Renders <paramref name="value"/> for use inside a <paramref name="reason"/> message,
    /// replacing every character the message would otherwise refuse to accept — anything
    /// <see cref="XmlConvert.IsXmlChar"/> rejects, and any unpaired surrogate — with its
    /// <c>U+XXXX</c> form. A properly paired surrogate is left as the character it represents.
    /// Without this, quoting the adopter's whole value verbatim (the shape
    /// <see cref="CSharpIdentifier.TryValidateDottedName"/> uses) would paste the very character
    /// the message is warning about into the terminal that displays it.
    /// </summary>
    private static string Display(string value)
    {
        var sb = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            if (char.IsSurrogate(c) || !XmlConvert.IsXmlChar(c))
            {
                sb.Append(CodePoint(c));
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
