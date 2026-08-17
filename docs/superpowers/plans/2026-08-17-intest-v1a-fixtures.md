# InTest v1-a — Fixtures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Operations with request bodies and path parameters generate tests that can actually send them, sourced from committed fixture files that a human owns.

**Architecture:** `intest fixtures repair` composes a fixture per operation using §10's four-tier precedence and writes it under `fixtures/`. `generate` stays read-only there and reports drift. At runtime `FixtureStore` loads each fixture, deep-merges any environment overlay, resolves `{{…}}` tokens, and validates everything once at `AssemblyInitialize`.

**Tech Stack:** Unchanged from v0 — net10.0 · Microsoft.OpenApi 3.10.0 · NJsonSchema 11.6.1 · Scriban 7.2.6 · MSTest 4.3.3.

**Spec:** [`../specs/2026-08-16-intest-api-test-generator-design.md`](../specs/2026-08-16-intest-api-test-generator-design.md), §10 primarily.

**Prerequisite:** v0 complete and merged (`a8add69`). 123 tests passing.

---

## Decisions this plan encodes

Four, of which two depart from the spec as written. Both departures get a task that amends the spec, so code and document do not drift.

**1. Path and query parameters live in fixtures, not `TestData`.** v0's `TestData.Require` throws until someone calls `TestData.Set` in `TestStartup`. That was a placeholder. One fixture per operation now carries both its parameters and its body, so there is one mechanism and one place to look, and startup validation covers parameters too. `TestData` is deleted.

**Only `required: true` parameters get a sentinel.** An optional parameter is omitted from
`$parameters` entirely and is not sent. This is not a detail — getting it wrong regresses the
suite. `GET /api/products` in `samples/Catalog.Api` declares five optional query parameters
(`name`, `minPrice`, `category`, `page`, `pageSize`, all `required: false`) and passes today; a
sentinel for each would block an operation that currently works, and Task 10 would finish with
fewer passing tests than v0 achieved.

The rule, stated once:

| Parameter | In `$parameters`? | Sent? |
|---|---|---|
| `required: true` | Yes, as `TODO:{name}` until filled | Yes |
| Optional with an `example` or `default` | Yes, as that real value — never a sentinel | Yes |
| Optional with neither | No | No |

Query parameters that are present are appended as a query string; path parameters substitute
into the path template. Both are the template's job (Task 8).

**2. A bad fixture fails its own operation, not the whole run.** §10 currently specifies that validation aborts everything. This plan reports *all* problems in one aggregated message at startup — that part is unchanged and is the valuable half — but fails only the operations whose fixtures are unresolved. On the current sample corpus the spec's behaviour would turn 6 passing Catalog tests red for a problem in 3 unrelated ones.

This does **not** reopen "no skip-flags, no silent green" (§1 principle 5). Nothing is skipped and nothing goes quietly green: affected tests fail loudly with a message naming the file and property. Task 9 amends §10.

**3. Every sentinel is a string, whatever the declared type.** A numeric property gets
`"price": "TODO:price"`, not `0`.

The plan originally emitted the type's zero value for non-strings and recorded the property
elsewhere so validation could still flag it. That created a lifecycle with no exit: `repair`
never overwrites, and it cannot distinguish a human's deliberate `19.99` from the `0` it wrote
itself, so the flag — and the block — would persist forever.

A string sentinel has none of that. It is unmistakably unfilled, it is the same mechanism for
every type, and replacing it with `19.99` clears the block by construction with nothing to
reconcile. The cost is that the fixture is not schema-valid until filled, which is harmless:
a blocked operation never sends its body. Nothing in v1-a validates a fixture body against the
request schema — validation looks for sentinels, not conformance.

This removes the risk the first draft of this plan flagged as having no clean answer.

