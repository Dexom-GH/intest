using System.Text;

namespace InTest.Runtime;

/// <summary>URL composition. Framework-neutral: must not reference any test framework.</summary>
public static class InTestUrl
{
    /// <summary>
    /// Returns an absolute base URI guaranteed to end in '/'. Without the trailing slash,
    /// <c>new Uri(base, relative)</c> silently discards the last path segment.
    /// </summary>
    public static Uri NormalizeBase(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Base URL must not be null or whitespace.", nameof(baseUrl));

        var trimmed = baseUrl.Trim();
        if (!trimmed.EndsWith('/')) trimmed += "/";

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Base URL '{baseUrl}' is not an absolute URI.", nameof(baseUrl));

        return uri;
    }

    /// <summary>
    /// Builds a relative path from an OpenAPI path template, substituting '{placeholder}'
    /// segments left to right. The leading '/' that OpenAPI paths always carry is stripped,
    /// because a leading slash resets resolution to the host root.
    /// </summary>
    public static string Build(string pathTemplate, params string[] values)
    {
        ArgumentNullException.ThrowIfNull(pathTemplate);
        ArgumentNullException.ThrowIfNull(values);

        var result = new StringBuilder(pathTemplate.Length + 16);
        var valueIndex = 0;
        var i = 0;

        while (i < pathTemplate.Length)
        {
            var open = pathTemplate.IndexOf('{', i);
            if (open < 0) { result.Append(pathTemplate, i, pathTemplate.Length - i); break; }

            var close = pathTemplate.IndexOf('}', open);
            if (close < 0)
                throw new ArgumentException($"Unterminated placeholder in path template '{pathTemplate}'.", nameof(pathTemplate));

            result.Append(pathTemplate, i, open - i);

            if (valueIndex >= values.Length)
                throw new ArgumentException(
                    $"Path template '{pathTemplate}' has more placeholders than the {values.Length} value(s) supplied.",
                    nameof(values));

            result.Append(Uri.EscapeDataString(values[valueIndex++] ?? string.Empty));
            i = close + 1;
        }

        if (valueIndex != values.Length)
            throw new ArgumentException(
                $"Path template '{pathTemplate}' has {valueIndex} placeholder(s) but {values.Length} value(s) were supplied.",
                nameof(values));

        var path = result.ToString();
        return path.StartsWith('/') ? path[1..] : path;
    }
}
