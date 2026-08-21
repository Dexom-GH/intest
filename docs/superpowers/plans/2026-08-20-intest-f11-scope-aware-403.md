# InTest F11 — Scope-Aware Wrong-Scope 403 Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop generating a wrong-scope 403 test that cannot pass. An operation whose scope the Secondary identity legitimately holds must **skip with a stated reason**, not fail.

**Architecture:** The CLI already reads each operation's declared scopes; the runtime already has a guard shape for "the provider cannot do this" (`RequireMultipleIdentities`). This plan connects the two — the case carries the scopes the *spec* requires, the provider describes the scopes the *identity* holds, and a runtime guard compares them.

**Tech Stack:** Unchanged — net10.0 · MSTest 4.3.3 · Microsoft.OpenApi 3.10.0.

**Spec:** [`../specs/2026-08-16-intest-api-test-generator-design.md`](../specs/2026-08-16-intest-api-test-generator-design.md), §9's auth table and its precondition section.

**Prerequisite:** v1-c complete and merged. 398 tests passing.

**Closes:** F11 (`docs/v0-acceptance.md`).

---

## Decisions are named, not numbered

v1-c numbered its decisions and the references drifted three times, twice inside one document — inserting a decision silently invalidated every reference after it. This plan names them: **[containment]**, **[descriptor]**, **[unknown-runs]**, **[counted]**, **[sample-unchanged]**. A reference is a word, so inserting or reordering cannot break one.

---

## How to read the sample tests in this plan

**Every test snippet below is a sketch of intent, not code to paste.** Write the test, run it, and confirm it fails *for the reason the method name states* before writing any implementation. If a snippet cannot be made to fail against an empty implementation, the snippet is wrong — say so in your report rather than working around it.

This matters more than usual here. The failure this plan fixes is a test asserting one status against a guaranteed different one; a fix that turns a failing test into a *skipping* test is one keystroke from a fix that turns it into a **vacuous** one.

---

## The defect, measured

`TestPlanBuilder.PlanAuthCases` emits a `_Forbidden` case for every operation declaring `security`, independent of which scope that operation requires. `RequireMultipleIdentities` gates *whether the case runs* (on identity count); nothing gates *whether it can be true*.

Against `samples/Orders.Api` with `samples/Identity.Server`'s own two clients:

| Operation | Requires | `orders-readonly` holds it? | Generated 403 |
|---|---|---|---|
| `GET /api/customers` | `orders.read` | **yes** | fails — real `200` |
| `GET /api/orders` | `orders.read` | **yes** | fails — real `200` |
| `GET /api/customers/{id}` | `orders.read` | **yes** | fails — real `404` (unmatchable id) |
| `GET /api/orders/{id}` | `orders.read` | **yes** | fails — real `404` |
| `POST /api/customers` | `orders.write` | no | passes |
| `POST /api/orders` | `orders.write` | no | passes |
| `DELETE /api/orders/{id}` | `orders.write` | no | passes |

4 of 7 assert `403` against a status the API is **correct** to return. Not sample-specific: a full-access / read-only identity pair is among the most common real role splits, and it produces this structurally — a read-only identity is never "wrong scope" for a read.

This is §9's precondition failing at the authorization stage: the asserted status is not produced by the stage the case means to test.

---

## Decisions this plan encodes

### [containment] — set containment over the spec's own scope strings, never a heuristic

Scopes are already in the document. Verified by reflection over the Microsoft.OpenApi 3.10.0 assembly:

```
OpenApiOperation.Security      -> IList<OpenApiSecurityRequirement>
OpenApiSecurityRequirement     -> Dictionary<OpenApiSecuritySchemeReference, List<string>>
```

Required scopes are the dictionary's **values**, flattened:

```csharp
operation.Security.SelectMany(r => r.Values).SelectMany(s => s).Distinct()
```

The rule: **skip when the Secondary identity's scopes are a superset of what this operation requires.** No string is inspected for meaning. `orders.read` and `orders.write` read as read and write only because this sample named them so; OpenAPI attaches no semantics to scope strings, and guessing is what §9's declared-only rule exists to prevent.

