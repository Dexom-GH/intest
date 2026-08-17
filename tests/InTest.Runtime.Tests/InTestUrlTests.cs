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

    [TestMethod]
    public void BuildQuery_ReturnsEmptyForNoParameters()
    {
        InTestUrl.BuildQuery(new Dictionary<string, string>()).ShouldBe(string.Empty);
    }

    [TestMethod]
    public void BuildQuery_PrefixesWithQuestionMarkAndJoinsWithAmpersand()
    {
        InTestUrl.BuildQuery(new Dictionary<string, string> { ["page"] = "2", ["sort"] = "name" })
                 .ShouldBe("?page=2&sort=name");
    }

    [TestMethod]
    public void BuildQuery_EscapesNamesAndValues()
    {
        InTestUrl.BuildQuery(new Dictionary<string, string> { ["q"] = "a b&c=d" })
                 .ShouldBe("?q=a%20b%26c%3Dd");
    }

    [TestMethod]
    public void BuildQuery_IsOrderIndependentSoGeneratedUrlsAreStable()
    {
        var forward = InTestUrl.BuildQuery(new Dictionary<string, string> { ["b"] = "1", ["a"] = "2" });
        var reverse = InTestUrl.BuildQuery(new Dictionary<string, string> { ["a"] = "2", ["b"] = "1" });
        forward.ShouldBe(reverse);
    }
}
