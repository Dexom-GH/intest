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

    private const string NeedsFixtureSpec = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{
        "/body":{"post":{
          "requestBody":{"content":{"application/json":{"schema":{"type":"object"}}}},
          "responses":{"201":{"description":"ok"}}}},
        "/path/{id}":{"get":{
          "parameters":[{"name":"id","in":"path","required":true,"schema":{"type":"string"}}],
          "responses":{"200":{"description":"ok"}}}},
        "/query":{"get":{
          "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer","example":2}}],
          "responses":{"200":{"description":"ok"}}}},
        "/nothing":{"get":{"responses":{"200":{"description":"ok"}}}},
        "/body-no-schema":{"post":{
          "requestBody":{"content":{"application/json":{}}},
          "responses":{"201":{"description":"ok"}}}}
      }
    }
    """;

    [TestMethod]
    public async Task NeedsFixtureAgreesExactlyWithWhatComposeActuallyProduces()
    {
        // Pins the two sides together: NeedsFixture must say yes precisely when Compose would
        // write something a caller could observe (a body, or a non-empty $parameters block) —
        // never a hardcoded true/false per case, since the point is the equivalence itself.
        var loaded = await SpecLoader.LoadFromTextAsync(NeedsFixtureSpec);
        var document = loaded.Document;

        var cases = new (string Path, string Method)[]
        {
            ("/body", "POST"),
            ("/path/{id}", "GET"),
            ("/query", "GET"),
            ("/nothing", "GET"),
            ("/body-no-schema", "POST"),
        };

        foreach (var (path, method) in cases)
        {
            var operation = document.Paths[path].Operations![new HttpMethod(method)];
            var fixture = FixtureComposer.Compose(document, path, method, "op_key", "intest 0.2.0");
            var composeProducesSomething = fixture.Body is not null || fixture.Parameters.Count > 0;

            FixtureComposer.NeedsFixture(operation).ShouldBe(composeProducesSomething, $"{method} {path}");
        }
    }
}