**The OR/AND approximation, and why "safe direction" is the wrong phrase for it.** OpenAPI's `security` array is a logical **OR** across requirements; the dictionary within one requirement is an **AND**. Flattening both into one set enlarges the required set, so a case skips less often and runs more often.

That is safe against *silently skipping* a real test. It is **not** safe against the failure this plan exists to fix: for a multi-requirement spec, an identity satisfying one alternative is asked to satisfy the union, so a case that should skip **runs and fails against a status the API is correct to return** — F11 itself, one level down. Every sample here is single-requirement, so nothing exercises it today.

Put that in the code comment in those terms. "Approximation, erring toward the safe direction" is how it stops being noticed.

### [descriptor] — an identity is a described thing, not a name with lookups beside it

`Identities` becomes `IReadOnlyList<TestIdentity>`, where a `TestIdentity` carries its own scopes:

```csharp
public sealed record TestIdentity(string Name, IReadOnlyCollection<string>? Scopes = null);
```

**Rejected: `ScopesFor(string identity)` as a default interface method.** It is a parallel lookup keyed by the same strings as `Identities`, so it can disagree with itself — an identity in `Identities` with no scopes entry, or a scopes answer for a name `Identities` does not contain. Neither has a defined meaning, and the second is a question a test would have to declare out of scope rather than answer. A shape that permits a meaningless state is the defect, independent of whether anyone hits it.

The reshape is **free now and not later**, and the interface's own doc comment already says so about its previous change: *"made while nothing outside this repository implements the interface yet — the last point at which it was free. From the first published version onward, this ordering is a semver promise (§3)."* Nothing is published; no project outside this repository has been scaffolded.

Measured cost: **8 implementers**, all in-repo — `StaticTokenProvider` plus 7 doubles across `ApiTestBaseAuthTests`, `ApiTestBaseTests`, `AuthHandlerTests` and `GoldenTokenProviderSources`.

Six are one-line constructors. **`GoldenTokenProviderSources.TwoIdentityTokenProvider` is not** — it is a `const string` of C# **source text** written into a scaffolded project, so reshaping it is a string edit that has to stay compilable inside a *generated* project, and it reads `Identities[0]` inside that text. Budget for it as the one that can fail at a different layer than the rest.

**`AuthHandler` needs no change.** It never reads `Identities` — it checks the `InTestIdentities.None` sentinel and calls `GetTokenAsync`, both untouched by this reshape. Named here because it is the obvious place to look and finding nothing there should not read as an oversight.

The alternative cost is a **major version bump** the moment any further per-identity attribute is wanted, and one is already written down: §9's auth table records wrong-tenant 403 as a future case using the same `Identities[1]` mechanism. Under the rejected shape that is a third parallel lookup; under this one it is an added property.

> **Greenfield argues for the bigger change here, not the smaller one.** The instinct it usually triggers — no consumers, so don't over-engineer — points the wrong way when the thing being designed *is* the public extension point. Earlier in this project greenfield was correctly used to delete work; here it is a reason to spend the freedom once, while it is free.

`GetTokenAsync(string audience, string? identity = null, …)` is **unchanged** and still selects by name — `TestIdentity.Name` is what callers pass. The descriptor enriches how an identity is *described*, not how one is *selected*.

**Names must be unique, and that invariant is stated rather than assumed.** This shape permits a smaller version of the defect it replaces: two entries sharing a `Name` with different `Scopes` is undefined, because the guard reads positionally (`Identities[1]`) while `GetTokenAsync` selects by name, so the two would disagree about which scopes apply. The argument that won this reshape — *a shape that permits a meaningless state is the defect, independent of whether anyone hits it* — applies to it too.

v1-c chose not to validate `Identities` at all, and this plan does not add validation either; the guard's job is to skip a test, not to police a provider. So say it on `TestIdentity`'s own doc comment: names are expected unique, and behaviour is undefined otherwise. **Knowingly unaddressed and written down beats silently permitted** — that is the whole standard [descriptor] is arguing from.

### [unknown-runs] — `Scopes is null` means unknown, and unknown runs the test

| `Scopes` | Meaning | Behaviour |
|---|---|---|
| `null` | not declared | **Runs** — the case is allowed to fail |
| `[]` | declared: holds no scopes | Runs — a real declaration |
| `["orders.read"]` ⊇ required | declared and sufficient | **Skips**, with a stated reason |

