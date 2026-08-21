using InTest.Cli.Naming;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class CSharpLiteralTests
{
    [TestMethod]
    public void Escape_LeavesOrdinaryTextUnchanged()
    {
        CSharpLiteral.Escape("getOrderById").ShouldBe("getOrderById");
    }

    [TestMethod]
    public void Escape_EscapesADoubleQuote()
    {
        CSharpLiteral.Escape("a\"b").ShouldBe("a\\\"b");
    }

    [TestMethod]
    public void Escape_EscapesABackslash()
    {
        CSharpLiteral.Escape("a\\b").ShouldBe("a\\\\b");
    }

    [TestMethod]
    public void Escape_EscapesTheBackslashBeforeAnAdjacentQuoteRatherThanAfter()
    {
        // Order matters: escaping the quote before the backslash would double-escape the
        // backslash the quote step introduces, producing the wrong character sequence (this
        // test asserts the sequence directly — it does not parse the result back through a
        // compiler, so "correct" here means "matches the sequence the correct order produces",
        // not "round-trips"). Built from char arrays rather than string escape sequences so the
        // raw input and expected output are unambiguous to read, rather than relying on
        // correctly hand-deriving nested C# escaping by eye.
        var input = new string(['\\', '"']); // one backslash followed by one quote
        var expected = new string(['\\', '\\', '\\', '"']); // \\  then \"  when read back as C#

        CSharpLiteral.Escape(input).ShouldBe(expected);
    }

    // --- New_Line_Characters (the C# grammar's forbidden set beyond '\' and '"') ---
    //
    // The full seven-character forbidden set, and why any of the five New_Line_Characters left
    // raw produces CS1010 "Newline in constant", is explained once, canonically, on
    // CSharpLiteral.Escape's own doc comment — not restated here. Locally relevant: every
    // DataRow below builds its character from a numeric code point rather than pasting the
    // literal glyph, for the same reason CSharpLiteral.cs itself does — NEL, LS and PS are
    // themselves line-terminator-like characters, so writing them as raw source text risks a
    // tool silently reinterpreting lines around them (this file must stay plain ASCII to avoid
    // that).
    [TestMethod]
    [DataRow(0x000D, "\\r", DisplayName = "CR")]
    [DataRow(0x000A, "\\n", DisplayName = "LF")]
    [DataRow(0x0085, "\\u0085", DisplayName = "NEL")]
    [DataRow(0x2028, "\\u2028", DisplayName = "LS")]
    [DataRow(0x2029, "\\u2029", DisplayName = "PS")]
    public void Escape_EscapesEachForbiddenNewLineCharacter(int codePoint, string expectedEscapeSequence)
    {
        var input = "a" + (char)codePoint + "b";
        var expected = "a" + expectedEscapeSequence + "b";

        CSharpLiteral.Escape(input).ShouldBe(expected);
    }

    [TestMethod]
    public void Escape_EscapesTheBackslashBeforeAnAdjacentLineFeedRatherThanAfter()
    {
        // Same ordering hazard as the quote case above, now for a character whose escape
        // sequence itself introduces a backslash. Same caveat too: this asserts the exact
        // character sequence the correct order produces, not a round trip through a compiler.
        var input = new string(['\\', (char)0x000A]); // one backslash followed by one LF
        var expected = new string(['\\', '\\', '\\', 'n']); // \\  then \n  when read back as C#

        CSharpLiteral.Escape(input).ShouldBe(expected);
    }

    [TestMethod]
    public void Escape_ThrowsOnNull()
    {
        Should.Throw<ArgumentNullException>(() => CSharpLiteral.Escape(null!));
    }
}
