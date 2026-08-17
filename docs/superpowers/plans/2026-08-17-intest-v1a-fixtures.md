# InTest v1-a — Fixtures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Operations with request bodies and path parameters generate tests that can actually send them, sourced from committed fixture files that a human owns.

**Architecture:** `intest fixtures repair` composes a fixture per operation using §10's four-tier precedence and writes it under `fixtures/`. `generate` stays read-only there and reports drift. At runtime `FixtureStore` loads each fixture, deep-merges any environment overlay, resolves `{{…}}` tokens, and validates everything once at `AssemblyInitialize`.

**Tech Stack:** Unchanged from v0 — net10.0 · Microsoft.OpenApi 3.10.0 · NJsonSchema 11.6.1 · Scriban 7.2.6 · MSTest 4.3.3.

**Spec:** [`../specs/2026-08-16-intest-api-test-generator-design.md`](../specs/2026-08-16-intest-api-test-generator-design.md), §10 primarily.

**Prerequisite:** v0 complete and merged (`a8add69`). 123 tests passing.

---

## Decisions this plan encodes

Three were taken before writing it, and two depart from the spec as written. Both departures get a task that amends the spec, so code and document do not drift.

**1. Path and query parameters live in fixtures, not `TestData`.** v0's `TestData.Require` throws until someone calls `TestData.Set` in `TestStartup`. That was a placeholder. One fixture per operation now carries both its parameters and its body, so there is one mechanism and one place to look, and startup validation covers parameters too. `TestData` is deleted.

**2. A bad fixture fails its own operation, not the whole run.** §10 currently specifies that validation aborts everything. This plan reports *all* problems in one aggregated message at startup — that part is unchanged and is the valuable half — but fails only the operations whose fixtures are unresolved. On the current sample corpus the spec's behaviour would turn 6 passing Catalog tests red for a problem in 3 unrelated ones.

This does **not** reopen "no skip-flags, no silent green" (§1 principle 5). Nothing is skipped and nothing goes quietly green: affected tests fail loudly with a message naming the file and property. Task 9 amends §10.

**3. `{{fixture:…}}` is out of scope.** It resolves after `IAssemblyFixture` implementations complete, and those are v1-b. v1-a ships `{{config:…}}`, `{{secret:…}}`, `{{runId}}` and `{{utcNow}}`. A `{{fixture:…}}` token encountered in v1-a fails validation with "not supported until v1-b" rather than being silently left as literal text.

---

## Fixture file shape

```jsonc
// fixtures/post_api_products.json
{
  "$meta": { "tier": 4, "operationId": "post_api_products", "generatedBy": "intest 0.2.0" },
  "$parameters": { "id": "TODO:id" },
  "body": {
    "sku": "TODO:sku",
    "name": "TODO:name",
    "price": 0,
    "categoryId": "{{config:TestData:CategoryId}}"
  }
}
```

`$meta` records provenance. `$parameters` covers path and query values by name. `body` is absent for operations that take no request body. Filenames are the operation key, sanitised — the same key everything else already uses.

## File structure

| File | Responsibility |
|---|---|
| **`src/InTest.Cli/Fixtures/`** | |
| `FixtureDocument.cs` | The model above, plus load/save with stable key ordering |
| `FixtureComposer.cs` | §10's four-tier precedence: media-type example → per-property examples → defaults → schema shape with `TODO:` |
| `FixtureDrift.cs` | Compares an existing fixture against the current schema |
| `src/InTest.Cli/Commands/FixturesRepairCommand.cs` | The only writer under `fixtures/` |
| **`src/InTest.Runtime/Neutral/`** | |
| `FixtureStore.cs` | Loads fixtures, deep-merges overlays, exposes resolved values |
| `TokenResolver.cs` | `{{config:}}`, `{{secret:}}`, `{{runId}}`, `{{utcNow}}` |
| `FixtureValidation.cs` | Aggregated report; per-operation resolution state |
| **Modified** | |
| `Rendering/Templates/mstest-class.scriban` | Emit request bodies and fixture-sourced parameters |
| `Commands/GenerateCommand.cs` | Drift reporting; exit 1 when fixtures need work |
| `MSTest/TestHost.cs` | Load and validate fixtures at `AssemblyInitialize` |
| **Deleted** | `Neutral/TestData.cs` |

---

## Task 1: Fixture document model

