using InTest.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class InTestUrlTests
{
    [TestMethod]
    [DataRow("https://h/api", DisplayName = "base without trailing slash")]
    [DataRow("https://h/api/", DisplayName = "base with trailing slash")]
    public void NormalizeBase_AlwaysEndsWithSlash(string input)
    {
        InTestUrl.NormalizeBase(input).ToString().ShouldBe("https://h/api/");
    }

    [TestMethod]
    public void NormalizeBase_RejectsEmpty()
    {
        Should.Throw<ArgumentException>(() => InTestUrl.NormalizeBase(" "));
    }

    [TestMethod]
    public void Build_StripsLeadingSlashSoBaseSegmentSurvives()
    {
        var resolved = new Uri(InTestUrl.NormalizeBase("https://h/api"), InTestUrl.Build("/orders/{id}", "7"));
        resolved.ToString().ShouldBe("https://h/api/orders/7");
    }

    [TestMethod]
    public void Build_EscapesSegmentValues()
    {
        InTestUrl.Build("/orders/{id}", "a b/c").ShouldBe("orders/a%20b%2Fc");
    }

    [TestMethod]
    public void Build_ThrowsWhenValueCountDoesNotMatchPlaceholders()
    {
        Should.Throw<ArgumentException>(() => InTestUrl.Build("/orders/{id}/items/{sku}", "7"));
    }
}
