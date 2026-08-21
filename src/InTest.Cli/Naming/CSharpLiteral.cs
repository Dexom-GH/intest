namespace InTest.Cli.Naming;

/// <summary>
/// Escapes text for embedding inside a C# double-quoted string literal — the caller supplies
/// the surrounding quotes, this only escapes what goes between them.
/// <para>
/// The returned text is valid <b>only</b> in literal position: pasted directly between a pair of
/// <c>"</c> in generated source. There is no check anywhere that enforces this — a caller that
/// instead composes an expression around the result, writes it to a file path, or otherwise
/// treats it as ordinary text will get a value with stray backslashes in it. A model field
/// carrying this method's output should be named to say so (a <c>_literal</c> suffix, e.g.),
/// since that naming is the only thing guarding against misuse.
/// </para>
/// </summary>
public static class CSharpLiteral
{
    /// <summary>
    /// Escapes backslash first, then double-quote. Reversing the order would re-escape the
    /// backslash the quote step introduces, corrupting the result. No other character is
    /// handled: this codebase's inputs (operationIds, path templates, parameter names, OAuth
    /// scopes) are single-line spec text, and nothing here has been observed to carry an
    /// embedded newline, carriage return, or other control character. A value that did would
    /// still break the enclosing C# string literal — <c>CSharpLiteral</c> does not defend
    /// against that today, since no verified call site needs it and speculative handling isn't
    /// worth the risk of guessing the wrong behaviour.
    /// </summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
