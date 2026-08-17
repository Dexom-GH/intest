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
    public void TheExplicitSeparatorListCarriesEveryCharacterUnixWouldOtherwiseAllow()
    {
        // Path.GetInvalidFileNameChars() returns 41 characters on Windows but only NUL and
        // '/' on Unix. Asserting through TryValidateOperationKey would pass on Windows even
        // if this list were empty, because the framework list masks it — so assert the list.
        FixtureDocument.InvalidOperationKeyCharacters.ShouldBe(
            new[] { '/', '\\', '?', '*', ':', '"', '<', '>', '|' }, ignoreOrder: true);
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
