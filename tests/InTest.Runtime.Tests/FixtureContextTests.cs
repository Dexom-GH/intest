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
    public void PublishedValuesReturnsEveryPublishedKeyAndValue()
    {
        var context = new FixtureContext();
        context.Publish("seededTenant.id", "tenant-1");
        context.Publish("seededUser.id", "user-1");

        context.PublishedValues.ShouldBe(
            new Dictionary<string, string>
            {
                ["seededTenant.id"] = "tenant-1",
                ["seededUser.id"] = "user-1",
            });
    }

    [TestMethod]
    public void PublishedValuesIsAFreshSnapshotEachCall()
    {
        var context = new FixtureContext();
        context.Publish("seededTenant.id", "tenant-1");
        var before = context.PublishedValues;

        context.Publish("seededUser.id", "user-1");

        // Same freshness contract as PublishedKeys and CleanupActions: a caller holding an
        // earlier snapshot must not see it grow as more fixtures publish.
        before.Count.ShouldBe(1);
        context.PublishedValues.Count.ShouldBe(2);
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
