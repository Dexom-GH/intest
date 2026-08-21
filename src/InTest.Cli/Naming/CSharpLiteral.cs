namespace InTest.Cli.Naming;

/// <summary>
/// Escapes text for embedding inside a C# double-quoted string literal — the caller supplies
/// the surrounding quotes, this only escapes what goes between them.
/// <para>
/// The escaped set is exactly what the C# grammar forbids raw inside a
/// <c>regular_string_literal_character</c>: <c>\</c> (U+005C), <c>"</c> (U+0022), and the five
/// <c>New_Line_Character</c>s — CR (U+000D), LF (U+000A), NEL (U+0085), LS (U+2028) and PS
/// (U+2029). Any of the five, left raw, ends the line the literal is on mid-string and the
/// compiler reports it as <c>CS1010: Newline in constant</c> — confirmed by direct experiment
/// for all five, not assumed from the grammar alone. This set is the language rule, not an
/// enumeration of characters some particular input happened to contain: an operationId can
/// carry an embedded LF and reach here unescaped by the identical path proven for <c>"</c> —
/// <c>OperationKey.Resolve</c> only trims leading/trailing whitespace, and the fixture-filename
/// validation that would otherwise reject a hostile operationId never runs for a parameterless
/// operation (see <c>TestPlanBuilder.cs</c>'s <c>needsFixture &amp;&amp;</c> gate) — so escaping
/// only the two characters a first bug report happened to name was never actually complete.
/// </para>
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
    // Built from numeric char codes rather than pasted as literal glyphs: NEL, LS and PS are
    // themselves line-terminator-like characters (NEL is even one of Unicode's own recognized
    // line terminators), so writing them as raw characters in this source file risks an editor,
    // diff tool, or this very file being silently reinterpreted around them. Spelling them as
    // (char)0x2028 etc. keeps this file itself plain ASCII.
    private static readonly string Nel = ((char)0x0085).ToString();
    private static readonly string LineSeparator = ((char)0x2028).ToString();
    private static readonly string ParagraphSeparator = ((char)0x2029).ToString();

    /// <summary>
    /// Escapes backslash first, then everything else. Reversing that would re-escape the
    /// backslash any later step introduces (the <c>\</c> in <c>\r</c>, <c>\n</c>, or a
    /// <c>\uXXXX</c> sequence). The relative order of the remaining six replacements does not
    /// matter — none of them can introduce a character another one of them targets.
    /// </summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace(Nel, "\\u0085", StringComparison.Ordinal)
            .Replace(LineSeparator, "\\u2028", StringComparison.Ordinal)
            .Replace(ParagraphSeparator, "\\u2029", StringComparison.Ordinal);
    }
}
