using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class FixtureGraphTests
{
    private sealed class Alpha : IAssemblyFixture
    {
        public Type[] DependsOn { get; init; } = [];
        public string[] AppliesTo => [];
        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Beta : IAssemblyFixture
    {
        public Type[] DependsOn { get; init; } = [];
        public string[] AppliesTo => [];
        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Gamma : IAssemblyFixture
    {
        public Type[] DependsOn { get; init; } = [];
        public string[] AppliesTo => [];
        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Delta : IAssemblyFixture
    {
        public Type[] DependsOn { get; init; } = [];
        public string[] AppliesTo => [];
        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Epsilon : IAssemblyFixture
    {
        public Type[] DependsOn { get; init; } = [];
        public string[] AppliesTo => [];
        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    [TestMethod]
    public void IndependentFixturesKeepRegistrationOrder()
    {
        // A suite whose seeding order varies between runs is a suite whose failures cannot be
        // reproduced. Independent nodes must not be reordered arbitrarily. Registered as
        // [Gamma, Alpha] — reverse-alphabetical — so an implementation that (incorrectly) sorted
        // independent nodes by type name would fail this instead of passing by coincidence.
        FixtureGraph.Order([new Gamma(), new Alpha()]).Select(f => f.GetType()).ShouldBe([typeof(Gamma), typeof(Alpha)]);
    }

    [TestMethod]
    public void ADependencyRunsBeforeItsDependent()
    {
        var beta = new Beta { DependsOn = [typeof(Alpha)] };
        var alpha = new Alpha();

        // Beta is registered first, but it depends on Alpha, so Alpha must come first regardless
        // of registration order.
        FixtureGraph.Order([beta, alpha]).Select(f => f.GetType()).ShouldBe([typeof(Alpha), typeof(Beta)]);
    }

    [TestMethod]
    public void ADiamondResolvesEachNodeOnceAndBeforeBothBranches()
    {
        var alpha = new Alpha();
        var beta = new Beta { DependsOn = [typeof(Alpha)] };
        var gamma = new Gamma { DependsOn = [typeof(Alpha)] };
        var delta = new Delta { DependsOn = [typeof(Beta), typeof(Gamma)] };

        var ordered = FixtureGraph.Order([delta, beta, gamma, alpha]).Select(f => f.GetType()).ToList();

        ordered.Count.ShouldBe(4, "Alpha must appear exactly once even though both Beta and Gamma depend on it");
        ordered.IndexOf(typeof(Alpha)).ShouldBeLessThan(ordered.IndexOf(typeof(Beta)));
        ordered.IndexOf(typeof(Alpha)).ShouldBeLessThan(ordered.IndexOf(typeof(Gamma)));
        ordered.IndexOf(typeof(Beta)).ShouldBeLessThan(ordered.IndexOf(typeof(Delta)));
        ordered.IndexOf(typeof(Gamma)).ShouldBeLessThan(ordered.IndexOf(typeof(Delta)));
    }

    [TestMethod]
    public void ACycleNamesEveryTypeInvolved()
    {
        // Alpha depends on Beta, Beta depends on Alpha. Naming only one sends the reader hunting
        // through the other for a dependency that is not there.
        var alpha = new Alpha { DependsOn = [typeof(Beta)] };
        var beta = new Beta { DependsOn = [typeof(Alpha)] };

        var ex = Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([alpha, beta]));

        ex.Message.ShouldContain(nameof(Alpha));
        ex.Message.ShouldContain(nameof(Beta));
        ex.Message.ShouldContain("cycle");
    }

    [TestMethod]
    public void ACycleEnteredFromOutsideNamesOnlyTheCycleMembers()
    {
        // Epsilon depends on Alpha, which is on the cycle, but Epsilon itself is not — it is
        // visited on the way in, not part of the cycle. Naming it would send the reader looking
        // for a DependsOn edge that does not exist. This pins the slice at `cycleStart` rather
        // than the whole `visiting` path: reporting the entire path would (wrongly) include
        // Epsilon.
        var epsilon = new Epsilon { DependsOn = [typeof(Alpha)] };
        var alpha = new Alpha { DependsOn = [typeof(Beta)] };
        var beta = new Beta { DependsOn = [typeof(Alpha)] };

        var ex = Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([epsilon, alpha, beta]));

        ex.Message.ShouldNotContain(nameof(Epsilon));
        ex.Message.ShouldContain(nameof(Alpha));
        ex.Message.ShouldContain(nameof(Beta));
    }

    [TestMethod]
    public void ASelfDependencyIsACycleOfOne()
    {
        // DependsOn = [typeof(Alpha)] on Alpha itself is the most common DependsOn typo. The
        // implementation already handles it as a cycle of one; this records that as intended
        // rather than incidental.
        var alpha = new Alpha { DependsOn = [typeof(Alpha)] };

        var ex = Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([alpha]));

        ex.Message.ShouldContain(nameof(Alpha));
        ex.Message.ShouldContain("cycle");
    }

    [TestMethod]
    public void ADependsOnEntryNobodyRegisteredNamesBothEnds()
    {
        // Gamma depends on a type nobody registered. The message must name Gamma (the dependent)
        // and the missing type, so the reader knows exactly which fixture to fix.
        var gamma = new Gamma { DependsOn = [typeof(Delta)] };

        var ex = Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([gamma]));

        ex.Message.ShouldContain(nameof(Gamma));
        ex.Message.ShouldContain(nameof(Delta));
    }

    [TestMethod]
    public void ADuplicateRegistrationOfTheSameTypeThrows()
    {
        // services.AddSingleton<IAssemblyFixture, X>() (unlike TryAddEnumerable) does not dedupe
        // a copy-pasted registration line, so resolving IEnumerable<IAssemblyFixture> can hand
        // Order the same fixture type twice. Collapsing that to one run silently would hide
        // exactly the non-determinism v1-b decision 3 exists to eliminate.
        var ex = Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([new Alpha(), new Alpha()]));

        ex.Message.ShouldContain(nameof(Alpha));
    }

    [TestMethod]
    public void ANullDependsOnIsRejected()
    {
        // Reachable from a consumer project without nullable reference types enabled, where
        // `Type[] DependsOn { get; set; }` is never initialized. Must fail naming the fixture
        // rather than a bare NullReferenceException.
        var alpha = new Alpha { DependsOn = null! };

        var ex = Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([alpha]));

        ex.Message.ShouldContain(nameof(Alpha));
    }

    [TestMethod]
    public void AnEmptySetIsNotAnError()
    {
        FixtureGraph.Order([]).ShouldBeEmpty();
    }

    [TestMethod]
    public void ANullFixtureListIsRejected()
    {
        Should.Throw<ArgumentNullException>(() => FixtureGraph.Order(null!));
    }
}
