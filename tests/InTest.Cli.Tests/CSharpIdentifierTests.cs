using InTest.Cli.Naming;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class CSharpIdentifierTests
{
    [TestMethod]
    [DataRow("getOrderById", "GetOrderById")]
    [DataRow("get_orders_id", "GetOrdersId")]
    [DataRow("orders-v2", "OrdersV2")]
    [DataRow("2fa", "_2fa")]
    // PascalCasing is itself the keyword escape: every C# reserved keyword is lowercase, so
    // capitalizing the first character already yields a legal identifier. The '@' guard in
    // ToPascalCase is unreachable defence-in-depth, kept for callers that bypass casing.
    [DataRow("class", "Class")]
    public void ToPascalCase_ProducesValidIdentifiers(string input, string expected)
    {
        CSharpIdentifier.ToPascalCase(input).ShouldBe(expected);
    }

    [TestMethod]
    public void ToPascalCase_ThrowsOnEmptyInput()
    {
        Should.Throw<ArgumentException>(() => CSharpIdentifier.ToPascalCase("   "));
    }

    [TestMethod]
    public void Dedupe_LeavesUniqueNamesUntouched()
    {
        var input = new Dictionary<string, string> { ["a"] = "GetOrder", ["b"] = "PostOrder" };
        CSharpIdentifier.Dedupe(input).Values.ShouldBe(["GetOrder", "PostOrder"], ignoreOrder: true);
    }

    [TestMethod]
    public void Dedupe_SuffixesCollisionsWithAStableKeyHash()
    {
        var input = new Dictionary<string, string> { ["get_a"] = "GetOrder", ["get_b"] = "GetOrder" };
        var result = CSharpIdentifier.Dedupe(input);

        result["get_a"].ShouldNotBe(result["get_b"]);
        result.Values.ShouldAllBe(v => v.StartsWith("GetOrder"));
    }

    [TestMethod]
    public void Dedupe_IsIndependentOfInsertionOrder()
    {
        var forward = CSharpIdentifier.Dedupe(new Dictionary<string, string> { ["get_a"] = "GetOrder", ["get_b"] = "GetOrder" });
        var reverse = CSharpIdentifier.Dedupe(new Dictionary<string, string> { ["get_b"] = "GetOrder", ["get_a"] = "GetOrder" });

        forward["get_a"].ShouldBe(reverse["get_a"]);
        forward["get_b"].ShouldBe(reverse["get_b"]);
    }
}