**Files:**
- Create: `src/InTest.Cli/Fixtures/FixtureDocument.cs`
- Test: `tests/InTest.Cli.Tests/FixtureDocumentTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using InTest.Cli.Fixtures;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class FixtureDocumentTests
{
    [TestMethod]
    public void RoundTripsThroughJson()
    {
        var document = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 2, OperationId = "post_api_products", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["id"] = "7" },
            Body = System.Text.Json.Nodes.JsonNode.Parse("""{"sku":"WGT-0001"}""")
        };

        var reloaded = FixtureDocument.Parse(document.ToJson());

        reloaded.Meta.Tier.ShouldBe(2);
        reloaded.Meta.OperationId.ShouldBe("post_api_products");
        reloaded.Parameters["id"].ShouldBe("7");
        reloaded.Body!.ToJsonString().ShouldContain("WGT-0001");
    }

    [TestMethod]
    public void OmitsBodyForOperationsThatTakeNone()
    {
        var document = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 1, OperationId = "get_api_products_id", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["id"] = "7" }
        };

        document.ToJson().ShouldNotContain("\"body\"");
    }

    [TestMethod]
    public void SerializationIsStableSoDiffsStayReviewable()
    {
        var document = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 4, OperationId = "op", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["zebra"] = "1", ["alpha"] = "2" }
        };

        document.ToJson().ShouldBe(FixtureDocument.Parse(document.ToJson()).ToJson());
        document.ToJson().IndexOf("alpha", StringComparison.Ordinal)
                .ShouldBeLessThan(document.ToJson().IndexOf("zebra", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RejectsAFixtureWithoutMeta()
    {
        Should.Throw<FixtureFormatException>(() => FixtureDocument.Parse("""{"body":{}}"""));
    }

    [TestMethod]
    public void FileNameIsDerivedFromTheOperationKey()
    {
        FixtureDocument.FileNameFor("post_api_products").ShouldBe("post_api_products.json");
        FixtureDocument.FileNameFor("Stock_GetBySku").ShouldBe("Stock_GetBySku.json");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~FixtureDocumentTests"
```

Expected: FAIL — `The type or namespace name 'FixtureDocument' could not be found`.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InTest.Cli.Fixtures;

public sealed class FixtureFormatException(string message) : Exception(message);

public sealed class FixtureMeta
{
    public required int Tier { get; init; }
    public required string OperationId { get; init; }
    public required string GeneratedBy { get; init; }
}

/// <summary>
/// One fixture per operation: its path and query parameters, and its request body if it takes
/// one. Committed, hand-edited, and never overwritten by tooling once written.
/// </summary>
public sealed class FixtureDocument
{
    public required FixtureMeta Meta { get; init; }
    public SortedDictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
    public JsonNode? Body { get; set; }

    public static string FileNameFor(string operationKey) => operationKey + ".json";

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject
            {
                ["tier"] = Meta.Tier,
                ["operationId"] = Meta.OperationId,
                ["generatedBy"] = Meta.GeneratedBy
            }
        };

        if (Parameters.Count > 0)
        {
            var parameters = new JsonObject();
            foreach (var (key, value) in Parameters) parameters[key] = value;
            root["$parameters"] = parameters;
        }

        if (Body is not null) root["body"] = Body.DeepClone();

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static FixtureDocument Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FixtureFormatException($"Fixture is not valid JSON: {ex.Message}"); }

        if (root is not JsonObject obj) throw new FixtureFormatException("Fixture root must be a JSON object.");

        if (obj["$meta"] is not JsonObject meta)
            throw new FixtureFormatException("Fixture is missing its '$meta' block. Regenerate it with `intest fixtures repair`.");

        var document = new FixtureDocument
        {
            Meta = new FixtureMeta
            {
                Tier = meta["tier"]?.GetValue<int>() ?? 4,
                OperationId = meta["operationId"]?.GetValue<string>() ?? string.Empty,
                GeneratedBy = meta["generatedBy"]?.GetValue<string>() ?? "unknown"
            },
            Body = obj["body"]?.DeepClone()
        };

        if (obj["$parameters"] is JsonObject parameters)
        {
            foreach (var (key, value) in parameters)
                document.Parameters[key] = value?.GetValue<string>() ?? string.Empty;
        }

        return document;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass, then commit**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~FixtureDocumentTests"
