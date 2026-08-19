using InTest.Cli.Planning;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class TestPlanBuilderTests
{
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "get": {
            "operationId": "getOrderById",
            "tags": ["Orders"],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "200": { "description": "ok", "content": { "application/json": {
              "schema": { "$ref": "#/components/schemas/Order" } } } } }
          }
        },
        "/health": { "get": { "responses": { "204": { "description": "no content" } } } },
        "/upload": { "post": { "tags": ["Files"],
          "requestBody": { "content": { "multipart/form-data": { "schema": { "type": "object" } } } },
          "responses": { "200": { "description": "ok" } } } }
      },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    private static async Task<TestPlan> BuildAsync()
        => TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document);

    [TestMethod]
    public async Task GroupsOperationsIntoClassesByFirstTag()
    {
        var plan = await BuildAsync();
        plan.Classes.Select(c => c.ClassName).ShouldContain("OrdersTests");
    }

    [TestMethod]
    public async Task PutsUntaggedOperationsInTheDefaultClass()
    {
        var plan = await BuildAsync();
        plan.Classes.Select(c => c.ClassName).ShouldContain("DefaultTests");
    }

    [TestMethod]
    public async Task NamesContractMethodsWithoutTheStatusCode()
    {
        var plan = await BuildAsync();
        var method = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById");
        method.MethodName.ShouldBe("GetOrderById_Contract");
    }

    [TestMethod]
    public async Task CarriesTheSchemaKeyForJsonResponses()
    {
        var plan = await BuildAsync();
        plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById").SchemaKey.ShouldBe("Order");
    }

    [TestMethod]
    public async Task EmitsAStatusOnlyCaseForBodilessResponses()
    {
        var plan = await BuildAsync();
        var health = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "get_health");
        health.ExpectedStatus.ShouldBe(204);
        health.SchemaKey.ShouldBeNull();
    }

    [TestMethod]
    public async Task SkipsUnsupportedContentTypesAndSaysWhy()
    {
        var plan = await BuildAsync();
        plan.Skipped.ShouldContain(s => s.OperationKey == "post_upload" && s.Reason.Contains("multipart/form-data"));
    }

    [TestMethod]
    public async Task RecordsPathParameterNamesInOrder()
    {
        var plan = await BuildAsync();
        plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById")
            .PathParameterNames.ShouldBe(["id"]);
    }

    [TestMethod]
    public async Task IsDeterministic()
    {
        var first = System.Text.Json.JsonSerializer.Serialize(await BuildAsync());
        var second = System.Text.Json.JsonSerializer.Serialize(await BuildAsync());
        first.ShouldBe(second);
    }

    [TestMethod]
    public async Task SkipsAnOperationWhoseIdCannotBeAFixtureFileName()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/a":{"post":{"operationId":"Orders/Create",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object"}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/b":{"get":{"operationId":"listOrders","responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/Create" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldContain(c => c.OperationKey == "listOrders",
            "one unusable operationId must not cost the rest of the document");
    }

    [TestMethod]
    public async Task DoesNotSkipAnUnusableIdWhenTheOperationNeedsNoFixture()
    {
        // No request body and no required parameter means no fixture is ever loaded, so the
        // filename is never needed. §12's rule is that skips remove tests and notes do not — this
        // operation is perfectly testable, so removing it would lose coverage for no reason.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List","responses":{"200":{"description":"ok"}}}}}}
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldBeEmpty();
        plan.Classes.SelectMany(c => c.Cases).Count().ShouldBe(1);
    }

    [TestMethod]
    public async Task SkipsAnUnusableIdWhenAnOptionalQueryParameterCarriesAnExample()
    {
        // No body and no required parameter, but the composer still surfaces a real value for
        // this optional query parameter (tier 2) — so a fixture IS written, and the unusable
        // operationId must be caught before that write is attempted.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List",
            "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer","example":2}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/List" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldNotContain(c => c.OperationKey == "Orders/List");
    }

    [TestMethod]
    public async Task SkipsAnUnusableIdWhenAnOptionalQueryParameterCarriesADefault()
    {
        // Same shape as above but tier 3 (a declared default) rather than tier 2 (an example) —
        // the composer still emits a real value, so the same skip must fire.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List",
            "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer","default":2}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/List" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldNotContain(c => c.OperationKey == "Orders/List");
    }

    [TestMethod]
    public async Task DoesNotSkipAnUnusableIdWhenTheOptionalQueryParameterHasNeitherExampleNorDefault()
    {
        // Extends DoesNotSkipAnUnusableIdWhenTheOperationNeedsNoFixture (which covers the
        // no-parameters case) to an optional parameter that carries no example and no default:
        // the composer emits nothing for it either, so no fixture file is ever written and the
        // unusable operationId still never matters.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List",
            "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer"}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldBeEmpty();
        plan.Classes.SelectMany(c => c.Cases).Count().ShouldBe(1);
    }

    // --- Declared-error cases (decision 5): 404 only, and only with a path parameter. ---

    private static async Task<TestPlan> BuildAsync(string spec)
        => TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

    private const string SpecDeclaring404 = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "get": {
            "operationId": "getOrderById",
            "tags": ["Orders"],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": {
              "200": { "description": "ok", "content": { "application/json": {
                "schema": { "$ref": "#/components/schemas/Order" } } } },
              "404": { "description": "not found" }
            }
          }
        }
      },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    [TestMethod]
    public async Task EmitsADeclaredErrorCaseFor404WhenTheOperationHasAPathParameter()
    {
        var plan = await BuildAsync(SpecDeclaring404);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.ShouldContain(c => c.ExpectedStatus == 404 && c.Role == CaseRole.DeclaredError);
    }

    [TestMethod]
    public async Task NamesTheDeclaredErrorCaseByItsStatusRatherThanACollisionSuffix()
    {
        // "GetOrderById_NotFound", not "GetOrderById_Contract2" — decision 4's dedupe machinery
        // must never be what names a genuinely distinct case; only real name collisions get a
        // hash suffix.
        var plan = await BuildAsync(SpecDeclaring404);
        var notFound = plan.Classes.SelectMany(c => c.Cases).Single(c => c.ExpectedStatus == 404);

        notFound.MethodName.ShouldBe("GetOrderById_NotFound");
    }

    [TestMethod]
    public async Task TheDeclaredErrorMethodNameDoesNotMoveWhenAnUnrelatedOperationIsAdded()
    {
        // Decision 4 warns that keying the dedupe machinery on operation identity + role must
        // never let the *number* or *order* of other declared-error cases in the document shift
        // a name that has nothing to do with them — only a genuine name collision may add a
        // suffix. Rebuilding the same document twice (the previous shape of this test) cannot
        // exercise that: TestPlanBuilder.Build is a pure function, so identical input trivially
        // produces identical output regardless of how names are derived. This spec instead adds
        // an unrelated operation — also declaring 404, also with a path parameter, ordered before
        // "getOrderById" in the document — and checks that getOrderById's declared-error name is
        // unaffected. An implementation that assigns the "_NotFound" suffix by counting declared-
        // error cases in processing order, rather than keying strictly on operation identity,
        // fails this: getCustomerById's declared-error case is now first in doc order, so
        // getOrderById's would shift.
        const string specWithAPrecedingUnrelated404 = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/customers/{id}": {
              "get": {
                "operationId": "getCustomerById",
                "tags": ["Customers"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            },
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(specWithAPrecedingUnrelated404);
        var notFound = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "getOrderById" && c.Role == CaseRole.DeclaredError);

        notFound.MethodName.ShouldBe("GetOrderById_NotFound");
    }

    [TestMethod]
    public async Task ANotFoundCaseUsesAnUnmatchableIdRatherThanAFixture()
    {
        var plan = await BuildAsync(SpecDeclaring404);
        var notFound = plan.Classes.SelectMany(c => c.Cases).Single(c => c.ExpectedStatus == 404);

        // A 404 test needs no data, so it must not be blocked by an unfilled fixture. Decision 6.
        notFound.NeedsFixture.ShouldBeFalse();
    }

    [TestMethod]
    public async Task TheSuccessCaseIsUnaffectedByTheDeclaredErrorCaseItGainsANeighbour()
    {
        var plan = await BuildAsync(SpecDeclaring404);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.Role == CaseRole.Success);

        success.MethodName.ShouldBe("GetOrderById_Contract");
        success.ExpectedStatus.ShouldBe(200);
    }

    [TestMethod]
    public async Task DoesNotGenerateADeclaredErrorCaseFor400()
    {
        // No deterministic fixture-free trigger exists for 400 — sending the valid success
        // request would assert 400 against a 200 on every run.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "400": { "description": "bad request" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.Count.ShouldBe(1);
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);
    }

    [TestMethod]
    [DataRow("401")]
    [DataRow("403")]
    public async Task DoesNotGenerateADeclaredErrorCaseForAuthOwnedStatuses(string authStatus)
    {
        // The auth cases (Task 5) already own 401/403. A declared-error case here would send a
        // valid authenticated request and assert 401/403 against it — failing on every run.
        var spec = $$"""
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "{{authStatus}}": { "description": "denied" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.Count.ShouldBe(1);
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);
    }

    [TestMethod]
    public async Task SkipsAndNotesA404WithNoPathParameterRatherThanGuessingWhereToPutAnUnmatchableValue()
    {
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

        var plan = await BuildAsync(spec);

        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "listOrders").ToList();
        cases.Count.ShouldBe(1, "the success case must still generate — only the declared-error case is affected");
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);

        // §12: skips remove tests, notes do not. listOrders' success case is generated and runs,
        // so it must never appear in Skipped — GenerateCommand's "Skipped N operation(s)" line
        // and coverage-report.json's `skipped` array both read that list verbatim, and either
        // would misreport a live, passing operation as skipped.
        plan.Skipped.ShouldNotContain(s => s.OperationKey == "listOrders");

        plan.Notes.ShouldContain(n => n.OperationKey == "listOrders" && n.Reason.Contains("404"),
            "a silently dropped 404 case is indistinguishable from a bug");
    }

    [TestMethod]
    public async Task SkipsAndNotesA404WithARequiredQueryParameterRatherThanSendingAnIncompleteRequest()
    {
        // Decision 5's postscript: whether a missing *required* query parameter answers 400 or
        // 404 depends on binding and route configuration, so it is a measurement to take, not an
        // assumption to ship. A declared-error case that targets only the unmatchable path id and
        // omits the required "tenant" query parameter risks asserting 404 against what a
        // compliant, correctly-routed API actually answers with 400 — exactly the wall of wrong
        // failures decision 5 opens with. Treated the same as the no-path-parameter case: a note,
        // not a guess shipped as a test.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "tenant", "in": "query", "required": true, "schema": { "type": "string" } }
                ],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);

        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();
        cases.Count.ShouldBe(1, "the success case must still generate — only the declared-error case is affected");
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);

        plan.Skipped.ShouldNotContain(s => s.OperationKey == "getOrderById");
        plan.Notes.ShouldContain(n => n.OperationKey == "getOrderById" && n.Reason.Contains("tenant"),
            "a silently dropped 404 case is indistinguishable from a bug");
    }

    [TestMethod]
    public async Task SkipsAndNotesA404WithARequiredRequestBodyRatherThanSendingAnIncompleteRequest()
    {
        // The strictly stronger case of the required-query-parameter branch above: against an
        // ASP.NET Core [ApiController] with a non-nullable [FromBody] parameter, a bodyless
        // request (decision 6: send no body) is rejected by model binding with 400 before the
        // action's NotFound() path ever runs. Sending only the unmatchable path id and omitting
        // a required request body risks asserting 404 against what a compliant API answers with
        // 400 on every run — the exact wall of wrong failures decision 5 opens with. Treated the
        // same as the no-path-parameter and required-query-parameter cases: a note, not a guess
        // shipped as a test.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "put": {
                "operationId": "updateOrder",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "requestBody": {
                  "required": true,
                  "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } }
                },
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);

        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "updateOrder").ToList();
        cases.Count.ShouldBe(1, "the success case must still generate — only the declared-error case is affected");
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);

        plan.Skipped.ShouldNotContain(s => s.OperationKey == "updateOrder");
        plan.Notes.ShouldContain(n => n.OperationKey == "updateOrder" && n.Reason.Contains("request body"),
            "a silently dropped 404 case is indistinguishable from a bug");
    }

    [TestMethod]
    public async Task NeitherStatusDeclaredMeansOnlyTheSuccessCase()
    {
        var plan = await BuildAsync(Spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.Count.ShouldBe(1);
        cases.Single().Role.ShouldBe(CaseRole.Success);
    }

    [TestMethod]
    public async Task SkipsTheDeclaredErrorCaseWhenTheSuccessCaseWasAlsoSkipped()
    {
        // An operation whose operationId cannot be a fixture filename is skipped entirely before
        // any case is built (SkipsAnOperationWhoseIdCannotBeAFixtureFileName above) — the
        // declared-error case must never appear on its own once the success case it would sit
        // beside was never generated, so the two can never disagree about the operation.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/a/{id}":{"get":{"operationId":"Orders/Get",
              "parameters":[{"name":"id","in":"path","required":true,"schema":{"type":"string"}}],
              "responses":{
                "200":{"description":"ok"},
                "404":{"description":"not found"}
              }}}}
        }
        """;

        var plan = await BuildAsync(spec);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/Get" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldNotContain(c => c.OperationKey == "Orders/Get");

        // Exactly one skip reason for this operation, not two — the fixture-key skip alone
        // explains its absence; a second, 404-shaped skip reason would say the two disagreed.
        plan.Skipped.Count(sk => sk.OperationKey == "Orders/Get").ShouldBe(1);
    }
}
