using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class FixtureContextTests
{
    [TestMethod]
    public void PublishedValueCanBeReadBack()
    {
        var context = new FixtureContext();

        context.Publish("seededTenant.id", "tenant-1");

        context.Get("seededTenant.id").ShouldBe("tenant-1");
    }

    [TestMethod]
    public void PublishingTheSameKeyTwiceIsAnError()
    {
        var context = new FixtureContext();
        context.Publish("seededTenant.id", "a");

        // A silent overwrite would make {{fixture:…}} depend on which fixture ran last, which is
        // precisely the non-determinism topological ordering exists to remove.
        Should.Throw<FixtureLifecycleException>(() => context.Publish("seededTenant.id", "b"))
              .Message.ShouldContain("seededTenant.id");
    }

    [TestMethod]
    public void OnCleanupRecordsWithoutRunning()
    {
        var ran = false;
        var context = new FixtureContext();
        context.OnCleanup(() => { ran = true; return Task.CompletedTask; });

        ran.ShouldBeFalse("the context records teardown; FixtureRunner decides when it runs");
        context.CleanupActions.Count.ShouldBe(1);
    }

    [TestMethod]
    public void PublishedKeysAreOrdinalSortedSoMessagesAreStable()
    {
        var context = new FixtureContext();
        context.Publish("zebra.id", "z");
        context.Publish("apple.id", "a");
        context.Publish("Middle.id", "m");

        // Ordinal, not culture-aware: a stable, reproducible order for error messages that list
        // every published key, independent of the machine's locale.
        context.PublishedKeys.ShouldBe(["Middle.id", "apple.id", "zebra.id"]);
    }

    [TestMethod]
    public void ANullKeyIsRejected()
    {
        var context = new FixtureContext();

        Should.Throw<ArgumentException>(() => context.Publish(null!, "value"));
    }

    [TestMethod]
    public void AWhitespaceKeyIsRejected()
    {
        var context = new FixtureContext();

        Should.Throw<ArgumentException>(() => context.Publish("   ", "value"));
    }

    [TestMethod]
    public void OnCleanupRecordsMultipleActionsInRegistrationOrder()
    {
        var context = new FixtureContext();
        var first = () => Task.CompletedTask;
        var second = () => Task.CompletedTask;

        context.OnCleanup(first);
        context.OnCleanup(second);

        context.CleanupActions.Count.ShouldBe(2);
        context.CleanupActions[0].ShouldBeSameAs(first);
        context.CleanupActions[1].ShouldBeSameAs(second);
    }
}
