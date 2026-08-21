using InTest.Cli.Commands;
using InTest.Cli.Fixtures;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class FixturesRepairCommandTests
{
    private string _root = null!;

    private const string Spec = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{"/api/products":{"post":{
        "operationId":"createProduct",
        "requestBody":{"content":{"application/json":{"schema":{"type":"object",
          "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
        "responses":{"201":{"description":"ok"}}}}}
    }
    """;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-fix-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec);
        InitCommand.Run(_root, "T.ApiTests", "spec.json").ShouldBe(0);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string FixturePath => Path.Combine(_root, "fixtures", "createProduct.json");

    [TestMethod]
    public async Task CreatesAMissingFixture()
    {
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        File.Exists(FixturePath).ShouldBeTrue();
        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
    }

    [TestMethod]
    public async Task ReturnsZeroWhenThereIsNothingToRepair()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        // A PR script running repair unconditionally must not fail on a clean tree.
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
    }

    [TestMethod]
    public async Task NeverOverwritesAHandWrittenValue()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        var document = FixtureDocument.Parse(File.ReadAllText(FixturePath));
        document.Body!["sku"] = "WGT-0001";
        File.WriteAllText(FixturePath, document.ToJson());

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["sku"]!.GetValue<string>()
            .ShouldBe("WGT-0001", "repair adds what is absent; it never replaces what a human wrote");
    }

    [TestMethod]
    public async Task AddsAPropertyThatBecameRequired()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec.Replace(
            """"required":["sku"],"properties":{"sku":{"type":"string"}}"""",
            """"required":["sku","name"],"properties":{"sku":{"type":"string"},"name":{"type":"string"}}""""));

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["name"]!.GetValue<string>().ShouldBe("TODO:name");
    }

    [TestMethod]
    public async Task ReportsAPropertyThatLeftTheSchemaWithoutDeletingIt()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        var document = FixtureDocument.Parse(File.ReadAllText(FixturePath));
        document.Body!["legacyRef"] = "kept-by-hand";
        File.WriteAllText(FixturePath, document.ToJson());

        var report = new StringWriter();
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None, report);

        // §10 requires both halves: not deleted, and reported. Silent retention is how a
        // property nobody meant to keep survives three refactors.
        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["legacyRef"].ShouldNotBeNull(
            "never silently deleted — it may be deliberate");
        report.ToString().ShouldContain("legacyRef");
        report.ToString().ShouldContain("no longer in schema");
    }

    [TestMethod]
    public async Task CreatesFixturesOnlyForOperationsTheTestPlanCovers()
    {
        // TestPlanBuilder already owns "which operations exist", including skips for non-JSON
        // request bodies and operations with no 2xx response. If repair iterated the raw
        // document instead, it would create fixtures for operations no generated test uses,
        // and generate's drift check would disagree with it about the operation set.
        const string withSkipped = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/api/products":{"post":{"operationId":"createProduct",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/api/upload":{"post":{"operationId":"upload",
              "requestBody":{"content":{"multipart/form-data":{"schema":{"type":"object"}}}},
              "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        File.WriteAllText(Path.Combine(_root, "spec.json"), withSkipped);
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        File.Exists(Path.Combine(_root, "fixtures", "createProduct.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "fixtures", "upload.json")).ShouldBeFalse(
            "multipart operations are skipped by the plan, so they get no fixture");
    }

    [TestMethod]
    public async Task NeverWritesOutsideFixtures()
    {
        var before = Directory.GetFiles(_root, "*", SearchOption.TopDirectoryOnly)
                              .ToDictionary(f => f, File.GetLastWriteTimeUtc);

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        foreach (var (file, written) in before)
        {
            File.GetLastWriteTimeUtc(file).ShouldBe(written, $"{Path.GetFileName(file)} must not be touched");
        }
    }

    [TestMethod]
    public async Task DoesNotCreateFixturesForOperationsThatDoNotNeedOne()
    {
        // FixtureComposer.NeedsFixture is the sole authority on whether an operation gets a
        // fixture. A parameterless GET and a GET whose only parameter is optional with no
        // example or default both compose to an empty body/$parameters — repair must not turn
        // that into a junk fixture file just because the test plan covers the operation.
        const string withNoFixtureNeeded = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/api/products":{"post":{"operationId":"createProduct",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/api/health":{"get":{"operationId":"getHealth",
              "responses":{"200":{"description":"ok"}}}},
            "/api/items":{"get":{"operationId":"listItems",
              "parameters":[{"name":"sort","in":"query","required":false,
                "schema":{"type":"string"}}],
              "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        File.WriteAllText(Path.Combine(_root, "spec.json"), withNoFixtureNeeded);
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        File.Exists(Path.Combine(_root, "fixtures", "createProduct.json")).ShouldBeTrue(
            "this operation needs a fixture and must still get one");
        File.Exists(Path.Combine(_root, "fixtures", "getHealth.json")).ShouldBeFalse(
            "a parameterless GET needs no fixture — NeedsFixture is false");
        File.Exists(Path.Combine(_root, "fixtures", "listItems.json")).ShouldBeFalse(
            "an all-optional query parameter with no example or default needs no fixture");
    }

    [TestMethod]
    public async Task AppliesLegitimateRepairsEvenWhenAnotherFixtureIsMalformed()
    {
        // Alphabetically, createProduct sorts before createWidget — the loop reaches the
        // corrupted fixture first. One bad committed fixture must not stop repair from adding a
        // sentinel to an unrelated operation that legitimately needs one.
        const string twoOperations = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/api/products":{"post":{"operationId":"createProduct",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/api/widgets":{"post":{"operationId":"createWidget",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["name"],"properties":{"name":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        File.WriteAllText(Path.Combine(_root, "spec.json"), twoOperations);
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        var productPath = Path.Combine(_root, "fixtures", "createProduct.json");
        var widgetPath = Path.Combine(_root, "fixtures", "createWidget.json");
        File.WriteAllText(productPath, "{ not valid json");

        File.WriteAllText(Path.Combine(_root, "spec.json"), twoOperations.Replace(
            """"required":["name"],"properties":{"name":{"type":"string"}}"""",
            """"required":["name","color"],"properties":{"name":{"type":"string"},"color":{"type":"string"}}""""));

        var report = new StringWriter();
        var exitCode = await FixturesRepairCommand.RunAsync(_root, CancellationToken.None, report);

        exitCode.ShouldBe(FixturesRepairCommand.ExitToolError,
            "a malformed committed fixture is a real tool error and must be reflected in the exit code");
        FixtureDocument.Parse(File.ReadAllText(widgetPath)).Body!["color"]!.GetValue<string>()
            .ShouldBe("TODO:color", "the unrelated, legitimate repair must still be applied");
        // The report should say which operation's fixture could not be read.
        report.ToString().ShouldContain("createProduct");
    }
}
