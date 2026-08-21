# InTest Housekeeping — Escaping, Phase Labels, and the Decision Convention

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close three small items left open after F11, none of which contains an unmade decision.

**Prerequisite:** F11 merged to `main` (`12c5c32`). 434 tests passing, 0 failing.

---

## Why one plan and not three

F11 ran seven tasks through implement → spec review → quality review → fix → re-review. That was right for 29 commits reshaping a public interface. It is wildly disproportionate for a three-line documentation correction.

**Scale the process to the change.** Three tasks here, one review pass at the end covering all three. If any task turns out to contain a real decision rather than a mechanical edit, stop and report — that is the signal it belongs in its own plan, not that it should be pushed through.

---

## Decisions are named, not numbered

Same convention F11 used, and Task 3 is about making it the house rule.

---

## What is deliberately NOT in this plan

**The OR/AND scope union.** `[containment]` flattens scopes across `security` requirements into one set, which is stricter than OpenAPI's OR semantics — for a multi-requirement spec, an identity satisfying one alternative is measured against the union, so a case that should skip runs and fails against a status the API is correct to return. That is F11 one level in, and it is a genuine design decision with tradeoffs, not housekeeping. It needs a conversation first.

**Probing whether a declared scope is true.** `ITestTokenProvider`'s own doc says `Identities` is *"a declared capability, never a probe."* A provider that over-declares silently converts a provable 403 into a silent skip. Fixing that means abandoning the declared-capability design, which is a deliberate choice, not an oversight. Recorded as a known limitation in `docs/v0-acceptance.md`; no task.

**Partial containment untested against live Duende.** Already covered in the golden suite by `getScopedSecureResourceRequiringDelete` (two scopes, partial overlap, so `All` and `Any` diverge). Closing the remaining gap means adding a two-scope operation to `samples/Orders.Api` — a decision about the samples, not a gap in the fix.

**`.trx` `<Counters>` under-reporting skips.** A fact about VSTest's output format, not our code. Already documented. Nothing to do.

**`samples/Inventory.Api/Inventory.Api.json` churn.** **Measured on 2026-08-21: it no longer reproduces.** From a clean tree, `dotnet build InTest.sln` leaves `git status --porcelain` empty, and `git diff --numstat` on that file returns nothing. `.gitattributes` already carries `* text=auto`, and no committed file under `samples/`, `src/`, `tests/` or `docs/` contains CR. The most likely cause was the stale zero-byte `.git/index.lock` (dated 2026-08-20 16:44, removed when F11 merged) preventing git from refreshing stat info after a build rewrote the file with identical normalized content — plus the F11 worktree carrying its own index, which is where the flap was actually observed. **Correction, later the same day: it returned, and my measurement was narrower than my claim.**

The session executing Task 1 hit it after a full `dotnet test` in a **fresh worktree**: file shown as modified, `git diff --numstat` reporting zero changed lines, git warning *"CRLF will be replaced by LF the next time Git touches it"*. `git checkout --` restored it losslessly. Re-measured in the main worktree afterwards — still does not reproduce there, 0 dirty before a full build and 0 after.

So the honest statement is **"does not reproduce in a long-lived worktree"**, which is not what I wrote. I measured one worktree and generalised to the repository.

What the two observations together support, for whoever picks this up:

- `core.autocrlf` is `input` — normalize on commit, no conversion on checkout — so the working copy holds LF and the blob holds LF.
- The build's NSwag target rewrites the file with CRLF. Content normalizes equal, which is why `numstat` is always empty, but the bytes on disk differ from what was checked out.
- Every observation has been in a **fresh** worktree, whose index has no stat cache for that file yet. The main worktree, whose index has seen it many times, stays clean.

That is a hypothesis with evidence, not a diagnosis. **The existing task chip still owns the fix** — this entry exists so it starts from the evidence rather than from zero, and so the earlier "does not reproduce" is not left standing as though it were repository-wide.

---

## Task 1: Escape spec-derived text in generated C# literals

> **Dispatched externally on 2026-08-21 — do not execute from this plan.** The user started this
> as a background task (`task_cf790217`) in a separate local session, from the original chip text
> rather than from this task. That chip is wrong in two ways this task corrects: it asks the
> implementer to *decide* between escaping and refusing (already decided — see Step 1), and it
> points only at `TemplateRenderer.cs` (most quoting is in the Scriban template — see Step 2).
> The corrections were sent to that session directly. **Verify against this task when its work
> lands**, particularly the byte-identical-ordinary-output constraint in Step 3, which the chip
> does not mention at all.

**Files:**
- Modify: `src/InTest.Cli/Rendering/TemplateRenderer.cs`, `src/InTest.Cli/Rendering/Templates/mstest-class.scriban`
- Test: `tests/InTest.Cli.Tests/TemplateRendererTests.cs`, `tests/InTest.Golden.Tests/CompileVerificationTests.cs`