The reason is **not** that existing adopters would break — there are none. It is that unknown-means-skip would make the *silent* path the **default** path, and a default outlives any migration window. An adopter who never fills in `Scopes` would get a permanently green suite with auth testing switched off and nothing saying so — the same silent-green failure that made `MemberCondition` unusable for this in v1-c. `null` = run keeps the loud path the default one.

`[]` is deliberately distinct from `null`: a bearer scheme with no scopes (`"security": [{ "bearerAuth": [] }]`) is ordinary, and an identity genuinely holding no scopes is a real state, not an absent answer.

### [counted] — the report counts what it can know, which is not the number of skips

`coverage-report.json` already carries `authTestsGatedOnSecondIdentity` (`CoverageReport.cs`), counting cases gated on whether a second identity exists **at all**. This is a different question — whether that identity is *usable for this operation* — and folding them into one number reproduces §12's bodiless-204 mistake, where a note means one thing and counts another. New key.

**The CLI cannot count actual skips.** Which cases skip is a runtime fact decided by a provider that does not exist at generation time. A key implying a runtime count would be wrong whenever the provider changed, and `generate --check` would report drift on an unchanged spec. It counts how many 403 cases carry scope requirements, and the note text says so — a reader seeing `7` against 3 real 403s needs the report itself to explain the difference.

### [sample-unchanged] — the sample's two clients stay as they are

`samples/Identity.Server/Config.cs`'s doc comment already says `orders-readonly` is "used to prove write endpoints return 403" — built for 3 of the 7, not all 7. After this plan that comment is accurate rather than aspirational. The fix makes InTest match a reasonable identity setup rather than demanding an unreasonable one.

**Rejected: requiring a null-scope Secondary identity.** Measured against the live sample — a `client_credentials` request omitting `scope` returns the client's **entire** allowed set, not none:

```
POST /connect/token  grant_type=client_credentials  client_id=orders-readonly  (no scope)
  aud   : orders-api
  scope : ['orders.read']
```

A null-scope identity cannot be obtained by omission. Because the audience arrives *via* a scope, a genuinely scopeless token carries no `aud` and gets **401 at authentication, not 403 at authorization** — a different failure than the test asserts. Satisfying the requirement would mean adding a scope to the API's own resource definition that no endpoint uses: changing production auth configuration so a test tool's assertion can hold. Wrong direction for a tool that tests the API as deployed.

---

## File structure

| File | Responsibility |
|---|---|
| **New — `src/InTest.Runtime/`** | |
| `Neutral/TestIdentity.cs` | The descriptor: name + optional scopes |
| **Modified — `src/InTest.Runtime/`** | |
| `Neutral/ITestTokenProvider.cs` | `Identities` becomes `IReadOnlyList<TestIdentity>` |
| `Neutral/StaticTokenProvider.cs` | One identity, scopes `null` |
| `MSTest/ApiTestBase.cs` | `RequireSecondaryIdentityLacks(params string[])`; **both** `ResolveIdentitySlot` and `ResolveDefaultIdentity` read `.Name` |
| **Modified — `src/InTest.Cli/`** | |
| `Planning/TestCasePlan.cs` | Carry `RequiredScopes` — empty for every non-auth case |
| `Planning/TestPlanBuilder.cs` | Populate it in `PlanAuthCases` from `operation.Security` |
| `Rendering/Templates/mstest-class.scriban` | Emit the guard on `_Forbidden` cases carrying scopes |
| `Coverage/CoverageReport.cs` | New count, separate from `authTestsGatedOnSecondIdentity` |
| `Commands/InitCommand.cs` | The scaffold's commented-out provider example |
| **Docs** | |
| `README.md`, `docs/getting-started.md` | Status banners — both say "20 of 24 … F11 still open" |
| spec §9, `docs/v0-acceptance.md` | The contract, and F11 closed |

`TestIdentity` is under `Neutral/` and names no MSTest type, so §3's portability boundary holds. The guard is on `ApiTestBase` because `Assert.Inconclusive` is an MSTest type.

---

## Task 1: The `TestIdentity` descriptor

