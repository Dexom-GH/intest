using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class TemplateRendererTests
{
    private static TestClassPlan Plan(string? schemaKey = "Order", string httpMethod = "GET") => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when getOrderById, then 200",
            "getOrderById", false, httpMethod, "/orders/{id}", ["id"], 200, schemaKey, "Contract")]);

    private static TestClassPlan PlanWithQueryParameters(params string[] queryParameterNames) => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "GetOrderById_Contract",
            DisplayName: "Given Orders, when getOrderById, then 200",
            OperationKey: "getOrderById",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 200,
            SchemaKey: "Order",
            Category: "Contract",
            QueryParameterNames: queryParameterNames)]);

    private static TestClassPlan PlanDeclaredError() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "GetOrderById_NotFound",
            DisplayName: "Given Orders, when getOrderById, then 404",
            OperationKey: "getOrderById",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 404,
            SchemaKey: null,
            Category: "Contract",
            Role: CaseRole.DeclaredError,
            NeedsFixture: false)]);

    private static TestClassPlan PlanWithBody() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "CreateOrder_Contract",
            DisplayName: "Given Orders, when createOrder, then 201",
            OperationKey: "createOrder",
            OperationKeySynthesized: false,
            HttpMethod: "POST",
            PathTemplate: "/orders",
            PathParameterNames: [],
            ExpectedStatus: 201,
            SchemaKey: "Order",
            Category: "Contract",
            HasRequestBody: true)]);

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

    [TestMethod]
    public void RendersAStringContentBodyFromTheFixture()
    {
        var rendered = Render(PlanWithBody());

        rendered.ShouldContain("FixtureBody(\"createOrder\")");
        rendered.ShouldContain("new StringContent(");
        rendered.ShouldContain("application/json");
    }

    [TestMethod]
    public void SubstitutesAPathParameterFromTheFixtureRatherThanTestData()
    {
        var rendered = Render(Plan());

        rendered.ShouldContain("FixtureParameter(\"getOrderById\", \"id\")");
        rendered.ShouldNotContain("TestData");
    }

    [TestMethod]
    public void AppendsOnlyTheQueryParametersTheFixtureSupplies()
    {
        var rendered = Render(PlanWithQueryParameters("page", "sort"));

        rendered.ShouldContain("InTestUrl.BuildQuery(");
        rendered.ShouldNotContain("?page=", customMessage: "values come from the fixture at runtime, not the template");
    }

    [TestMethod]
    public void EmitsNoQueryStringWhenThereAreNoQueryParameters()
    {
        Render(Plan()).ShouldNotContain("BuildQuery");
    }

    [TestMethod]
    public void CallsRequireFixtureBeforeBuildingTheRequest()
    {
        var rendered = Render(Plan());

        rendered.ShouldContain("RequireFixture(\"getOrderById\")");
        rendered.IndexOf("RequireFixture(", StringComparison.Ordinal)
            .ShouldBeLessThan(rendered.IndexOf("new HttpRequestMessage(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NeverReferencesTestData()
    {
        Render(PlanWithBody()).ShouldNotContain("TestData");
        Render(PlanWithQueryParameters("page")).ShouldNotContain("TestData");
    }

    [TestMethod]
    public void ADeclaredErrorCaseCallsNoFixtureLookup()
    {
        // Decision 6: a declared-error case shares its operation key with the success case it
        // sits beside. Calling RequireFixture here would let that sibling's unfilled or
        // unresolved fixture block a case that needs no data at all — exactly what decision 6
        // exists to prevent.
        Render(PlanDeclaredError()).ShouldNotContain("RequireFixture(");
    }

    [TestMethod]
    public void ADeclaredErrorCaseUsesAGeneratedUnmatchableIdRatherThanAFixtureParameter()
    {
        var rendered = Render(PlanDeclaredError());

        rendered.ShouldContain("Guid.NewGuid().ToString()");
        rendered.ShouldNotContain("FixtureParameter(");
    }

    [TestMethod]
    public void ASuccessCaseIsUnaffectedByTheDeclaredErrorBranch()
    {
        // Guards the branch itself, not just one arm of it: without this, an implementation that
        // always skips RequireFixture (satisfying the two tests above unconditionally) would pass
        // unnoticed.
        var rendered = Render(Plan());

        rendered.ShouldContain("RequireFixture(\"getOrderById\")");
        rendered.ShouldContain("FixtureParameter(\"getOrderById\", \"id\")");
    }

    [TestMethod]
    public void EmitsNoStrayBlankLinesForADeclaredErrorCase()
    {
        // The RequireFixture conditional is new territory for whitespace control —
        // EmitsNoStrayBlankLines above only exercises the mutates/[DoNotParallelize] branch, and
        // EmitsNoStrayBlankLinesWithABodyOrQueryParameters only the has_body/query_expression
        // ones. An unclosed '~}}' here leaks its own blank line the same way those did.
        var rendered = Render(PlanDeclaredError());

        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain("    {\n        using var request",
            customMessage: "no RequireFixture line and no leftover blank line ahead of it");
    }

    [TestMethod]
    public void EmitsNoStrayBlankLinesWithABodyOrQueryParameters()
    {
        // EmitsNoStrayBlankLines above only exercises the mutates/[DoNotParallelize] branch.
        // The has_body and query_expression conditionals are newer still (Task 8) and are
        // exactly where Scriban whitespace control breaks (unclosed '~}}' leaks a blank line
        // per tag), so they get their own guard rather than trusting the older test to cover them.
        var withBody = Render(PlanWithBody());
        withBody.ShouldNotContain("\n\n\n");
        withBody.ShouldNotContain("\n\n    }");

        var withQuery = Render(PlanWithQueryParameters("page", "sort"));
        withQuery.ShouldNotContain("\n\n\n");
        withQuery.ShouldNotContain("\n\n    }");
    }
}
