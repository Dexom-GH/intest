using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class FixtureValidationTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-fixvalid-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Writes one fixture per (operationKey, problems) pair, each problem a dotted/indexed
    /// property path (e.g. "items[0].sku") that gets a "TODO:{leaf}" sentinel at exactly that
    /// position in the body, then runs the real <see cref="FixtureStore"/> load and
    /// <see cref="FixtureValidation"/> scan over them — the same path a running suite takes.
    /// </summary>
    private FixtureValidation.Report Validate(params (string OperationKey, string[] Problems)[] specs)
    {
        foreach (var (operationKey, problems) in specs)
        {
            var body = new JsonObject();
            foreach (var problem in problems) SetSentinelAtPath(body, problem);

            var document = new JsonObject
            {
                ["$meta"] = new JsonObject { ["tier"] = 4, ["operationId"] = operationKey, ["generatedBy"] = "t" },
                ["body"] = body
            };

            WriteFixture(operationKey, document.ToJsonString());
        }

        var store = FixtureStore.Load(_root, profile: null);
        var resolver = new TokenResolver(new ConfigurationBuilder().Build(), runId: "run-1");
        return FixtureValidation.Build(store, resolver);
    }

    private void WriteFixture(string operationKey, string json)
    {
        var dir = Path.Combine(_root, "fixtures");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, operationKey + ".json"), json);
    }

    /// <summary>
    /// Places a "TODO:{leaf}" sentinel at a path like "items[0].sku", building whatever nested
    /// objects/arrays the path implies along the way.
    /// </summary>
    private static void SetSentinelAtPath(JsonObject root, string path)
    {
        var segments = path.Split('.');
        JsonObject current = root;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment[..bracket];
            var index = bracket < 0 ? (int?)null : int.Parse(segment[(bracket + 1)..segment.IndexOf(']')]);
            var isLast = i == segments.Length - 1;

            if (index is null)
            {
                if (isLast) { current[name] = JsonValue.Create($"TODO:{name}"); return; }

                if (current[name] is not JsonObject next)
                {
                    current[name] = next = new JsonObject();
                }
                current = next;
            }
            else
            {
                if (current[name] is not JsonArray array)
                {
                    current[name] = array = new JsonArray();
                }
                while (array.Count <= index)
                {
                    array.Add(new JsonObject());
                }

                if (isLast) { array[index.Value] = JsonValue.Create($"TODO:{name}"); return; }

                if (array[index.Value] is not JsonObject element)
                {
                    array[index.Value] = element = new JsonObject();
                }
                current = element;
            }
        }
    }

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
    public void AnOperationWithNoFixtureIsNotBlocked()
    {
        // The majority case, and the one v0 already passes: a parameterless GET never loads a
        // fixture. FixtureStore.Get throws FixtureNotFoundException for an unknown key by design
        // (Task 5), and the obvious implementation of RequireFixture — delegate to Get — would
        // inherit that and fail every such operation. Task 10 would report 0 of 9, not 9 of 9.
        Validate(("create-order", new[] { "customerId" })).IsBlocked("get_api_products").ShouldBeFalse();
    }

    [TestMethod]
    public void RequireFixtureIsANoOpForAnOperationWithNoFixture()
    {
        Should.NotThrow(() => Validate().ThrowIfBlocked("get_api_products"));
    }

    [TestMethod]
    public void BlockedOperationsFailWithTheirOwnFileAndProperty()
    {
        var report = Validate(("create-order", new[] { "customerId" }));

        var ex = Should.Throw<FixtureUnresolvedException>(() => report.ThrowIfBlocked("create-order"));
        ex.Message.ShouldContain("create-order.json");
        ex.Message.ShouldContain("customerId");
    }
}