- [ ] **Step 1: Establish the rule before touching code**

Two behaviours already exist in this codebase for hostile spec text, and both are correct:

| Situation | Behaviour | Precedent |
|---|---|---|
| The text also names a **file** | **Refuse** with a coverage note | `FixtureDocument.cs:64` — `"operationId '…' cannot be a fixture filename: it contains …"` |
| The text is only a C# **literal** | **Escape** it | *(this task)* |

**Do not change the refusing behaviour.** An operationId that cannot be a filename must keep being skipped with a stated reason — it is a real, reportable problem with the spec. A scope string, by contrast, names nothing on disk; there is no reason to refuse it, only to emit it correctly.

Keep the two distinguishable. If you find yourself making scopes refuse, or operationIds escape, you have merged two rules that exist for different reasons.

- [ ] **Step 2: Find every site — there are more than the renderer**

The escaping is not confined to `TemplateRenderer.cs`. **The template does most of the quoting itself**, wrapping model values in literal quotes:

```
mstest-class.scriban:17   [TestMethod, TestCategory("{{ tc.category }}")]
mstest-class.scriban:18   [Description("{{ tc.display_name }}")]
mstest-class.scriban:35   RequireFixture("{{ tc.operation_key }}");
mstest-class.scriban:40   InTestUrl.Build("{{ tc.path_template }}"…)
mstest-class.scriban:51   …, "{{ tc.schema_key }}", …
```

plus `TemplateRenderer.cs`'s `required_scopes_args`, which quotes in C#.

Enumerate them all before deciding where the helper goes. Sites whose value cannot come from the spec (`tc.category` is always `"Contract"`) need no escaping and should be left alone with a note saying why — but **verify** that rather than assuming it.

- [ ] **Step 3: Write the failing tests**

A single shared helper, applied at every site whose value is spec-derived. Cover, per site:

