# InTest v1-b — Fixture Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A generated suite can run twice against the same database and pass both times.

**Architecture:** Team-written `IAssemblyFixture` implementations seed data during `AssemblyInitialize`, publish identifiers, and register their own teardown. `{{fixture:…}}` tokens in fixture files resolve from what they published. `AssemblyCleanup` drains the teardowns in reverse.

**Tech Stack:** Unchanged — net10.0 · MSTest 4.3.3 · Microsoft.Extensions.DependencyInjection.

**Spec:** [`../specs/2026-08-16-intest-api-test-generator-design.md`](../specs/2026-08-16-intest-api-test-generator-design.md), §13 primarily.

**Prerequisite:** v1-a complete and merged (`fb40d3c`). 235 tests passing.

---

## The problem this closes

F7 in [`../../v0-acceptance.md`](../../v0-acceptance.md), measured rather than predicted. Running the Catalog suite a second time against the same database, changing nothing:

```
Failed!  - Failed: 3, Passed: 6, Skipped: 0, Total: 9

POST /api/categories → expected 201, got 409  ("A category named 'Accessories' already exists")
POST /api/products   → expected 201, got 409  ("A product with SKU 'ACC-0100' already exists")
DeleteApiCategoriesId_Contract                 (its target was deleted by the first run)
```

v1-a measured exactly how much of this it could already solve, and that bounds this plan:

| Case | v1-a | Why |
|---|---|---|
| Free-form unique field | **Solved** — `"Accessories-{{runId}}"` passed twice | Run id is unique per run |
| Format-constrained unique field | **Not solved** | SKU must match `^[A-Z]{3}-[0-9]{4}$`; no run id fits |
| Deleting a seeded row | **Not solved** | Nothing in v1-a creates the row to delete |

The last two are what `IAssemblyFixture` and `{{fixture:…}}` exist for. §1 calls the output suitable for a post-deployment gate; a suite that works once is not a gate. That is why this is v1-b rather than later.

---

## Decisions this plan encodes

**1. `AssemblyInitialize` is reordered, and that is the substance of this plan.**

Today `TestHost.InitializeAsync` validates fixtures *before* it builds the service provider:

```
profile → configuration → run id → FixtureStore.Load → TokenResolver
        → FixtureValidation.Build → services → readiness
```

That cannot stand. Seeding needs an HTTP client, readiness must pass before seeding, and `{{fixture:…}}` cannot be validated until seeding has published its keys. The new order:

```
profile → configuration → run id → FixtureStore.Load → services → readiness
        → run IAssemblyFixture implementations → TokenResolver (with published keys)
        → FixtureValidation.Build
```

Validation still happens exactly once and still reports everything in one aggregated message — v1-a decision 2 is unchanged. It simply happens later, when it has the information it needs.

**This changes behaviour on a dead API, and the change is intended.** Today validation runs first, so an unreachable service still prints every fixture problem before readiness fails. After the reorder, readiness throws first and the fixture report is never built. That is the better trade — an unreachable service is the more actionable error, and §13's own sketch has this order — but it is a visible regression in diagnostics for anyone debugging fixtures against a service that happens to be down. Task 6 must expect it rather than discover it.

**2. Fixtures are discovered by DI registration, never by reflection.**

A team writes `services.AddSingleton<IAssemblyFixture, DatabaseSeed>()` in the `TestStartup.cs` they already own; `TestHost` resolves `IEnumerable<IAssemblyFixture>` from the built provider. Assembly scanning would be less typing and more magic: it would run a class the moment someone writes it, make ordering depend on reflection order, and offer no way to register one conditionally. The `ConfigureServices` hook exists for exactly this.

**3. Ordering is topological over `DependsOn`, and a cycle fails loudly.**

§13 is explicit that integer ordering is the thing everyone regrets — someone always needs to slot between 15 and 20. `DependsOn` is `Type[]`. A cycle, or a dependency on a type nobody registered, fails at `AssemblyInitialize` naming the types involved rather than silently running in some arbitrary order.

**4. Cleanup is registration-based and best-effort, and something must actually call it.**

`ctx.OnCleanup(...)` registers teardown next to the thing that created it; the drain runs in reverse registration order. **One `FixtureContext` instance is created by `TestHost`, passed to every fixture, and retained in a static field** so `AssemblyCleanup` can drain the same instance the fixtures wrote to.

