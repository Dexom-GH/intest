# InTest v1-c — Declared-Error and Auth Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the deterministic, fixture-free tests a spec already describes — declared 4xx responses, and the auth behaviour of operations that declare `security` — and make `ITestTokenProvider` a working extension point rather than a documented dead end.

**Architecture:** `TestPlanBuilder` emits additional `TestCasePlan`s per operation instead of only the success case. A new `AuthHandler` consumes the registered `ITestTokenProvider`; readiness gets its own client so a token failure can no longer masquerade as a timeout.

**Tech Stack:** Unchanged — net10.0 · MSTest 4.3.3.

**Spec:** [`../specs/2026-08-16-intest-api-test-generator-design.md`](../specs/2026-08-16-intest-api-test-generator-design.md), §9 primarily.

**Prerequisite:** v1-b complete and merged (`1448570`). 315 tests passing.

---

## Scope: variation tests are **not** in this plan

The v1 roadmap grouped declared-error, auth and variation tests as one phase. Splitting them, and why:

Declared-error and auth tests are *more cases from the same machinery* — one extra `TestCasePlan` per declared response, rendered by the template that already exists. Both are deterministic, fixture-free and gate-safe, and both close an open finding (F8, F10).

Variation tests are a subsystem: the per-position string catalog, the `DataRow`/`DynamicData` split, `MemberCondition` gating on a profile flag, and the expected-outcome promotion config that lets a suite ratchet stricter. They share almost nothing with the above except the word "test".

Merging them would also blur the acceptance criterion. Auth acceptance is crisp — a read-only token gets 403 on a write against the live Orders sample. Variation acceptance is "a lot of malformed payloads did roughly what we hoped", which is not a criterion.

**Variations become v1-c2**, planned separately. If you want them merged back, that is a deliberate choice to make now rather than discover at Task 8.

---

## How to read the sample tests in this plan

**Every test snippet below is a sketch of intent, not code to paste.** v1-b's plan shipped three that passed against deliberately wrong implementations — two in its Task 2 that a sorted-by-accident implementation satisfied, and one whose body was identical to a neighbouring test and asserted nothing about the behaviour its name claimed.

So: **write the test, run it, and confirm it fails for the reason the method name states, before writing any implementation.** That step already appears in every task. It is the step that catches this, and skipping it is how a vacuous guard ships looking like rigour.

If a snippet here cannot be made to fail against an empty implementation, the snippet is wrong. Say so in your report rather than working around it.

---

## Decisions this plan encodes

**1. Readiness gets its own HTTP client.**

F10, measured: getting-started Phase 3 tells adopters to attach a bearer handler to `InTestClients.Api`, and `TestHost` hands that same named client to `Readiness.WaitAsync`. When the identity provider is unreachable the handler throws on every request through it — including the anonymous `/health/ready` probe that needed no token at all — and the failure surfaces as:

```
ReadinessTimeoutException: Service did not become ready within 120s
(last response: HttpRequestException)
```

A dead identity server reported as a dead API, after a two-minute wait. Readiness therefore resolves `InTestClients.Readiness`, registered without auth handlers. This is a prerequisite for everything else here: with auth tests generating, the misdiagnosis becomes routine rather than a one-off.

**2. `AuthHandler` is what finally consumes `ITestTokenProvider`.**

F8's remaining half. The interface, `StaticTokenProvider` and the `Identities` property all ship today and nothing calls them. `AuthHandler` is a `DelegatingHandler` that requests a token for the ambient identity and sets `Authorization`. It reads the identity from an `AsyncLocal`, for the same measured reason `RunIdHandler` does — factory-created handlers are not DI-scoped.

**3. Auth tests split by what the provider can actually do, and the split already exists in the spec.**

| Test | Needs | Behaviour |
|---|---|---|
| no token → 401 | nothing; send no `Authorization` | **Always generated, always runs** |
| wrong scope → 403 | a second identity | Generated, gated on `Identities.Count > 1` |

Gating is `MemberCondition` over `InTestConditions.MultiIdentityAvailable` — framework-native, and the same mechanism §9 already specifies for variations. A gated-off test **skips with a stated reason and is counted in the coverage report**; a generated suite must not be red on day one for a capability the adopter has not supplied.

**4. Declared-error tests come from declared responses only — never inferred.**

Where the spec declares a 404 or a 400, generate a test for it. Where it does not, generate nothing. InTest does not guess that an endpoint "probably" 404s on a bad id, because §9's expected-outcome policy is explicit that guessed assertions produce a wall of wrong failures that get bulk-ignored.

