using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class TemplateRendererTests
{
    private static TestClassPlan Plan(string? schemaKey = "Order") => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when getOrderById, then 200",
            "getOrderById", false, "GET", "/orders/{id}", ["id"], 200, schemaKey, "Contract")]);

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
    public void IsDeterministic()
    {
        Render(Plan()).ShouldBe(Render(Plan()));
    }
}
