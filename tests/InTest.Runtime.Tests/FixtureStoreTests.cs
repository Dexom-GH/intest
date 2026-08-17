using InTest.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class FixtureStoreTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-fixstore-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WriteBase(string operationKey, string json)
    {
        var dir = Path.Combine(_root, "fixtures");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, operationKey + ".json"), json);
    }

    private void WriteOverlay(string profile, string operationKey, string json)
    {
        var dir = Path.Combine(_root, "fixtures", profile);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, operationKey + ".json"), json);
    }

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

    [TestMethod]
    public void OverlayMergesPerPropertyRatherThanReplacingTheObject()
    {
        WriteBase("op", """
            {"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
             "body":{"a":1,"nested":{"x":1,"y":2}}}
            """);
        WriteOverlay("qa", "op", """
            {"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
             "body":{"nested":{"y":99}}}
            """);

        var store = FixtureStore.Load(_root, "qa");
        var body = store.Get("op").Body!;

        body["a"]!.GetValue<int>().ShouldBe(1, "untouched base properties survive");
        body["nested"]!["x"]!.GetValue<int>().ShouldBe(1, "sibling properties survive a nested merge");
        body["nested"]!["y"]!.GetValue<int>().ShouldBe(99, "the environment wins");
    }

    [TestMethod]
    public void LoadsEveryBaseFixture()
    {
        WriteBase("op-a", """{"$meta":{"tier":1,"operationId":"op-a","generatedBy":"t"},"$parameters":{"id":"1"}}""");
        WriteBase("op-b", """{"$meta":{"tier":1,"operationId":"op-b","generatedBy":"t"},"$parameters":{"id":"2"}}""");

        var store = FixtureStore.Load(_root, profile: null);

        store.Count.ShouldBe(2);
        store.Get("op-a").Parameters["id"].ShouldBe("1");
        store.Get("op-b").Parameters["id"].ShouldBe("2");
    }

    [TestMethod]
    public void QueryParametersFromTheOverlayWinOverTheBase()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"id":"1","page":"2"}}""");
        WriteOverlay("qa", "op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"page":"99"}}""");

        var parameters = FixtureStore.Load(_root, "qa").Get("op").Parameters;

        parameters["id"].ShouldBe("1", "untouched base parameters survive");
        parameters["page"].ShouldBe("99", "the environment wins");
    }

    [TestMethod]
    public void AnOverlayWithNoBaseFixtureIsAnErrorNamingTheFile()
    {
        WriteOverlay("qa", "orphan", """{"$meta":{"tier":1,"operationId":"orphan","generatedBy":"t"},"body":{"x":1}}""");

        Should.Throw<FixtureFormatException>(() => FixtureStore.Load(_root, "qa"))
              .Message.ShouldContain("orphan.json");
    }

    [TestMethod]
    public void AMalformedFixtureReportsItsFilenameRatherThanABareJsonException()
    {
        WriteBase("broken", "{ not valid json");

        Should.Throw<FixtureFormatException>(() => FixtureStore.Load(_root, profile: null))
              .Message.ShouldContain("broken.json");
    }
}
