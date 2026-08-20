using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// <see cref="TestIdentity"/> replaces the bare identity-name strings <see
/// cref="ITestTokenProvider.Identities"/> used to carry: a name plus the scopes that identity
/// holds, so a later scope-aware 403 guard can read both from one descriptor instead of a parallel
/// lookup keyed by the same strings.
/// </summary>
[TestClass]
public class TestIdentityTests
{
    [TestMethod]
    public void AnIdentityWithNoDeclaredScopesReportsNullNotEmpty()
    {
        // null runs the 403 case, [] declares an identity holding nothing and also
        // runs it — but they are different states and the guard treats them differently. Collapsing
        // them to [] would make every undeclared identity look like a deliberate declaration.
        new TestIdentity("default").Scopes.ShouldBeNull();
    }

    [TestMethod]
    public void DeclaredScopesRoundTripThroughTheDescriptor()
    {
        var identity = new TestIdentity("reader", ["orders:read", "orders:list"]);

        identity.Name.ShouldBe("reader");
        identity.Scopes.ShouldBe(["orders:read", "orders:list"]);
    }

    [TestMethod]
    public void AnEmptyScopesDeclarationIsADeliberateStateNotTheSameAsNull()
    {
        var identity = new TestIdentity("no-scopes", []);

        identity.Scopes.ShouldNotBeNull();
        identity.Scopes.ShouldBeEmpty();
    }
}