**Files:**
- Create: `src/InTest.Runtime/Neutral/TestIdentity.cs`
- Modify: `ITestTokenProvider.cs`, `StaticTokenProvider.cs`, `ApiTestBase.cs` (**both** `ResolveIdentitySlot` and `ResolveDefaultIdentity`)
- **Not** `AuthHandler.cs` — it never reads `Identities`, only the `InTestIdentities.None` sentinel and `GetTokenAsync`. Finding nothing to change there is correct, not an oversight.
- Test: `tests/InTest.Runtime.Tests/` — 6 one-line test doubles, plus `tests/InTest.Golden.Tests/GoldenTokenProviderSources.cs`, which is emitted **source text**, not a type in the test assembly

- [ ] **Step 1: Write the failing tests**

| Case | Expected |
|---|---|
| `StaticTokenProvider` | One identity, `Name == "default"`, `Scopes` **null** — [unknown-runs] |
| A provider declaring scopes | Round-trips on the descriptor |
| `TestIdentity("a")` | `Scopes` is null, not `[]` — the two differ |
| `ResolveIdentitySlot` | Still resolves Default/Secondary/None, now reading `.Name` |
| A provider whose `Identities` is null | Still guarded — see Task 2 Step 2 |

```csharp
[TestMethod]
public void AnIdentityWithNoDeclaredScopesReportsNullNotEmpty()
{
    // [unknown-runs]: null runs the 403 case, [] declares an identity holding nothing and also
    // runs it — but they are different states and the guard treats them differently. Collapsing
    // them to [] would make every undeclared identity look like a deliberate declaration.
    new TestIdentity("default").Scopes.ShouldBeNull();
}
```

- [ ] **Step 2: Reshape every implementer, and keep `GetTokenAsync` alone**

`GetTokenAsync(string audience, string? identity = null, …)` does **not** change — it selects by name, and `TestIdentity.Name` is what callers pass ([descriptor]). Changing both at once is how a mechanical reshape acquires a behavioural bug.

`StaticTokenProvider`'s existing `ArgumentException` message names the identities it serves; it must keep naming them by **name**, not by rendering a record.

- [ ] **Step 3: Update the interface doc comment**

`Identities`' doc currently justifies `IReadOnlyList` over `IReadOnlyCollection` on ordering, and calls that "the last point at which it was free". That reasoning now covers the descriptor too. Say what index 1 is *for*: some other identity, **whose scopes decide which 403 cases are provable**.

- [ ] **Step 4–6: Run, implement, re-run, commit**

```bash
git commit -m "refactor(runtime)!: identities are described, not named — TestIdentity carries scopes"
```

---

## Task 2: The runtime guard

**Files:**
- Modify: `src/InTest.Runtime/MSTest/ApiTestBase.cs`
- Test: `tests/InTest.Runtime.Tests/`

- [ ] **Step 1: Write the failing tests**

`RequireSecondaryIdentityLacks(params string[] requiredScopes)` — `protected internal static`, matching `RequireMultipleIdentities` for the same two reasons: `protected` so the generated suite in another assembly can call it, `internal` so `InTest.Runtime.Tests` can reach it directly.

| Secondary's `Scopes` | Operation requires | Behaviour |
|---|---|---|
| `null` | anything | **Runs** — [unknown-runs] |
| `["orders.read"]` | `orders.read` | **Skips**, message names identity and scope |
| `["orders.read"]` | `orders.write` | Runs |
| `["orders.read"]` | `orders.read`, `orders.write` | Runs — holds one of two; containment is over the whole set |
| `[]` | `orders.write` | Runs — a real declaration |
| No provider / one identity | anything | Runs — `RequireMultipleIdentities` owns that skip; **never skip twice for one reason** |

```csharp
[TestMethod]
public void PartialScopeOverlapStillRunsTheTest()
{
    // Holding one of two required scopes does not authorize the operation, so a 403 is still
    // provable. Must fail against an `Any` implementation — the easy wrong version of this.
    TestHost.TokenProvider = new FakeProvider([
        new TestIdentity("default"),
        new TestIdentity("readonly", ["orders.read"])]);

    Should.NotThrow(() => Probe.RequireSecondaryIdentityLacks("orders.read", "orders.write"));
}
```

**`Any` vs `All` is the defect most likely to ship here.** Write this test before the implementation, not after.