git add src/InTest.Cli/Fixtures/FixtureDocument.cs tests/InTest.Cli.Tests/FixtureDocumentTests.cs
git commit -m "feat(cli): fixture document model"
```

Expected: `Passed! - Failed: 0, Passed: 5`.

---

## Task 2: Tier resolution

Implements §10's precedence. The tier is recorded so a reader knows how much to trust a fixture.

**Files:**
- Create: `src/InTest.Cli/Fixtures/FixtureComposer.cs`
- Test: `tests/InTest.Cli.Tests/FixtureComposerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using InTest.Cli.Fixtures;
using InTest.Cli.Spec;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class FixtureComposerTests
{
    private static async Task<FixtureDocument> ComposeAsync(string spec, string path, string method)
    {
        var loaded = await SpecLoader.LoadFromTextAsync(spec);
        return FixtureComposer.Compose(loaded.Document, path, method, "op_key", "intest 0.2.0");
    }

    private const string TierOne = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{"/p":{"post":{
        "requestBody":{"content":{"application/json":{
          "schema":{"type":"object","properties":{"sku":{"type":"string"}}},
          "example":{"sku":"REAL-0001"}}}},
        "responses":{"201":{"description":"ok"}}}}}
    }
    """;

    [TestMethod]
    public async Task Tier1UsesTheMediaTypeExampleVerbatim()
    {
        var fixture = await ComposeAsync(TierOne, "/p", "POST");
        fixture.Meta.Tier.ShouldBe(1);
        fixture.Body!["sku"]!.GetValue<string>().ShouldBe("REAL-0001");
    }

    [TestMethod]
    public async Task Tier2ComposesFromPerPropertyExamples()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"sku":{"type":"string","example":"EX-1"},"qty":{"type":"integer","example":5}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");
        fixture.Meta.Tier.ShouldBe(2);
        fixture.Body!["sku"]!.GetValue<string>().ShouldBe("EX-1");
        fixture.Body["qty"]!.GetValue<int>().ShouldBe(5);
    }

    [TestMethod]
    public async Task Tier3UsesDefaults()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"currency":{"type":"string","default":"GBP"}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");
        fixture.Meta.Tier.ShouldBe(3);
        fixture.Body!["currency"]!.GetValue<string>().ShouldBe("GBP");
    }

    [TestMethod]
    public async Task Tier4EmitsObviousSentinelsNeverPlausibleValues()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(4);
        // "string" or 0 would be schema-valid, so a permissive endpoint would accept them and
        // the suite would assert nothing while looking healthy. The sentinel must be obvious.
        fixture.Body!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
    }

    [TestMethod]
    public async Task ComposesNestedObjectsAndArrays()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{
                "dims":{"type":"object","properties":{"w":{"type":"number"}}},
                "lines":{"type":"array","items":{"type":"object","properties":{"sku":{"type":"string"}}}}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Body!["dims"]!["w"].ShouldNotBeNull();
        fixture.Body["lines"]!.AsArray().Count.ShouldBe(1, "one element is enough to show the shape");
        fixture.Body["lines"]![0]!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
    }

    [TestMethod]
    public async Task IncludesPathAndQueryParametersAsSentinels()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p/{id}":{"get":{
            "parameters":[
              {"name":"id","in":"path","required":true,"schema":{"type":"string"}},
              {"name":"page","in":"query","schema":{"type":"integer","example":2}},
              {"name":"X-Trace","in":"header","schema":{"type":"string"}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p/{id}", "GET");

        fixture.Parameters["id"].ShouldBe("TODO:id");
        fixture.Parameters["page"].ShouldBe("2", "a per-parameter example is a real value");
        fixture.Parameters.ShouldNotContainKey("X-Trace", "headers are not path or query parameters");
    }

    [TestMethod]
    public async Task OmitsBodyWhenTheOperationTakesNone()
    {
        const string spec = """
        {"openapi":"3.0.3","info":{"title":"T","version":"1"},
         "paths":{"/p":{"get":{"responses":{"200":{"description":"ok"}}}}}}
        """;

        (await ComposeAsync(spec, "/p", "GET")).Body.ShouldBeNull();
    }

    [TestMethod]
    public async Task TerminatesOnASelfReferencingSchema()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Node"}}}},
            "responses":{"201":{"description":"ok"}}}}},
          "components":{"schemas":{"Node":{"type":"object","properties":{
            "name":{"type":"string"},"child":{"$ref":"#/components/schemas/Node"}}}}}
        }
        """;

        var compose = ComposeAsync(spec, "/p", "POST");
        (await Task.WhenAny(compose, Task.Delay(TimeSpan.FromSeconds(10)))).ShouldBe(compose);
        (await compose).Body.ShouldNotBeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~FixtureComposerTests"
