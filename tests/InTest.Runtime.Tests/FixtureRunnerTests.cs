using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class FixtureRunnerTests
{
    private sealed class QaOnlyFixture : IAssemblyFixture
    {
        public bool Ran { get; private set; }
        public Type[] DependsOn => [];
        public string[] AppliesTo => ["qa"];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingFixture : IAssemblyFixture
    {
        // A single, reusable instance rather than `throw new ...` inline, so a test can assert
        // this exact exception survives as FixtureLifecycleException.InnerException rather than
        // merely a look-alike message.
        public readonly InvalidOperationException Exception = new("fixture boom");
        public Type[] DependsOn => [];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct) => throw Exception;
    }

    private sealed class CancelsDuringInitializeFixture(CancellationTokenSource cts) : IAssemblyFixture
    {
        public Type[] DependsOn => [];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            // Cancels its own token mid-InitializeAsync, simulating a real CI timeout landing
            // while a fixture is in flight — as opposed to AnAlreadyCancelledTokenStopsBeforeAnyFixtureRuns,
            // where the token is already cancelled before RunAsync starts the loop.
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class AllProfilesFixture : IAssemblyFixture
    {
        public Type[] DependsOn => [];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            ctx.Publish("ran", "yes");
            return Task.CompletedTask;
        }
    }

    // For the transitive-skip scenario (Task 7's plan gap): a fixture whose own AppliesTo would
    // otherwise let it run under any profile, but whose DependsOn names a fixture skipped for
    // this one.
    private sealed class DependsOnQaOnlyFixture : IAssemblyFixture
    {
        public bool Ran { get; private set; }
        public Type[] DependsOn => [typeof(QaOnlyFixture)];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    // A second hop: depends on DependsOnQaOnlyFixture, which itself only depends on (and does
    // not directly reference) QaOnlyFixture — so this exercises propagation through a skip that
    // is itself transitive, not just a skip that is itself direct.
    private sealed class TransitivelyDependsOnQaOnlyFixture : IAssemblyFixture
    {
        public bool Ran { get; private set; }
        public Type[] DependsOn => [typeof(DependsOnQaOnlyFixture)];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    // A consumer project without nullable reference types enabled can easily leave AppliesTo
    // uninitialized (the same scenario FixtureGraph.ANullDependsOnIsRejected covers for
    // DependsOn) — except here null is not an error, it means the same thing as empty.
    private sealed class NullAppliesToFixture : IAssemblyFixture
    {
        public bool Ran { get; private set; }
        public Type[] DependsOn => [];
        public string[] AppliesTo => null!;

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    // Two distinct types (not one reused parameterized class) so DependsOn can name a real,
    // distinguishable graph node — FixtureGraph keys fixtures by GetType(), so two instances of
    // the same class can never represent two different nodes in the dependency graph.
    private sealed class SeedsCustomerFixture(List<string> order) : IAssemblyFixture
    {
        public Type[] DependsOn => [];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            order.Add(nameof(SeedsCustomerFixture));
            return Task.CompletedTask;
        }
    }

    private sealed class SeedsInvoiceFixture(List<string> order) : IAssemblyFixture
    {
        public Type[] DependsOn => [typeof(SeedsCustomerFixture)];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            order.Add(nameof(SeedsInvoiceFixture));
            return Task.CompletedTask;
        }
    }

    private sealed class RegistersCleanupFixture : IAssemblyFixture
    {
        // A counter, not a bool: a flag can't tell "ran once" from "ran twice", and that
        // distinction is exactly what the double-drain composition test below needs to pin.
        public int CleanupRunCount { get; private set; }
        public bool Drained => CleanupRunCount > 0;
        public Type[] DependsOn => [];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            ctx.OnCleanup(() => { CleanupRunCount++; return Task.CompletedTask; });
            return Task.CompletedTask;
        }
    }

    private sealed class PublishesThenThrowsFixture : IAssemblyFixture
    {
        public bool Drained { get; private set; }
        public Type[] DependsOn => [];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            ctx.Publish("createdRow.id", "row-1");
            ctx.OnCleanup(() => { Drained = true; return Task.CompletedTask; });
            throw new InvalidOperationException("fixture boom");
        }
    }

    private sealed class RegistersFailingCleanupThenThrowsFixture : IAssemblyFixture
    {
        public Type[] DependsOn => [];
        public string[] AppliesTo => [];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            ctx.OnCleanup(() => throw new InvalidOperationException("drain boom"));
            throw new InvalidOperationException("fixture boom");
        }
    }

    // --- RunAsync: profile filtering ---

    [TestMethod]
    public async Task ASkippedFixtureSaysSo()
    {
        var log = new StringWriter();
        var fixture = new QaOnlyFixture();

        await FixtureRunner.RunAsync([fixture], new FixtureContext(), "local", log, default);

        // A fixture silently not running because the profile did not match is indistinguishable
        // from one that ran and did nothing — and the second-run acceptance in Task 8 would pass
        // for the wrong reason.
        log.ToString().ShouldContain(nameof(QaOnlyFixture));
        log.ToString().ShouldContain("local");
        // Strengthens the assertion above: it must actually have been skipped, not merely logged
        // alongside a run that happened anyway.
        fixture.Ran.ShouldBeFalse();
    }

    [TestMethod]
    public async Task AFixtureRunsWhenAppliesToIncludesTheCurrentProfile()
    {
        var log = new StringWriter();
        var fixture = new QaOnlyFixture();

        await FixtureRunner.RunAsync([fixture], new FixtureContext(), "qa", log, default);

        // Guards against an implementation that always skips (which ASkippedFixtureSaysSo alone
        // would not catch) or that logs a "profile" line unconditionally. ShouldNotContain
        // rather than ShouldBeEmpty: the plan says log is where "skip and progress lines go", so
        // a future progress line on a successful run (Task 5/6) must not make this fail — only a
        // skip line would mean this fixture was wrongly treated as not applying.
        fixture.Ran.ShouldBeTrue();
        log.ToString().ShouldNotContain("Skipping", customMessage: "a matching profile must run without being logged as skipped");
    }

    [TestMethod]
    public async Task AnEmptyAppliesToRunsForEveryProfile()
    {
        foreach (var profile in new[] { "local", "qa", "prod" })
        {
            var context = new FixtureContext();

            await FixtureRunner.RunAsync([new AllProfilesFixture()], context, profile, TextWriter.Null, default);

            context.Get("ran").ShouldBe("yes", $"an empty AppliesTo must run for profile '{profile}'");
        }
    }

    [TestMethod]
    public async Task ANullAppliesToRunsForEveryProfile()
    {
        var fixture = new NullAppliesToFixture();

        await FixtureRunner.RunAsync([fixture], new FixtureContext(), "local", TextWriter.Null, default);

        fixture.Ran.ShouldBeTrue("a null AppliesTo must be treated the same as empty — every profile");
    }

    [TestMethod]
    public async Task ASkippedFixtureAlsoSkipsItsDependent()
    {
        var log = new StringWriter();
        var qaOnly = new QaOnlyFixture();
        var dependent = new DependsOnQaOnlyFixture();

        await FixtureRunner.RunAsync([qaOnly, dependent], new FixtureContext(), "local", log, default);

        // qaOnly is skipped for profile "local"; dependent declared a dependency on it, so
        // running dependent anyway would be exactly the silent-wrong-state failure AppliesTo
        // exists to prevent — it would seed against state qaOnly never built.
        qaOnly.Ran.ShouldBeFalse();
        dependent.Ran.ShouldBeFalse();
        log.ToString().ShouldContain(nameof(DependsOnQaOnlyFixture));
        log.ToString().ShouldContain(nameof(QaOnlyFixture));
        log.ToString().ShouldContain("does not apply to profile 'local'");
    }

    [TestMethod]
    public async Task ASkippedFixturePropagatesThroughATwoHopDependencyChain()
    {
        var log = new StringWriter();
        var qaOnly = new QaOnlyFixture();
        var dependent = new DependsOnQaOnlyFixture();
        var transitive = new TransitivelyDependsOnQaOnlyFixture();

        // Registered out of dependency order to also exercise ordering + transitive skip
        // together, the way a real container resolving fixtures would.
        await FixtureRunner.RunAsync(
            [transitive, dependent, qaOnly], new FixtureContext(), "local", log, default);

        qaOnly.Ran.ShouldBeFalse();
        dependent.Ran.ShouldBeFalse();
        transitive.Ran.ShouldBeFalse(
            "transitive depends on dependent, which depends on qaOnly; qaOnly's skip must propagate two hops");
        log.ToString().ShouldContain(nameof(TransitivelyDependsOnQaOnlyFixture));
    }

    [TestMethod]
    public async Task ASkippedFixtureDoesNotSkipAFixtureThatDoesNotDependOnIt()
    {
        var qaOnly = new QaOnlyFixture();
        var context = new FixtureContext();

        // Guards against an implementation that skips everything registered after the first
        // skip, rather than only fixtures that actually declare the dependency.
        await FixtureRunner.RunAsync(
            [qaOnly, new AllProfilesFixture()], context, "local", TextWriter.Null, default);

        qaOnly.Ran.ShouldBeFalse();
        context.Get("ran").ShouldBe("yes", "a fixture with no DependsOn edge to the skipped one must still run");
    }

    // --- RunAsync: ordering ---

    [TestMethod]
    public async Task RunAsyncOrdersFixturesByDependencyNotByRegistrationOrder()
    {
        var order = new List<string>();

        // Registered dependent-first — the reverse of the true dependency order — so an
        // implementation that runs fixtures in whatever order the caller passed them (i.e. does
        // not call FixtureGraph.Order itself) would run SeedsInvoiceFixture first and fail this.
        await FixtureRunner.RunAsync(
            [new SeedsInvoiceFixture(order), new SeedsCustomerFixture(order)],
            new FixtureContext(), "local", TextWriter.Null, default);

        order.ShouldBe([nameof(SeedsCustomerFixture), nameof(SeedsInvoiceFixture)]);
    }

    // --- RunAsync: failure ---

    [TestMethod]
    public async Task AFailingFixtureSaysWhichOne()
    {
        var fixture = new ThrowingFixture();

        var ex = await Should.ThrowAsync<FixtureLifecycleException>(
            () => FixtureRunner.RunAsync([fixture], new FixtureContext(), "local", TextWriter.Null, default));

        // §13: an unhandled exception in AssemblyInitialize otherwise fails every test with an
        // error that does not say "setup broke".
        ex.Message.ShouldContain(nameof(ThrowingFixture));
        // The fixture's own exception must survive as the cause, not just its message text
        // folded into ours — an adopter reading a CI log needs its real stack trace for a bug
        // that is not as self-explanatory as this one.
        ex.InnerException.ShouldBeSameAs(fixture.Exception);
    }

    [TestMethod]
    public async Task AFailingFixtureStopsLaterFixturesFromRunning()
    {
        var order = new List<string>();
        var context = new FixtureContext();

        await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.RunAsync(
            [new ThrowingFixture(), new SeedsCustomerFixture(order)], context, "local", TextWriter.Null, default));

        order.ShouldBeEmpty("a fixture registered after one that failed may depend on it and must not run");
    }

    [TestMethod]
    public async Task AFailingFixtureDrainsCleanupAlreadyRegisteredByAnEarlierFixture()
    {
        var earlier = new RegistersCleanupFixture();
        var context = new FixtureContext();

        await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.RunAsync(
            [earlier, new ThrowingFixture()], context, "local", TextWriter.Null, default));

        earlier.Drained.ShouldBeTrue(
            "cleanup registered by a fixture that already succeeded must not leak just because a later fixture failed");
    }

    [TestMethod]
    public async Task ADoubleDrainAfterAFailedRunOnlyRunsCleanupOnce()
    {
        var earlier = new RegistersCleanupFixture();
        var context = new FixtureContext();

        // RunAsync's own failure-path drain already runs "earlier"'s cleanup once, below. This
        // is precisely the composed scenario the idempotency guarantee exists for: Task 5's
        // TestHost.CleanupAsync calls DrainAsync again, unconditionally, during AssemblyCleanup,
        // on the very context RunAsync already drained. A bool flag cannot tell "ran once" from
        // "ran twice" — this needs the counter.
        await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.RunAsync(
            [earlier, new ThrowingFixture()], context, "local", TextWriter.Null, default));

        await FixtureRunner.DrainAsync(context);

        earlier.CleanupRunCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task AFixtureThatRegistersCleanupBeforeThrowingIsStillDrained()
    {
        var fixture = new PublishesThenThrowsFixture();
        var context = new FixtureContext();

        var ex = await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.RunAsync(
            [fixture], context, "local", TextWriter.Null, default));

        // The fixture created a row (published a key) before it threw; its own cleanup must
        // still run so that row does not leak.
        fixture.Drained.ShouldBeTrue();
        ex.Message.ShouldContain(nameof(PublishesThenThrowsFixture));
    }

    [TestMethod]
    public async Task AFailingFixtureMessageSurvivesEvenWhenDrainAlsoFails()
    {
        var context = new FixtureContext();

        var ex = await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.RunAsync(
            [new RegistersFailingCleanupThenThrowsFixture()], context, "local", TextWriter.Null, default));

        // The fixture's own failure is why the run failed; a drain failure on top of it must not
        // bury the original cause.
        ex.Message.ShouldContain(nameof(RegistersFailingCleanupThenThrowsFixture));
        ex.Message.ShouldContain("fixture boom");
        ex.Message.ShouldContain("drain boom");
    }

    // --- RunAsync: misc ---

    [TestMethod]
    public async Task AnEmptyFixtureListCompletesWithoutError()
    {
        await FixtureRunner.RunAsync([], new FixtureContext(), "local", TextWriter.Null, default);
    }

    [TestMethod]
    public async Task AnAlreadyCancelledTokenStopsBeforeAnyFixtureRuns()
    {
        var order = new List<string>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => FixtureRunner.RunAsync(
            [new SeedsCustomerFixture(order)], new FixtureContext(), "local", TextWriter.Null, cts.Token));

        order.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task ACancellationDuringInitializeAsyncGetsCancellationWordingNotBugRemediation()
    {
        using var cts = new CancellationTokenSource();
        var fixture = new CancelsDuringInitializeFixture(cts);

        var ex = await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.RunAsync(
            [fixture], new FixtureContext(), "local", TextWriter.Null, cts.Token));

        // A cancellation landing mid-fixture is not a bug in that fixture's code; the message
        // must not send the reader after "the underlying error" when there is not one.
        ex.Message.ShouldNotContain("Fix the underlying error");
        ex.Message.ShouldContain("cancelled");
        ex.InnerException.ShouldBeOfType<OperationCanceledException>();
    }

    [TestMethod]
    public async Task ANullFixtureListIsRejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => FixtureRunner.RunAsync(null!, new FixtureContext(), "local", TextWriter.Null, default));
    }

    [TestMethod]
    public async Task ANullContextIsRejectedByRunAsync()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => FixtureRunner.RunAsync([], null!, "local", TextWriter.Null, default));
    }

    [TestMethod]
    public async Task ANullLogIsRejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => FixtureRunner.RunAsync([], new FixtureContext(), "local", null!, default));
    }

    [TestMethod]
    public async Task ANullProfileIsRejected()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => FixtureRunner.RunAsync([], new FixtureContext(), null!, TextWriter.Null, default));
    }

    // --- DrainAsync ---

    [TestMethod]
    public async Task DrainRunsActionsInReverseRegistrationOrder()
    {
        var order = new List<string>();
        var context = new FixtureContext();
        context.OnCleanup(() => { order.Add("first"); return Task.CompletedTask; });
        context.OnCleanup(() => { order.Add("second"); return Task.CompletedTask; });
        context.OnCleanup(() => { order.Add("third"); return Task.CompletedTask; });

        await FixtureRunner.DrainAsync(context);

        order.ShouldBe(["third", "second", "first"]);
    }

    [TestMethod]
    public async Task OneFailingTeardownDoesNotStrandTheOthers()
    {
        var drained = new List<string>();
        var boom = new InvalidOperationException("boom");
        var context = new FixtureContext();
        context.OnCleanup(() => { drained.Add("first"); return Task.CompletedTask; });
        context.OnCleanup(() => throw boom);
        context.OnCleanup(() => { drained.Add("third"); return Task.CompletedTask; });

        var ex = await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.DrainAsync(context));

        // Reverse order, and the failure in the middle must not strand "first". Every action
        // skipped here becomes work for §14's sweeper.
        drained.ShouldBe(["third", "first"]);
        ex.Message.ShouldContain("boom");
        // A single failure survives unwrapped as InnerException — not folded into an
        // AggregateException of one — so its real stack trace is one hop away, not two.
        ex.InnerException.ShouldBeSameAs(boom);
        // "Don't assume a sibling succeeded" is a non-sequitur with only one failure — there are
        // no siblings — and the index the previous implementation reported ("action registered
        // at index 1") is relative to a batch that gets re-taken from empty on every drain, so it
        // is misleading rather than useful. Neither belongs in a single-failure message.
        ex.Message.ShouldNotContain("sibling");
        ex.Message.ShouldNotContain("index");
    }

    [TestMethod]
    public async Task MultipleTeardownFailuresAggregateIntoOneMessage()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("first boom"));
        context.OnCleanup(() => throw new InvalidOperationException("second boom"));

        var ex = await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.DrainAsync(context));

        ex.Message.ShouldContain("first boom");
        ex.Message.ShouldContain("second boom");
        // More than one failure means no single exception can be preferred as "the" cause, so
        // both survive via an AggregateException rather than only the first (or last) winning.
        var aggregate = ex.InnerException.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions.Count.ShouldBe(2);
        // With more than one failure, "don't assume a sibling succeeded" is exactly the correct
        // remediation, unlike the single-failure case above.
        ex.Message.ShouldContain("sibling");
    }

    [TestMethod]
    public async Task DrainingTwiceRunsEachActionOnce()
    {
        var runs = 0;
        var context = new FixtureContext();
        context.OnCleanup(() => { runs++; return Task.CompletedTask; });

        await FixtureRunner.DrainAsync(context);
        await FixtureRunner.DrainAsync(context);

        runs.ShouldBe(1, "draining the same context twice must not re-run cleanup that already ran");
    }

    [TestMethod]
    public async Task DrainingASecondTimeAfterAFailureDoesNotThrowAgain()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("boom"));

        await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.DrainAsync(context));

        // The first drain already reported the failure; a second drain (e.g. TestHost's
        // AssemblyCleanup running after RunAsync already drained on a fixture failure) must be a
        // silent no-op, not a repeat of the same error.
        await FixtureRunner.DrainAsync(context);
    }

    [TestMethod]
    public async Task DrainingAContextWithNoCleanupActionsIsANoOp()
    {
        await FixtureRunner.DrainAsync(new FixtureContext());
    }

    [TestMethod]
    public async Task ANullContextIsRejectedByDrainAsync()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => FixtureRunner.DrainAsync(null!));
    }
}
