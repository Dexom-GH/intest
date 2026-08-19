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
    public void SeparatesOperationCountFromCaseCount()
    {
        // Task 6: "generated" stays a case count (GenerateCommand.cs's own "Generated N test(s)"
        // line already fixed that meaning), so a distinct field is what has to carry the operation
        // count now that one operation can produce more than one case (success + declared error).
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("DefaultTests", "Default",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("A_NotFound", "d", "a", true, "GET", "/a/{id}", ["id"], 404, null, "Contract",
                     Role: CaseRole.DeclaredError, NeedsFixture: false)])],
            [],
            []);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        doc.RootElement.GetProperty("generated").GetInt32().ShouldBe(2,
            "generated must keep counting cases");
        doc.RootElement.GetProperty("operationsGenerated").GetInt32().ShouldBe(1,
            "operationsGenerated must count the one distinct operation, not its two cases");
    }

    [TestMethod]
    public void ExcludesDeclaredErrorAndAuthCasesFromStatusOnlyContractTests()
    {
        // Every declared-error and auth case has a null SchemaKey by construction (decision 5 /
        // decision 3) — counting them here would inflate a note whose stated meaning is "no
        // response schema declared — fixable in the spec" with cases that never had a schema
        // question to begin with.
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("DefaultTests", "Default",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("A_NotFound", "d", "a", true, "GET", "/a/{id}", ["id"], 404, null, "Contract",
                     Role: CaseRole.DeclaredError, NeedsFixture: false),
                 new TestCasePlan("A_Unauthorized", "d", "a", true, "GET", "/a/{id}", ["id"], 401, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.None),
                 new TestCasePlan("A_Forbidden", "d", "a", true, "GET", "/a/{id}", ["id"], 403, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.Secondary)])],
            [],
            []);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        doc.RootElement.GetProperty("notes").GetProperty("statusOnlyContractTests").GetInt32().ShouldBe(0,
            "only Role.Success cases with a null SchemaKey belong in this note");
    }

    [TestMethod]
    public void CountsDeclaredErrorAndAuthTestsGenerated()
    {
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("DefaultTests", "Default",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("A_NotFound", "d", "a", true, "GET", "/a/{id}", ["id"], 404, null, "Contract",
                     Role: CaseRole.DeclaredError, NeedsFixture: false),
                 new TestCasePlan("A_Unauthorized", "d", "a", true, "GET", "/a/{id}", ["id"], 401, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.None),
                 new TestCasePlan("A_Forbidden", "d", "a", true, "GET", "/a/{id}", ["id"], 403, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.Secondary)])],
            [],
            []);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        doc.RootElement.GetProperty("notes").GetProperty("declaredErrorTestsGenerated").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("notes").GetProperty("authTestsGenerated").GetInt32().ShouldBe(2);
    }

    [TestMethod]
    public void CountsAuthTestsGatedOnASecondIdentityRatherThanGuessingHowManyWillBeSkipped()
    {
        // Decision 3's table: only the wrong-scope 403 case (IdentitySlot.Secondary) needs a
        // second identity; the no-token 401 case always generates and always runs. The CLI
        // generates code long before any ITestTokenProvider exists (decision 7), so it can only
        // report how many generated cases *require* a second identity to run — never how many
        // will actually be skipped, which depends on a provider this process never sees.
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("DefaultTests", "Default",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("A_Unauthorized", "d", "a", true, "GET", "/a/{id}", ["id"], 401, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.None),
                 new TestCasePlan("A_Forbidden", "d", "a", true, "GET", "/a/{id}", ["id"], 403, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.Secondary)])],
            [],
            []);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        doc.RootElement.GetProperty("notes").GetProperty("authTestsGatedOnSecondIdentity").GetInt32().ShouldBe(1,
            "only the Secondary-slot (403) case is gated on a second identity, not the None-slot 401 case too");
    }

    [TestMethod]
    public void CountsOperationsDeclaring404WithNoPathParameterToTarget()
    {
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("OrdersTests", "Orders",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a", [], 200, "Order", "Contract")])],
            [],
            [new CoverageNote("a", "declares 404 but has no path parameter to target with an unmatchable value"),
             new CoverageNote("b", "declares 404 but has required query parameter(s) (q) that an unmatchable-id-only request would omit")]);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        // Only the no-path-parameter note counts here — the required-query-parameter note is a
        // different withheld reason and must not be conflated with it.
        doc.RootElement.GetProperty("notes").GetProperty("notFoundWithoutPathParameter").GetInt32().ShouldBe(1);
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