```

Expected: FAIL — `The type or namespace name 'FixtureComposer' could not be found`.

- [ ] **Step 3: Write the implementation**

Key requirements the tests encode, restated so the implementation is not guessed at:

- Tier is the **highest-quality source used anywhere** in the body: if any property fell through to a `TODO:` sentinel, the fixture is tier 4 regardless of how many properties had examples.
- Recursion into `$ref` must track visited schema names and stop on revisit, emitting `null` for the recursive property. Self-referencing schemas are common and inlining them does not terminate.
- Arrays get exactly one element — enough to show the shape, not so many that a human editing it despairs.
- Only `in: path` and `in: query` parameters are emitted. Headers are excluded: §9 notes empty headers are often dropped before reaching app code, and they are not part of the request line.
- Sentinels are `TODO:{propertyName}` for strings. For non-strings the sentinel cannot be a string without breaking the schema, so emit the type's zero value **and** record the property in `$meta` so validation still flags it — see Task 7.

```csharp
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace InTest.Cli.Fixtures;

public static class FixtureComposer
{
    private const string JsonMediaType = "application/json";

    public static FixtureDocument Compose(
        OpenApiDocument document, string path, string httpMethod, string operationKey, string generatedBy)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operation = document.Paths[path].Operations![new HttpMethod(httpMethod)];
        var tier = new TierTracker();

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in operation.Parameters ?? [])
        {
            if (parameter.In is not (ParameterLocation.Path or ParameterLocation.Query)) continue;

            parameters[parameter.Name!] = ParameterValue(parameter, tier);
        }

        JsonNode? body = null;
        if (operation.RequestBody?.Content?.TryGetValue(JsonMediaType, out var media) is true && media.Schema is not null)
            body = ComposeBody(media, tier);

        return new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = tier.Value, OperationId = operationKey, GeneratedBy = generatedBy },
            Parameters = parameters,
            Body = body
        };
    }

    // Implementation of ParameterValue, ComposeBody, ComposeFromSchema and TierTracker follows
    // the rules listed above. ComposeFromSchema takes a HashSet<string> of visited reference
    // ids and returns JsonValue.Create(null) when a reference repeats.
}
```

> **Implementer:** the four private helpers are deliberately left for you to write against the tests above rather than transcribed here — every behaviour they must exhibit is asserted. Do not add behaviour the tests do not require.

- [ ] **Step 4: Run tests to verify they pass, then commit**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~FixtureComposerTests"
git add src/InTest.Cli/Fixtures/FixtureComposer.cs tests/InTest.Cli.Tests/FixtureComposerTests.cs
git commit -m "feat(cli): four-tier fixture composition"
```

Expected: `Passed! - Failed: 0, Passed: 8`.

---

## Task 3: `fixtures repair`

The only command that writes under `fixtures/`, owning creation, sentinel addition and stale flagging (§10).

**Files:**
- Create: `src/InTest.Cli/Fixtures/FixtureDrift.cs`, `src/InTest.Cli/Commands/FixturesRepairCommand.cs`
- Test: `tests/InTest.Cli.Tests/FixturesRepairCommandTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using InTest.Cli.Commands;
using InTest.Cli.Fixtures;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
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
            """"required":["sku"],"properties":{"sku":{"type":"string"}}""",
            """"required":["sku","name"],"properties":{"sku":{"type":"string"},"name":{"type":"string"}}"""));

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["name"]!.GetValue<string>().ShouldBe("TODO:name");
    }

    [TestMethod]
    public async Task FlagsAPropertyThatLeftTheSchemaWithoutDeletingIt()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        var document = FixtureDocument.Parse(File.ReadAllText(FixturePath));
        document.Body!["legacyRef"] = "kept-by-hand";
        File.WriteAllText(FixturePath, document.ToJson());

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["legacyRef"].ShouldNotBeNull(
            "a stale property is reported, never silently deleted — it may be deliberate");
    }

    [TestMethod]
    public async Task NeverWritesOutsideFixtures()
    {
        var before = Directory.GetFiles(_root, "*", SearchOption.TopDirectoryOnly)
                              .ToDictionary(f => f, File.GetLastWriteTimeUtc);

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        foreach (var (file, written) in before)
            File.GetLastWriteTimeUtc(file).ShouldBe(written, $"{Path.GetFileName(file)} must not be touched");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~FixturesRepairCommandTests"
```