- [ ] **Step 2: Never index blind — the rule, not just the row**

v1-c shipped a live `NullReferenceException` on exactly this shape: `RequireMultipleIdentities` guarded the provider but not `Identities`, and it was caught only in the cleanup pass. `ApiTestBase.cs` now carries a comment about `?.Identities?.Count` because of it.

This guard reaches **further** — `Identities[1]` — so it must guard: provider null, `Identities` null, `Count < 2`, and the element itself. Every one of those falls through to **run the test**, never to skip and never to throw.

**Guard like `RequireMultipleIdentities`. Leave `ResolveIdentitySlot` alone.** The two neighbours are deliberately opposite and an implementer cannot match both:

| Member | Style | Why |
|---|---|---|
| `RequireMultipleIdentities` | Guards provider, `Identities`, and count | It **is** the gate — nothing ran before it |
| `ResolveIdentitySlot` | `provider!.Identities[1]`, blind | It runs **after** the gate, in the same generated method body; its doc comment says exactly that |

`RequireSecondaryIdentityLacks` takes the first style — **but not because nothing runs before it.** Task 4 emits it *after* `RequireMultipleIdentities`, in the same method body, so in generated code its provider/`Identities`/count checks are strictly redundant. It guards anyway for two reasons the comment must state, because "it is the gate" is false of it and a reader who checks that claim against the template will conclude the guards are pointless:

- **Its wrong answer is silent.** `ResolveIdentitySlot` failing throws — loud, immediate. This one failing *skips a test*, which looks like success. Redundant guards are cheap; a silently-skipped auth test is the failure this whole plan exists to prevent.
- **It is directly callable outside the generated ordering.** `protected internal` on a shipped base class means an adopter's hand-written 403 test reaches it with no `RequireMultipleIdentities` before it.

Do not "fix" `ResolveIdentitySlot`'s blind index to match — that indexing is correct and documented, and making it defensive would hide a template-ordering bug rather than prevent one.

- [ ] **Step 3: State the message contract**

The skip must name the identity, the scopes it holds, **and the scopes the operation required** — a skip nobody can explain is indistinguishable from a bug, and one that explains wrongly is worse than the failure it replaced.

**Name both sets, not one.** The guard skips on *superset*, not equality, so a message that joins only the held scopes under the predicate "which this operation requires" states something false the moment the identity holds more than the operation needs — the ordinary shape of a read-only identity with several read scopes. `samples/Identity.Server`'s `orders-readonly` escapes it only by holding exactly one scope.

```
Skipped: the secondary identity 'readonly' holds orders.read, products.read — including
orders.read, which this operation requires — so it cannot produce a 403. Declare different
scopes on that identity, or leave Scopes null to run this test anyway.
```

**A test must cover the strict-superset case**, asserting the message does not claim the operation requires the extra scopes. Without it, joining the wrong collection passes every test.

- [ ] **Step 4–6: Run, implement, re-run, commit**

```bash
git commit -m "feat(runtime): skip a wrong-scope 403 the secondary identity is authorized for"
```

---

## Task 3: Carry the required scopes into the plan