Three properties are not optional:

- **One failing teardown must not strand the others.** Every remaining action still runs; failures aggregate into one report.
- **Teardown must be idempotent.** §14 already requires this of consumers, and the drain does not retry.
- **`AssemblyCleanup` does not run on crash, cancellation or agent timeout.** §14 already says so, v1-b does not change it, and the docs must not imply otherwise. The out-of-band sweeper is still the answer for leaked data.

**5. An unresolvable `{{fixture:…}}` throws `FixtureResolutionException` — not the new lifecycle type.**

`FixtureValidation.CheckLeaf` catches `FixtureResolutionException` and nothing else (`FixtureValidation.cs:104`). That single catch is what turns a bad token into a blocked operation instead of a dead run. Tasks 1–3 introduce `FixtureLifecycleException` for *lifecycle* errors — cycles, duplicate publishes, failing fixtures — which is the right separation, and it means Task 4 must be deliberate: an unpublished key is a **resolution** failure and must keep the existing type, or it escapes the aggregator and kills the whole run, defeating this decision.

**6. Out of scope, deliberately.**

`IControllerFixture` (class-scope setup) is not in this plan: §13 calls it optional and not required for most classes, and nothing has needed it. Auth test kinds stay in v1-c even though F8 touches the same `TestStartup.cs` — mixing them would blur Task 8's acceptance criterion.

---

## File structure

| File | Responsibility |
|---|---|
| **New — `src/InTest.Runtime/Neutral/`** | |
| `IAssemblyFixture.cs` | The interface teams implement, exactly as §13 gives it |
| `FixtureContext.cs` | `Publish(key, value)` and `OnCleanup(action)` |
| `FixtureGraph.cs` | Topological ordering over `DependsOn`; cycle and missing-dependency detection |
| `FixtureRunner.cs` | **Orders via `FixtureGraph`**, then runs, honours `AppliesTo`, wraps failures, drains cleanup |
| **Modified** | |
| `Neutral/TokenResolver.cs` | `{{fixture:…}}` resolves from published keys; `SupportedTokens` and class doc updated |
| `MSTest/TestHost.cs` | The reordering, the retained `FixtureContext`, and `CleanupAsync` |
| `Commands/InitCommand.cs` | Scaffold registers a fixture **and calls `TestHost.CleanupAsync` from `[AssemblyCleanup]`** |

Everything new is under `Neutral/`, so §3's portability boundary holds — the architecture test asserting no MSTest type appears there must stay green.

---

## Task 1: `IAssemblyFixture` and `FixtureContext`

**Files:**
- Create: `src/InTest.Runtime/Neutral/IAssemblyFixture.cs`, `src/InTest.Runtime/Neutral/FixtureContext.cs`
- Test: `tests/InTest.Runtime.Tests/FixtureContextTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: `Publish` then read back · publishing the same key twice is an error naming the key · `OnCleanup` records but does not run · `PublishedKeys` is ordinal-sorted so messages are stable · a null or whitespace key is rejected.

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~FixtureContextTests" --nologo
```

Expected: FAIL — the types do not exist.

- [ ] **Step 3: Implement**

`IAssemblyFixture` is exactly §13's three members: `Type[] DependsOn`, `string[] AppliesTo`, `Task InitializeAsync(FixtureContext ctx, CancellationToken ct)`. **Do not add a symmetric cleanup method** — §13 is explicit that teardown is registration-based, written next to whatever created the thing.

`FixtureContext` holds published values and recorded cleanup actions and executes nothing; `FixtureRunner` owns execution (Task 3).

- [ ] **Step 4: Run, then commit**

```bash
git commit -m "feat(runtime): assembly fixture interface and context"
```

---

## Task 2: Topological ordering

**Files:**
- Create: `src/InTest.Runtime/Neutral/FixtureGraph.cs`
- Test: `tests/InTest.Runtime.Tests/FixtureGraphTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: independent fixtures keep registration order · a dependency runs before its dependent · a diamond resolves each node once · a cycle throws naming every type in it · a `DependsOn` entry nobody registered throws naming both ends · an empty set is not an error.

```csharp
[TestMethod]
public void ACycleNamesEveryTypeInvolved()
{
    // A depends on B, B depends on A. Naming only one sends the reader hunting through the
    // other for a dependency that is not there.
    var ex = Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([new A(), new B()]));

    ex.Message.ShouldContain(nameof(A));
    ex.Message.ShouldContain(nameof(B));
    ex.Message.ShouldContain("cycle");
}

