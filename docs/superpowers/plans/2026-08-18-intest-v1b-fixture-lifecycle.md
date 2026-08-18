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

Validation still happens exactly once and still reports everything in one aggregated message — v1-a decision 2 is unchanged. It simply happens later, when it has the information it needs. **Every existing test touching the current order needs revisiting;** Task 6 owns that and starts by finding them.

**2. Fixtures are discovered by DI registration, never by reflection.**

A team writes `services.AddSingleton<IAssemblyFixture, DatabaseSeed>()` in the `TestStartup.cs` they already own. Assembly scanning would be less typing and more magic: it would run a class the moment someone writes it, make ordering depend on reflection order, and offer no way to register one conditionally. The `ConfigureServices` hook exists for exactly this.

**3. Ordering is topological over `DependsOn`, and a cycle fails loudly.**

§13 is explicit that integer ordering is the thing everyone regrets — someone always needs to slot between 15 and 20. `DependsOn` is `Type[]`. A cycle, or a dependency on a type nobody registered, fails at `AssemblyInitialize` naming the types involved rather than silently running in some arbitrary order.

**4. Cleanup is registration-based and best-effort, and says so.**

`ctx.OnCleanup(...)` registers teardown next to the thing that created it; `AssemblyCleanup` drains in reverse registration order. Three properties are not optional:

- **One failing teardown must not strand the others.** Every remaining action still runs; failures aggregate into one report.
- **Teardown must be idempotent.** §14 already requires this of consumers, and the drain does not retry.
- **`AssemblyCleanup` does not run on crash, cancellation or agent timeout.** §14 already says so, v1-b does not change it, and the docs must not imply otherwise. The out-of-band sweeper is still the answer for leaked data.

**5. An unresolvable `{{fixture:…}}` is a validation failure, not a crash.**

An unpublished key produces §10's message — naming the key, and listing what *is* available — and blocks only the operations whose fixtures reference it, exactly as an unfilled `TODO:` sentinel does today. v1-a's per-operation blocking extends to this unchanged.

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
| `FixtureRunner.cs` | Runs ordered fixtures, honours `AppliesTo`, wraps failures, drains cleanup |
| **Modified** | |
| `Neutral/TokenResolver.cs` | `{{fixture:…}}` resolves from published keys instead of always failing |
| `MSTest/TestHost.cs` | The reordering (decision 1), plus `AssemblyCleanup` draining |
| `Commands/InitCommand.cs` | Scaffold shows registering an `IAssemblyFixture` |

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

Cover: independent fixtures keep registration order · a dependency runs before its dependent · a diamond resolves each node once · a cycle throws naming every type in it · a `DependsOn` entry nobody registered throws naming both the dependent and the missing type · an empty set is not an error.

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

