using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class TemplateRendererTests
{
    private static TestClassPlan Plan(string? schemaKey = "Order", string httpMethod = "GET") => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when getOrderById, then 200",
            "getOrderById", false, httpMethod, "/orders/{id}", ["id"], 200, schemaKey, "Contract")]);

    private static string Render(TestClassPlan plan)
        => new TemplateRenderer().RenderClass(plan, "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase");

    [TestMethod]
    public void EmitsAPartialClassDerivingFromTheConfiguredBase()
    {
        Render(Plan()).ShouldContain("public partial class OrdersTests : Orders.ApiTests.OrdersTestBase");
    }

    [TestMethod]
    public void EmitsBlockBodiedMethodsWithTheAssertionOnItsOwnLine()
    {
        // Shouldly reads source text at runtime and garbles messages on expression-bodied
        // methods, so generated methods must never be expression-bodied (§15).
        var rendered = Render(Plan());
        rendered.ShouldNotContain("() =>");
        rendered.ShouldContain("    {");
    }

    [TestMethod]
    public void CallsTheContractAssertionWhenASchemaIsKnown()
    {
        Render(Plan()).ShouldContain("ShouldMatchContractAsync");
    }

    [TestMethod]
    public void FallsBackToStatusOnlyWhenNoSchemaIsDeclared()
    {
        var rendered = Render(Plan(schemaKey: null));
        rendered.ShouldContain("ShouldMatchStatusAsync");
        rendered.ShouldNotContain("ShouldMatchContractAsync");
    }

    [TestMethod]
    public void ThreadsTheCancellationTokenSoCooperativeCancellationWorks()
    {
        Render(Plan()).ShouldContain("TestContext.CancellationToken");
    }

    [TestMethod]
    [DataRow("GET", DisplayName = "non-mutating")]
    [DataRow("POST", DisplayName = "mutating")]
    public void EmitsNoStrayBlankLines(string httpMethod)
    {
        // Every Scriban control tag must be closed '~}}', or it leaks its own trailing newline
        // and the generated code grows a blank line per tag. The golden file only covers a
        // non-mutating GET, so without this the [DoNotParallelize] branch is unguarded — and
        // each template added after v0 would be free to reintroduce the same defect.
        var rendered = Render(Plan(httpMethod: httpMethod));

        rendered.ShouldNotContain("\n\n\n");        // no double blank line anywhere
        rendered.ShouldNotContain("\n\n    }");     // no blank line before a closing brace

        rendered.ShouldContain(httpMethod == "POST"
            ? "then 200\")]\n    [DoNotParallelize]\n    public async Task"
            : "then 200\")]\n    public async Task");
    }

    [TestMethod]
    public void IsDeterministic()
    {
        Render(Plan()).ShouldBe(Render(Plan()));
    }
}
