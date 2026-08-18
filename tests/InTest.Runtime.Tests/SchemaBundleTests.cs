using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class SchemaBundleTests
{
    private const string BundleJson = """
    {
      "definitions": {
        "Order": {
          "type": "object",
          "required": ["id", "quantity"],
          "properties": {
            "id": { "type": "string" },
            "quantity": { "type": "integer", "minimum": 1 },
            "notes": { "type": ["null", "string"] }
          }
        }
      }
    }
    """;

    private static SchemaBundle Bundle() => SchemaBundle.FromJson(BundleJson);

    [TestMethod]
    public void Validate_AcceptsAConformingPayload()
    {
        Bundle().Validate("Order", """{"id":"a","quantity":2,"notes":null}""").ShouldBeEmpty();
    }

    [TestMethod]
    [DataRow("""{"id":"a","quantity":0}""", "#/quantity", DisplayName = "below minimum")]
    [DataRow("""{"id":"a"}""", "#/quantity", DisplayName = "missing required")]
    [DataRow("""{"id":"a","quantity":2,"notes":5}""", "#/notes", DisplayName = "wrong type")]
    public void Validate_RejectsAndReportsThePath(string payload, string expectedPath)
    {
        var errors = Bundle().Validate("Order", payload);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Path == expectedPath);
    }

    [TestMethod]
    public void Validate_ThrowsForAnUnknownKey()
    {
        Should.Throw<KeyNotFoundException>(() => Bundle().Validate("Nope", "{}"));
    }

    [TestMethod]
    public void Validate_ReportsMalformedJsonRatherThanThrowing()
    {
        Bundle().Validate("Order", "not json").ShouldContain(e => e.Kind == "MalformedJson");
    }
}
