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
}