**Files:**
- Modify: `src/InTest.Cli/Planning/TestCasePlan.cs`, `src/InTest.Cli/Planning/TestPlanBuilder.cs`
- Test: `tests/InTest.Cli.Tests/TestPlanBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

| Spec | Expected `RequiredScopes` |
|---|---|
| `security: [{ oauth2: ["orders.write"] }]` | `["orders.write"]` on the `_Forbidden` case |
| Two schemes each naming scopes | The union, distinct |
| `security: [{ bearerAuth: [] }]` | **Empty** — secured but scope-free |
| No `security` | No auth cases at all (unchanged) |
| Any non-auth case, and every 401 case | Empty — never null |

The empty row is the one to get right. `"security": [{ "bearerAuth": [] }]` is ordinary and is what `tests/InTest.Golden.Tests/Specs/orders.json` declares today. It must yield an auth case with **no** scopes rather than no case, and the guard must then run the test — nothing to be a superset of, so the identity is never disqualified.

The 401 case never carries scopes: it sends no token, so no scope can make it unprovable.

- [ ] **Step 2: The extraction, verified not assumed**

Measured by reflection over Microsoft.OpenApi 3.10.0 — do not re-derive:

```
OpenApiOperation.Security  -> IList<OpenApiSecurityRequirement>
OpenApiSecurityRequirement -> Dictionary<OpenApiSecuritySchemeReference, List<string>>
```

Scopes are the **values**; the keys are scheme references. Confirmed end to end against `samples/Orders.Api/Orders.Api.json` — `orders.read` on its 4 GETs, `orders.write` on its 2 POSTs and 1 DELETE.

- [ ] **Step 3–5: Run, implement, re-run, commit**

```bash
git commit -m "feat(cli): auth cases carry the scopes their operation declares"
```

---

## Task 4: Render the guard, and prove it over the wire

**Files:**
- Modify: `src/InTest.Cli/Rendering/Templates/mstest-class.scriban`
- Test: `tests/InTest.Cli.Tests/TemplateRendererTests.cs`, `tests/InTest.Golden.Tests/`

- [ ] **Step 1: Write the failing tests**

Ordering is the contract. Assert it by index, the way `TemplateRendererTests`' `CallsRequireFixtureBeforeBuildingTheRequest` already does — **cite the method by name, not by line**: v1-c had three line citations invalidated mid-branch and Task 9 had to convert them all to constructs.

```csharp
RequireMultipleIdentities();                              // 1. is there a second identity at all
RequireSecondaryIdentityLacks("orders.write");            // 2. can it prove anything here
using var _ = UseIdentity(IdentitySlot.Secondary);        // 3. then select it
```

Both guards before the identity override, both before the request is built. A `_Forbidden` case whose `RequiredScopes` is empty emits **only** the first — never a bare `RequireSecondaryIdentityLacks()`, which reads as a scope-free assertion rather than an absent one.

- [ ] **Step 2: Extend both corpora — and keep a scope-free secured operation**

> **`Specs/orders.json` already declares `"security": [{ "bearerAuth": [] }]` on `GET /orders/{id}` — its only secured operation, and scope-free.** Adding scopes to *that* operation deletes the corpus's only coverage of the empty-scope path, which is Task 3's empty row and the template's only-emit-the-first-guard branch. It would also leave [counted]'s two coverage keys unpinned, since they differ **only** on scope-free secured operations.
>
> **Keep `GET /orders/{id}` scope-free. Add `security` *with* scopes to the other operation.**

| Corpus | Add |
|---|---|
| `tests/InTest.Golden.Tests/Specs/orders.json` | Scoped `security` on the second operation; leave the first scope-free |
| The inline spec in `GeneratedSuiteExecutionTests.cs` that v1-c gave `security` | Scopes, and a provider declaring the secondary holds them |

Then the live proof, which is the one that matters:

```csharp
[TestMethod]
public async Task AForbiddenCaseTheSecondaryIdentityIsAuthorizedForSkipsRatherThanFails()
{
    // The whole of F11 in one assertion. Before this plan the generated suite fails here.
    // Assert BOTH: the run has no failures, AND the case is NotExecuted — "no failures" alone
    // would also describe a suite that stopped generating the case at all.
}
```

Assert `outcome="NotExecuted"` in the `.trx`. Measured during v1-c: that is the file's spelling; "Skipped" is only the console summary's word.

- [ ] **Step 3–5: Run, implement, re-run, commit**

```bash
git commit -m "feat(cli): emit the scope guard on wrong-scope 403 cases"
```

---

## Task 5: Coverage report

**Files:**
- Modify: `src/InTest.Cli/Coverage/CoverageReport.cs`
- Test: `tests/InTest.Cli.Tests/CoverageReportTests.cs`

- [ ] **Step 1: Write the failing tests**

A new key, separate from `authTestsGatedOnSecondIdentity` ([counted]), named for what it counts:

```
"authTestsRequiringAnUnderScopedSecondIdentity": 7
```

> **Not "Unauthorized".** In this codebase `_Unauthorized` means **401** and `_Forbidden` means **403** (`TestPlanBuilder` mints both). An adopter reads `coverage-report.json` beside generated methods named `Foo_Unauthorized` and `Foo_Forbidden`, so a key using "Unauthorized" to describe a **403** condition collides with the vocabulary the generated code already established. "UnderScoped" says the actual condition: the identity authenticates fine, it simply holds too few scopes.

**The test that matters is the one that separates the two keys**, and it needs a spec with both a scoped and a scope-free secured operation — the same shape Task 4 Step 2 preserves in the golden corpus. Given both, the keys must differ. A fixture with only scoped operations lets one key be a copy of the other and still pass.

**The report itself must say the count is not a skip count — in the emitted JSON, not in a source comment.** JSON carries no comments, so a `//` explanation in `CoverageReport.cs` reaches nobody who opens the artefact, and a reader seeing `7` here against 3 actual skips in a run has nothing to reconcile them with. The key name alone is not enough; the pre-existing `authTestsGatedOnSecondIdentity` already uses that convention, so restating the requirement for this key is asking for more than a careful name.

