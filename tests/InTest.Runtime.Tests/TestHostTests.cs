using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// Covers <see cref="TestHost.CleanupAsync"/> (Task 5) — the caller that makes
/// <see cref="FixtureRunner.DrainAsync"/> reachable at all from a generated project's
/// [AssemblyCleanup]. The behaviour that matters most here is the one a test merely asserting
/// "the method exists and calls DrainAsync" would miss entirely: a throwing drain must not
/// propagate out of [AssemblyCleanup], or a teardown complaint becomes the whole run's headline
/// and buries whatever test actually failed.
/// <para>
/// The narrowness of <see cref="TestHost.CleanupAsync"/>'s catch clause is deliberately not
/// pinned here: <see cref="FixtureRunner.DrainAsync"/>'s own contract (hardened in
/// <c>FixtureRunnerTests.DrainWrapsACauseEvenWhenItsOwnMessageGetterThrows</c>, Task 5) promises
/// to only ever throw <see cref="FixtureLifecycleException"/>, so once that promise genuinely
/// holds, no cleanup action can make anything else reach this class's catch clause — a test for
/// that path would assert nothing. The decision stays argued in
/// <see cref="TestHost.CleanupAsync"/>'s own doc comment instead.
/// </para>
/// </summary>
[TestClass]
public class TestHostTests
{
    /// <summary>
    /// A minimal <see cref="TestContext"/> double. Every abstract member must be overridden to
    /// instantiate at all; only <see cref="WriteLine(string?)"/> is exercised by
    /// <see cref="TestHost.CleanupAsync"/> today, but the rest still need bodies to compile.
    /// </summary>
    private sealed class FakeTestContext : TestContext
    {
        public List<string> Lines { get; } = [];

        /// <summary>Calls to <see cref="DisplayMessage"/>, in order — the sink
        /// <c>TestHost.ContextTextWriter</c> and the fixture-validation report now actually use,
        /// since <see cref="WriteLine(string?)"/> was confirmed to be invisible on a passing
        /// [AssemblyInitialize] under VSTest (see <c>TestHost.ContextTextWriter</c>'s doc).</summary>
        public List<(MessageLevel Level, string Message)> DisplayedMessages { get; } = [];

        public override IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

        public override void WriteLine(string? message) => Lines.Add(message ?? "");

        public override void WriteLine(string format, params object?[] args) =>
            Lines.Add(string.Format(format, args));

        public override void Write(string? message) => Lines.Add(message ?? "");

        public override void Write(string format, params object?[] args) =>
            Lines.Add(string.Format(format, args));

        public override void AddResultFile(string fileName)
        {
        }

        public override void DisplayMessage(MessageLevel messageLevel, string message) =>
            DisplayedMessages.Add((messageLevel, message));
    }

    // TestHost.RetainedFixtureContext is process-wide static state. Reset both before and after
    // each test — before, so a test is never at the mercy of whatever its predecessor left
    // behind if that predecessor's own cleanup were ever skipped; after, so this test does not
    // leak into whichever one runs next (DoNotParallelize makes that deterministic rather than
    // merely unlikely, but still wrong).
    [TestInitialize]
    public void ResetRetainedFixtureContextBeforeTest() => TestHost.RetainedFixtureContext = null;

    [TestCleanup]
    public void ResetRetainedFixtureContextAfterTest() => TestHost.RetainedFixtureContext = null;

    [TestMethod]
    public async Task CleanupAsyncDoesNotRethrowWhenDrainFails()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        TestHost.RetainedFixtureContext = context;

