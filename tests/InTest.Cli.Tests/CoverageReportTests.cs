using System.Text.Json;
using InTest.Cli.Coverage;
using InTest.Cli.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class CoverageReportTests
{
    private static TestPlan Plan() => new(
        "Orders",
        [new TestClassPlan("OrdersTests", "Orders",
            [new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a", [], 200, "Order", "Contract"),
             new TestCasePlan("B_Contract", "d", "b", false, "GET", "/b", [], 204, null, "Contract")])],
        [new SkippedOperation("c", "request body media type(s) multipart/form-data not supported in v0")]);

    [TestMethod]
    public void CountsGeneratedAndSkippedOperations()
    {
        using var doc = JsonDocument.Parse(CoverageReport.ToJson(Plan()));
        doc.RootElement.GetProperty("generated").GetInt32().ShouldBe(2);
        doc.RootElement.GetProperty("skipped").GetArrayLength().ShouldBe(1);
    }

    [TestMethod]
    public void NotesSynthesizedOperationIds()
    {
        using var doc = JsonDocument.Parse(CoverageReport.ToJson(Plan()));
        doc.RootElement.GetProperty("notes").GetProperty("synthesizedOperationIds").GetInt32().ShouldBe(1);
    }

    [TestMethod]
    public void NotesStatusOnlyTestsSoMissingSchemasAreVisible()
    {
        using var doc = JsonDocument.Parse(CoverageReport.ToJson(Plan()));
        doc.RootElement.GetProperty("notes").GetProperty("statusOnlyContractTests").GetInt32().ShouldBe(1);
    }

    [TestMethod]
    public void IsDeterministic()
    {
        CoverageReport.ToJson(Plan()).ShouldBe(CoverageReport.ToJson(Plan()));
    }
}
