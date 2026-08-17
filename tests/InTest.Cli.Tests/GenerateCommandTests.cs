using InTest.Cli.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class GenerateCommandTests
{
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
        "responses": { "200": { "description": "ok", "content": { "application/json": {
          "schema": { "$ref": "#/components/schemas/Order" } } } } } } } },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-gen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "orders.json"), Spec);
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<int> RunAsync() => await GenerateCommand.RunAsync(_root, CancellationToken.None);

    [TestMethod]
    public async Task WritesGeneratedClassesAndTheSchemaBundle()
    {
        (await RunAsync()).ShouldBe(0);
        File.Exists(Path.Combine(_root, "Generated", "OrdersTests.g.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "Generated", "spec-schemas.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "coverage-report.json")).ShouldBeTrue();
    }

    [TestMethod]
    public async Task NeverWritesUnderFixtures()
    {
        await RunAsync();
        Directory.Exists(Path.Combine(_root, "fixtures")).ShouldBeFalse();
    }

    [TestMethod]
    public async Task IsDeterministic()
    {
        await RunAsync();
        var first = File.ReadAllText(Path.Combine(_root, "Generated", "OrdersTests.g.cs"));
        await RunAsync();
        File.ReadAllText(Path.Combine(_root, "Generated", "OrdersTests.g.cs")).ShouldBe(first);
    }

    [TestMethod]
    public async Task ReturnsToolErrorWhenTheSpecIsMissing()
    {
        File.Delete(Path.Combine(_root, "orders.json"));
        (await RunAsync()).ShouldBe(2);
    }
}
