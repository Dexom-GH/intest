# Getting started

End-to-end walkthrough: from an existing .NET API with an OpenAPI document, to a committed
integration test suite running as a post-deployment gate.

> **This describes the designed flow. None of it runs yet.**
>
> InTest is at design stage — no code, nothing published to NuGet. This document exists so the
> intended experience can be reviewed, argued with, and checked for gaps before it is built.
> Tracing it end to end has already exposed one: nothing owned the creation of the first
> fixture files. If you spot another, that is the most useful thing you can send us.
>
> Design detail lives in the [specification](superpowers/specs/2026-08-16-intest-api-test-generator-design.md);
> section references like §10 point there.

Running example: an `Orders` API using Swashbuckle, deployed to a `staging` environment.

## Prerequisites

| | |
|---|---|
| .NET SDK | 10.0 or later — the **test project** targets `net10.0`; your API can target anything |
| Test framework | MSTest. xUnit and NUnit are not supported in v1 |
| Spec | OpenAPI 3.x, JSON or YAML, local file or URL |
| API | Deployed and reachable from wherever the tests run |

---

## Phase 0 — decide whether to adopt

Before scaffolding anything, find out what adoption will cost you.

```bash
dotnet tool install -g InTest.Cli
intest survey "https://orders-staging.example.com/swagger/v1/swagger.json"
```

`survey` takes the same inputs as `spec.source` — a glob over local files, or a URL — because
when you are still deciding whether to adopt, a Swagger endpoint is often all you have. It
reads specs and reports; it writes nothing. What it tells you and why you care:

| Measure | What it means for you |
|---|---|
| % with `operationId` | How many test names are synthesized from method and path rather than taken from the spec |
| % with request `example` | **Your fixture workload.** The single biggest number here — see Phase 5 |
| % with response schemas | How many tests assert a full contract vs. status code only |
| % with `security` declared | How many auth tests you get |
| OpenAPI 3.0 vs 3.1, keyword census | Whether any schema uses keywords the validator cannot evaluate (§9) |

Low `example` coverage is not a blocker, but it is work. Better to know now than in Phase 5.

---

## Phase 1 — make the spec available

### Same repository as the API

Emit the document at build time so it is always current.

| Producer | Add | Note |
|---|---|---|
| Swashbuckle | `Microsoft.Extensions.ApiDescription.Server` | — |
| Built-in `Microsoft.AspNetCore.OpenApi` | Native build-time generation | JSON only; YAML at build time is not supported yet |
| NSwag | `NSwag.MSBuild` | Set `NoBuild=true` or the build recurses |

The document lands somewhere like `Orders/bin/Debug/net10.0/orders.json`. Pointing InTest at a
build artifact is correct — it cannot go stale.

### Different repository, or only a URL

Skip this phase. Point `spec.source` at the URL; `generate` snapshots it to a committed
`spec.json` so you still get a reviewable diff when the spec changes (§9).

---

## Phase 2 — scaffold the test project

```bash
mkdir Orders.ApiTests && cd Orders.ApiTests
intest init --spec ../Orders/bin/Debug/net10.0/orders.json
```

`init` refuses to overwrite anything that already exists (exit `3`). It writes:

| File | Owner | Purpose |
|---|---|---|
| `intest.json` | yours | Configuration |
| `Orders.ApiTests.csproj` | yours | Pins packages, copies the spec to output, sets `RunSettingsFilePath`, adds the `INTEST0001` guard |
| `AssemblyInfo.cs` | yours | `[assembly: DoNotParallelize]` — the **only** place parallelization is declared |
| `TestStartup.cs` | yours | DI registrations, named `HttpClient`, handlers |
| `OrdersTestBase.cs` | yours | Your shared helpers; derives from `ApiTestBase` |
| `appsettings.json`, `appsettings.staging.json` | yours | Profiles, base URLs, readiness |
| `orders.runsettings` | yours | Ships with `profile` **commented out** — see Phase 3 |
| `.config/dotnet-tools.json` | yours | Pins the CLI version so CI and your machine agree |

Everything above is yours to edit and is never regenerated.

---

## Phase 3 — configure

### Base URL

