using System.Text;

namespace InTest.Cli.Spec;

/// <summary>
/// The identity every downstream concern keys on: naming, fixture filenames, the operations
/// config map, dedupe and orphan detection.
/// </summary>
public sealed record OperationKey(string Value, bool Synthesized)
{
    /// <summary>
    /// Uses a declared operationId when present. Otherwise synthesizes a stable key from
    /// method and normalized path — never from ordinal or declaration order, which would
    /// churn every name when an operation is added above another.
    /// </summary>
    public static OperationKey Resolve(string? declaredOperationId, string httpMethod, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentNullException.ThrowIfNull(path);

        if (!string.IsNullOrWhiteSpace(declaredOperationId))
        {
            return new OperationKey(declaredOperationId.Trim(), Synthesized: false);
        }

        var sb = new StringBuilder(httpMethod.ToLowerInvariant());
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return new OperationKey(sb.Append("_root").ToString(), Synthesized: true);
        }

        foreach (var segment in segments)
        {
            var cleaned = segment.Trim('{', '}');
            sb.Append('_');
            foreach (var ch in cleaned)
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        }

        return new OperationKey(Collapse(sb.ToString()), Synthesized: true);
    }

    private static string Collapse(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastWasUnderscore = false;

        foreach (var ch in value)
        {
            if (ch == '_')
            {
                if (lastWasUnderscore)
                {
                    continue;
                }
                lastWasUnderscore = true;
            }
            else
            {
                lastWasUnderscore = false;
            }
            sb.Append(ch);
        }

        return sb.ToString().Trim('_');
    }
}
