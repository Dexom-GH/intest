using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace InTest.Cli.Naming;

public static class CSharpIdentifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
        "continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern",
        "false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface",
        "internal","is","lock","long","namespace","new","null","object","operator","out","override",
        "params","private","protected","public","readonly","ref","return","sbyte","sealed","short",
        "sizeof","stackalloc","static","string","struct","switch","this","throw","true","try","typeof",
        "uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while"
    };

    public static string ToPascalCase(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var sb = new StringBuilder(value.Length);
        var capitalizeNext = true;

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch)) { capitalizeNext = true; continue; }
            sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
            capitalizeNext = false;
        }

        var identifier = sb.ToString();
        if (identifier.Length == 0)
        {
            throw new ArgumentException($"'{value}' contains no characters usable in an identifier.", nameof(value));
        }

        if (char.IsDigit(identifier[0]))
        {
            identifier = "_" + identifier;
        }
        if (Keywords.Contains(identifier))
        {
            identifier = "@" + identifier;
        }

        return identifier;
    }

    /// <summary>
    /// Resolves collisions deterministically. Keyed by the caller's stable key — decision 4's
    /// composite case identity (<see cref="Planning.TestPlanBuilder.CaseIdentity"/>: operation
    /// key plus role plus expected status), not an operation key alone — never by position, so
    /// adding an operation, or a second case to an existing one, cannot rename an unrelated case.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Dedupe(IReadOnlyDictionary<string, string> proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);

        var byName = proposed.GroupBy(p => p.Value, StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in byName)
        {
            if (group.Count() == 1)
            {
                var only = group.Single();
                result[only.Key] = only.Value;
                continue;
            }

            foreach (var entry in group)
            {
                result[entry.Key] = entry.Value + "_" + ShortHash(entry.Key);
            }
        }

        return result;
    }

    private static string ShortHash(string key)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..6];

    /// <summary>
    /// Validates a dotted C# name — a namespace or a base class name — before it is emitted as
    /// declaration syntax. <c>mstest-class.scriban</c> writes <c>project.rootNamespace</c> and
    /// <c>project.testBaseClass</c> as <c>namespace {{ namespace }};</c> and
    /// <c>: {{ base_class }}</c> — declaration syntax, not a string literal. No quoting or
    /// escaping construct makes an invalid identifier resolve there, so refusing a bad value
    /// before it is ever rendered is the only fix; this is that refusal. That makes this the
    /// adopter-config rule: it governs <c>intest.json</c>'s <c>project.rootNamespace</c> and
    /// <c>project.testBaseClass</c> (and <c>init</c>'s <c>--name</c>, which seeds both), and is
    /// unrelated to the separate rule for escaping spec-derived text — <c>tc.category</c>,
    /// <c>tc.display_name</c>, and the like — that <c>TemplateRenderer</c> writes into string
    /// literals instead.
    /// <para>
    /// Deliberately not supported: an <c>@</c>-escaped verbatim segment (no legitimate
    /// <c>rootNamespace</c> or <c>testBaseClass</c> needs one), and generic type arguments in a
    /// base class name (a generated class derives from it non-generically, so <c>Base&lt;T&gt;</c>
    /// has nowhere for <c>T</c> to bind).
    /// </para>
    /// </summary>
    public static bool TryValidateDottedName(
        [NotNullWhen(true)] string? value, string setting, out string reason)
    {
        const string rule = "Each dot-separated segment must be a C# identifier — a letter or " +
                             "underscore, then letters, digits or underscores.";

        if (string.IsNullOrWhiteSpace(value))
        {
            reason = $"{setting} is empty. {rule}";
            return false;
        }

        var segments = value.Split('.');
        if (segments.Any(string.IsNullOrEmpty))
        {
            reason = $"{setting} '{value}' is not a valid C# name: it has an empty segment. {rule}";
            return false;
        }

        // Every per-segment message below leads with the setting and the whole value the
        // adopter typed, then narrows to the offending segment — the same shape as
        // FixtureDocument.TryValidateOperationKey ("operationId '<value>' cannot be a fixture
        // filename: it contains …"). A message that quoted only the segment would drop the value
        // the adopter actually wrote from anything but the shortest inputs.
        foreach (var segment in segments)
        {
            var first = segment[0];
            if (!char.IsLetter(first) && first != '_')
            {
                reason = char.IsDigit(first)
                    ? $"{setting} '{value}' is not a valid C# name: the segment '{segment}' starts with a digit. {rule}"
                    : $"{setting} '{value}' is not a valid C# name: the segment '{segment}' starts with '{first}'. {rule}";
                return false;
            }

            var offending = segment.Skip(1)
                .Where(c => !char.IsLetterOrDigit(c) && c != '_')
                .Distinct()
                .ToArray();
            if (offending.Length > 0)
            {
                reason = $"{setting} '{value}' is not a valid C# name: the segment '{segment}' contains " +
                         $"{string.Join(", ", offending.Select(c => $"'{c}'"))}. {rule}";
                return false;
            }

            if (Keywords.Contains(segment))
            {
                reason = $"{setting} '{value}' is not a valid C# name: the segment '{segment}' is a C# keyword. {rule}";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }
}