In `appsettings.staging.json`:

```json
{ "Api": { "BaseUrl": "https://orders-staging.example.com/api/" } }
```

**Keep the trailing slash.** `https://host/api` + `orders/1` resolves to `https://host/orders/1`
— the `api` segment is silently dropped, and you get a green suite hitting the wrong routes.
InTest normalizes this, but the configured value is yours and worth getting right.

### Choosing a profile

Precedence, first match wins:

1. `.runsettings` → `TestRunParameters` → `profile`
2. Environment variable `INTEST_PROFILE`
3. Default in `appsettings.json`

The scaffolded `orders.runsettings` leaves `profile` commented out on purpose. Uncomment it and
tier 1 always matches, making `INTEST_PROFILE` unreachable. Pin the profile only in
environment-specific files like `qa.runsettings`.

### Secrets

Never in `intest.json`, never in fixtures. Register providers in `TestStartup.cs` — user-secrets
locally, whatever your organisation uses in CI — and reference them from fixtures as
`{{config:Orders:ApiKey}}` (§10).

### Auth

Implement `ITestTokenProvider` if your API needs a token. InTest ships only a static-token
provider — no cloud SDK, no identity library.

```csharp
public sealed class OrdersTokenProvider : ITestTokenProvider
{
    public IReadOnlyCollection<string> Identities => ["default", "wrong-scope"];

    public Task<string> GetTokenAsync(string audience, string? identity = null,
                                      CancellationToken ct = default) => /* ... */;
}
```

`Identities` decides which auth tests run. Return one and the "wrong scope → 403" tests skip
with a stated reason. Return more and they run. No-token → 401 tests always run regardless.

---

## Phase 4 — generate

```bash
intest generate
```

Writes `Generated/` — `TestHost.g.cs`, `OrdersTests.g.cs`, `Schemas.g.cs` — plus
`coverage-report.json`. All regenerated wholesale; never hand-edit them.

Read `coverage-report.json` now. It tells you what was skipped and why, which operations run on
synthesized IDs, which produce status-only tests, and which auth tests are gated off.

If any operation takes a request body, `generate` exits non-zero and reports the missing
fixtures. That is expected on a first run.

---

## Phase 5 — fixtures

```bash
intest fixtures repair
```

The only command that writes under `fixtures/`. It creates missing fixtures, adds `TODO:`
sentinels for newly-required properties, flags properties that left the schema, and **never
overwrites a value you wrote**.

Now the real work. A generated fixture looks like:

```jsonc
{
  "$meta": { "tier": 4, "operationId": "createOrder" },
  "customerId": "TODO:customerId",
  "items": [ { "sku": "TODO:sku", "quantity": 1 } ]
}
```

**Tests fail while `TODO:` sentinels remain, by design.** The alternative is inventing
plausible values, which a permissive endpoint accepts — leaving a green suite that asserts
nothing. A red test gets fixed; a passing test that proves nothing never does. Failures are
aggregated into a single message at startup naming every unresolved sentinel and its file, not
one failure per test.

Replace sentinels with real values, or with tokens:

| Token | Resolved |
|---|---|
| `{{config:Orders:ApiKey}}` | Once per run, from configuration — keeps credentials out of committed files |
| `{{fixture:seededCustomer.id}}` | After assembly fixtures complete — for referential integrity |
| `{{runId}}` | Once per run |
| `{{utcNow}}` | Per request |

For data that must exist in the target environment, write an `IAssemblyFixture`:

```csharp
var customer = await _api.CreateCustomerAsync(ct);
ctx.Publish("seededCustomer.id", customer.Id);        // now available to {{fixture:…}}
ctx.OnCleanup(() => _api.DeleteCustomerAsync(customer.Id));
```

Cleanup is registered next to what created it and drained in reverse. Make every cleanup
idempotent — it will sometimes not run at all (§14).

### Reducing this work permanently

```bash
intest fixtures promote
```

Prints a paste-ready snippet — an `ISchemaFilter`, an XML `<example>`, a transformer — for
adding examples to the API itself. It writes nothing, because `spec.source` is a build artifact
the next build would overwrite. Examples added there improve your Swagger UI and any generated
clients too, and every InTest run reports the percentage so the number visibly moves.

