using System.Text.Json.Nodes;
using InTest.Cli.Fixtures;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Decision 5: <c>FixtureDocument</c> (this project, writer) and <c>InTest.Runtime.Fixture</c>
/// (reader) are deliberately separate types over one file format, and neither project
/// references the other. This project references both, so it is the one place that can see both
/// sides of the contract — nothing but this test couples them, so if the two ever disagree about
/// <c>$parameters</c> or <c>body</c>, it fails here rather than at <c>AssemblyInitialize</c> in
/// an adopter's suite.
/// </summary>
[TestClass]
public class FixtureContractTests
{
    [TestMethod]
    public void AFixtureTheCliWritesIsOneTheRuntimeCanRead()
    {
        var written = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 4, OperationId = "createProduct", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["id"] = "7", ["page"] = "2" },
            Body = JsonNode.Parse("""{"sku":"TODO:sku","nested":{"x":1}}""")
        };

        var read = InTest.Runtime.Fixture.Parse(written.ToJson());

        read.Parameters.ShouldBe(written.Parameters);
        read.Body!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
        read.Body["nested"]!["x"]!.GetValue<int>().ShouldBe(1);
    }

    [TestMethod]
    public void ABodylessFixtureRoundTripsAsBodyless()
    {
        var written = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 1, OperationId = "getById", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["id"] = "7" }
        };

        InTest.Runtime.Fixture.Parse(written.ToJson()).Body.ShouldBeNull();
    }
}
