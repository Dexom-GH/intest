using System.Text.Json;
using InTest.Cli.Coverage;
using InTest.Cli.Planning;
using InTest.Cli.Spec;
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
        // Not "every declared-error and auth case has a null SchemaKey" — a real DeclaredError
        // case commonly does not (TestPlanBuilder.cs:171 asks the 404 response for a schema,
        // and every shipped sample's 404 declares one). This plan gives the DeclaredError case
        // a null SchemaKey anyway, deliberately, to prove the filter is Role-based rather than
        // leaning on "declared-error cases happen to have no schema": if the implementation
        // instead filtered on `SchemaKey is not null`, this case would slip through undetected.
        // Either way, a non-success case is excluded regardless of its SchemaKey, because a
        // null SchemaKey on a non-success case is never the "no response schema declared —
        // fixable in the spec" gap this note names (see CoverageReport.cs's own comment).
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
    public void SeparatesScopedFromScopeFreeSecuredOperationsInTheSecondIdentityKeys()
    {
        // Task 5's crux: authTestsGatedOnSecondIdentity counts 403 cases gated on a second
        // identity *existing at all*; authTestsRequiringAnUnauthorizedSecondIdentity counts the
        // narrower set whose provability also depends on that identity lacking the operation's
        // declared scopes. The two keys differ only on a scope-free secured operation, so this
        // plan deliberately carries both shapes — operation "a" is scoped (RequiredScopes:
        // ["orders.read"]), operation "b" is secured but declares no scopes (RequiredScopes left
        // at its empty-never-null default). A fixture with only scoped operations would let one
        // key be a copy of the other and still pass; this one cannot.
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("DefaultTests", "Default",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("A_Unauthorized", "d", "a", true, "GET", "/a/{id}", ["id"], 401, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.None),
                 new TestCasePlan("A_Forbidden", "d", "a", true, "GET", "/a/{id}", ["id"], 403, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.Secondary,
                     RequiredScopes: ["orders.read"]),
                 new TestCasePlan("B_Contract", "d", "b", true, "GET", "/b/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("B_Unauthorized", "d", "b", true, "GET", "/b/{id}", ["id"], 401, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.None),
                 new TestCasePlan("B_Forbidden", "d", "b", true, "GET", "/b/{id}", ["id"], 403, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.Secondary)])],
            [],
            []);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        doc.RootElement.GetProperty("notes").GetProperty("authTestsGatedOnSecondIdentity").GetInt32().ShouldBe(2,
            "both 403 cases are gated on a second identity existing at all, scoped or not");
        doc.RootElement.GetProperty("notes").GetProperty("authTestsRequiringAnUnauthorizedSecondIdentity").GetInt32().ShouldBe(1,
            "only operation \"a\"'s 403 case carries a scope requirement whose satisfaction by the " +
            "second identity would make it unprovable; operation \"b\" is secured but scope-free, " +
            "so its 403 case has no such requirement to fail on");
    }

    [TestMethod]
    public void CountsOperationsDeclaring404WithNoPathParameterToTarget()
    {
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("OrdersTests", "Orders",
                [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a", [], 200, "Order", "Contract")])],
            [],
            [new CoverageNote("a", $"declares 404 but has {TestPlanBuilder.NoPathParameterNoteReason}"),
             new CoverageNote("b", "declares 404 but has required query parameter(s) (q) that an unmatchable-id-only request would omit")]);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        // Only the no-path-parameter note counts here — the required-query-parameter note is a
        // different withheld reason and must not be conflated with it.
        doc.RootElement.GetProperty("notes").GetProperty("notFoundWithoutPathParameter").GetInt32().ShouldBe(1);
    }

    [TestMethod]
    public void ExcludesNonSuccessRolesFromUntaggedOperationsAndSynthesizedOperationIds()
    {
        // Review finding on Task 6: the Role.Success filter on untaggedOperations and
        // synthesizedOperationIds had no test that could tell it apart from the bare,
        // unfiltered Distinct/Where it replaced — the only prior test used one operation
        // whose success and declared-error cases share an OperationKey, so filtered and
        // unfiltered counts agreed by coincidence. Here, operation "b" carries *only* a
        // non-success case (Auth) and no success case of its own, so it must not be counted
        // as an untagged operation, and its OperationKeySynthesized: true must not surface in
        // synthesizedOperationIds either — a bare Distinct over every role would count both.
        var plan = new TestPlan(
            "Orders",
            [new TestClassPlan("DefaultTests", "Default",
                [new TestCasePlan("A_Contract", "d", "a", false, "GET", "/a/{id}", ["id"], 200, "Order", "Contract"),
                 new TestCasePlan("B_Forbidden", "d", "b", true, "GET", "/b/{id}", ["id"], 403, null, "Contract",
                     Role: CaseRole.Auth, NeedsFixture: false, Slot: IdentitySlot.Secondary)])],
            [],
            []);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));

        doc.RootElement.GetProperty("notes").GetProperty("untaggedOperations").GetInt32().ShouldBe(1,
            "operation \"b\" has no Role.Success case of its own and must not be counted");
        doc.RootElement.GetProperty("notes").GetProperty("synthesizedOperationIds").GetInt32().ShouldBe(0,
            "operation \"b\" is the only synthesized key, but it is not a Role.Success case");
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
            [new CoverageNote("a", $"declares 404 but has {TestPlanBuilder.NoPathParameterNoteReason}")]);

        var json = CoverageReport.ToJson(plan);

        json.ShouldContain("\"a\"");
        json.ShouldContain(TestPlanBuilder.NoPathParameterNoteReason);
    }

    [TestMethod]
    public async Task NotFoundWithoutPathParameterCountTracksTestPlanBuilderRatherThanACopiedString()
    {
        // Review finding on Task 6: a hand-copied reason string in this test file would keep
        // passing even if TestPlanBuilder's wording drifted from CoverageReport's match. Building
        // a real plan from TestPlanBuilder itself, from the same 404-declaring, path-parameterless
        // spec TestPlanBuilderTests uses for
        // SkipsAndNotesA404WithNoPathParameterRatherThanGuessingWhereToPutAnUnmatchableValue,
        // proves the count tracks the operations TestPlanBuilder actually notes rather than a
        // string this test happens to also know.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders": {
              "get": {
                "operationId": "listOrders",
                "tags": ["Orders"],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Order" } } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        using var doc = JsonDocument.Parse(CoverageReport.ToJson(plan));
        doc.RootElement.GetProperty("notes").GetProperty("notFoundWithoutPathParameter").GetInt32().ShouldBe(1);
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