---

## Phase 6 — run

```bash
dotnet test
```

Startup order: build configuration → mint the run ID → build the service provider → load the
schema bundle → wait for readiness → run assembly fixtures → validate every fixture.

Readiness matters more than it sounds. Post-deploy cold start is the largest single source of
flaky gates, so InTest polls until the service answers — by default requiring two consecutive
successes, because during a slot swap a single 200 can come from the old instance. It fails
with `Service did not become ready within 120s (last response: 503)` rather than 200 confusing
test failures.

Every request carries `X-Test-Run-Id: {TestId}`, so a failed gate run can be traced in your
telemetry down to the individual test.

---

## Phase 7 — commit

| Commit | Ignore |
|---|---|
| `Generated/`, `coverage-report.json` | `appsettings.local.json` |
| `fixtures/`, `intest.json` | user-secrets |
| `appsettings*.json` (non-local), `*.runsettings` | anything with a credential in it |
| `.config/dotnet-tools.json` | |
| **`spec.json`** — only when `spec.source` is a URL | `spec.json` is **not** created for a local `spec.source`; the build copies that file instead |

Generated code is committed so a spec change arrives as a reviewable diff on the pull request,
where someone can see that an endpoint's contract moved.

**If you took the URL path in Phase 1, `spec.json` must be committed.** It is the snapshot
`generate` took, it is what `--check` compares against in Phase 8, and it is the only thing
that gives a URL-sourced spec a reviewable diff at all. Leave it uncommitted and Phase 8 fails
against a file that is not in the repository.

---

## Phase 8 — wire CI

Two pipelines, two different jobs.

### Pull request

```bash
dotnet tool restore
dotnet build ../Orders                 # produce the spec artifact
intest generate --check                # fail if committed output is stale
dotnet test
```

`--check` compares `Generated/` and `coverage-report.json` against a fresh run. Exit codes:
`0` identical, `1` output differs, `2` tool error, `4` the tool version does not match
`intestVersion` in `intest.json`. That last one exists so a tool upgrade is never mistaken for
spec drift — adopt a new version deliberately with `intest upgrade`.

Cross-repo, the API build step means cloning the API repo. That is a real cost and worth
knowing before you start.

### Post-deployment gate

```bash
dotnet test --filter "TestCategory=Contract" --settings qa.runsettings
```

Contract tests only. Variation tests send hundreds of malformed payloads — useful in lower
environments, noise in a gate, and liable to trip a WAF or rate limiter.

---

## The steady state

```
spec changes  →  regenerate on a branch  →  `generate` reports drift, exits non-zero
              →  `fixtures repair`       →  fill new sentinels
              →  PR shows the whole change as a diff
```

The gate never sees red, because red happens on the branch where someone can fix it. That is
the entire point of generating at pull-request time rather than in the pipeline.

---

## Things that will bite you

**Trailing slashes.** Covered above, and worth repeating: it fails silently and looks like
passing tests.

**Non-ASCII in display names.** `X-Test-Run-Id` must be ASCII — `HttpClient` throws otherwise.
InTest transliterates and appends a hash when a name is lossy, so emoji and RTL variation cases
stay distinct. Custom display names you write yourself go through the same path.

**Parallelization.** Declared **only** in `AssemblyInfo.cs`. Setting `MSTestParallelizeScope` in
the project file generates a second attribute and breaks the build; InTest catches this with a
clear `INTEST0001` error rather than letting you meet `CS0579`. The default is sequential.
Before enabling parallelism, make sure every test creates its own data — and note that two
concurrent pipelines against one environment cannot coordinate at all.

**Cleanup is best-effort.** `AssemblyCleanup` does not run on a crash, a cancelled pipeline, or
an agent timeout. Everything is tagged with a run ID whose timestamp is UTC, so an out-of-band
sweeper can delete anything older than a day using the ID alone. Write one. Without it,
cancelled pipelines slowly fill your environment with orphans nobody can reproduce locally.

**This is for pre-production.** InTest adds no guard rails against being pointed at production,
deliberately. Pointing it there is your decision and your consequences.
