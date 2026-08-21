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
    public void Escape_EscapesBackslashBeforeQuoteSoTheResultRoundTripsThroughAStringLiteral()
    {
        // Order matters: escaping the quote before the backslash would double-escape the
        // backslash the quote step introduces. Built from char arrays rather than string
        // escape sequences so the raw input and expected output are unambiguous to read,
        // rather than relying on correctly hand-deriving nested C# escaping by eye.
        var input = new string(['\\', '"']); // one backslash followed by one quote
        var expected = new string(['\\', '\\', '\\', '"']); // \\  then \"  when read back as C#

        CSharpLiteral.Escape(input).ShouldBe(expected);
    }

    [TestMethod]
    public void Escape_ThrowsOnNull()
    {
        Should.Throw<ArgumentNullException>(() => CSharpLiteral.Escape(null!));
    }
}
