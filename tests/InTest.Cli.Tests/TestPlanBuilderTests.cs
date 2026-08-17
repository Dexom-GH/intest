using InTest.Cli.Planning;
using InTest.Cli.Spec;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
}
