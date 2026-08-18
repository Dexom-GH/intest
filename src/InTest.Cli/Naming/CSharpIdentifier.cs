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
    /// Resolves collisions deterministically. Keyed by the caller's stable key (an operation
    /// key), never by position, so adding an operation cannot rename an unrelated one.
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
                result[entry.Key] = entry.Value + "_" + ShortHash(entry.Key);
        }

        return result;
    }

    private static string ShortHash(string key)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..6];
}