**4. `{{fixture:…}}` is out of scope.** It resolves after `IAssemblyFixture` implementations complete, and those are v1-b. v1-a ships `{{config:…}}`, `{{secret:…}}`, `{{runId}}` and `{{utcNow}}`. A `{{fixture:…}}` token encountered in v1-a fails validation with "not supported until v1-b" rather than being silently left as literal text.

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
    "price": "TODO:price",
    "categoryId": "{{config:TestData:CategoryId}}"
  }
}
```

`$meta` records provenance. `$parameters` carries **required** path and query values by name,
plus optional ones that have a real value (decision 1). `body` is absent for operations that
take no request body. Note `price` is a **string** sentinel although the schema declares a
number — decision 3.

`generatedBy` is `"intest " + <the CLI assembly's informational version>`, not a literal.
`InitCommand` currently hardcodes `0.1.0` in two places (`intest.json` and
`.config/dotnet-tools.json`); Task 4a makes all three read one source, so a fixture cannot
claim a version the tool does not have.

Filenames are the operation key verbatim. A key that cannot be a filename is **rejected**, not
mangled — see Task 1.

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
    [DataRow("post_api_products", DisplayName = "synthesized key")]
    [DataRow("Stock_GetBySku", DisplayName = "NSwag {Controller}_{Action} key")]
    [DataRow("getOrderById", DisplayName = "hand-written camelCase operationId")]
    public void AcceptsAnOperationKeyThatIsAlreadyFileNameSafe(string key)
    {
        FixtureDocument.FileNameFor(key).ShouldBe(key + ".json");
    }

    [TestMethod]
    [DataRow("Orders/Create", "/", DisplayName = "path separator")]
    [DataRow("Orders?Create", "?", DisplayName = "wildcard character")]
    [DataRow("orders:create", ":", DisplayName = "stream separator")]
    [DataRow("Orders\\Create", "\\", DisplayName = "backslash — invalid on Windows, legal on Unix")]
    public void ReportsAnOperationKeyThatCannotBeAFileName(string key, string offending)
    {
        // Try-pattern, not an exception: an unusable operationId is one operation InTest cannot
        // serve, not a reason to abandon the other 147 in the document. The caller records a
        // skip and continues — see Task 2a.
        FixtureDocument.TryValidateOperationKey(key, out var reason).ShouldBeFalse();

        reason.ShouldContain(key);
        reason.ShouldContain(offending);
        reason.ShouldContain("operationId");
    }

    [TestMethod]
    public void RejectsBackslashOnEveryPlatformNotJustWindows()
    {
        // Path.GetInvalidFileNameChars() is platform-specific: 41 characters on Windows
        // (verified), but only NUL and '/' on Unix. Delegating to it would accept
        // Orders\Create on Linux and write a file literally named Orders\Create.json, so the
        // explicit list carries the separators rather than trusting the framework's per-OS answer.
        FixtureDocument.TryValidateOperationKey("Orders\\Create", out _).ShouldBeFalse();
    }

    [TestMethod]
    public void ReportsAWindowsReservedDeviceName()
    {
        FixtureDocument.TryValidateOperationKey("CON", out var reason).ShouldBeFalse();
        reason.ShouldContain("reserved");
    }

    [TestMethod]
    public void FileNameForStillThrowsBecauseCallersMustValidateFirst()
    {
        // FileNameFor is only reached for keys the plan already accepted. Throwing here is an
        // invariant violation, not flow control — the flow-control path is TryValidateOperationKey.
        Should.Throw<FixtureFormatException>(() => FixtureDocument.FileNameFor("Orders/Create"));
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

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
         "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>
    /// Operation keys become fixture filenames. Synthesized keys are safe by construction, but
    /// a declared operationId is used verbatim and OpenAPI permits any string.
    /// <para>
    /// Returns false with a reason rather than throwing, because an unusable operationId is one
    /// operation InTest cannot serve — not grounds for abandoning a whole document. The caller
    /// records a skip and carries on, the same route non-JSON request bodies already take.
    /// </para>
    /// </summary>
    public static bool TryValidateOperationKey(string operationKey, out string reason)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
        {
            reason = "operationId is empty.";
            return false;
        }

        // Explicit, not Path.GetInvalidFileNameChars(): that returns 41 characters on Windows
        // but only NUL and '/' on Unix, so trusting it would make generation depend on the
        // developer's operating system.
        char[] separators = ['/', '\\', '?', '*', ':', '"', '<', '>', '|'];
        var invalid = separators.Concat(Path.GetInvalidFileNameChars()).ToHashSet();

        var offending = operationKey.Where(invalid.Contains).Distinct().ToArray();
        if (offending.Length > 0)
        {
            reason = $"operationId '{operationKey}' cannot be a fixture filename: it contains " +
                     $"{string.Join(", ", offending.Select(c => $"'{c}'"))}. Change the operationId " +
                     "in the OpenAPI document — it also names generated client methods, so a " +
                     "filename-safe value is worth having anyway.";
            return false;
        }

        if (ReservedNames.Contains(operationKey, StringComparer.OrdinalIgnoreCase))
        {
            reason = $"operationId '{operationKey}' is a reserved device name on Windows and cannot " +
                     "be a filename. Change the operationId in the OpenAPI document.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Only valid for a key that has already passed <see cref="TryValidateOperationKey"/>.
    /// Throws otherwise, because reaching here with an unusable key means a caller skipped
    /// validation — an invariant violation rather than a condition to handle.
    /// </summary>
    public static string FileNameFor(string operationKey)
    {
        if (!TryValidateOperationKey(operationKey, out var reason))
            throw new FixtureFormatException(reason);

        return operationKey + ".json";
    }

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

Expected: `Passed! - Failed: 0, Passed: 14` — 9 test methods; one carries 3 `DataRow`s and one carries 4, so 7 plain results plus 7 rows.

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
    public async Task OnlySentinelsRequiredParameters()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p/{id}":{"get":{
            "parameters":[
              {"name":"id","in":"path","required":true,"schema":{"type":"string"}},
              {"name":"page","in":"query","schema":{"type":"integer","example":2}},
              {"name":"sort","in":"query","schema":{"type":"string","default":"name"}},
              {"name":"filter","in":"query","schema":{"type":"string"}},
              {"name":"X-Trace","in":"header","schema":{"type":"string"}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p/{id}", "GET");

        fixture.Parameters["id"].ShouldBe("TODO:id", "required parameters must be supplied");
        fixture.Parameters["page"].ShouldBe("2", "an example is a real value, not a sentinel");
        fixture.Parameters["sort"].ShouldBe("name", "a default is a real value too");

        // The regression this prevents: Catalog's GET /api/products declares five optional
        // query parameters and passes today. Sentinelling them would block a working operation
        // and leave Task 10 below the 6 passing tests v0 already achieved.
        fixture.Parameters.ShouldNotContainKey("filter", "an optional parameter with no value is omitted");
        fixture.Parameters.ShouldNotContainKey("X-Trace", "headers are not path or query parameters");
    }

    [TestMethod]
    public async Task SentinelsAreStringsRegardlessOfDeclaredType()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "required":["price","active"],"properties":{
                "price":{"type":"number"},"active":{"type":"boolean"}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        // A zero would be schema-valid and indistinguishable from a deliberate value, leaving
        // repair no way to know it was never filled in. See decision 3.
        fixture.Body!["price"]!.GetValue<string>().ShouldBe("TODO:price");
        fixture.Body["active"]!.GetValue<string>().ShouldBe("TODO:active");
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
    public async Task StopsAtARepeatedSchemaReference()
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

        var fixture = await ComposeAsync(spec, "/p", "POST");

        // Asserted on observable output, not by racing a timeout. Compose is synchronous, so
        // non-termination stack-overflows or hangs the test host before any timeout could be
        // observed — a timeout guard here passes only when the bug is absent, which is not the
        // case it exists for.
        fixture.Body!["name"]!.GetValue<string>().ShouldBe("TODO:name");
        fixture.Body["child"].ShouldBeNull("a repeated reference emits null and stops");
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
- **Sentinels are always the string `TODO:{propertyName}`**, whatever the schema declares (decision 3). Never emit a typed zero value.
- **Only `required: true` parameters are sentinelled** (decision 1). An optional parameter appears only when the spec gives it an `example` or `default`; otherwise it is omitted entirely.
- Tier reflects the worst source used anywhere: one `TODO:` makes the whole fixture tier 4.

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

Expected: `Passed! - Failed: 0, Passed: 9`.

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

`FixtureDrift.Compare(existing, composed)` returns three lists: `MissingProperties` (in composed, absent from existing), `StaleProperties` (in existing, absent from composed), and `MissingParameters`. Repair merges the first and third into the existing document, leaves values it did not create untouched, and **prints** the second.

**Repair iterates `TestPlanBuilder.Build(...)`, not the raw document.** That type already owns
which operations exist — including skips for non-JSON request bodies and operations with no 2xx
response. Iterating anything else makes `repair` and `generate`'s drift check disagree about the
operation set, and creates fixtures for operations no generated test will ever load.

`RunAsync(projectRoot, cancellationToken, TextWriter? report = null)` — the optional writer
defaults to `Console.Out`. Tests pass a `StringWriter` and assert the drift report directly,
rather than capturing `Console` globally in a test assembly where that is shared process state.
`GenerateCommand` gains the same parameter for the same reason (Task 4).

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

Expected: `Passed! - Failed: 0, Passed: 7`.

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

- [ ] **Step 2: Reconcile the two v0 tests this breaks**

`generate` returning `1` when fixtures are missing breaks two existing tests. Both must be
updated in this task, not discovered later:

| Test | Why it breaks | Reconciliation |
|---|---|---|
| `CompileVerificationTests.cs:65` — `ShouldBe(0)` | Its spec `Specs/orders.json` has `GET /orders/{id}` with a **required** path parameter, so under decision 1 that operation needs a fixture | Call `FixturesRepairCommand.RunAsync` in the test's setup, before `GenerateCommand`. Note this test never calls `init` — it hand-writes `intest.json` and the `.csproj` (`CompileVerificationTests.cs:24-46`) — and `repair` needs only `intest.json` plus the spec, so calling it directly works. Keeps the test asserting what it is named for: that generated code compiles |
| `GeneratedSuiteExecutionTests.cs:98,118` | Its spec is a bare `GET` with no body and no parameters, so it survives — **but** it has no `fixtures/` directory at all | Add the same `repair` call for realism, and add the `FixtureStore` case in Task 5 below so an absent directory is proven harmless rather than assumed so |

- [ ] **Step 3: Run to verify failure, implement, re-run, commit**

Expected after implementation: `Passed! - Failed: 0, Passed: 4`, and the full suite still green.

```bash
dotnet test
git commit -m "feat(cli): report fixture drift from generate without writing fixtures"
```

---

## Task 4a: Scaffold changes — fixtures must reach the output directory

**This is F1 again**, and it is the task most likely to be skipped. `FixtureStore` loads at
runtime, meaning from `AppContext.BaseDirectory`. `InitCommand` copies `spec-schemas.json`,
`spec-paths.json` and `appsettings*.json` to the output directory — and nothing else. Without
this task every fixture is invisible at runtime, and it surfaces at Task 10, live, after nine
tasks of green.

The v0 acceptance record states the lesson: *compile verification proves generated code builds,
never that it runs.* This task is that lesson applied before the fact rather than after.

**Files:**
- Modify: `src/InTest.Cli/Commands/InitCommand.cs`
- Test: `tests/InTest.Cli.Tests/InitCommandTests.cs`, `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void CsprojCopiesFixturesToTheOutputDirectory()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

    File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"))
        .ShouldContain("fixtures/**/*.json",
            "FixtureStore loads from AppContext.BaseDirectory — this is the F1 defect repeating");
}

[TestMethod]
public void TestStartupDoesNotReferenceTheDeletedTestDataType()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

    File.ReadAllText(Path.Combine(_root, "TestStartup.cs"))
        .ShouldNotContain("TestData", "Task 8 deletes it; a scaffold must not teach a dead API");
}
```

**The execution test's spec must first be given an operation that needs a fixture.**
`GeneratedSuiteExecutionTests` currently uses a single `GET /api/status` — no body, no
parameters — so under decision 1 it composes no fixture at all and there would be nothing to
assert. Asserting the csproj contains a content item proves only that a string is present.

Add a second operation to that spec:

```json
"/api/status/{id}": {
  "get": {
    "operationId": "getStatusById",
    "tags": ["Status"],
    "parameters": [
      { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
    ],
    "responses": {
      "200": { "description": "ok",
        "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Status" } } } }
    }
  }
}
```

and serve `/api/status/{anything}` from the stub. Then the only test that runs a generated suite
also becomes the test that proves a fixture reaches the output directory, is loaded, has its
sentinel filled, and produces a request that succeeds live — the whole chain, in the one place
that executes rather than compiles.

`GeneratedSuiteExecutionTests` setup gains an `intest fixtures repair` call between `generate`
and `build`, plus a step that replaces `TODO:id` with a value the stub accepts. That mirrors
exactly what an adopter does, and it means the F1 class of defect cannot recur silently.

- [ ] **Step 2: Run to verify failure, then implement**

Add to the scaffolded `.csproj`:

```xml
<Content Include="fixtures/**/*.json" CopyToOutputDirectory="PreserveNewest" />
```

Replace the `TestStartup.cs` comment — it currently tells developers to register
"path-parameter test data here" with a `TestData.Set(...)` example — with one pointing at
`fixtures/` and `intest fixtures repair`.

Have `intest.json`'s `intestVersion`, `.config/dotnet-tools.json`'s pinned version, and
`FixtureMeta.GeneratedBy` all read the CLI assembly's informational version instead of the two
hardcoded `0.1.0` literals.

- [ ] **Step 3: Run tests, then commit**

```bash
dotnet test tests/InTest.Cli.Tests tests/InTest.Golden.Tests
git commit -m "feat(cli): scaffold copies fixtures to output and drops the TestData example"
```

---

## Task 5: `FixtureStore` — loading and overlays

**Files:**
- Create: `src/InTest.Runtime/Neutral/FixtureStore.cs`
- Test: `tests/InTest.Runtime.Tests/FixtureStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: loads every `fixtures/*.json`; deep-merges `fixtures/{profile}/x.json` over the base with the environment winning; a nested object merges per property rather than replacing wholesale; an overlay for an operation with no base fixture is an error naming the file; a malformed fixture reports its filename rather than a bare `JsonException`; **and an absent `fixtures/` directory loads to an empty store rather than throwing** — `GeneratedSuiteExecutionTests` has no fixtures at all and must keep working.

```csharp
[TestMethod]
public void AnAbsentFixturesDirectoryIsAnEmptyStoreNotAnError()
{
    // A spec whose every operation is a parameterless GET needs no fixtures. That is the
    // shape GeneratedSuiteExecutionTests uses, so this must not throw.
    var store = FixtureStore.Load(Path.Combine(_root, "no-such-directory"), profile: null);

    store.Count.ShouldBe(0);
    Should.Throw<FixtureNotFoundException>(() => store.Get("anything"))
          .Message.ShouldContain("intest fixtures repair");
}
```

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

**`FixtureStore.Load(root, profile)` takes the directory that *contains* `fixtures/`, not
`fixtures/` itself** — so base fixtures are `{root}/fixtures/*.json` and overlays are
`{root}/fixtures/{profile}/*.json`. `TestHost` passes `AppContext.BaseDirectory`, which is why
Task 4a must copy `fixtures/**` there. Stating it here because "root" could mean either and the
wrong reading fails at runtime with an empty store rather than at compile time.

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

#### The interface, pinned

§10 makes `{{utcNow}}` per-request and uncached while `{{config:}}` is once-per-run and cached.
That needs two passes over the same fixture, so the store exposes both and generated code is
explicit about which it wants:

| Member | Returns | Used by |
|---|---|---|
| `FixtureStore.Get(key)` | The raw `FixtureDocument`, tokens **unresolved** | Startup validation (Task 7) — it inspects tokens, so it must not resolve them |
| `FixtureStore.ResolvedBody(key)` | A fresh `JsonNode` with every token resolved, **per call** | Generated tests (Task 8), once per request, so `{{utcNow}}` differs between them |
| `FixtureStore.ResolvedParameter(key, name)` | A resolved `string`, per call | Generated tests building the path and query string |

Cached tokens are resolved once at startup and reused; only `{{utcNow}}` is evaluated per call.
Without this split, either validation would resolve `{{config:}}` before configuration exists,
or every request would re-read configuration.

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
- Modify: `src/InTest.Runtime/Neutral/InTestUrl.cs` — **add `BuildQuery`**, which does not exist yet
- Delete: `src/InTest.Runtime/Neutral/TestData.cs`
- Test: `tests/InTest.Cli.Tests/TemplateRendererTests.cs` (extend), `tests/InTest.Runtime.Tests/InTestUrlTests.cs` (extend), `tests/InTest.Golden.Tests/` (regenerate golden)

- [ ] **Step 0: Add `InTestUrl.BuildQuery` with its own tests**

`InTestUrl` currently has `NormalizeBase`, `Build` and `EnsureNoPrefixDuplication`. The template
below emits a call to `BuildQuery`, so it must exist first. Percent-encoding is where this goes
wrong, and `Build` already has tests covering that concern for path segments — match them.

```csharp
[TestMethod]
public void BuildQuery_ReturnsEmptyForNoParameters()
{
    InTestUrl.BuildQuery(new Dictionary<string, string>()).ShouldBe(string.Empty);
}

[TestMethod]
public void BuildQuery_PrefixesWithQuestionMarkAndJoinsWithAmpersand()
{
    InTestUrl.BuildQuery(new Dictionary<string, string> { ["page"] = "2", ["sort"] = "name" })
             .ShouldBe("?page=2&sort=name");
}

[TestMethod]
public void BuildQuery_EscapesNamesAndValues()
{
    InTestUrl.BuildQuery(new Dictionary<string, string> { ["q"] = "a b&c=d" })
             .ShouldBe("?q=a%20b%26c%3Dd");
}

[TestMethod]
public void BuildQuery_IsOrderIndependentSoGeneratedUrlsAreStable()
{
    var forward = InTestUrl.BuildQuery(new Dictionary<string, string> { ["b"] = "1", ["a"] = "2" });
    var reverse = InTestUrl.BuildQuery(new Dictionary<string, string> { ["a"] = "2", ["b"] = "1" });
    forward.ShouldBe(reverse);
}
```

- [ ] **Step 1: Extend the renderer tests**

A POST operation must render a `StringContent` body from the fixture with `application/json`;
a GET with a **path** parameter must substitute it into the path template from the fixture
rather than `TestData`; a GET with **query** parameters present in the fixture must append them
as a percent-encoded query string, and must append nothing when the fixture has none (decision 1
omits optional parameters, so the common case is no query string at all); every generated method
must call `RequireFixture` before building its request; and no generated file may still
reference `TestData`.

```csharp
[TestMethod]
public void AppendsOnlyTheQueryParametersTheFixtureSupplies()
{
    var rendered = Render(PlanWithQueryParameters("page", "sort"));

    rendered.ShouldContain("InTestUrl.BuildQuery(");
    rendered.ShouldNotContain("?page=", "values come from the fixture at runtime, not the template");
}

[TestMethod]
public void EmitsNoQueryStringWhenThereAreNoQueryParameters()
{
    Render(Plan()).ShouldNotContain("BuildQuery");
}
```

This closes the other half of decision 1: sentinelling a parameter the generated code never
sends would block an operation for a value that could not have mattered.

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

- [ ] **Step 3: Update both documents that claim fixtures do not exist**

Three specific places, all currently false once this plan lands:

| File | What it says now |
|---|---|
| `README.md` status banner (lines 10–19) | Lists fixtures and `fixtures repair` under "Not yet built" |
| `README.md` "What day one actually looks like" (lines 57–62) | "At v0 there are no fixtures at all, so operations taking a request body generate a test that cannot send one and fails with 415" |
| `docs/getting-started.md` Phase 5 and its status banner | Describes fixtures as designed-but-unbuilt |

`survey`, `--check` and YAML stay on the not-yet-built list — this plan does not deliver them.

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

The seeded identifiers, so the implementer does not have to read the seed code:

| Sample | Entity | Value |
|---|---|---|
| Catalog | Product "Widget" | `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa` |
| Catalog | Product "Sparse" (all nullables null) | `bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb` |
| Catalog | Category "Hardware" (referenced — delete returns 409) | `11111111-1111-1111-1111-111111111111` |
| Catalog | Category "Software" | `22222222-2222-2222-2222-222222222222` |
| Catalog | Category "Deprecated" (unreferenced — delete returns 204) | `33333333-3333-3333-3333-333333333333` |
| Orders | Customer "Acme Ltd" | `cccccccc-cccc-cccc-cccc-cccccccccccc` |
| Orders | Order ORD-0001, status Placed (cancellable) | `dddddddd-dddd-dddd-dddd-dddddddddddd` |
| Orders | Order ORD-0002, status Shipped (cancel returns 409) | `eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee` |
| Inventory | Warehouse London `1`, Leeds `2`; stock rows `1` and `2` | integers, not GUIDs |

New products need a unique SKU matching `^[A-Z]{3}-[0-9]{4}$` — reusing `WGT-0001` returns 409
from a real unique index, which is a valid thing to assert but not what the 201 test wants.

**Record how many sentinels needed filling per API.** That number is the fixture workload a
real adopter faces, and it is the measurement `intest survey` will eventually predict.

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

**Review corrections folded in.** A review of the first draft found seven issues; all are
resolved above rather than left as notes.

The three that would have produced a failed acceptance run:

1. **Optional query parameters would have blocked green operations.** Composing a sentinel for
   every parameter would have blocked Catalog's `GET /api/products` — five optional query
   parameters, passing today — and finished Task 10 *below* v0's six. Decision 1 now sentinels
   only `required: true`, and Task 8 pins that the template sends exactly what the fixture
   carries.
2. **Numeric sentinels had no exit.** The first draft emitted a typed zero and flagged the
   property elsewhere; nothing could ever un-flag it, because `repair` cannot distinguish a
   human's `19.99` from the `0` it wrote. Decision 3 makes every sentinel a string, which
   removes the lifecycle entirely rather than managing it.
3. **Fixtures never reached the output directory** — F1 exactly, and it would have surfaced
   live at Task 10 after nine tasks of green. Task 4a now owns the scaffold.

Round two found Task 1 could not pass at all — its filename tests contradicted each other, its
doc comment described hash-suffixing that neither tests nor code implemented, and its character
list contained an unterminated escape that would not compile. Resolved by not sanitising: an
unusable operationId is **reported**, which needs no collision story. Round two also found Task
4a's proof asserted a fixture reached the output directory using a spec that composes no fixture,
and that `InTestUrl.BuildQuery` was asserted by Task 8 but created by no task.

Round three found the blast radius of that rejection was wrong. Throwing would abandon an entire
document for one bad operationId, where v0 already skips a single operation and records why —
hence Task 2a, and `TryValidateOperationKey` alongside `FileNameFor`. It also caught that fixing
the unterminated `'\'` had silently **removed** backslash from the invalid set rather than
escaping it, which would have made generation depend on the developer's operating system:
`Path.GetInvalidFileNameChars()` returns 41 characters on Windows (verified here) but only NUL
and `/` on Unix, so `Orders\Create` would be rejected on one and written as a literal filename
on the other.

The rest, corrected in place: Task 4 names the two v0 tests it breaks and how they reconcile;
Task 2's recursion test asserts observable output instead of racing a timeout it could never
reach against synchronous code; Task 6 pins raw-vs-resolved access so `{{utcNow}}` can be
per-call while `{{config:}}` is cached; Task 3 asserts stale properties are *reported* as well as
retained, and iterates the test plan so `repair` and `generate` cannot disagree about which
operations exist; both commands take an optional `TextWriter` so their reports are asserted
without capturing `Console` globally in a test assembly.

**One risk that remains.** `repair` merges by property name. A fixture whose body a human has
restructured — say wrapping fields in an envelope the spec later adopted — will have properties
added at the top level where the human put them nested. The tests pin "never overwrites", not
"merges intelligently", and intelligent merging of hand-edited JSON is not something this plan
attempts. If Task 10 shows it happening on real specs, that is a finding for v1-b, not a defect
to fix mid-plan.