        // DrainAsync throws FixtureLifecycleException by design (Task 3) whenever a cleanup
        // action fails. If CleanupAsync let that propagate, an unhandled exception out of
        // [AssemblyCleanup] would become the whole run's headline, burying whatever test
        // actually failed. Should.NotThrowAsync puts that expectation in the assertion itself
        // rather than leaving it implicit in "the test would fail if this threw".
        await Should.NotThrowAsync(() => TestHost.CleanupAsync(new FakeTestContext()));
    }

    [TestMethod]
    public async Task CleanupAsyncWritesTheDrainFailureToTheTestContext()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        TestHost.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // Swallowed silently, a drain failure would be invisible in the .trx even though a
        // fixture's teardown genuinely failed and something it created may have leaked. The
        // exception's own message must survive into what gets written.
        testContext.Lines.ShouldContain(line => line.Contains("drain boom"),
            "a drain failure must still be visible in the TestContext log even though it does not fail the run");
    }

    [TestMethod]
    public async Task CleanupAsyncAlsoWritesTheDrainFailureToConsoleError()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        TestHost.RetainedFixtureContext = context;

        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            await TestHost.CleanupAsync(new FakeTestContext());
        }
        finally
        {
            // Restored even if the assertion below fails, so a red test here does not also
            // corrupt every other test's Console.Error for the rest of this run.
            Console.SetError(originalError);
        }

        // TestContext.WriteLine lands in the .trx but is invisible at `dotnet test`'s default
        // console verbosity (confirmed against a real MSTest 4.3.3 run) — the common CI shape
        // of console log plus exit code, with no .trx published, would otherwise make a drain
        // failure completely invisible even though it does not fail the run.
        capturedError.ToString().ShouldContain("drain boom");
    }

    [TestMethod]
    public async Task CleanupAsyncNamesTheRunIdInTheDrainFailureMessage()
    {
        // RunIdValue is null! (unset) here: nothing in this test file calls
        // TestHost.InitializeAsync, which is the only place that assigns it. That is also the
        // real scenario this guards — InitializeAsync throwing before it gets that far, while
        // RetainedFixtureContext is still non-null because an earlier fixture already ran, is
        // exactly the readiness-failure path CleanupAsyncIsANoOpWhenNoFixtureContextWasRetained
        // covers the opposite side of.
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        TestHost.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // The run id is the one handle an operator has for finding what a leaked row belongs
        // to — RunIdHandler stamps every request with it. An unset run id must say so
        // explicitly rather than silently vanishing from the message.
        testContext.Lines.ShouldContain(line => line.Contains("AssemblyInitialize did not complete"),
            "an unavailable run id must be named explicitly, not silently omitted");
    }

    [TestMethod]
    public async Task CleanupAsyncNamesTheRiskToALaterRunNotThisOnesResults()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        TestHost.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // "This run's results are unaffected" is true but wrong-footed: F7 exists because state
        // a run fails to tear down can break a *later* run, which is the risk worth naming.
        testContext.Lines.ShouldContain(line => line.Contains("later run"),
            "the message must name the risk to a later run, not just reassure about this one");
    }

    [TestMethod]
    public async Task CleanupAsyncActuallyDrainsTheRetainedContextOnSuccess()
    {
        var ran = false;
        var context = new FixtureContext();
        context.OnCleanup(() => { ran = true; return Task.CompletedTask; });
        TestHost.RetainedFixtureContext = context;

        await TestHost.CleanupAsync(new FakeTestContext());

        // Guards against an implementation that merely catches-and-swallows without ever
        // draining at all, which the no-rethrow test above would not by itself catch.
        ran.ShouldBeTrue("CleanupAsync must actually drain the retained context, not merely avoid throwing");
    }

    [TestMethod]
    public async Task CleanupAsyncWritesHowManyActionsItDrainedOnSuccess()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => Task.CompletedTask);
        context.OnCleanup(() => Task.CompletedTask);
        TestHost.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // Before this, a drain that ran two actions and a context nobody ever registered
        // anything against both wrote nothing at all — a reader of the .trx could not tell
        // "cleanup ran and succeeded" from "cleanup was never wired up" from the log alone.
        testContext.Lines.ShouldContain(line => line.Contains("drained 2 action(s)"),
            "a successful drain must say how many actions it drained");
    }

    [TestMethod]
    public async Task CleanupAsyncWritesNothingWhenThereWasNothingToDrain()
    {
        var context = new FixtureContext();
        TestHost.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // RetainedFixtureContext is non-null here (InitializeAsync always creates one now), but
        // no fixture registered any teardown against it — the overwhelmingly common case for a
        // suite with no fixtures at all. Announcing "drained 0 action(s)" every run would be
        // noise; staying silent keeps the log free of the null-vs-zero distinction that already
        // requires no announcement.
        testContext.Lines.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task CleanupAsyncIsANoOpWhenNoFixtureContextWasRetained()
    {
        TestHost.RetainedFixtureContext = null;

        // AssemblyInitialize can throw before ever creating the retained context — a readiness
        // failure, say (Task 6). AssemblyCleanup still runs in that case, and null here must not
        // throw a NullReferenceException out of [AssemblyCleanup] on top of whatever already
        // failed during AssemblyInitialize.
        await TestHost.CleanupAsync(new FakeTestContext());
    }

    [TestMethod]
    public async Task CleanupAsyncRejectsANullTestContext()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => TestHost.CleanupAsync(null!));
    }

    /// <summary>
    /// Covers <c>TestHost.ContextTextWriter</c> — the <see cref="TextWriter"/>
    /// <see cref="TestHost.InitializeAsync"/> hands to <c>FixtureRunner.RunAsync</c> for its skip
    /// lines. This is as close to that wiring as a cheap, in-process test can get: it proves the
    /// writer class itself forwards correctly. It cannot prove that
    /// <see cref="TestHost.InitializeAsync"/> actually constructs and passes <em>this</em> writer
    /// rather than, say, <see cref="TextWriter.Null"/> — that is an implementation detail of a
    /// private call site inside a method this repo deliberately does not build an in-process
    /// harness for (it needs <c>AppContext.BaseDirectory</c>, a real <see cref="TestContext"/>,
    /// and live HTTP). See <c>TestHost.ContextTextWriter</c>'s own doc for why
    /// <see cref="TestContext.DisplayMessage"/> at <see cref="MessageLevel.Warning"/>, not
    /// <see cref="TestContext.WriteLine(string)"/>, is what it forwards to.
    /// </summary>
    [TestMethod]
    public void ContextTextWriterForwardsWriteLineToDisplayMessageAtWarning()
    {
        var testContext = new FakeTestContext();
        var writer = new TestHost.ContextTextWriter(testContext);

        writer.WriteLine("Skipping fixture 'Some.Fixture': its AppliesTo does not include profile 'local'.");

        testContext.DisplayedMessages.ShouldContain(
            (MessageLevel.Warning, "Skipping fixture 'Some.Fixture': its AppliesTo does not include profile 'local'."));
    }

    [TestMethod]
    public void ContextTextWriterTreatsANullLineAsEmpty()
    {
        var testContext = new FakeTestContext();
        var writer = new TestHost.ContextTextWriter(testContext);

        writer.WriteLine((string?)null);

        testContext.DisplayedMessages.ShouldContain((MessageLevel.Warning, ""));
    }

    /// <summary>
    /// Pins, rather than fixes, the restriction <c>TestHost.ContextTextWriter</c>'s own doc
    /// names: only <see cref="TextWriter.WriteLine(string?)"/> is overridden, so every other
    /// <see cref="TextWriter"/> member silently no-ops via the base type's empty-bodied
    /// <c>Write(char)</c>. Harmless today — <c>FixtureRunner</c> never calls anything else on
    /// this writer — but a regression test for the doc's own claim, so a future caller who adds
    /// one of these calls to <c>FixtureRunner</c> gets a failing test here rather than silently
    /// losing output the way the doc warns about.
    /// </summary>
    [TestMethod]
    public void ContextTextWriterSwallowsEveryWriteExceptWriteLineOfString()
    {
        var testContext = new FakeTestContext();
        var writer = new TestHost.ContextTextWriter(testContext);

        writer.Write('x');
        writer.Write("a string");
        writer.WriteLine();
        writer.WriteLine(42);

        testContext.DisplayedMessages.ShouldBeEmpty(
            "if this now fails, ContextTextWriter has started forwarding more than WriteLine(string) — " +
            "update its doc comment rather than treating this test as the bug.");
    }
}