[TestMethod]
public void AMissingDependencyNamesBothEnds()
{
    Should.Throw<FixtureLifecycleException>(() => FixtureGraph.Order([new DependsOnUnregistered()]))
          .Message.ShouldContain(nameof(DependsOnUnregistered));
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

- [ ] **Step 1: Write the failing tests**

**Execution:** runs in `FixtureGraph` order · a fixture whose `AppliesTo` excludes the current profile is skipped · an empty `AppliesTo` runs for every profile · a throwing fixture fails the run with a message naming **which fixture** · a throwing fixture stops later ones (they may depend on it) but still drains what already registered cleanup.

**Cleanup:** drains in reverse registration order · one throwing action does not prevent the rest · failures aggregate into one message · draining twice runs each action once.

```csharp
[TestMethod]
public async Task AFailingFixtureSaysWhichOne()
{
    var ex = await Should.ThrowAsync<FixtureLifecycleException>(
        () => FixtureRunner.RunAsync([new ThrowingFixture()], "local", CancellationToken.None));

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

Cover: a published key resolves · an unpublished key fails with §10's message, naming the key **and listing the available ones** · the available list is sorted so the message is stable · a resolver with no published keys still fails usefully · resolution is cached per run, matching §10's timing table · a published value containing another token is **not** re-expanded (no recursive substitution).

```csharp
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
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

**Repoint the existing "not supported until v1-b" test rather than deleting it.** If it still passes unchanged after this task, the new branch is unreachable and something is wrong.

```bash
git commit -m "feat(runtime): resolve fixture tokens from published keys"
```

---

## Task 5: Scaffold registers a fixture

**Files:**
- Modify: `src/InTest.Cli/Commands/InitCommand.cs`
- Test: `tests/InTest.Cli.Tests/InitCommandTests.cs` (extend)

- [ ] **Step 1: Write the failing tests**

The scaffolded `TestStartup.cs` shows `services.AddSingleton<IAssemblyFixture, …>()` commented out with a worked example, and must not imply reflection-based discovery.

**Assert on the registration call and the interface name, never on prose.** v1-a's F8 fix learned this the hard way: a test pinning comment wording breaks on rewording and teaches nothing.

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(cli): scaffold shows registering an assembly fixture"
```

---

## Task 6: Reorder `AssemblyInitialize` — the crux

Everything before this task is inert until `TestHost` runs it.

**Files:**
- Modify: `src/InTest.Runtime/MSTest/TestHost.cs`
- Test: `tests/InTest.Runtime.Tests/`, `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`

- [ ] **Step 1: Find every test that depends on the current order — before changing anything**

```bash
cd D:/TestGen && grep -rln "TestHost\|FixtureValidationReport\|FixtureTokens" tests/
```

Name each in your report with what it assumes. A test asserting validation happens before the service provider exists is asserting the bug this task fixes, and changes. A test asserting validation happens *once* does not.

- [ ] **Step 2: Write the failing test for the new order**

```csharp
[TestMethod]
public async Task SeedingRunsAfterReadinessAndBeforeValidation()
{
    // The whole ordering constraint in one assertion. Seeding needs a client and a live
    // service; validation needs the keys seeding publishes. Getting this backwards makes
    // {{fixture:…}} unresolvable no matter how correct Task 4 is.
    var order = await RunInitializeRecordingOrder();

    order.ShouldBe(["services", "readiness", "fixtures", "validation"]);
}
```

- [ ] **Step 3: Implement the reorder**

New sequence in `InitializeAsync`:

1. `Profile`, `Configuration`, `RunIdValue` — unchanged
2. `FixtureStore.Load` — unchanged
3. Build the service provider, including team registrations via `ConfigureServices`
4. `Readiness.WaitAsync`
5. Resolve every registered `IAssemblyFixture`, order via `FixtureGraph`, run via `FixtureRunner`
6. Build `TokenResolver` **with the published keys**
7. `FixtureValidation.Build`, writing the one aggregated message to `TestContext`

`AssemblyCleanup` calls `FixtureRunner.DrainAsync`. **A drain failure must not mask a test failure that already happened** — report it rather than throwing over the top of a more interesting error.

- [ ] **Step 4: Run the full suite**

```bash
cd D:/TestGen && dotnet test --nologo
```

Every previously-passing test stays green except those you deliberately changed in Step 1. Report anything you did not anticipate rather than adjusting it to fit.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(runtime): seed fixtures between readiness and validation"
```

---

## Task 7: Amend the spec and the walkthrough

- [ ] **Step 1: §13 — record the initialisation order**

It is now load-bearing and undocumented. Add it with decision 1's reasoning: validation moved because it needs what seeding publishes, not because validation itself changed.

- [ ] **Step 2: §10 — `{{fixture:…}}` is live**

Its resolution-timing row says v1-b. Remove the deferral; keep the timing.

- [ ] **Step 3: `docs/getting-started.md` Phase 5**

It currently tells adopters a suite expects a reset database and to reach for `{{runId}}` — F7's workaround, now half-obsolete. `{{runId}}` stays right for free-form uniqueness; `{{fixture:…}}` is the answer for the two cases it cannot reach. **Rewrite rather than append:** leaving both makes the reader guess which applies.

**Do not claim cleanup is guaranteed.** §14 says `AssemblyCleanup` does not run on crash, cancellation or agent timeout. v1-b does not change that, and the sweeper is still required.

- [ ] **Step 4: Commit**

```bash
git commit -m "docs: fixture lifecycle is built; record the initialisation order"
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

**Expected: 9 of 9 both times.** The first run alone is not the result — v1-a already achieved that. If the second run does not match the first, F7 is not closed, and it stays open with the new evidence attached.

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

## Self-review

**Spec coverage.** §13's `IAssemblyFixture` is covered end to end: `DependsOn` as `Type[]` with topological ordering (Task 2), `AppliesTo` profile filtering (Task 3), `ctx.Publish` feeding `{{fixture:…}}` (Tasks 1 and 4), registration-based cleanup drained in reverse (Tasks 1 and 3), and failures naming the fixture rather than failing every test unhelpfully (Task 3). §10's `{{fixture:…}}` timing row and its available-keys message are Task 4.

**Deliberately deferred, with reasons.** `IControllerFixture` — §13 calls it optional and nothing has needed it. Auth wiring, F8's remaining half — same file, different concern, and mixing it would blur Task 8's criterion. Retry and partial re-seeding — no measured need.

**The risk worth stating.** Task 6 reorders code every existing runtime test touches, and this plan cannot list which tests break without running them. Step 1 exists to discover that rather than pretend it is known. If the count is large, that is information about coupling in `TestHost`, not a reason to push through — report it before proceeding.

**What this plan does not fix.** A suite still cannot run twice *concurrently* against one environment: two runs seeding simultaneously collide on the same unique constraints, and §11 already states cross-process coordination is unsolvable at this layer. Task 8 proves sequential repeatability only, and the acceptance record must say so plainly rather than letting "runs twice" be read as "runs in parallel".
