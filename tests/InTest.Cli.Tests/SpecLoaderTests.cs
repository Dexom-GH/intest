using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class SpecLoaderTests
{
    private const string MinimalSpec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "get": {
            "tags": ["Orders"],
            "responses": { "200": { "description": "ok" } }
          }
        }
      }
    }
    """;

    [TestMethod]
    public async Task LoadsAValidDocument()
    {
        var result = await SpecLoader.LoadFromTextAsync(MinimalSpec);
        result.Document.Info.Title.ShouldBe("Orders");
    }

    [TestMethod]
    [DataRow("3.0.3", DisplayName = "OpenAPI 3.0")]
    [DataRow("3.1.0", DisplayName = "OpenAPI 3.1")]
    public async Task AcceptsSupportedVersions(string version)
    {
        var result = await SpecLoader.LoadFromTextAsync(MinimalSpec.Replace("3.0.3", version));
        result.Document.Paths.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task ThrowsSpecLoadExceptionOnMalformedInput()
    {
        var ex = await Should.ThrowAsync<SpecLoadException>(() => SpecLoader.LoadFromTextAsync("{ not json"));
        ex.Message.ShouldContain("could not be parsed");
    }

    [TestMethod]
    public async Task ThrowsWhenTheDocumentHasNoPaths()
    {
        var empty = """{ "openapi": "3.0.3", "info": { "title": "T", "version": "1" }, "paths": {} }""";
        var ex = await Should.ThrowAsync<SpecLoadException>(() => SpecLoader.LoadFromTextAsync(empty));
        ex.Message.ShouldContain("no operations");
    }
}
