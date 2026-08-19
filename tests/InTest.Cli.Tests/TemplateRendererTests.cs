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

    private static TestClassPlan PlanDeclaredError(string httpMethod = "GET") => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "DeleteOrder_NotFound",
            DisplayName: "Given Orders, when deleteOrder, then 404",
            OperationKey: "deleteOrder",
            OperationKeySynthesized: false,
            HttpMethod: httpMethod,
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 404,
            SchemaKey: null,
            Category: "Contract",
            Role: CaseRole.DeclaredError,
            NeedsFixture: false)]);

    private static TestClassPlan PlanDeclaredErrorWithIntegerPathParameter() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "DeleteOrder_NotFound",
            DisplayName: "Given Orders, when deleteOrder, then 404",
            OperationKey: "deleteOrder",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 404,
            SchemaKey: null,
            Category: "Contract",
            Role: CaseRole.DeclaredError,
            NeedsFixture: false,
            PathParameterKinds: [PathParameterKind.Integer])]);

    private static TestClassPlan PlanAuth(int expectedStatus, IdentitySlot slot, string httpMethod = "GET") => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: expectedStatus == 401 ? "DeleteOrder_Unauthorized" : "DeleteOrder_Forbidden",
            DisplayName: $"Given Orders, when deleteOrder, then {expectedStatus}",
            OperationKey: "deleteOrder",
            OperationKeySynthesized: false,
            HttpMethod: httpMethod,
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: expectedStatus,
            SchemaKey: null,
            Category: "Contract",
            Role: CaseRole.Auth,
            NeedsFixture: false,
            Slot: slot)]);

    private static TestClassPlan PlanWithRole(CaseRole role) => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "DeleteOrder_UnknownRole",
            DisplayName: "Given Orders, when deleteOrder, then unknown-role",
            OperationKey: "deleteOrder",
            OperationKeySynthesized: false,
            HttpMethod: "DELETE",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 404,
            SchemaKey: null,
            Category: "Contract",
            Role: role,
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
    public void ADeclaredErrorCaseWithAnIntegerPathParameterUsesAWellTypedUnmatchableValueRatherThanAGuid()
    {
        // Review finding on Task 4: PathArguments rendered Guid.NewGuid().ToString() for every
        // path parameter regardless of declared type. For `type: integer`, that is not an
        // unmatchable id — it is an ill-typed one, and an ASP.NET Core [ApiController] binding
        // `int id` without a route constraint answers 400 from model binding before the action's
        // NotFound() path ever runs, so the generated test would assert 404 against a guaranteed
        // 400 on every run — the same 400-vs-404 hazard the three note-not-guess branches in
        // TestPlanBuilder exist to avoid, just undetected here even though the same spec data
        // (the parameter's declared schema type) was available.
        var rendered = Render(PlanDeclaredErrorWithIntegerPathParameter());

        rendered.ShouldNotContain("Guid.NewGuid()");
        rendered.ShouldContain("\"2147483647\"");
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
    public void ADeclaredErrorCaseOnAMutatingMethodDoesNotGetDoNotParallelize()
    {
        // Task 5's own bug fix: [DoNotParallelize] existed to serialize cases that write real
        // state, derived from HTTP method alone (TemplateRenderer.cs). A declared-error DELETE
        // sends a generated, unmatchable id and no body (decision 6) — it mutates nothing real —
        // so gating it behind [DoNotParallelize] bought nothing but slower runs. This is a
        // regression test for the exact bug: before the fix, this DELETE case rendered
        // [DoNotParallelize] because the flag never consulted Role.
        var rendered = Render(PlanDeclaredError(httpMethod: "DELETE"));

        rendered.ShouldNotContain("[DoNotParallelize]");
    }

    [TestMethod]
    public void EmitsNoStrayBlankLinesForAMutatingDeclaredErrorCase()
    {
        // A declared 404 on a DELETE or PUT no longer stacks [DoNotParallelize] with
        // emits_fixture_lookup (the fix above removes that combination), but it is still the one
        // case with both a non-mutating attribute list and a fixture-free body — kept as its own
        // guard against a regression reintroducing a leaked blank line.
        var rendered = Render(PlanDeclaredError(httpMethod: "DELETE"));

        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain("public async Task DeleteOrder_NotFound()\n    {\n        using var request",
            customMessage: "no RequireFixture line and no leftover blank line ahead of it");
    }

    // --- Auth cases (Task 5, decisions 3, 6 & 7) ---

    [TestMethod]
    public void AWrongScopeCaseCallsTheGuardBeforeBuildingTheRequest()
    {
        var rendered = Render(PlanAuth(403, IdentitySlot.Secondary));

        rendered.ShouldContain("RequireMultipleIdentities();");
        rendered.IndexOf("RequireMultipleIdentities(", StringComparison.Ordinal)
            .ShouldBeLessThan(rendered.IndexOf("new HttpRequestMessage(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ANoTokenCaseDoesNotCallTheGuard()
    {
        // Decision 3's table: the 401 case always runs — it needs no second identity, so it must
        // never pay the guard's Assert.Inconclusive gate.
        Render(PlanAuth(401, IdentitySlot.None)).ShouldNotContain("RequireMultipleIdentities(");
    }

    [TestMethod]
    public void AWrongScopeCaseOverridesTheAmbientIdentityToSecondary()
    {
        var rendered = Render(PlanAuth(403, IdentitySlot.Secondary));

        rendered.ShouldContain("using var _ = UseIdentity(IdentitySlot.Secondary);");
        rendered.IndexOf("UseIdentity(", StringComparison.Ordinal)
            .ShouldBeLessThan(rendered.IndexOf("new HttpRequestMessage(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ANoTokenCaseOverridesTheAmbientIdentityToNone()
    {
        // The 401 case's whole mechanism (decision 7): it does not send a bad token, it sends
        // none, by overriding the ambient identity to the sentinel slot.
        Render(PlanAuth(401, IdentitySlot.None)).ShouldContain("using var _ = UseIdentity(IdentitySlot.None);");
    }

    [TestMethod]
    public void ADefaultSlotCaseEmitsNoIdentityOverride()
    {
        // Every existing Success case defaults to IdentitySlot.Default — this is what keeps the
        // golden file byte-identical for every case that existed before Task 5.
        Render(Plan()).ShouldNotContain("UseIdentity(");
    }

    [TestMethod]
    public void AnAuthCaseCallsNoFixtureLookup()
    {
        // Decision 6, same reasoning as the declared-error case: an auth case's operation key can
        // be shared with a success case whose fixture is unfilled, and that must never block it.
        Render(PlanAuth(403, IdentitySlot.Secondary)).ShouldNotContain("RequireFixture(");
    }

    [TestMethod]
    public void AnAuthCaseUsesAGeneratedUnmatchableIdRatherThanAFixtureParameter()
    {
        var rendered = Render(PlanAuth(403, IdentitySlot.Secondary));

        rendered.ShouldContain("Guid.NewGuid().ToString()");
        rendered.ShouldNotContain("FixtureParameter(");
    }

    [TestMethod]
    public void AnAuthCaseSendsNoBody()
    {
        Render(PlanAuth(403, IdentitySlot.Secondary)).ShouldNotContain("request.Content");
    }

    [TestMethod]
    public void AWrongScopeCaseOnAMutatingMethodDoesNotGetDoNotParallelize()
    {
        // The other half of Task 5's [DoNotParallelize] fix: a 403 case on a DELETE sends an
        // unmatchable id (decision 6) and mutates nothing real, so it must not be serialized
        // against other tests either.
        Render(PlanAuth(403, IdentitySlot.Secondary, httpMethod: "DELETE")).ShouldNotContain("[DoNotParallelize]");
    }

    [TestMethod]
    public void EmitsNoStrayBlankLinesForAWrongScopeAuthCase()
    {
        // The guard and the identity override are two body-level conditionals stacked directly
        // on top of each other — a combination nothing before Task 5 exercises, and exactly
        // where an unclosed '~}}' would leak its own blank line between them.
        var rendered = Render(PlanAuth(403, IdentitySlot.Secondary));

        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain(
            "    {\n        RequireMultipleIdentities();\n        using var _ = UseIdentity(IdentitySlot.Secondary);\n\n        using var request",
            customMessage: "guard and override must sit on adjacent lines, with exactly one blank line before the request");
    }

    [TestMethod]
    public void EmitsNoStrayBlankLinesForANoTokenAuthCase()
    {
        var rendered = Render(PlanAuth(401, IdentitySlot.None));

        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain(
            "    {\n        using var _ = UseIdentity(IdentitySlot.None);\n\n        using var request",
            customMessage: "no guard line for a 401 case, and no leftover blank line ahead of the override");
    }

    [TestMethod]
    public void ARoleNotYetDefinedDefaultsToTheFixtureFreeUnmatchableIdBehaviour()
    {
        // Review finding on Task 4: both conditionals in TemplateRenderer tested "is this
        // DeclaredError" rather than "is this Success", so the unsafe, fixture-backed arm was
        // the default for any role neither of today's two names — CaseRole's own doc comment
        // says Task 5 adds Auth next. Decision 6 requires every non-success case to stay
        // fixture-free and pointed at an unmatchable id; a role this code has never seen must
        // fail *toward* that safety, not away from it. There's no third CaseRole member yet to
        // prove it with, so an undefined enum value stands in for "a future role."
        var rendered = Render(PlanWithRole((CaseRole)99));

        rendered.ShouldNotContain("RequireFixture(");
        rendered.ShouldContain("Guid.NewGuid().ToString()");
        rendered.ShouldNotContain("FixtureParameter(");
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