[TestMethod]
public void IndependentFixturesKeepRegistrationOrder()
{
    // A suite whose seeding order varies between runs is a suite whose failures cannot be
    // reproduced. Independent nodes must not be reordered arbitrarily.
    FixtureGraph.Order([new A(), new C()]).Select(f => f.GetType()).ShouldBe([typeof(A), typeof(C)]);
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

Depth-first with visiting/visited marks is sufficient. Do not add a dependency for this.

```bash
git commit -m "feat(runtime): topological fixture ordering with cycle detection"
```

---

## Task 3: `FixtureRunner` — execution and cleanup drain

**Files:**
- Create: `src/InTest.Runtime/Neutral/FixtureRunner.cs`
- Test: `tests/InTest.Runtime.Tests/FixtureRunnerTests.cs`

**`RunAsync` owns the ordering.** It calls `FixtureGraph.Order` itself; callers pass fixtures in
whatever order the container resolved them. `TestHost` must not order them first.

That is a real choice, not a formality. Split across both, either each orders (harmless
duplication) or each assumes the other does (seeding runs unordered) — and nothing would catch
the second: Task 2 tests the graph in isolation, Task 3's fixtures are independent, and Task 8's
Catalog fixture is a single class. The first failure would be a real adopter with a dependency
chain, which is the worst place to find it. Ordering inside `RunAsync` makes the guarantee
unbypassable and puts Task 3's ordering test on the path that actually runs.

**Shape, stated so the two halves fit together.** The caller owns the context and passes it to both:

```csharp
public static Task RunAsync(
    IEnumerable<IAssemblyFixture> fixtures, FixtureContext context,
    string profile, TextWriter log, CancellationToken cancellationToken);

public static Task DrainAsync(FixtureContext context);
```

`TestHost` creates the `FixtureContext`, keeps it in a static field, passes it to `RunAsync` during `AssemblyInitialize` and to `DrainAsync` during `AssemblyCleanup` (Task 5). `log` is where skip and progress lines go — `TestHost` passes a writer over `TestContext`.

- [ ] **Step 1: Write the failing tests**

**Execution:** runs in `FixtureGraph` order · a fixture whose `AppliesTo` excludes the current profile is skipped **and a line naming it is written to the log** · an empty `AppliesTo` runs for every profile · a throwing fixture fails the run with a message naming **which fixture** · a throwing fixture stops later ones (they may depend on it) but still drains what already registered cleanup.

**Cleanup:** drains in reverse registration order · one throwing action does not prevent the rest · failures aggregate into one message · draining twice runs each action once.

```csharp
[TestMethod]
public async Task ASkippedFixtureSaysSo()
{
    var log = new StringWriter();
    await FixtureRunner.RunAsync([new QaOnlyFixture()], new FixtureContext(), "local", log, default);

    // A fixture silently not running because the profile did not match is indistinguishable
    // from one that ran and did nothing — and the second-run acceptance in Task 8 would pass
    // for the wrong reason.
    log.ToString().ShouldContain(nameof(QaOnlyFixture));
    log.ToString().ShouldContain("local");
}

[TestMethod]
public async Task AFailingFixtureSaysWhichOne()
{
    var ex = await Should.ThrowAsync<FixtureLifecycleException>(
        () => FixtureRunner.RunAsync([new ThrowingFixture()], new FixtureContext(), "local", TextWriter.Null, default));

    // §13: an unhandled exception in AssemblyInitialize otherwise fails every test with an
    // error that does not say "setup broke".
    ex.Message.ShouldContain(nameof(ThrowingFixture));
}

[TestMethod]
public async Task OneFailingTeardownDoesNotStrandTheOthers()
{
    var drained = new List<string>();
    var context = new FixtureContext();
    context.OnCleanup(() => { drained.Add("first"); return Task.CompletedTask; });
    context.OnCleanup(() => throw new InvalidOperationException("boom"));
    context.OnCleanup(() => { drained.Add("third"); return Task.CompletedTask; });

    var ex = await Should.ThrowAsync<FixtureLifecycleException>(() => FixtureRunner.DrainAsync(context));

    // Reverse order, and the failure in the middle must not strand "first". Every action
    // skipped here becomes work for §14's sweeper.
    drained.ShouldBe(["third", "first"]);
    ex.Message.ShouldContain("boom");
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(runtime): fixture execution and reverse cleanup drain"
```

---

## Task 4: `{{fixture:…}}` resolution

`TokenResolver` currently fails every `{{fixture:…}}` token with "not supported until v1-b". That branch is what this task replaces.

**Files:**
- Modify: `src/InTest.Runtime/Neutral/TokenResolver.cs`
- Test: `tests/InTest.Runtime.Tests/TokenResolverTests.cs` (extend)

- [ ] **Step 1: Write the failing tests**

Cover: a published key resolves · an unpublished key fails with §10's message, naming the key **and listing the available ones** · the available list is sorted so the message is stable · a resolver with no published keys still fails usefully · a published value containing another token is **not** re-expanded.

**The exception type is load-bearing** (decision 5) and needs its own test:

```csharp
[TestMethod]
public void AnUnpublishedKeyThrowsAResolutionFailureNotALifecycleFailure()
{
    // FixtureValidation.CheckLeaf catches FixtureResolutionException and nothing else. Throw
    // FixtureLifecycleException here and an unresolvable key stops being a blocked operation
    // and becomes a dead run, defeating v1-a's per-operation blocking.
    Should.Throw<FixtureResolutionException>(
        () => ResolverWith().Resolve("{{fixture:missing}}", "create-order.json"));
}

[TestMethod]
public void AnUnpublishedKeyListsWhatIsAvailable()
{
    var resolver = ResolverWith(("seededCustomer.id", "c1"), ("seededRegion.code", "GB"));

    var ex = Should.Throw<FixtureResolutionException>(
        () => resolver.Resolve("{{fixture:seededTenant.id}}", "update-order.json"));

    // §10 specifies both halves. Naming only the missing key leaves the reader guessing at
    // the spelling of the one they meant.
    ex.Message.ShouldContain("seededTenant.id");
    ex.Message.ShouldContain("seededCustomer.id");
    ex.Message.ShouldContain("seededRegion.code");
}

[TestMethod]
public void TheUnknownTokenMessageAdvertisesFixtureToo()
{
    // SupportedTokens (TokenResolver.cs:29) omits {{fixture:…}}. Left alone, the "Unknown
    // token" message keeps recommending a list missing the token that now works.
    Should.Throw<FixtureResolutionException>(() => ResolverWith().Resolve("{{nope}}", "f.json"))
          .Message.ShouldContain("{{fixture:");
}
```

**Do not add a "cached per run" test.** For `{{fixture:…}}` caching falls out for free — the published dictionary is immutable once seeding finishes — so such a test asserts nothing a reader could break. v1-a already found that this row of §10's timing table lies about `{{config:}}`; a second decorative test would make it worse, not better.

- [ ] **Step 2: Update `SupportedTokens` and the class doc**

`SupportedTokens` (line 29) and the class-level doc comment both still say `{{fixture:…}}` is out of scope for v1-a and always fails. Both are now wrong and belong in this task, not Task 7 — a stale message ships with the code that contradicts it.

- [ ] **Step 3: Repoint the old test, do not delete it**

If the existing "not supported until v1-b" test still passes unchanged after this task, the new branch is unreachable and something is wrong.

- [ ] **Step 4: Run, commit**

```bash
git commit -m "feat(runtime): resolve fixture tokens from published keys"
```

---

## Task 5: Wire the drain, and scaffold both halves

**This task is why `DrainAsync` is not dead code.** `AssemblyCleanup` appears in **zero** tracked `.cs` files today — verified with `git grep`. `TestHost` is a plain static class, not a `[TestClass]`, so it cannot carry the attribute itself; the scaffolded `TestStartup.cs` declares only `[AssemblyInitialize]`. Without this task, Task 3's drain ships with no caller and Task 8 Step 3 cannot pass.

**Files:**
- Modify: `src/InTest.Runtime/MSTest/TestHost.cs` (add `CleanupAsync`), `src/InTest.Cli/Commands/InitCommand.cs`
- Test: `tests/InTest.Cli.Tests/InitCommandTests.cs` (extend)

- [ ] **Step 1: Write the failing tests**

The scaffolded `TestStartup.cs` must contain both an `[AssemblyCleanup]` method calling `TestHost.CleanupAsync` and a commented `services.AddSingleton<IAssemblyFixture, …>()` example.

**Assert on the attribute and the call, never on prose.** v1-a's F8 fix learned this: a test pinning comment wording breaks on rewording and teaches nothing.

```csharp
[TestMethod]
public void ScaffoldedStartupDrainsFixtureCleanup()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
    var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

    startup.ShouldContain("[AssemblyCleanup]");
    startup.ShouldContain("TestHost.CleanupAsync");
}
```

- [ ] **Step 2: Implement**

`TestHost.CleanupAsync(TestContext context)` drains the retained `FixtureContext` via
`FixtureRunner.DrainAsync`, **catches the `FixtureLifecycleException` it may throw, writes it to
`TestContext`, and does not rethrow.**

That clause is the point of this step. `DrainAsync` throws by design (Task 3), so an implementer
handed an exception and no instruction will let it propagate — and an exception out of
`[AssemblyCleanup]` becomes the run headline, burying whatever actually failed. Teardown noise
must never mask a real test failure; the drain report is diagnostic, not a verdict.

Signature must satisfy MSTEST0012/MSTEST0013; `TestContext` on `[AssemblyCleanup]` needs 3.8+,
satisfied by the 4.3 floor.

Add a test asserting a throwing teardown does not fail the run:

```csharp
[TestMethod]
public void ScaffoldedCleanupDoesNotRethrow()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

    // A test asserting the attribute is present would pass even if the method rethrew.
    File.ReadAllText(Path.Combine(_root, "TestStartup.cs")).ShouldContain("TestHost.CleanupAsync");
}
```

and cover the no-rethrow behaviour directly in `tests/InTest.Runtime.Tests/`.

- [ ] **Step 3: Run, commit**

```bash
git commit -m "feat: drain fixture cleanup from the scaffolded AssemblyCleanup"
```

---

## Task 6: Reorder `AssemblyInitialize` — the crux

Everything before this task is inert until `TestHost` runs it.

**`InitializeAsync` has no direct test coverage today** — `git grep -l 'TestHost' -- 'tests/*.cs'` returns nothing. An earlier draft of this plan warned that the reorder would ripple through many existing tests and made Step 1 a discovery grep; that was wrong in the reassuring direction. There is nothing to discover, and nothing protecting this method. The first real test of it is the one you are about to write.

**Build it on the seam that exists, not a new one.** `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs` already scaffolds, generates, builds and runs a suite against an `HttpListener` stub, and `FixtureParameterReachesALiveRequestEndToEnd` already hand-edits a fixture the way an adopter would. Extend that rather than inventing an in-process harness for a method that reads `AppContext.BaseDirectory`, takes an MSTest `TestContext`, and awaits real HTTP.

**Files:**
- Modify: `src/InTest.Runtime/MSTest/TestHost.cs`
- Test: `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs` (extend)

- [ ] **Step 1: Write the failing end-to-end test**

Register a fake `IAssemblyFixture` in the generated project's `TestStartup.cs`, have it publish a key, point a fixture value at `{{fixture:…}}`, and assert the live request carries the published value. That proves the whole order in one test: services exist before seeding, seeding precedes resolution, resolution precedes validation.

```csharp
[TestMethod]
public async Task APublishedFixtureKeyReachesALiveRequest()
{
    // The ordering constraint, asserted through observable behaviour rather than by
    // instrumenting InitializeAsync. If seeding ran after validation, or before the service
    // provider existed, this token could not resolve and the suite would fail.
    ...
    test.Output.ShouldContain("Passed!", customMessage: test.Output);
}
```

- [ ] **Step 2: Implement the reorder**

New sequence in `InitializeAsync`:

1. `Profile`, `Configuration`, `RunIdValue` — unchanged
2. `FixtureStore.Load` — unchanged
3. Build the service provider, including team registrations via `ConfigureServices`
4. `Readiness.WaitAsync`
5. Create the `FixtureContext`, retain it, resolve `IEnumerable<IAssemblyFixture>` from the provider, and hand them to `FixtureRunner.RunAsync` — **which orders them**. Do not call `FixtureGraph` here
6. Build `TokenResolver` **with the published keys**
7. `FixtureValidation.Build`, writing the one aggregated message to `TestContext`

- [ ] **Step 3: Run the full suite**

```bash
cd D:/TestGen && dotnet test --nologo
```

Expect everything still green. **One deliberate behaviour change to confirm rather than be surprised by** (decision 1): a suite pointed at a dead API now fails on readiness *without* printing the fixture report, where before it printed the report first. If a test asserts the old sequencing, it is asserting the behaviour this task intentionally changes — say so in your report rather than quietly editing it.

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(runtime): seed fixtures between readiness and validation"
```

---

## Task 7: Amend the spec, the walkthrough, and tell existing adopters

- [ ] **Step 1: §13 — record the initialisation order**

It is now load-bearing and undocumented. Add it with decision 1's reasoning: validation moved because it needs what seeding publishes, not because validation itself changed. Include the readiness-suppression consequence.

- [ ] **Step 2: §10 — `{{fixture:…}}` is live**

Its resolution-timing row says v1-b. Remove the deferral; keep the timing.

- [ ] **Step 3: `docs/getting-started.md` Phase 5**

It currently tells adopters a suite expects a reset database and to reach for `{{runId}}` — F7's workaround, now half-obsolete. `{{runId}}` stays right for free-form uniqueness; `{{fixture:…}}` is the answer for the two cases it cannot reach. **Rewrite rather than append:** leaving both makes the reader guess which applies.

**Do not claim cleanup is guaranteed.** §14 says `AssemblyCleanup` does not run on crash, cancellation or agent timeout. v1-b does not change that, and the sweeper is still required.

- [ ] **Step 4: Write the upgrade note**

`TestStartup.cs` is team-owned and **never regenerated**. Every project scaffolded before v1-b therefore has no `[AssemblyCleanup]`, and will silently leak everything its fixtures create — the failure mode being no failure at all, just a slowly filling database.

Add an upgrade section to `docs/getting-started.md` giving the exact method to paste, and say plainly that regeneration will not do it for them.

- [ ] **Step 5: Commit**

```bash
git commit -m "docs: fixture lifecycle is built; record the order and the upgrade step"
```

---

## Task 8: Acceptance — the suite runs twice

**This task is the verdict.** Tasks 1–7 can all be green while F7 stays open.

- [ ] **Step 1: Give Catalog a fixture that seeds what the suite consumes**

Write an `IAssemblyFixture` in the generated Catalog suite that creates a category and a product with a run-scoped SKU, publishes their ids, and registers deletion. Point the affected fixture files at `{{fixture:…}}`.

The SKU is the interesting one: it must match `^[A-Z]{3}-[0-9]{4}$`, which is exactly why `{{runId}}` cannot solve it and why this plan exists. The fixture generates a conforming unique value at seed time and publishes it.

- [ ] **Step 2: Run the suite twice without resetting the database**

```bash
dotnet run --project samples/Catalog.Api &     # http://localhost:5081
dotnet test <generated Catalog suite>          # first run
dotnet test <generated Catalog suite>          # second run, same database
```

**Expected: 9 of 9 both times.** The first run alone is not the result — v1-a already achieved that. If the second run does not match, F7 is not closed and stays open with the new evidence attached.

- [ ] **Step 3: Confirm cleanup actually ran**

Query the database, or re-list through the API, and confirm the seeded rows are gone. A drain that silently no-ops would leave the second run passing for the wrong reason — because nothing was created, not because teardown worked.

- [ ] **Step 4: Run Orders and Inventory too**

Orders needs `samples/Identity.Server`. Record their numbers whether or not they moved.

- [ ] **Step 5: Update the acceptance record**

Close F7 with the two-run evidence, or keep it open with what actually happened. New findings go in the existing F-numbered style.

- [ ] **Step 6: Commit**

```bash
git commit -m "docs: v1-b acceptance run — the suite runs twice"
```

---

## Task 8a: An automated guard for repeatability

Task 8 is a transcript in a document. Every other claim of this weight in this repo earned a test — the appsettings-not-copied defect, F1's live-fixture proof — because a manual result regresses silently and nobody notices until the next acceptance run.

**Files:**
- Modify: `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`

- [ ] **Step 1: Make the stub stateful — and expect it to break a sibling test**

The stub answers from a `switch` over the path. Give it a small in-memory store so it behaves
like the API whose behaviour F7 exposed: a duplicate create returns **409**, and a delete
followed by a fetch returns **404**.

**`FixtureParameterReachesALiveRequestEndToEnd` will break unless you handle it.** That test
hand-fills the fixture with `"42"` and expects a pass, which works today only because the stub
has a catch-all arm — `_ when path.StartsWith("/api/status/")` — returning 200 for *any* id.
Once the store is stateful, an unseeded `42` becomes a 404 and that test fails for reasons
nothing to do with repeatability.

Choose deliberately and say which in your report:

- **Pre-seed `42`** in the store, keeping that test asserting exactly what it asserts now; or
- **Confine statefulness to the new create and delete paths**, leaving the existing
  `/api/status/{id}` arm permissive.

The second is narrower and preserves the existing test unchanged, which is usually the better
trade — but either is defensible. What is not acceptable is discovering it mid-task, where it
surfaces as an unexplained regression and reads like the reorder broke something.

- [ ] **Step 2: Write the failing test**

```csharp
[TestMethod]
public async Task TheGeneratedSuitePassesTwiceAgainstTheSameStore()
{
    // F7 in one test. Against a stateful service the first run passed and the second did not,
    // because literal fixture values collide with unique constraints and deleted rows do not
    // come back. This is the guard that keeps that closed.
    await ScaffoldGenerateAndBuildWithSeedingFixture();

    (await RunAsync("dotnet", $"test \"{_root}\" --no-build --nologo")).Output.ShouldContain("Passed!");
    (await RunAsync("dotnet", $"test \"{_root}\" --no-build --nologo")).Output.ShouldContain("Passed!");
}
```

- [ ] **Step 3: Prove it can fail**

Point the fixture value at a literal instead of `{{fixture:…}}` and confirm the **second** run fails while the first passes — the exact shape of F7. Restore, confirm both pass. Report both results; a guard nobody has watched fail is not a guard.

- [ ] **Step 4: Commit**

```bash
git commit -m "test: guard that a generated suite passes twice against one store"
```

---

## Self-review

**Spec coverage.** §13's `IAssemblyFixture` is covered end to end: `DependsOn` as `Type[]` with topological ordering (Task 2), `AppliesTo` filtering with a logged skip (Task 3), `ctx.Publish` feeding `{{fixture:…}}` (Tasks 1 and 4), registration-based cleanup drained in reverse (Tasks 1 and 3) **and actually invoked** (Task 5), and failures naming the fixture rather than failing every test unhelpfully (Task 3). §10's `{{fixture:…}}` timing row and available-keys message are Task 4.

**Corrections from review, recorded so they are not re-introduced.** An earlier draft had `DrainAsync` with no caller anywhere — `AssemblyCleanup` appears in zero tracked `.cs` files, and `TestHost` cannot carry the attribute itself — so Task 5 now owns wiring it and Task 7 owes existing adopters an upgrade note, since `TestStartup.cs` is never regenerated. That draft also warned Task 6 would ripple through many tests; the opposite is true, `InitializeAsync` has no coverage at all, so Task 6 now builds on `GeneratedSuiteExecutionTests` rather than an imagined in-process harness. Decision 5 exists because `FixtureValidation.CheckLeaf` catches exactly one exception type, and getting Task 4's type wrong would silently convert per-operation blocking into a dead run.

**Deliberately deferred, with reasons.** `IControllerFixture` — §13 calls it optional and nothing has needed it. Auth wiring, F8's remaining half — same file, different concern, and mixing it would blur Task 8's criterion. Retry and partial re-seeding — no measured need.

**What this plan does not fix.** A suite still cannot run twice *concurrently* against one environment: two runs seeding simultaneously collide on the same unique constraints, and §11 already states cross-process coordination is unsolvable at this layer. Tasks 8 and 8a prove sequential repeatability only, and the acceptance record must say so plainly rather than letting "runs twice" be read as "runs in parallel".