`notes.withheld` is the precedent — it emits `{operation, reason}` objects with real explanatory text. Whatever shape you choose must be **deterministic**: no runtime data, nothing that could vary between two runs against the same spec, or `generate --check` reports drift on an unchanged spec — which is the very failure this explanation exists to describe.

**Emit it unconditionally, including when the count is 0 — but not for the reason it is tempting to give.** A key whose *presence* depended on spec content would still be perfectly deterministic under `--check`, which compares a committed artefact against a fresh run of the **same** spec; it would never report drift. The real reason is plainer: a stable key set is easier for a human to diff and for a consumer to parse. Say that, and do not claim a `--check` justification the mechanism does not support.

**Every key carrying the same caveat needs an entry.** Once an explanations map exists, a key's *absence* from it reads as "this one needs no explanation." `authTestsGatedOnSecondIdentity` carries the identical runtime caveat and is the number a reader meets first, so leaving it unexplained makes the older key look more trustworthy than it is.

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(cli): report auth cases whose provability depends on the second identity"
```

---

## Task 6: Docs, spec, and close F11

> **Ordering defect, found during execution: this task straddles Task 7.**
>
> Steps 1 and 5 **cannot be done before Task 7 runs.** Step 1's banners state a live pass rate ("20 of 24") that only Task 7 produces, and Step 5 closes F11 "with Task 7's evidence". Writing either beforehand means inventing numbers — the exact failure the acceptance log exists to prevent.
>
> Steps 2, 3 and 4 have no such dependency, and Step 4 touches `InitCommand.cs`'s scaffold, which Task 7 then generates from — so it should land *before* the acceptance run rather than after it.
>
> **Execution order is therefore: 6.2 → 6.3 → 6.4 → Task 7 → 6.1 → 6.5 → 6.6.** The task is left whole rather than renumbered, because splitting it would break every reference to "Task 6 Step 4" elsewhere in this document.

- [ ] **Step 1: The two status banners — do this first, not last**

Both currently read "passes **20 of 24** live … (**F11**, still open)":

- `README.md`
- `docs/getting-started.md`

v1-c made exactly this miss — the front page contradicting the body — and needed a follow-up task to clean it up. Listed first here for that reason.

- [ ] **Step 2: `ITestTokenProvider`'s doc comment**

Task 1 Step 3 already rewrites it. Confirm it says what index 1 is *for* — the identity whose scopes decide which 403 cases are provable — rather than only "some other identity".

- [ ] **Step 3: §9's auth table**

F11 names this as the third place carrying the identical gap, and warns that fixing only the docs leaves the spec disagreeing with them. The "Needs: A second identity" row gains the scope dimension.

Also update §9's **precondition section**: its F11 row and the blockquote marking F11 *planned rather than built* both describe current behaviour that this plan changes.

- [ ] **Step 4: `getting-started.md`'s Auth section, and the scaffold comment**

The worked `OrdersTokenProvider` moves to `TestIdentity`, with one sentence on why: a read-only second identity is the common case, and without declared scopes its read operations' 403 tests cannot pass. `InitCommand.cs`'s commented-out provider example changes with it.

- [ ] **Step 5: Close F11 in `docs/v0-acceptance.md` — without rewriting what the v1-c run produced**

There are **8** F11 mentions and exactly **one** carries a status: the v1-c actions table, row 3. The other seven are historical narrative of the v1-c run — the phase table, four lines inside the v1-c acceptance section, the finding heading, and F12's cross-reference to F11's reasoning.

**Do not "update every mention that says Open."** Seven of them record what a run actually produced on 2026-08-19, and a suite that failed 4 of 24 that day still failed them. Rewriting that turns an evidence log into a changelog.

Follow the precedent F7 set when v1-b closed it, and F8/F9/F10 after it:

| Mention | Action |
|---|---|
| v1-c actions table, row 3 | Flip to **Closed**, with Task 7's evidence |
| The `### F11 —` heading | Append `· **closed in F11 phase**` |
| Immediately under that heading | A `>` blockquote: closed, where the evidence lives, and that the failure recorded below is **preserved as the original evidence** |
| The other six | **Leave exactly as they are** |

