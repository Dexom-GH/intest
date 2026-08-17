using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using InTest.Cli.Spec;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Golden.Tests;

[TestClass]
public class GoldenFileTests
{
    private static string SpecPath => Path.Combine(AppContext.BaseDirectory, "Specs", "orders.json");
    private static string ExpectedPath => Path.Combine(AppContext.BaseDirectory, "Expected", "OrdersTests.g.cs.txt");

    /// <summary>
    /// The golden in the *source* tree. Updating must not write to the build output: with
    /// CopyToOutputDirectory="PreserveNewest" the freshly written copy under bin/ becomes newer
    /// than the committed one, so MSBuild stops refreshing it and the assertion then compares
    /// that copy against itself — green forever, whatever the repository actually contains.
    /// </summary>
    private static string SourceExpectedPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Expected", "OrdersTests.g.cs.txt"));

    private static async Task<string> RenderAsync()
    {
        var spec = await SpecLoader.LoadFromFileAsync(SpecPath);
        var plan = TestPlanBuilder.Build(spec.Document);
        var ordersClass = plan.Classes.Single(c => c.ClassName == "OrdersTests");
        return new TemplateRenderer().RenderClass(ordersClass, "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase");
    }

    [TestMethod]
    public async Task OutputMatchesTheGoldenFile()
    {
        var actual = await RenderAsync();

        if (Environment.GetEnvironmentVariable("INTEST_UPDATE_GOLDEN") == "1")
        {
            await File.WriteAllTextAsync(SourceExpectedPath, actual);
            Assert.Inconclusive(
                $"Golden file updated at {SourceExpectedPath}. Review the diff, then rebuild and "
                + "re-run without INTEST_UPDATE_GOLDEN to verify.");
        }

        actual.ShouldBe(await File.ReadAllTextAsync(ExpectedPath));
    }

    [TestMethod]
    public async Task GenerationIsDeterministic()
    {
        (await RenderAsync()).ShouldBe(await RenderAsync());
    }
}