Expected: FAIL — `The type or namespace name 'FixturesRepairCommand' could not be found`.

- [ ] **Step 3: Implement `FixtureDrift` and `FixturesRepairCommand`**

`FixtureDrift.Compare(existing, composed)` returns three lists: `MissingProperties` (in composed, absent from existing), `StaleProperties` (in existing, absent from composed), and `MissingParameters`. Repair merges the first and third into the existing document, leaves values it did not create untouched, and reports the second.

Exit codes per §5: `0` including nothing to repair, `2` on a tool error.

- [ ] **Step 4: Wire into `Program.cs`, run tests, commit**

```csharp
var fixtures = new Command("fixtures", "Fixture maintenance.");
var repair = new Command("repair", "Create missing fixtures and add sentinels for new required properties.");
repair.Options.Add(projectOption);
repair.SetAction((parseResult, cancellationToken) =>
    FixturesRepairCommand.RunAsync(parseResult.GetValue(projectOption)!, cancellationToken));
fixtures.Subcommands.Add(repair);
root.Subcommands.Add(fixtures);
```

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~FixturesRepairCommandTests"
git add src/InTest.Cli/Fixtures src/InTest.Cli/Commands src/InTest.Cli/Program.cs tests/InTest.Cli.Tests/FixturesRepairCommandTests.cs
git commit -m "feat(cli): fixtures repair owning creation, sentinels and stale flagging"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

---

## Task 4: `generate` reports drift and stays read-only

**Files:**
- Modify: `src/InTest.Cli/Commands/GenerateCommand.cs`
- Test: `tests/InTest.Cli.Tests/GenerateDriftTests.cs`

- [ ] **Step 1: Write the failing tests**

Assert all four behaviours: a missing fixture is reported as drift and `generate` exits `1`; the message names the operation and says `Run 'intest fixtures repair'`; **nothing is created under `fixtures/`**; and with every fixture resolved, `generate` exits `0`.

```csharp
[TestMethod]
public async Task ReportsAMissingFixtureAsDriftAndWritesNothing()
{
    var exitCode = await GenerateCommand.RunAsync(_root, CancellationToken.None);

    exitCode.ShouldBe(1);
    Directory.Exists(Path.Combine(_root, "fixtures")).ShouldBeFalse(
        "generate is read-only under fixtures/ — that is what keeps --check a pure comparison");
}
```

- [ ] **Step 2: Run to verify failure, implement, re-run, commit**

Expected after implementation: `Passed! - Failed: 0, Passed: 4`.

```bash
git commit -m "feat(cli): report fixture drift from generate without writing fixtures"
```

---

## Task 5: `FixtureStore` — loading and overlays