- [ ] **Step 6: Commit**

```bash
git commit -m "docs: scope-aware 403 tests; F11 closed"
```

---

## Task 7: Acceptance — the Orders suite

**This task is the verdict.** Tasks 1–6 can be green while the sample still fails.

- [ ] **Step 1: Recreate the two-identity provider — it was never committed**

`docs/v0-acceptance.md` records v1-c's `OrdersTokenProvider` as "**in the scratch suite, not committed**". There is no file to edit. Write it, now on `TestIdentity`, taking scopes from `samples/Identity.Server/Config.cs`, which is where the truth is:

```csharp
public IReadOnlyList<TestIdentity> Identities { get; } =
[
    new("orders-client",   ["orders.read", "orders.write"]),
    new("orders-readonly", ["orders.read"])
];
```

- [ ] **Step 2: Run the Orders suite**

**Expected: `Failed: 0, Passed: 20, Skipped: 4`** — where v1-c's run was 20 passed and **4 failed**.

> **Not "24 of 24".** MSTest reports skips separately (measured in v1-c: `Failed: 0, Passed: 1, Skipped: 1, Total: 2`), and a plan whose entire subject is *a skip is not a pass* must not describe its own success by conflating them. Record: 0 failed · 4 skipped with stated reasons · **3 write-scope 403s still running and passing**.
>
> That last number is the one that makes the run meaningful. A fix that skipped all 7 would also show 0 failures.

Record, per skipped case, which of the two guards skipped it.

> **This result deserves an automated guard, not only a transcript.** An acceptance run is prose, and this repo has already written down what happens to prose results — `TheGeneratedSuitePassesTwiceAgainstTheSameStore`'s own doc says a manual result "regresses silently — nobody notices until the next acceptance run", which is why v1-b converted F7's manual proof into a live test. "Three write-scope 403s run and pass, four read-scoped ones skip" is F11's equivalent headline. Before closing this task, decide deliberately whether it becomes a live test too, and say so either way rather than leaving it implied.

- [ ] **Step 3: Prove the skip is not vacuous**

Set the secondary identity's `Scopes` back to `null` and re-run. **All 7 must run again and 4 must fail** — reproducing F11 exactly. A guard that skips regardless of what the provider declares passes Step 2 and fails this.

Run this step first if anything about the work feels too easy.

- [ ] **Step 4: Catalog and Inventory unaffected**

Neither declares `security`. Both must still pass twice against an unreset store — v1-b's guarantee survives.

- [ ] **Step 5: Update the acceptance log and commit**

```bash
git commit -m "docs: F11 acceptance — 0 failed, 4 skipped with stated reasons"
```

---

## Self-review

**What this does not do.** It does not resolve document-level `security` inheritance — `PlanAuthCases` still reads operation-level declarations only, and still emits a `CoverageNote` when a document declares `security` and an operation does not. That gap is v1-c's and stays open.

**The risk worth stating.** Three tasks add a skip path, and every skip path is a place a test can stop testing without anyone noticing. Task 7 Step 3 is the control. [unknown-runs], together with Task 2's no-provider row, is what keeps the default behaviour loud rather than quiet.

**Where a reviewer should push.** Task 1 is a breaking reshape of a public interface across 8 implementers, done for a shape argument rather than a failing test. If the descriptor turns out to cost materially more than the 7 one-line test doubles it looks like, that is worth reporting before finishing it — the incremental `ScopesFor` path remains defensible, and the difference is a major version bump later rather than a wrong answer.
