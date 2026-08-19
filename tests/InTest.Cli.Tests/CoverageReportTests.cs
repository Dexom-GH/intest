using System.Text.Json;
using InTest.Cli.Coverage;
using InTest.Cli.Planning;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class CoverageReportTests
{
    private static TestPlan Plan() => new(
        "Orders",
        [new TestClassPlan("OrdersTests", "Orders",
            [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a", [], 200, "Order", "Contract"),
             new TestCasePlan("B_Contract", "d", "b", false, "GET", "/b", [], 204, null, "Contract")])],
        [new SkippedOperation("c", "request body media type(s) multipart/form-data not supported in v0")],
        []);

    [TestMethod]
    public void CountsGeneratedAndSkippedOperations()
    {
        using var doc = JsonDocument.Parse(CoverageReport.ToJson(Plan()));
        doc.RootElement.GetProperty("generated").GetInt32().ShouldBe(2);
        doc.RootElement.GetProperty("skipped").GetArrayLength().ShouldBe(1);
    }

    [TestMethod]
    public void NotesSynthesizedOperationIds()
    {
        using var doc = JsonDocument.Parse(CoverageReport.ToJson(Plan()));
        doc.RootElement.GetProperty("notes").GetProperty("synthesizedOperationIds").GetInt32().ShouldBe(1);
    }

    [TestMethod]
    public void NotesStatusOnlyTestsSoMissingSchemasAreVisible()
    {
        using var doc = JsonDocument.Parse(CoverageReport.ToJson(Plan()));
        doc.RootElement.GetProperty("notes").GetProperty("statusOnlyContractTests").GetInt32().ShouldBe(1);
    }

    [TestMethod]
    public void CountsDistinctOperationsRatherThanCasesForTheOperationNamedMetrics()
    {
        // untaggedOperations and synthesizedOperationIds name *operations*, not cases. Since an
        // operation can now emit more than one case (success + declared error, decision 5), a
        // plan with two cases sharing one OperationKey must still count as one operation — every
        // sample spec in the repo declares 404s, so counting cases here double-counts in practice,
        // not just in theory.
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("DefaultTests", "Default",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("A_NotFound", "d", "a", true, "GET", "/a/{id}", ["id"], 404, null, "Contract",
                     Role: CaseRole.DeclaredError, NeedsFixture: false)])],
            [],
            []);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        doc.RootElement.GetProperty("notes").GetProperty("untaggedOperations").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("notes").GetProperty("synthesizedOperationIds").GetInt32().ShouldBe(1);
    }

    [TestMethod]
    public void IsDeterministic()
    {
        CoverageReport.ToJson(Plan()).ShouldBe(CoverageReport.ToJson(Plan()));
    }

    [TestMethod]
    public void SurfacesAWithheldDeclaredErrorCaseInTheArtefact()
    {
        // Review finding on Task 4: TestPlan.Notes was populated by TestPlanBuilder's three
        // withholding branches and then read by nothing — a withheld declared-error case was a
        // completely silent omission, exactly what §12 legislates against ("skips remove tests.
        // Notes do not" only helps if something reports the notes).
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("OrdersTests", "Orders",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a", [], 200, "Order", "Contract")])],
            [],
            [new CoverageNote("a", "declares 404 but has no path parameter to target with an unmatchable value")]);

        var json = CoverageReport.ToJson(plan);

        json.ShouldContain("\"a\"");
        json.ShouldContain("no path parameter to target with an unmatchable value");
    }

    [TestMethod]
    public void CarriesAnUnusableOperationIdSkip()
    {
        var plan = new TestPlan("Api", [],
            [new SkippedOperation("Orders/Create", "operationId 'Orders/Create' cannot be a fixture filename: it contains '/'.")],
            []);

        var json = CoverageReport.ToJson(plan);

        json.ShouldContain("Orders/Create");
        json.ShouldContain("cannot be a fixture filename");
    }
}
