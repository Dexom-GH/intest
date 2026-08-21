# Contributing to InTest

Thanks for looking. InTest is a working tool with an incomplete command surface. `intest init`,
`generate`, and `fixtures repair` run end to end today, verified against three sample APIs with
a documented walkthrough in [`docs/getting-started.md`](docs/getting-started.md). `survey`,
`generate --check`, and `upgrade` don't exist yet — that doc's own preamble tracks the gap
precisely, and is the source of truth if this file and it ever disagree. Nothing is published to
NuGet, so building from source is still how anyone tries it. The
[design spec](docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md) remains the
reference for why things are built the way they are.

## The most useful contribution today

**Read the [design spec](docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md)
and tell us where it is wrong.**

It is long, and that is deliberate: it records not just decisions but the evidence behind them
and the alternatives rejected. Claims marked *measured* were established by building and
running code, not by reading documentation. If one of them is wrong, we want to know, and a
reproduction beats an assertion.

Reviews have already caught a build-breaking interaction between two documented MSTest
mechanisms, a correlation identifier that collapsed to one value across every data-driven test
row, and a validator gap that would have passed invalid responses silently. That kind of reading
is still the highest-leverage contribution: it catches defects a fresh implementation would only
rediscover later, and it costs nothing to build or run first.

## Ground rules for changes to the spec

The spec has conventions worth keeping:

- **Back claims with evidence.** If you assert a library behaves a certain way, say how you
  know. "The docs say" and "I ran this and got that" are both fine; they are just not the same
  thing, and the spec distinguishes them.
- **Record what was rejected and why.** §19 exists so decisions are not silently relitigated.
- **Prefer deletion.** Several revisions made the design smaller. Removing a contradiction beats
  documenting a workaround for it.
- **No capability may be gated on any one organisation's spec population.** InTest is used by
  people whose specs we cannot see. A survey informs priority; it never decides whether a
  feature exists. This has been violated twice and corrected twice — please do not reintroduce
  it.

## Writing plans

New implementation plans (`docs/superpowers/plans/`) name their decisions with short slugs —
`[containment]`, `[descriptor]` — rather than numbering them. Numbered decisions drifted three
times during v1-c, twice inside a single document: inserting a decision silently invalidates
every reference after it. A related failure has already cost a commit of its own: `1448570`
("docs: disambiguate v1-a and v1-b decision references") had to qualify every bare "decision N"
in `src/` and `tests/` as "v1-b decision N", because decision numbering restarts in each plan and
a bare number does not say which plan it belongs to. That is a different failure mode than
reference drift within one document — but it is the same class, numbered decision references
going wrong, and it is an additional argument for slugs rather than a restatement of the first
one: a slug is unique across plans in a way `3` never is.

F11's plan named its decisions instead — `[containment]`, `[descriptor]`,
`[unknown-runs]`, `[counted]`, `[sample-unchanged]` — and had zero reference drift across 29
commits and several rounds of insertions. A slug is a word that insertion and reordering cannot
break; a number is not.

**Do not retrofit this onto plans that are already done.** `2026-08-17-intest-v1a-fixtures.md`,
`2026-08-18-intest-v1b-fixture-lifecycle.md` and `2026-08-19-intest-v1c-error-and-auth-tests.md`
still number their decisions, and that is correct as-is — leave them numbered. The drift risk
only exists while a plan is still being edited; a finished plan is never renumbered again, so the
risk it closed against is already zero, and renaming its decisions now would be pure churn
against a document whose entire value is being an accurate record of what was decided when. This
is the same reasoning that kept F11's closure from rewriting the v1-c run record. Treat this rule
as governing plans not yet written, never as a mandate to clean up the ones already closed.

## Dependency policy

New dependencies are held to a hard line, because adopters inherit whatever we take on.

- **No preview or prerelease packages**, in the tool or in generated output.
- **No licence surface.** Permissive licences only. A package that is technically excellent but
  charges commercial users is excluded on that ground alone — this is why `JsonSchema.Net` and
  FluentAssertions v8 are not used, both documented in §4.
- **No assumed vendor.** No cloud SDK, no identity library. If a capability needs one, it
  belongs behind an interface the adopter implements.
- **Deprecated or vulnerable versions are disqualifying.** Check nuget.org's deprecation and
  vulnerability metadata, not just the version number. The entire `Microsoft.OpenApi` 2.x line
  is deprecated, which an earlier revision missed.

## Scope requests

Two are expected often enough to answer up front:

**xUnit or NUnit support.** Reasonable ask, genuinely not free. The lifecycle, parameterization
and parallelism models differ enough that generated code, the runtime base class and the
frozen-axis machinery all change. It is the most likely v2 feature. Open an issue describing
your setup rather than a PR.

**Targeting below `net10.0`.** .NET 8 and 9 both reach end of support on 10 November 2026, and
MSTest v4's own floor is .NET 8. The test project's TFM is independent of your API's, so an API
on `net8.0` works today. If the SDK requirement is the blocker for you, say so in an issue —
that is useful data.

## Pull requests

- One logical change per PR, with a description saying what it changes and why.
- Tests for behaviour changes. §16 lists the suites the project commits to, including several
  that guard failures which are otherwise invisible until they reach production.
- Follow the existing style; do not reformat unrelated code.
- Update the spec in the same PR when a change alters documented behaviour. The spec is the
  source of truth, not an afterthought.
- Update [`docs/getting-started.md`](docs/getting-started.md) when a change alters the adoption
  path. It is deliberately a full end-to-end trace rather than a summary, because walking it is
  what catches gaps — reading it top to bottom is how the unowned initial-fixture creation was
  found, after the design had already been through several review rounds.

## Releases

Both packages follow semantic versioning and their majors move together. The compatibility
contract is in §3 of the spec and is public API:

- `InTest.Runtime` **N.x** accepts code generated by `InTest.Cli` **N.y** for any `y`.
- Majors may change generated code shape, the `intest.json` schema, or the runtime's public
  surface. They require `intest upgrade` and a reviewed diff.
- The previous major is supported for **12 months** after its successor ships.

Covered by semver: the runtime's exported types, the `intest.json` schema, CLI commands, flags
and exit codes, and the coverage report's JSON shape. Not covered: failure message text, the
internal `TestPlan` JSON, and template internals.

## Code of conduct

Be decent. Assume good faith, disagree about the work rather than the person, and accept that
maintainers may decline a change without it being a judgement on you. Behaviour that makes
people not want to participate is not welcome, and maintainers will act on it.

## Licence

Contributions are accepted under the MIT licence covering this repository.