A 404 test needs an id that does not exist. That is a **generated constant**, not a fixture: a fresh GUID or an unmatchable string, chosen so no seeded row can collide. It must not consume a fixture value, or a suite whose fixtures are unfilled would block a test that needs no data at all.

**5. Every new case is `TestCategory("Contract")`.**

§9 splits the gate on category: `--filter "TestCategory=Contract"` runs in the post-deployment gate, `Variation` does not. Declared-error and auth tests are deterministic, fixture-free and safe against a deployed environment — exactly what belongs in a gate. Only variations get a different category, and they are not in this plan.

---

## File structure

| File | Responsibility |
|---|---|
| **New — `src/InTest.Runtime/`** | |
| `Neutral/AuthHandler.cs` | Sets `Authorization` from `ITestTokenProvider` for the ambient identity |
| `Neutral/InTestConditions.cs` | `MultiIdentityAvailable`, read by `MemberCondition` |
| `MSTest/InTestClients.cs` *(modify)* | Add `Readiness` alongside `Api` |
| **Modified** | |
| `MSTest/TestHost.cs` | Register both clients; resolve the readiness one for probing; expose the token provider |
| `MSTest/ApiTestBase.cs` | Set and clear the ambient identity per test |
| `Planning/TestCasePlan.cs` | Carry the expected status' role — success, declared error, or auth — and the identity to use |
| `Planning/TestPlanBuilder.cs` | Emit declared-error and auth cases |
| `Rendering/Templates/mstest-class.scriban` | Render them, including the `MemberCondition` gate |
| `Coverage/CoverageReport.cs` | Count generated and gated auth tests |
| `Commands/InitCommand.cs` | Scaffold registers a token provider; Phase 3's guidance changes |

`AuthHandler` and `InTestConditions` are under `Neutral/`, so §3's portability boundary holds and the architecture test stays green. **`MemberCondition` is an MSTest attribute — it is emitted by the template, never referenced from `Neutral/`.**

---

## Task 1: A dedicated readiness client (F10)

Do this first. Every later task exercises auth against a live service, and until this lands a token failure looks like a dead API for two minutes.

**Files:**
- Modify: `src/InTest.Runtime/MSTest/InTestClients.cs`, `src/InTest.Runtime/MSTest/TestHost.cs`
- Test: `tests/InTest.Runtime.Tests/`, `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`

- [ ] **Step 1: Write the failing test**

The point is that a handler on the *API* client does not run for the readiness probe.

```csharp
[TestMethod]
public async Task ReadinessProbeDoesNotRunApiClientHandlers()
{
    // F10: a bearer handler attached to InTestClients.Api threw on the anonymous
    // /health/ready probe, and a dead identity server was reported as a dead API after a
    // 120-second wait.
    var apiHandlerRan = false;
    // register a throwing handler on Api only, then probe readiness
    ...
    apiHandlerRan.ShouldBeFalse();
}
```

Confirm it fails today — both clients are currently the same one.

- [ ] **Step 2: Implement**

Add `InTestClients.Readiness`. Register it in `TestHost` with the same base address and **no** additional handlers. `Readiness.WaitAsync` gets that client.

`RunIdHandler` is a judgement call worth making explicitly: keep it on the readiness client, so probe traffic still carries `X-Test-Run-Id` and remains traceable, but nothing else. State which you chose and why in your report.

- [ ] **Step 3: Prove the misdiagnosis is gone end to end**

Extend `GeneratedSuiteExecutionTests`: register a handler on `Api` that always throws, and assert the suite fails on the *first test* with that handler's error rather than on a readiness timeout. That is F10 inverted, and it is what stops it recurring.

- [ ] **Step 4: Run full suite, commit**

```bash
dotnet test --nologo
git commit -m "fix(runtime): probe readiness on a client without auth handlers"
```

---

## Task 2: `AuthHandler` — consume `ITestTokenProvider` (F8)

