using InTest.Cli.Commands;
using InTest.Cli.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class CommonPathPrefixTests
{
    private static TestPlan PlanFor(params string[] paths) => new(
        "Api",
        [new TestClassPlan("T", "T", paths.Select((p, i) =>
            new TestCasePlan($"M{i}", "d", $"op{i}", false, "GET", p, [], 200, null, "Contract")).ToList())],
        []);

    [TestMethod]
    [DataRow("/api", "/api/products", "/api/categories")]
    [DataRow("/api/v2", "/api/v2/products", "/api/v2/orders")]
    [DataRow("", "/products", "/categories")]
    [DataRow("", "/api/products", "/health")]
    public void FindsTheLongestSharedLeadingSegments(string expected, string first, string second)
    {
        GenerateCommand.CommonPathPrefix(PlanFor(first, second)).ShouldBe(expected);
    }

    [TestMethod]
    public void StopsAtAParameterSegment()
    {
        // A shared "{id}" is not a fixed prefix — a base URL cannot duplicate it.
        GenerateCommand.CommonPathPrefix(PlanFor("/{tenant}/products", "/{tenant}/orders"))
                       .ShouldBe(string.Empty);
    }

    [TestMethod]
    public void HandlesASingleOperation()
    {
        GenerateCommand.CommonPathPrefix(PlanFor("/api/products/{id}")).ShouldBe("/api/products");
    }

    [TestMethod]
    public void ReturnsEmptyForAnEmptyPlan()
    {
        GenerateCommand.CommonPathPrefix(new TestPlan("Api", [], [])).ShouldBe(string.Empty);
    }
}