> **Correction, 2026-08-21 — the escape set is the C# grammar's, not the two characters I listed.**
> C# forbids **five** characters in a regular string literal, not two: `\`, `"`, and the
> `new_line` set — CR, LF, U+0085, U+2028, U+2029. A raw newline in a generated literal is
> `CS1010`.
>
> This is reachable by the same mechanism `bfa668d` documents, verified by trace:
> `OperationKey.Resolve` only `.Trim()`s, so an embedded newline survives; `char.IsControl` in
> `TryValidateOperationKey` runs only behind `TestPlanBuilder.cs:60`'s `needsFixture` gate, which
> never fires for a parameterless operation. So `"operationId": "list
Things"` — valid JSON,
> valid OpenAPI — reaches a literal and does not compile.
>
> **Define the set by `regular_string_literal_character` in the C# grammar, not by enumeration.**
> My listing two characters was the defect: an enumeration is a snapshot, a grammar rule is a
> boundary with an authority outside our judgement. "Handle a few more that seem risky" would be
> scope creep; "the set is what the language forbids" is not.
>
> **Broader than the operationId:** `path_template`, path and query parameter names, and a
> `components.schemas` reference id have **no** validation gate at all — `TryValidateOperationKey`
> only ever validates the operation key. Scopes remain genuinely safe: RFC 6749's `scope-token`
> grammar excludes everything below `0x21`, which rules out CR and LF for the same reason it rules
> out `"` and `\`.

| Input | Expected |
|---|---|
| A value containing `"` | Emitted escaped; generated project **compiles** |
| A value containing `\` | Same |
| A value containing a raw **newline** | Same — this is the one an enumeration missed |
| An ordinary value | **Byte-identical output to today** — this is what protects the golden file |

The last row matters most: a helper that escapes correctly but alters ordinary output would rewrite `Expected/OrdersTests.g.cs.txt` wholesale and bury the real change.

**`CompileVerificationTests` is the test that actually proves this.** A string assertion can confirm a backslash appears; only compiling the generated project proves the result is valid C#. Add a spec with hostile text there.

- [ ] **Step 4–6: Run, implement, re-run, commit**

If the golden file changes at all, stop and report before regenerating — that would mean ordinary output moved, which this task must not do.

```bash
git commit -m "fix(cli): escape spec-derived text embedded in generated C# literals"
```

---

## Task 2: Correct three mislabelled phase references

**Files:** `docs/v0-acceptance.md`

- [ ] **Step 1: Fix the labels**

Three action rows carry an owner phase of `v1-f`. Per the roadmap in `docs/superpowers/plans/2026-08-17-intest-v0.md:3959`, **`intest survey` is v1-d**; v1-f is YAML input, URL snapshotting, and `fixtures promote`.

| Line | Item | Correct owner |
|---|---|---|
| 488 | `intest survey` should predict from request-body leaf properties + path parameters | **v1-d** |
| 1169 | Same item, carried forward from v1-a action 6 | **v1-d** |
| 1167 | The `CatalogSeedFixture` product-row leak — §14 sweeper coverage and a getting-started note | **judge it** |

Row 1167 is not obviously a `survey` item; read it and assign the phase that actually owns it, or leave it and say why. Do not change it just because its neighbours moved.

- [ ] **Step 2: Check the phase letters are consistent everywhere**

The roadmap assigns v1-a fixtures, v1-b lifecycle, v1-c more test kinds, v1-d `survey`, v1-e `--check`/`upgrade`/`assertions add`, v1-f YAML/URL-snapshot/`fixtures promote`. Sweep for any other reference that disagrees.

**Do not touch the historical narrative.** Same rule F11's closure followed: these are *action rows* carrying a forward-looking owner, which is a live field. Anything recording what a run produced on a given date stays exactly as it is.

- [ ] **Step 3: Commit**

```bash
git commit -m "docs: three action rows named v1-f for work the roadmap assigns to v1-d"
```

---

## Task 3: Make named decisions the house rule — without rewriting history

**Files:** `CONTRIBUTING.md`

- [ ] **Step 1: Document the convention**

Numbered decisions drifted three times in v1-c, twice inside a single document: inserting a decision silently invalidated every reference after it, and commit `1448570` had already been spent fixing the same failure across documents. F11 used named decisions — `[containment]`, `[descriptor]`, `[unknown-runs]`, `[counted]`, `[sample-unchanged]` — and had zero reference drift across 29 commits and several rounds of insertions.

Write it into `CONTRIBUTING.md` as the rule for new plans: **decisions get short slugs, not numbers**, because a reference is then a word that insertion and reordering cannot break.

- [ ] **Step 2: Do NOT retrofit completed plans**

`2026-08-17-intest-v1a-fixtures.md` (5), `2026-08-18-intest-v1b-fixture-lifecycle.md` (6) and `2026-08-19-intest-v1c-error-and-auth-tests.md` (8) carry numbered decisions.

**Leave them.** They are records of completed phases. The drift problem bites a plan while it is being *edited*; a finished plan is never renumbered again, so the risk is zero and a rename is pure churn against documents whose value is being an accurate record of what was decided when. This is the same reasoning that kept F11's closure from rewriting the v1-c run record.

Say so explicitly in `CONTRIBUTING.md`, so nobody reads the new rule as a cleanup mandate.

- [ ] **Step 3: Commit**

```bash
git commit -m "docs: name decisions rather than numbering them, in new plans only"
```

---

## Review

One pass at the end covering all three tasks, rather than two per task. Check specifically:

- **Task 1**: does the golden file's ordinary output survive byte-identically, and does `CompileVerificationTests` genuinely compile hostile input rather than string-matching it?
- **Task 2**: was any historical narrative touched? Only forward-looking owner fields should have moved.
- **Task 3**: does `CONTRIBUTING.md` state the no-retrofit half as clearly as the rule itself?

---

## Self-review

**Correction, 2026-08-21 — this defect is reachable from a valid OpenAPI document.** The session executing Task 1 found the gap and I verified it. `TestPlanBuilder.cs:60` validates an operationId only when `needsFixture` is true:

```csharp
if (needsFixture && !FixtureDocument.TryValidateOperationKey(key.Value, out var reason))
```

But `TemplateRenderer`'s `emits_fixture_lookup` is `c.Role == CaseRole.Success` with **no** `NeedsFixture` condition, so `RequireFixture("{{ tc.operation_key }}")` is emitted for every success case. A **parameterless** operation — no path or query parameter carrying a value, no JSON body — has `NeedsFixture == false`, so its operationId is never validated and flows straight into a C# literal. `operationId: 'list"Things'` on a parameterless `GET` produces a project that does not compile, today, from a fully valid document.

Scopes remain unreachable (RFC 6749 excludes `"` and `\`). **Operation keys are not.** `CompileVerificationTests` must therefore use a *parameterless* operation with a hostile operationId — a parameterised one is refused before it reaches the template and would prove nothing.

**The gate stays as it is.** Its refusal exists because the key becomes a *filename*; an operation needing no fixture names no file, so validating there would skip a perfectly testable operation over a character that causes no problem once escaped. Escaping is the fix; the two rules stay distinguishable.

**The risk worth stating.** Task 1 touches the renderer and the template, which every generated file flows through. The justification is that generated code is committed and diffed by adopters, and this codebase already refuses rather than emitting code that will not compile; the inconsistency is the defect, not the reachability. But it is the one task here with real blast radius, and the byte-identical-output test is what keeps it honest. If that test cannot be made to pass, the helper is wrong.