**Files:**
- Create: `src/InTest.Runtime/Neutral/AuthHandler.cs`
- Modify: `src/InTest.Runtime/Neutral/InTestAmbient.cs`, `src/InTest.Runtime/MSTest/TestHost.cs`, `src/InTest.Runtime/MSTest/ApiTestBase.cs`
- Test: `tests/InTest.Runtime.Tests/AuthHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: sets `Authorization: Bearer <token>` from the provider · requests the token for the **ambient identity**, not always the default · **sends no header at all when the ambient identity is the sentinel meaning "no token"** — that is the 401 test's whole mechanism · a provider that throws surfaces an error naming the provider and the identity, not a bare `HttpRequestException` · the ambient identity is isolated per async flow, as `RunIdHandler`'s already is.

```csharp
[TestMethod]
public async Task SendsNoAuthorizationHeaderForTheNoTokenIdentity()
{
    // The 401 test does not "use a bad token" — it sends none. A handler that always sets a
    // header would make that test unwritable.
    InTestAmbient.Identity.Value = InTestIdentities.None;
    var request = await SendThroughHandler();

    request.Headers.Authorization.ShouldBeNull();
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

`ApiTestBase` sets the ambient identity in `[TestInitialize]` and clears it in `[TestCleanup]`, exactly as it already does for `TestId`. The default is the provider's first identity.

```bash
git commit -m "feat(runtime): auth handler consuming the registered token provider"
```

---

## Task 3: Plan declared-error cases

**Files:**
- Modify: `src/InTest.Cli/Planning/TestCasePlan.cs`, `src/InTest.Cli/Planning/TestPlanBuilder.cs`
- Test: `tests/InTest.Cli.Tests/TestPlanBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: an operation declaring `404` yields a second case with `ExpectedStatus: 404` · an operation declaring `400` yields one · an operation declaring neither yields only its success case · the method name distinguishes them and is stable (`GetOrderById_NotFound`, not `GetOrderById_Contract2`) · a 404 case takes an **unmatchable generated id**, not a fixture value · declared errors on an operation whose success case was skipped are also skipped, so the two never disagree.

```csharp
[TestMethod]
public async Task ANotFoundCaseUsesAnUnmatchableIdRatherThanAFixture()
{
    var plan = await BuildAsync(SpecDeclaring404);
    var notFound = plan.Classes.SelectMany(c => c.Cases).Single(c => c.ExpectedStatus == 404);

    // A 404 test needs no data, so it must not be blocked by an unfilled fixture. Decision 4.
    notFound.NeedsFixture.ShouldBeFalse();
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

`TestCasePlan` gains what distinguishes a case: its role (success / declared error / auth) and, for auth, the identity. Extend the existing record rather than introducing a parallel type — v1-b's `NeedsFixture` and `QueryParameterNames` set the precedent, including their comments explaining why the value is carried rather than recomputed.

```bash
git commit -m "feat(cli): plan declared-error cases from declared responses"
```

---

## Task 4: Render declared-error cases

**Files:**
- Modify: `src/InTest.Cli/Rendering/Templates/mstest-class.scriban`, `src/InTest.Cli/Rendering/TemplateRenderer.cs`
- Test: `tests/InTest.Cli.Tests/TemplateRendererTests.cs`, golden regeneration

- [ ] **Step 1: Write the failing tests**

A declared-error case renders a method asserting its status, uses the generated unmatchable id, and **calls no fixture lookup**. `EmitsNoStrayBlankLines` must still pass — the template gains conditionals, which is exactly where whitespace control breaks.

- [ ] **Step 2: Regenerate the golden file and read it**

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~GoldenFileTests"
dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~GoldenFileTests"
```

Read the regenerated file before committing. It locks in whatever it is handed.

- [ ] **Step 3: Commit**

```bash
git commit -m "feat(cli): render declared-error contract tests"
```

---

## Task 5: Plan and render auth cases

**Files:**
- Modify: `src/InTest.Cli/Planning/TestPlanBuilder.cs`, `src/InTest.Cli/Rendering/Templates/mstest-class.scriban`
- Create: `src/InTest.Runtime/Neutral/InTestConditions.cs`
- Test: `tests/InTest.Cli.Tests/`, `tests/InTest.Runtime.Tests/`

- [ ] **Step 1: Write the failing tests**

Planning: an operation declaring `security` yields a no-token 401 case · it also yields a wrong-scope 403 case **carrying the gate** · an operation declaring no `security` yields neither · the 403 case names a non-default identity · neither case needs a fixture.

Gating: `InTestConditions.MultiIdentityAvailable` is `Identities.Count > 1` · it is false for `StaticTokenProvider`, which advertises one · a gated test skips rather than fails.

```csharp
[TestMethod]
public void TheShippedProviderGatesTheForbiddenTestsOff()
{
    // StaticTokenProvider advertises exactly one identity, so 403 tests gate off by
    // construction. A suite must not be red on day one for a capability we chose not to ship.
    InTestConditions.MultiIdentityAvailable.ShouldBeFalse();
}
```

- [ ] **Step 2–4: Run, implement, re-run, commit**

The template emits `[MemberCondition(typeof(InTestConditions), nameof(InTestConditions.MultiIdentityAvailable))]` on 403 cases only.

```bash
git commit -m "feat: generate auth contract tests, gated on available identities"
```

---

## Task 6: Coverage report and scaffold

**Files:**
- Modify: `src/InTest.Cli/Coverage/CoverageReport.cs`, `src/InTest.Cli/Commands/InitCommand.cs`
- Test: `tests/InTest.Cli.Tests/CoverageReportTests.cs`, `InitCommandTests.cs`

- [ ] **Step 1: Write the failing tests**

The report counts declared-error tests generated, auth tests generated, and **auth tests gated off**. That last line is why a reader can tell "gated" from "never generated" — without it they are indistinguishable, which §12 treats as the same failure as a silent skip.

The scaffold's `TestStartup.cs` shows registering an `ITestTokenProvider`, and Phase 3's example must attach `AuthHandler` to `InTestClients.Api` **only** — never to readiness (Task 1). Assert on the registration call, not on prose.

- [ ] **Step 2–4: Run, implement, re-run, commit**

```bash
git commit -m "feat(cli): report auth coverage and scaffold a token provider"
```

---

## Task 7: Amend the spec and the guide

- [ ] **Step 1: §9 — declared-error and auth tests are built**

Remove the deferrals; keep the reasoning. Record decision 4: declared errors come from declared responses, never inferred.

- [ ] **Step 2: §13 — readiness uses its own client**

With F10's evidence, so the reason survives.

- [ ] **Step 3: `docs/getting-started.md` Phase 3 — fix the example that caused F10**

It currently attaches the handler to the client readiness also uses. Correct it, and say why plainly enough that nobody reverts it: an anonymous probe must not fail because a token provider is unreachable.

- [ ] **Step 4: Close F8 and F10 in the acceptance log**

Both with the evidence from Tasks 1 and 2 — not merely marked done.

- [ ] **Step 5: Commit**

```bash
git commit -m "docs: declared-error and auth tests are built; F8 and F10 closed"
```

---

## Task 8: Acceptance — auth against the live Orders sample

**This task is the verdict.** Tasks 1–7 can be green while nothing works against a real identity server.

`samples/Orders.Api` declares `security` on all 7 operations with per-operation scopes, and `samples/Identity.Server` has two Duende clients — `orders-client` (read + write) and `orders-readonly` (read only) — put there in v0 precisely so this could be tested.

- [ ] **Step 1: Implement a two-identity `ITestTokenProvider` in the generated Orders suite**

`Identities` returns both client ids, so the 403 tests un-gate. Note F9: `samples/README.md`'s documented port is wrong — use the port the app actually binds, and fix the README as part of this task.

- [ ] **Step 2: Run the Orders suite**

**Expected: every generated test passes, including 401s and 403s**, with the 403 tests now running rather than skipping. Record how many of each kind were generated.

- [ ] **Step 3: Prove the 403 tests can fail**

Point the read-only identity at the full-access client. The write-scope 403 tests must now **fail** — because the request succeeds where a 403 was expected. Restore. A gate that passes whatever the token is, is not testing auth.

- [ ] **Step 4: Confirm F10 is closed against a real failure**

Stop `Identity.Server` and run the suite. The failure must name the token provider, not a readiness timeout.

- [ ] **Step 5: Run Catalog and Inventory**

Neither declares `security`, so neither should gain an auth test. Both must still pass twice against an unreset store — v1-b's guarantee must survive this plan.

- [ ] **Step 6: Update the acceptance log and commit**

```bash
git commit -m "docs: v1-c acceptance run — auth tests against a live identity server"
```

---

## Self-review

**Spec coverage.** §9's declared-error tests (Tasks 3, 4), auth tests with the 401/403 split and `MemberCondition` gating (Tasks 2, 5), and the coverage lines that keep gated tests visible (Task 6). §13's readiness client (Task 1). F8 and F10 both close with evidence (Tasks 1, 2, 7).

**Deliberately deferred.** Variation tests — the reasoning is at the top, and it is a scope call worth overriding now rather than at Task 8 if you disagree. `IControllerFixture` — still no measured need. Multi-identity providers beyond client-credentials — the interface takes an identity string; what an adopter does with it is theirs.

**The risk worth stating.** Task 3 changes the shape of `TestCasePlan`, which every consumer touches — the template, the coverage report, `FixtureComposer`'s `NeedsFixture` verdict, and `fixtures repair`. v1-b added two fields to that record with comments explaining why the value is carried rather than recomputed; a third field with a different role invites exactly the divergence those comments warn about. If the record starts feeling like a bag of flags, that is a signal to split success and error cases into distinct types, and better noticed at Task 3 than at Task 8.

**What this plan does not do.** It generates no test for an undeclared error. An API that 404s correctly but does not say so in its spec gets no 404 test, and that is deliberate — §9's expected-outcome policy holds that a guessed assertion is worse than none, because a wall of wrong failures gets bulk-ignored and takes the real failures with it.
