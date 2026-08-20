# InTest F11 — Scope-Aware Wrong-Scope 403 Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop generating a wrong-scope 403 test that cannot pass. An operation whose scope the Secondary identity legitimately holds must **skip with a stated reason**, not fail.

**Architecture:** The CLI already reads each operation's declared scopes; the runtime already has a guard shape for "the provider cannot do this" (`RequireMultipleIdentities`, v1-c decision 3). This plan connects the two — the case carries the scopes the *spec* requires, the provider declares the scopes the *identity* holds, and a runtime guard compares them.

**Tech Stack:** Unchanged — net10.0 · MSTest 4.3.3 · Microsoft.OpenApi 3.10.0.

**Spec:** [`../specs/2026-08-16-intest-api-test-generator-design.md`](../specs/2026-08-16-intest-api-test-generator-design.md), §9's auth table.

**Prerequisite:** v1-c complete and merged. 398 tests passing.

**Closes:** F11 (`docs/v0-acceptance.md`).

---

## How to read the sample tests in this plan

**Every test snippet below is a sketch of intent, not code to paste.** Write the test, run it, and confirm it fails *for the reason the method name states* before writing any implementation. If a snippet cannot be made to fail against an empty implementation, the snippet is wrong — say so in your report rather than working around it.

This matters more than usual here: the failure this plan fixes is a test that asserts one status against a guaranteed different one. A fix that turns a failing test into a *skipping* test is one keystroke away from a fix that turns it into a **vacuous** one.

---

## The defect, measured

`TestPlanBuilder.PlanAuthCases` emits a `_Forbidden` case for every operation declaring `security`, independent of which scope that operation requires. Decision 3 gates *whether the case runs* (on identity count); nothing gates *whether it can be true*.

Against `samples/Orders.Api` with `samples/Identity.Server`'s own two clients:

| Operation | Requires | `orders-readonly` holds it? | Generated 403 |
|---|---|---|---|
| `GET /api/customers` | `orders.read` | **yes** | fails — real `200` |
| `GET /api/orders` | `orders.read` | **yes** | fails — real `200` |
| `GET /api/customers/{id}` | `orders.read` | **yes** | fails — real `404` (decision 6's unmatchable id) |
| `GET /api/orders/{id}` | `orders.read` | **yes** | fails — real `404` |
| `POST /api/customers` | `orders.write` | no | passes |
| `POST /api/orders` | `orders.write` | no | passes |
| `DELETE /api/orders/{id}` | `orders.write` | no | passes |

4 of 7 assert `403` against a status the API is **correct** to return. This is not sample-specific: a full-access / read-only identity pair is among the most common real role splits, and it produces this outcome structurally — a read-only identity is never "wrong scope" for a read.

---

## Decisions this plan encodes

**1. The comparison is set containment on the spec's own scope strings — never a heuristic.**

Scopes are already in the document. Measured against `samples/Orders.Api/Orders.Api.json`, and against the Microsoft.OpenApi 3.10.0 assembly directly:

```
OpenApiOperation.Security      -> IList<OpenApiSecurityRequirement>
OpenApiSecurityRequirement     -> Dictionary<OpenApiSecuritySchemeReference, List<string>>
```

So the required scopes for an operation are the dictionary's **values**, flattened:

```csharp
operation.Security.SelectMany(r => r.Values).SelectMany(s => s).Distinct()
```

The rule is: **skip when the Secondary identity's scopes are a superset of what this operation requires.** No string is inspected for meaning. `orders.read` and `orders.write` read as read and write only because this sample named them that way, and F11's own analysis rejects classifying them — OpenAPI attaches no semantics to scope strings, and guessing is what decision 5 exists to prevent.

**2. The identity→scope mapping comes from the adopter at runtime, not from the CLI.**

v1-c decision 7 rules out the CLI knowing anything about a provider that does not exist yet. It does **not** rule out the *runtime* asking one that does. This is the same move decision 3 made when `MemberCondition` was measured to run before `[AssemblyInitialize]`: the question is answerable, just not at the moment the original design asked it.

`ITestTokenProvider` gains one member:

```csharp
/// <returns>The scopes <paramref name="identity"/> holds, or <c>null</c> for "not declared" —
/// which keeps today's behaviour: the wrong-scope 403 case runs and is allowed to fail.</returns>
IReadOnlyCollection<string>? ScopesFor(string identity) => null;
```

A **default interface method**, so no existing implementer breaks and `StaticTokenProvider` needs no change (with one identity, the 403 case never runs anyway). Adopters who declare nothing keep exactly what they have today.

**3. `null` means "unknown", and unknown means run the test.**

The alternative — unknown means skip — would silently switch off auth testing for every adopter who has not implemented the new member, which is every adopter on the day this ships. That is the silent-green failure decision 3 was written to prevent, and it must not be reintroduced by the fix for F11.

Concretely: `null` runs, an empty collection is a real declaration (an identity holding no scopes) and runs, and only a declared superset skips.

**4. A skip is counted, and the count is not the same number as the gate.**

`coverage-report.json` already carries `authTestsGatedOnSecondIdentity` (`CoverageReport.cs:91`). That counts cases gated on *whether a second identity exists at all*. This is a different question — whether that identity is *usable for this operation* — and folding them into one number reproduces §12's bodiless-204 mistake, where a note means one thing and counts another. It gets its own key.

**5. The sample is not changed.**

`samples/Identity.Server/Config.cs`'s two clients stay. Its doc comment already says `orders-readonly` is "used to prove write endpoints return 403" — built for 3 of the 7, not all 7. After this plan that comment is accurate rather than aspirational, which is the point: the fix makes InTest match a reasonable identity setup, rather than demanding an unreasonable one.

**Rejected: requiring a null-scope Secondary identity.** Measured against the live sample — a `client_credentials` request omitting `scope` returns the client's **entire** allowed set, not none:

```
POST /connect/token  grant_type=client_credentials  client_id=orders-readonly  (no scope)
  aud   : orders-api
  scope : ['orders.read']
```

So a null-scope identity cannot be obtained by omission. Because the audience arrives *via* a scope, a genuinely scopeless token carries no `aud` and gets **401 at authentication, not 403 at authorization** — a different failure than the test asserts. Satisfying the requirement would mean adding a scope to the API's own resource definition that no endpoint uses, i.e. changing production auth configuration so a test tool's assertion can hold. Wrong direction for a tool that tests the API as deployed.

---

## File structure

| File | Responsibility |
|---|---|
| **Modified — `src/InTest.Runtime/`** | |
| `Neutral/ITestTokenProvider.cs` | Add `ScopesFor(identity)` as a default interface method |
| `MSTest/ApiTestBase.cs` | Add `RequireSecondaryIdentityLacks(params string[])`, beside `RequireMultipleIdentities` |
| **Modified — `src/InTest.Cli/`** | |
| `Planning/TestCasePlan.cs` | Carry `RequiredScopes` — empty for every non-auth case |
| `Planning/TestPlanBuilder.cs` | Populate it in `PlanAuthCases` from `operation.Security` |
| `Rendering/Templates/mstest-class.scriban` | Emit the guard on `_Forbidden` cases carrying scopes |
| `Coverage/CoverageReport.cs` | New count, separate from `authTestsGatedOnSecondIdentity` |
| **Docs** | |
| `docs/getting-started.md`, spec §9, `docs/v0-acceptance.md` | The contract, and F11 closed |

`ScopesFor` is on the `Neutral/` interface and names no MSTest type, so §3's portability boundary holds. The guard is on `ApiTestBase` for the same reason `RequireMultipleIdentities` is — `Assert.Inconclusive` is an MSTest type.

---

## Task 1: `ScopesFor` on the provider

**Files:**
- Modify: `src/InTest.Runtime/Neutral/ITestTokenProvider.cs`
- Test: `tests/InTest.Runtime.Tests/`

- [ ] **Step 1: Write the failing tests**

| Case | Expected |
|---|---|
| A provider declaring nothing (does not override) | `ScopesFor(any)` returns `null` — decision 3's "unknown" |
| `StaticTokenProvider` | Same; it is not modified by this task |
| A provider overriding it | Returns what it declares, for the identity asked about |
| An identity not in `Identities` | The implementer's choice; assert only that the **interface** does not constrain it |

The one that matters is the first: it must be reachable **without** the implementer writing the member, or the default-interface-method claim is untested and every existing implementer breaks on upgrade.

```csharp
[TestMethod]
public void AProviderThatDeclaresNothingReportsUnknownRatherThanEmpty()
{
    // Must fail if ScopesFor is made abstract, or if the default returns []. The distinction is
    // decision 3: null runs the test, empty is a real declaration that an identity holds no
    // scopes. Collapsing them silently switches auth testing off for every existing adopter.
    ITestTokenProvider provider = new StaticTokenProvider("t");

    provider.ScopesFor("default").ShouldBeNull();
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(runtime): identities can declare the scopes they hold"
```

---

## Task 2: The runtime guard

**Files:**
- Modify: `src/InTest.Runtime/MSTest/ApiTestBase.cs`
- Test: `tests/InTest.Runtime.Tests/`

- [ ] **Step 1: Write the failing tests**

`RequireSecondaryIdentityLacks(params string[] requiredScopes)` — `protected internal static`, matching `RequireMultipleIdentities` (`ApiTestBase.cs:104`) for the same two reasons: `protected` so the generated suite in another assembly can call it, `internal` so `InTest.Runtime.Tests` can reach it directly. Set `TestHost.TokenProvider` and reset it in `[TestCleanup]`, as the existing runtime tests do.

| Secondary declares | Operation requires | Behaviour |
|---|---|---|
| `null` (unknown) | anything | **Runs** — decision 3 |
| `["orders.read"]` | `orders.read` | **Skips**, message names the scope and the identity |
| `["orders.read"]` | `orders.write` | Runs |
| `["orders.read"]` | `orders.read`, `orders.write` | Runs — holds one, not both; containment is over the whole set |
| `[]` (declared empty) | `orders.write` | Runs — a real declaration, not unknown |
| No provider / one identity | anything | Runs — `RequireMultipleIdentities` already owns that skip; **do not skip twice for one reason** |

```csharp
[TestMethod]
public void AnIdentityHoldingEveryRequiredScopeSkipsAndNamesWhy()
{
    TestHost.TokenProvider = new FakeProvider(
        identities: ["default", "readonly"],
        scopes: new() { ["readonly"] = ["orders.read"] });

    var ex = Should.Throw<AssertInconclusiveException>(
        () => ApiTestBaseProbe.RequireSecondaryIdentityLacks("orders.read"));

    ex.Message.ShouldContain("readonly");      // which identity
    ex.Message.ShouldContain("orders.read");   // which scope makes it unprovable
}

[TestMethod]
public void PartialScopeOverlapStillRunsTheTest()
{
    // Holding one of two required scopes does not authorize the operation, so a 403 is still
    // provable. Must fail against an `Any` implementation — the easy wrong version of this.
    TestHost.TokenProvider = new FakeProvider(
        identities: ["default", "readonly"],
        scopes: new() { ["readonly"] = ["orders.read"] });

    Should.NotThrow(
        () => ApiTestBaseProbe.RequireSecondaryIdentityLacks("orders.read", "orders.write"));
}
```

**The `Any` vs `All` distinction is the defect most likely to ship here.** Write `PartialScopeOverlapStillRunsTheTest` before the implementation, not after.

- [ ] **Step 2: State the message contract**

The skip message must name the identity, the scope(s) it holds, and what that means — the same standard decision 3 set, because a skip nobody can explain is indistinguishable from a bug:

```
Skipped: the secondary identity 'readonly' holds orders.read, which this operation requires,
so it cannot produce a 403. Declare a different identity, or return null from ScopesFor to run
this test anyway.
```

- [ ] **Step 3–5: Run, implement, re-run, commit**

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
| `security` present, scope list empty (`[]`) | Empty — the operation is secured but scope-free |
| No `security` | No auth cases at all (unchanged) |
| Any non-auth case | Empty — never null |

The empty-list row is the one to get right: `security: [{ bearer: [] }]` is ordinary for a bearer scheme with no scopes. It must yield an auth case with **no** scopes rather than no case, and the guard must then run the test (nothing to be a superset of, so the identity is never disqualified).

- [ ] **Step 2: The extraction, verified not assumed**

Measured against Microsoft.OpenApi 3.10.0 by reflection — do not re-derive it:

```
OpenApiOperation.Security  -> IList<OpenApiSecurityRequirement>
OpenApiSecurityRequirement -> Dictionary<OpenApiSecuritySchemeReference, List<string>>
```

Scopes are the **values**, not the keys — the keys are scheme references. Confirmed end to end against `samples/Orders.Api/Orders.Api.json`, which yields `orders.read` for its 4 GETs and `orders.write` for its 2 POSTs and 1 DELETE.

Only the `_Forbidden` case carries scopes. The 401 case sends no token at all, so no scope makes it unprovable.

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

Ordering is the contract, and it is asserted by index the way `TemplateRendererTests.cs:149` already does for `RequireFixture`:

```csharp
RequireMultipleIdentities();                              // 1. is there a second identity at all
RequireSecondaryIdentityLacks("orders.write");            // 2. can it prove anything here
using var _ = UseIdentity(IdentitySlot.Secondary);        // 3. then select it
```

Both guards before the identity override, and both before the request is built. A `_Forbidden` case whose `RequiredScopes` is empty emits **only** the first — no bare `RequireSecondaryIdentityLacks()` call, which would read as a scope-free assertion rather than an absent one.

- [ ] **Step 2: Extend both corpora**

The two-guard distinction v1-c Task 4 Step 2 drew still applies, and both halves are needed:

| Corpus | Add |
|---|---|
| `tests/InTest.Golden.Tests/Specs/orders.json` | Scopes on its `security` block, so the golden text and compile checks cover the new line |
| The inline spec in `GeneratedSuiteExecutionTests.cs` that v1-c gave `security` | Scopes, and a fake provider declaring the secondary holds them |

Then the live proof, which is the one that matters:

```csharp
[TestMethod]
public async Task AForbiddenCaseTheSecondaryIdentityIsAuthorizedForSkipsRatherThanFails()
{
    // The whole of F11 in one assertion. Before this plan the generated suite fails here.
    // Assert BOTH: the run is green, AND the case is NotExecuted — green alone would also
    // describe a suite that stopped generating the case at all.
}
```

Assert `outcome="NotExecuted"` in the `.trx`, not "Skipped" — measured during v1-c, that is the file's spelling; "Skipped" is only the console summary's word.

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

A new key, separate from `authTestsGatedOnSecondIdentity` (decision 4). Name it for what it counts — cases whose *provability* depends on the Secondary identity's scopes, which the CLI cannot know:

```
"authTestsRequiringAnUnauthorizedSecondIdentity": 7
```

The CLI cannot count actual skips: which cases skip is a runtime fact, decided by a provider that does not exist at generation time. Counting what the report *can* know — how many 403 cases carry scope requirements — keeps `--check` deterministic. A key implying a runtime count would be wrong every time the provider changed, and `generate --check` would report drift on an unchanged spec.

Say this in the note text, not only here. A reader seeing `7` against 3 real 403s in the run needs the report itself to explain the difference.

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(cli): report auth cases whose provability depends on the second identity"
```

---

## Task 6: Docs, spec, and close F11

- [ ] **Step 1: `ITestTokenProvider`'s own doc comment**

`Identities`' doc says index 1 is "some other identity" the 403 case selects. That is the under-statement F11 names. It becomes: some other identity, whose scopes decide which 403 cases are provable — declare them with `ScopesFor`, or leave it unimplemented and the cases run as before.

- [ ] **Step 2: §9's auth table**

F11 names this explicitly as the third place that carries the identical gap, and warns that fixing only the docs leaves the spec disagreeing with them. The "Needs: A second identity" row gains the scope dimension.

- [ ] **Step 3: `getting-started.md`'s Auth section**

The worked `OrdersTokenProvider` gains `ScopesFor`, with one sentence on why: a read-only second identity is the common case, and without this its read operations' 403 tests cannot pass.

- [ ] **Step 4: Close F11 in `docs/v0-acceptance.md`**

With the run evidence from Task 7, not merely marked done. Update the v1-c actions table row 3.

- [ ] **Step 5: Commit**

```bash
git commit -m "docs: scope-aware 403 tests; F11 closed"
```

---

## Task 7: Acceptance — the Orders suite goes green

**This task is the verdict.** Tasks 1–6 can be green while the sample still fails.

- [ ] **Step 1: Declare the sample provider's scopes**

The two-identity provider written in v1-c Task 8 Step 1 gains `ScopesFor`: `orders-client` → both, `orders-readonly` → `["orders.read"]`. Taken from `samples/Identity.Server/Config.cs`, which is where the truth is.

- [ ] **Step 2: Run the Orders suite**

**Expected: 24 of 24**, where v1-c's run was 20 of 24. The 4 previously-failing cases now skip. Record, per case, which of the two guards skipped it — and confirm the 3 write-scope 403s still **run and pass**, because a fix that skips all 7 also produces a green run.

- [ ] **Step 3: Prove the skip is not vacuous**

Remove `ScopesFor` from the sample provider (back to unknown) and re-run. **All 7 must run again and 4 must fail** — reproducing F11 exactly. A guard that skips regardless of what the provider declares would pass Step 2 and fail this.

- [ ] **Step 4: Catalog and Inventory unaffected**

Neither declares `security`. Both must still pass twice against an unreset store — v1-b's guarantee survives.

- [ ] **Step 5: Update the acceptance log and commit**

```bash
git commit -m "docs: F11 acceptance — the Orders suite passes 24 of 24"
```

---

## Self-review

**What this does not do.** It does not resolve document-level `security` inheritance — `PlanAuthCases` still only reads operation-level declarations, and still emits a `CoverageNote` when a document declares `security` and an operation does not. That gap is v1-c's and stays open.

**The risk worth stating.** Three of this plan's seven tasks add a skip path, and every skip path is a place a test can stop testing without anyone noticing. Task 7 Step 3 is the control for that, and it is the step to run first if anything about this plan feels too easy. Decision 3's `null` = run, together with Task 2's no-provider row, is what keeps the default behaviour loud rather than quiet.

**Where a reviewer should push.** Decision 1's containment rule assumes an operation's declared scopes are *all* required — OpenAPI's `security` array is actually a logical OR across requirements, and the dictionary within one requirement is an AND. This plan flattens both into one set, which is correct for the single-requirement case every sample uses and **stricter than necessary** for a multi-requirement spec: an identity satisfying one alternative would still be asked to satisfy the union. That errs toward running the test rather than skipping it, which is the safe direction, but it is an approximation and should be named in the code comment rather than discovered later.
