using InTest.Cli.Spec;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class OperationKeyTests
{
    [TestMethod]
    public void UsesTheDeclaredOperationIdWhenPresent()
    {
        OperationKey.Resolve("getOrderById", "GET", "/orders/{id}")
                    .ShouldBe(new OperationKey("getOrderById", Synthesized: false));
    }

    [TestMethod]
    [DataRow("GET", "/orders", "get_orders")]
    [DataRow("POST", "/orders", "post_orders")]
    [DataRow("GET", "/orders/{id}", "get_orders_id")]
    [DataRow("DELETE", "/orders/{id}/items/{sku}", "delete_orders_id_items_sku")]
    [DataRow("GET", "/", "get_root")]
    public void SynthesizesFromMethodAndPathWhenAbsent(string method, string path, string expected)
    {
        OperationKey.Resolve(null, method, path).ShouldBe(new OperationKey(expected, Synthesized: true));
    }

    [TestMethod]
    public void SynthesisIsStableAndIndependentOfDeclarationOrder()
    {
        OperationKey.Resolve(null, "GET", "/orders/{id}")
                    .ShouldBe(OperationKey.Resolve(null, "GET", "/orders/{id}"));
    }

    [TestMethod]
    public void BlankOperationIdIsTreatedAsAbsent()
    {
        OperationKey.Resolve("   ", "GET", "/orders").Synthesized.ShouldBeTrue();
    }
}
