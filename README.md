# InTest

Generates a complete, owned .NET test project that exercises a deployed API over real HTTP,
from its OpenAPI document.

The output is a normal MSTest project. You commit it, edit it, and run it with `dotnet test`
like any other test project. InTest is a development-time tool — it generates on your machine
or in a pull request, never as part of the deployment pipeline.

> **Status: design. There is no code yet.**
>
> This repository currently contains the design specification and nothing else. Nothing is
> published to NuGet, no command described below is runnable, and the package IDs are not yet
> reserved. If you found this looking for a working tool, come back later — or read the design
> and tell us where it's wrong, which is more useful to us right now.
>
> Read it: [`docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`](docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md)

## What it is for

Post-deployment gates. You deploy to an environment, and you want to know the API actually
works there — that routes resolve, responses match their declared schemas, auth is wired up,
and nothing 500s. That is a different job from unit tests, and it is usually either skipped or
hand-written once and left to rot.

InTest generates that suite from the OpenAPI document you already produce, and then gets out of
the way: the code is yours, it's committed, and you edit it like any other code you own.

## What it is not

- **Not a unit test generator.** It needs a deployed, reachable service.
- **Not a load or performance tool.**
- **Not a stateful flow tester.** Create → read → update → delete has no ordering model here and
  stays hand-written.
- **Not a mocking framework.** Real HTTP, real environment, real data.

## Requirements

Read these before evaluating — they are firm for v1, and they rule InTest out for some teams.

| | |
|---|---|
| Test project TFM | `net10.0`. Independent of your API's TFM — an API on `net8.0` is fine |
| Test framework | **MSTest only in v1.** xUnit and NUnit are the highest-priority v2 work, and the architecture is built to keep them additive — but today, if you are standardised on either, InTest is not for you yet |
| Spec | OpenAPI 3.x, JSON or YAML, local file or URL |
| Target | A deployed, reachable API |

## What day one actually looks like

Worth knowing before you start, because it surprises people.

InTest generates a request body for every operation that needs one. Where your spec provides an
`example`, that body is real. Where it does not, InTest emits an obvious `TODO:` placeholder —
**and the test fails until a human replaces it.**

That is deliberate. The alternative is filling in plausible-looking junk (`"string"`, `0`),
which a permissive endpoint accepts, so the suite passes while asserting nothing. A red test
gets fixed; a green test that proves nothing never does.

In practice that means, on an API with lots of POSTs and few spec examples, your first run is
mostly red and there is real work to do. Two things make that manageable:

- Run `intest survey` **before** adopting — it tells you what fraction of operations carry
  examples, so you can size the work in advance instead of discovering it.
- A useful suite runs immediately with no fixture work at all: every GET and DELETE contract
  test, every declared-error test (404s, 400s), and every no-token 401 test needs no body.

## How it will work

```bash
# See what InTest would make of your specs, before committing to anything
intest survey "specs/**/*.json"

# Scaffold a test project
intest init

# Generate tests from the spec
intest generate

# In CI: fail if the committed output is stale
intest generate --check
```

Generated code lands in `Generated/` and is regenerated wholesale. Your code lives in
same-named partial classes outside it, and InTest never touches those. Test data lives in
`fixtures/`, which `generate` never writes to.

## Design principles

1. **You own the output.** A full test project, committed, readable, editable.
2. **Generation happens at pull-request time**, never in the deployment pipeline. Bad output
   fails on the PR where someone can fix it.
3. **Fail loudly.** Placeholder data causes a clear failure with a name attached. There are no
   skip flags and no silent green.
4. **Prefer the framework's own mechanism.** Parallelization, timeouts, retries and filtering
   are MSTest's job, not InTest's.
5. **Stable dependencies only.** No preview packages, and no dependency carrying a licence
   obligation you would inherit.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). While the
project is at design stage, the most valuable contribution is a careful reading of the spec.
Prior review rounds have already caught contradictions, a build-breaking interaction, and a
correlation identifier that silently collapsed across data-driven test rows.

## Security

To report a vulnerability, see [SECURITY.md](SECURITY.md). Please do not open a public issue
for one.

## Licence

MIT — see [LICENSE](LICENSE).