**Files:**
- Create: `src/InTest.Runtime/Neutral/FixtureStore.cs`
- Test: `tests/InTest.Runtime.Tests/FixtureStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: loads every `fixtures/*.json`; deep-merges `fixtures/{profile}/x.json` over the base with the environment winning; a nested object merges per property rather than replacing wholesale; an overlay for an operation with no base fixture is an error naming the file; a malformed fixture reports its filename rather than a bare `JsonException`.

```csharp
[TestMethod]
public void OverlayMergesPerPropertyRatherThanReplacingTheObject()
{
    WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
                        "body":{"a":1,"nested":{"x":1,"y":2}}}""");
    WriteOverlay("qa", "op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
                                 "body":{"nested":{"y":99}}}""");

    var store = FixtureStore.Load(_root, "qa");
    var body = store.Get("op").Body!;

    body["a"]!.GetValue<int>().ShouldBe(1, "untouched base properties survive");
    body["nested"]!["x"]!.GetValue<int>().ShouldBe(1, "sibling properties survive a nested merge");
    body["nested"]!["y"]!.GetValue<int>().ShouldBe(99, "the environment wins");
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(runtime): fixture loading with environment overlays"
```

---

## Task 6: Token resolution

**Files:**
- Create: `src/InTest.Runtime/Neutral/TokenResolver.cs`
- Test: `tests/InTest.Runtime.Tests/TokenResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

Per §10's resolution-timing table. Cover: `{{config:Orders:ApiKey}}` reads configuration; `{{secret:…}}` behaves identically but is never echoed in any message; `{{runId}}` is the run id and is identical across two resolutions; `{{utcNow}}` differs between resolutions (per request, not cached); an unknown token fails naming the token and listing the supported ones; `{{fixture:…}}` fails with "not supported until v1-b" rather than being left as literal text; a missing config key fails naming the key; a value containing no token is returned unchanged.

```csharp
[TestMethod]
public void SecretValuesNeverAppearInAnErrorMessage()
{
    var resolver = Resolver(("Orders:ApiKey", "super-secret-value"));

    var ex = Should.Throw<FixtureResolutionException>(
        () => resolver.Resolve("{{secret:Orders:Missing}}", "create-order.json"));

    ex.Message.ShouldNotContain("super-secret-value");
    ex.Message.ShouldContain("Orders:Missing");
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(runtime): fixture token resolution"
```

---

## Task 7: Aggregated validation, per-operation failure

The decision recorded above: one report, but only affected operations fail.

**Files:**
- Create: `src/InTest.Runtime/Neutral/FixtureValidation.cs`
- Modify: `src/InTest.Runtime/MSTest/TestHost.cs`, `src/InTest.Runtime/MSTest/ApiTestBase.cs`
- Test: `tests/InTest.Runtime.Tests/FixtureValidationTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void ReportsEveryProblemAcrossEveryFixtureInOneMessage()
{
    var report = Validate(
        ("create-order", new[] { "customerId", "items[0].sku" }),
        ("update-order", new[] { "shippingMethod" }));

    // N identical per-test failures teach nothing. One message with every file and property
    // is the whole value of validating at startup.
    report.Message.ShouldContain("3 problems");
    report.Message.ShouldContain("create-order");
    report.Message.ShouldContain("items[0].sku");
    report.Message.ShouldContain("update-order");
}

[TestMethod]
public void OnlyOperationsWithUnresolvedFixturesAreBlocked()
{
    var report = Validate(("create-order", new[] { "customerId" }));

    report.IsBlocked("create-order").ShouldBeTrue();
    report.IsBlocked("get-order").ShouldBeFalse(
        "an unrelated operation must not be failed by someone else's unfilled sentinel");
}

[TestMethod]
public void BlockedOperationsFailWithTheirOwnFileAndProperty()
{
    var report = Validate(("create-order", new[] { "customerId" }));

    var ex = Should.Throw<FixtureUnresolvedException>(() => report.ThrowIfBlocked("create-order"));
    ex.Message.ShouldContain("create-order.json");
    ex.Message.ShouldContain("customerId");
}
```

- [ ] **Step 2: Implement**

`TestHost` builds the report at `AssemblyInitialize` and writes the full message to `TestContext` **once**, so it appears in the `.trx` and the CI summary even though it does not abort. `ApiTestBase` exposes `RequireFixture(operationKey)`, which generated tests call before building a request; it throws `FixtureUnresolvedException` for blocked operations only.

- [ ] **Step 3–4: Run, commit**

```bash
git commit -m "feat(runtime): aggregated fixture validation with per-operation blocking"
```

---

## Task 8: Templates emit bodies and fixture-sourced parameters

**Files:**
- Modify: `src/InTest.Cli/Rendering/Templates/mstest-class.scriban`, `src/InTest.Cli/Rendering/TemplateRenderer.cs`
- Delete: `src/InTest.Runtime/Neutral/TestData.cs`
- Test: `tests/InTest.Cli.Tests/TemplateRendererTests.cs` (extend), `tests/InTest.Golden.Tests/` (regenerate golden)

- [ ] **Step 1: Extend the renderer tests**

A POST operation must render a `StringContent` body from the fixture with `application/json`; a GET with a path parameter must take its value from the fixture rather than `TestData`; every generated method must call `RequireFixture` before building its request; and no generated file may still reference `TestData`.

- [ ] **Step 2: Update the template, delete `TestData`, regenerate the golden file**

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~GoldenFileTests"
dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~GoldenFileTests"
```

Read the regenerated golden file before committing. It locks in whatever it is handed.

- [ ] **Step 3: Verify no stray blank lines survived the template change**

`EmitsNoStrayBlankLines` already guards this and must still pass — the template now has more conditional blocks, which is exactly where whitespace control breaks.

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(cli): emit request bodies and fixture-sourced parameters"
```

---

## Task 9: Amend §10 and the walkthrough

Code and spec must not drift. Two documented behaviours changed.

- [ ] **Step 1: Amend §10 — validation blocks operations, not the run**

Record the decision *and* the reasoning against the alternative, in the style the spec already uses: the aggregated report is unchanged and is the valuable half; failing unaffected operations punishes tests that would pass; and this does not reopen "no skip-flags, no silent green" because nothing is skipped and nothing goes quietly green.

- [ ] **Step 2: Amend §10 — parameters live in fixtures**

Document the file shape including `$parameters`, and state that `{{fixture:…}}` is v1-b and fails loudly until then.

- [ ] **Step 3: Update `docs/getting-started.md` Phase 5**

It currently describes fixtures as designed-but-unbuilt. Phase 5 becomes real; the status banner loses fixtures from its "not yet built" list.

- [ ] **Step 4: Commit**

```bash
git commit -m "docs: fixtures are built; validation blocks operations rather than the run"
```

---

## Task 10: Acceptance run against the samples

Per the standing decision, every phase ends by regenerating against `samples/` and updating the acceptance record.

- [ ] **Step 1: Regenerate all three sample suites**

```bash
dotnet build samples/Catalog.Api samples/Orders.Api samples/Inventory.Api
# for each: intest init (fresh temp project) → intest generate → intest fixtures repair
```

- [ ] **Step 2: Fill the sentinels using seeded data**

`samples/README.md` lists the fixed GUIDs. The point of this step is to feel how much work a real adopter faces — record how many sentinels needed filling per API.

- [ ] **Step 3: Run Catalog live and record the result**

```bash
dotnet run --project samples/Catalog.Api &   # http://localhost:5081
dotnet test <generated Catalog suite>
```

**Expected: 9 of 9 passing.** The three v0 failures were `POST`/`PUT` returning 415 for want of a body; if they do not turn green, that is the finding.

- [ ] **Step 4: Run Orders and Inventory too**

Orders needs `samples/Identity.Server` running. Auth tests are not generated until v1-c, so Orders exercises bodies under a bearer token, not the 401/403 paths.

- [ ] **Step 5: Update `docs/v0-acceptance.md` into a living record**

Rename the heading to cover v0 **and** v1-a, add a v1-a results section, record every new defect in the existing F-numbered style, and close or carry forward the "Not covered" list.

- [ ] **Step 6: Commit**

```bash
git commit -m "docs: v1-a acceptance run against the sample APIs"
```

---

## Self-review

**Spec coverage.** §10 is covered end to end: four-tier precedence (Task 2), `$meta` (Task 1), no review flag with sentinels that fail (Tasks 2, 7), fail-on-the-fixture-before-the-request (Task 7 via `RequireFixture`), aggregated validation (Task 7), runtime tokens and their resolution timing (Task 6), environment overlays (Task 5), drift reported by `generate` and mutated only by `repair` (Tasks 3, 4), and `repair` never overwriting (Task 3).

**Deliberately deferred, each with a reason.** `{{fixture:…}}` needs `IAssemblyFixture` (v1-b) and fails loudly meanwhile. `fixtures promote` and the spec-example percentage are v1-f. Credential heuristics on literal fixture values are v1-f, with `{{secret:}}` available from Task 6. Non-JSON request bodies stay out of v1 entirely.

**Type consistency.** `FixtureDocument.Parameters` is `SortedDictionary<string, string>` in Task 1 and consumed as such in Tasks 2, 5 and 8. `FixtureComposer.Compose` returns `FixtureDocument` and is called by both `FixturesRepairCommand` (Task 3) and `GenerateCommand`'s drift check (Task 4). `FixtureStore.Get(operationKey)` returns `FixtureDocument` in Task 5 and is used in Tasks 7 and 8. Operation keys are the same strings `OperationKey.Resolve` produces in v0 — synthesized ones included, which is most of the sample corpus.

**One risk worth stating.** Task 2's non-string sentinel problem has no clean answer: `"TODO:price"` is not a valid number, so a schema-valid placeholder must be a real number, and a real number is indistinguishable from a deliberate value. The plan records the property in `$meta` so validation can still flag it, but an implementer who skips that produces exactly the plausible-but-fake value §10 calls the genuinely dangerous alternative. Task 7's tests must cover a numeric sentinel, not only a string one.
