using InTest.Cli.Commands;
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

    // listOrders declares 404 but has no path parameter to target with an unmatchable value
    // (decision 5's postscript), so TestPlanBuilder withholds its declared-error case as a
    // CoverageNote rather than a guess — exactly one noted operation.
    private const string SpecWithANotedOperation = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders": { "get": { "operationId": "listOrders", "tags": ["Orders"],
        "responses": {
          "200": { "description": "ok", "content": { "application/json": {
            "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Order" } } } } },
          "404": { "description": "not found" }
        } } } },
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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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

    [TestMethod]
    public async Task ReturnsToolErrorForAnInvalidRootNamespaceAndWritesNothing()
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "My Project", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = await RunAsync();
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
        capturedError.ToString().ShouldContain("rootNamespace");
    }

    [TestMethod]
    public async Task ReturnsToolErrorForAnInvalidTestBaseClassAndWritesNothing()
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.class" } }
        """);

        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = await RunAsync();
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
        capturedError.ToString().ShouldContain("testBaseClass");
    }

    [TestMethod]
    public async Task ReturnsToolErrorWhenRootNamespaceIsJsonNull()
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": null, "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        (await RunAsync()).ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
    }

    [TestMethod]
    public async Task PrintsHowManyOperationsWereNoted()
    {
        // Task 10 item 8(a): found by mutation — deleting the whole `if (plan.Notes.Count > 0)`
        // block in GenerateCommand passed the full Cli suite. coverage-report.json's own
        // `notes.withheld` array is already guarded by other tests, but this console line is the
        // only thing a developer sees without opening that artefact, and CoverageNote's entire
        // point is that a withheld case must not be a silent omission.
        File.WriteAllText(Path.Combine(_root, "orders.json"), SpecWithANotedOperation);

        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            (await RunAsync()).ShouldBe(0);
        }
        finally
        {
            Console.SetOut(original);
        }

        captured.ToString().ShouldContain("Noted 1 operation(s)");
    }
}
