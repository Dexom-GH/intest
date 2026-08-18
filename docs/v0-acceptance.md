# Acceptance runs — v0 and v1-a

A living record. Each phase ends by regenerating against `samples/` and appending its results
here, so the defect numbering (`F1`, `F2`, …) runs continuously across phases and the "carried
forward" list at the end is always the current one.

| Phase | Date | Commit | Headline |
|---|---|---|---|
| v0 | 2026-08-17 | `bec4ee1` + F1 fix | Catalog **6 of 9**; Orders and Inventory generated but never run |
| v1-a | 2026-08-17 | `466e118` | All three run live: **22 of 22**; **44 sentinels** filled by hand |

---

# v0 acceptance run

**Task:** Plan Task 22 — point InTest at real deployed APIs and record what happens.

The v0 plan's acceptance criterion was a run against "one real API in a real pipeline". This
run used three purpose-built sample APIs instead (`samples/`), one per OpenAPI producer, so the
producer matrix and the acceptance run are the same exercise. They are committed, so every
finding below is reproducible.

## What was exercised

| Sample | Auth | Producer | OpenAPI version | Operations | `operationId` |
|---|---|---|---|---|---|
| `Catalog.Api` | none | built-in `Microsoft.AspNetCore.OpenApi` | **3.1.1** | 9 | 0 of 9 |
| `Orders.Api` | Duende, client-credentials | Swashbuckle 10.2.3 | **3.0.4** | 7 | 0 of 7 |
| `Inventory.Api` | none | NSwag 14.7.1 | **3.0.0** | 6 | 6 of 6 |

All three producers behaved exactly as §6 documents. Swashbuckle and the built-in package emit
no `operationId` for controller actions; NSwag derives `{Controller}_{Action}` — `Stock_GetAll`,
`Warehouses_GetById`.

Three producers also produced **three different OpenAPI versions**, which was not planned and is
worth knowing: the built-in package emits 3.1, Swashbuckle 3.0.4, NSwag 3.0.0. A tool claiming
OpenAPI 3.x support meets all three in a single organisation.

## Results

| Stage | Result |
|---|---|
| `intest generate` across all three | **22 operations generated, 0 skipped** |
| Generated projects compile | **3 of 3** |
| Catalog suite against a live API | **6 of 9 passing** |
| Orders, Inventory live runs | **Not run** — closed in v1-a below |

**16 of 22 operations (73%) ran on synthesized operationIds.** The decision to treat synthesis
as a first-class path rather than a fallback (§6) is load-bearing, not defensive: on this
corpus most of the suite depends on it.

## Defects found

### F1 — `appsettings.json` never reached the output directory · **fixed**

Every generated project failed at `AssemblyInitialize`:

```
System.IO.FileNotFoundException: The configuration file 'appsettings.json' was not found
and is not optional. The expected physical path was '…/bin/Debug/net10.0/appsettings.json'.
```

`init` scaffolds `appsettings.json`, but the generated `.csproj` copied only
`Generated/spec-schemas.json` to the output directory. `TestHost` resolves configuration from
`AppContext.BaseDirectory`, so nothing could start.

**No existing test caught this**, and the reason matters: §16's compile-verification test proves
generated code *builds*, never that it *runs*. Building and running are different gates, and v0
only had the first.

Fixed in `InitCommand` by adding `appsettings*.json` to the copied content, and now guarded by
`GeneratedSuiteExecutionTests` — which was verified to fail with this exact exception when the
fix is reverted.

### F2 — readiness path is resolved against the API base URL · **fixed**

Readiness probed `http://localhost:5081/api/health/ready` and got 404, because the base URL was
`…/api/` and the probe path is relative. Health endpoints conventionally live at the **host
root**, not under the API prefix — the sample follows that convention, as most services do.

**Not a design flaw — a scaffold default.** Ordinary URI resolution already distinguishes the
two cases: `health/ready` resolves against the base URL, `/health/ready` against the origin.
The scaffold shipped the former; it now ships the latter, and both forms are tested.

### F3 — base URL and spec path prefix silently duplicate · **fixed**

```
GET http://localhost:5081/api/api/products/aaaaaaaa-… → expected 200, got 404 (2ms)
```

The configured base URL was `http://localhost:5081/api/`; the spec's paths already begin
`/api/products`. InTest ignores `servers[]` by design (§7), so the configured base URL plays
that role and spec paths append to it — meaning the base must be the **origin**, not the API
prefix, whenever the spec's paths carry it.

Nothing states this. §7 documents the *opposite* failure at length — a missing trailing slash
silently dropping a base segment — and the guard added for it does not detect duplication.
The symptom is every test returning 404 with a correct-looking configuration.

Detected rather than documented: `generate` writes the shared operation prefix to
`Generated/spec-paths.json`, and `AssemblyInitialize` fails before the first request with a
message naming both halves and the value to use instead.

### F4 — readiness burns the full timeout on a 404 · **fixed**

F2 took the full 120 seconds to fail. A 404 or 405 on the probe path is a misconfiguration, not
a cold start, and no amount of waiting fixes it. 404, 405, 410 and 501 are now terminal, and the
message explains leading-slash resolution — F2 would have been reported in three seconds.

### F5 — route constraints do not disambiguate OpenAPI paths · **sample fixed, worth documenting**

The first Inventory spec was rejected:

```
The OpenAPI document could not be parsed:
  The path signature '/api/stock/{}' MUST be unique.
exit code 2
```

`GET /api/stock/{sku}` and `DELETE /api/stock/{id:int}` are distinct routes to ASP.NET, which
disambiguates by constraint. **OpenAPI has no notion of route constraints** — both collapse to
`/api/stock/{}`, which the specification requires to be unique. Any producer will emit this
invalid document from such a controller.

InTest behaved correctly: it refused to generate, named the exact problem, and returned exit
code 2 per §5's convention. This is a real-world trap worth a line in the documentation, since
the API compiles and serves traffic perfectly well.

## Known v0 gaps confirmed

The three Catalog failures were all `POST`/`PUT`:

```
POST http://localhost:5081/api/products → expected 201, got 415 (2ms)
Body: {"title":"Unsupported Media Type","status":415,…}
```

No request body was sent, because v0 has no fixtures — `TestData` covered path parameters only.
This was the documented v0 boundary and the entire subject of plan **v1-a**. It was not a
defect, and it is **closed below**: all three are green.

Also confirmed working as designed:

- **Failure messages.** Every failure named method, URL, expected vs actual, elapsed time, run
  id and response body. Diagnosing F3 took one message.
- **Run identity.** `tjayo-20260817T111559Z-c578e154-postapiproducts-contract` — prefix, UTC
  timestamp, entropy, and a slug derived from the display name, all ASCII.
- **Readiness messages.** `Service did not become ready within 120s (last response: 404).
  Probed 'health/ready' expecting 200, requiring 2 consecutive successes.` Named everything
  needed to diagnose F2.
- **Status-only tests.** 4 of 22 operations returned 204 and generated status-only tests rather
  than being skipped — the case an earlier revision silently dropped.

## v0 actions

All five closed.

| # | Action | Resolution |
|---|---|---|
| 1 | F3 — detect base-URL/path-prefix duplication | `generate` writes the shared operation prefix to `Generated/spec-paths.json`; `AssemblyInitialize` fails before the first request, naming both halves and the correct value. Segment-wise, so `/api` against `/apiary` is not flagged. §7 documents `Api:BaseUrl` as substituting for `servers[0].url` |
| 2 | F2 — readiness path resolution | Not a design flaw: ordinary URI rules already distinguish `health/ready` (base-relative) from `/health/ready` (origin-rooted). The scaffold shipped the former; it now ships the latter. Both forms tested |
| 3 | A test that **runs** a generated suite | `GeneratedSuiteExecutionTests` scaffolds, generates, builds and runs against a live `HttpListener` stub. **Negative control performed**: with the F1 fix reverted it fails with the original `FileNotFoundException`; restored, it passes |
| 4 | F4 — terminal readiness statuses | 404, 405, 410 and 501 now fail immediately with a message explaining leading-slash resolution, rather than consuming the timeout |
| 5 | F5 — route-constraint trap | Documented in `docs/getting-started.md` under "Things that will bite you" |

Two further defects surfaced while fixing these, both in the scaffold and both fixed: the
runsettings file was named `orders.runsettings` regardless of project name, and the default
`Api:BaseUrl` shipped **with** an `/api/` prefix — which is what produced F3 in the first place.

Test count went from 103 to 123.

---

# v1-a acceptance run — fixtures

**Date:** 2026-08-17 (UTC) · **Commit:** `466e118` (branch `feature/v1a-fixtures`)
**Task:** Plan v1-a Task 10 — regenerate against the samples now that fixtures exist, and
measure the fixture workload a real adopter faces.

Unit suite before the run: **226 passing, 0 failing** — Architecture 2, Cli 130, Runtime 88,
Golden 6.

Each sample got a **fresh test project in a scratch directory outside the repository**, taken
through `intest init` → `intest generate` → `intest fixtures repair` → fill sentinels →
`intest generate` → `dotnet test` against the live API. One deviation from a real adopter's
setup, in every project: `InTest.Runtime` is not published to NuGet, so the scaffolded
`PackageReference` was swapped for a `ProjectReference` — the same substitution
`GeneratedSuiteExecutionTests` makes.

## Results

| Sample | Ops | Fixtures composed | Sentinels filled | Live result |
|---|---|---|---|---|
| `Catalog.Api` | 9 | 8 | **23** | **9 of 9** |
| `Orders.Api` | 7 | 5 | **14** | **7 of 7** |
| `Inventory.Api` | 6 | 4 | **7** | **6 of 6** |
| **Total** | **22** | **17** | **44** | **22 of 22** |

```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 3 s - Catalog.ApiTests.dll (net10.0)
Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 3 s - Orders.ApiTests.dll (net10.0)
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 3 s - Inventory.ApiTests.dll (net10.0)
```

**Catalog reached 9 of 9**, as the plan predicted. The three v0 failures were exactly the three
operations carrying a request body — `POST /api/categories`, `POST /api/products`,
`PUT /api/products/{id}` — all of which returned 415 for want of one. Every one is now green.

**Orders and Inventory ran live for the first time**, closing the largest v0 gap. Orders needed
`samples/Identity.Server` for a client-credentials token; auth *tests* are still v1-c, so what
Orders proves here is that a secured API's bodies and parameters flow correctly under a bearer
token — not the 401/403 paths.

`generate` exiting 1 while fixtures are unresolved is by design (Task 4), and it did, every
time. Catalog's first run:

```
delete_api_categories_id: no fixture found.
get_api_categories_id: no fixture found.
get_api_products: no fixture found.
get_api_products_id: no fixture found.
get_api_products_id_tags: no fixture found.
post_api_categories: no fixture found.
post_api_products: no fixture found.
put_api_products_id: no fixture found.
Run 'intest fixtures repair' to create or update the fixture(s) listed above.
exit code 1
```

**Reproduced independently.** A second scratch project built from scratch off the same spec
produced `Created 8 fixture(s), updated 0 fixture(s)` and the same **23** sentinels; a second
live run against a freshly seeded database returned **9 of 9** again.

## The fixture workload — what `intest survey` will need to predict

This is the measurement the task existed for. **44 sentinels across 17 fixture files for 22
operations — two per operation on average**, but the average badly understates the shape:

| Where the work is | Sentinels |
|---|---|
| Path parameters (one per operation that has one) | 12 |
| Request-body properties | 32 |

Body properties are **73% of the work** and they cluster. Catalog's single
`post_api_products` is **11 of that sample's 23** — one operation, nearly half the API's
fixture cost — because every leaf property of a request body is sentinelled, required or not:

```jsonc
{
  "$meta": { "tier": 4, "operationId": "post_api_products", "generatedBy": "intest 0.1.0" },
  "body": {
    "sku": "TODO:sku",              "name": "TODO:name",
    "description": "TODO:description", "price": "TODO:price",
    "stockQuantity": "TODO:stockQuantity", "categoryId": "TODO:categoryId",
    "category": "TODO:category",    "availableFrom": "TODO:availableFrom",
    "supplierEmail": "TODO:supplierEmail", "dimensions": "TODO:dimensions",
    "tags": [ "TODO:tags" ]
  }
}
```

Only five of those eleven are in the schema's `required` set. **A useful predictor is therefore
not operation count but total leaf-property count across all JSON request bodies, plus one per
path parameter** — not `required` count, which would have predicted 5 where the real cost was 11.

Three shapes cost **nothing**, and all three are decisions working as designed:

- **Operations with no parameters and no body compose no fixture at all** — `GET /api/categories`,
  `GET /api/customers`, `GET /api/warehouses`.
- **Optional query parameters are omitted entirely** unless the spec gives them an `example` or
  a `default` (decision 1), so an operation whose only parameters are optional filters also
  composes nothing — `GET /api/orders` and `GET /api/stock`.

  Together those account for **5 of 22 operations, which is why there are 17 fixtures and not 22.**
- **Where a default exists, it is used and no sentinel appears.** Catalog's `GET /api/products`
  has five optional query parameters and produced a **tier-3 fixture with zero sentinels**:

  ```jsonc
  { "$meta": { "tier": 3, … }, "$parameters": { "page": "1", "pageSize": "20" } }
  ```

  This is the case the plan's self-review flagged as nearly fatal: sentinelling every parameter
  would have blocked an operation that already passed in v0 and finished v1-a *below* v0's six.
  The decision held.

## Defects found

### F6 — a nullable object property composes a scalar sentinel, losing its shape · **fixed**

`CreateProductRequest.dimensions` is a nullable reference to another schema. The built-in
producer emits OpenAPI 3.1's idiom for that:

```json
"dimensions": { "oneOf": [ { "type": "null" }, { "$ref": "#/components/schemas/DimensionsRequest" } ] }
```

`FixtureComposer.ComposeFromSchema` handles `$ref`, `object`, and `array`, but not `oneOf` or
`anyOf`. The schema is none of the three, so composition falls through to the bottom of the
method and emits `"dimensions": "TODO:dimensions"` — a **string** sentinel for what is actually
an object with three required properties. The adopter is told the property needs a value and
given no indication of its shape; the real fixture had to be written by hand:

```jsonc
"dimensions": { "lengthCentimetres": 10.0, "widthCentimetres": 5.0, "heightCentimetres": 2.0 }
```

**Nesting itself is fine** — the contrast proves it. Orders' `CreateOrderRequest.lines` is a
plain `array` of `$ref` and composed correctly, all the way into the nested object:

```jsonc
"lines": [ { "sku": "TODO:sku", "quantity": "TODO:quantity", "unitPrice": "TODO:unitPrice" } ]
```

So the gap is specifically **un-navigated `oneOf`/`anyOf`**. It is not cosmetic: `oneOf` with a
null branch is how OpenAPI 3.1 expresses *any* nullable complex property, and 3.1 is what the
built-in ASP.NET producer emits. Every adopter on the default .NET stack with a nullable
sub-object hits this.

Not blocking — the sentinel still fails loudly and the property here was optional — but it
under-reports the workload, and a required nullable sub-object would leave an adopter guessing.

**Fixed in `8d0367a`, hardened in `6952aeb`.** `ComposeFromSchema` now resolves a
`oneOf`/`anyOf`/`allOf` union by discarding branches that declare the JSON `null` type and
recursing into the single survivor. Zero or more than one remaining branch is genuine ambiguity
and still falls through to a sentinel rather than guessing — so the OpenAPI 3.0 composition
idiom `allOf: [{$ref: Base}, {…}]` is unchanged, not silently half-composed.

The check sits *after* the object and array checks, so a schema carrying both `type: object` and
an `allOf` still composes its declared properties. **That ordering is the fragile part of the
fix**, and review caught it stated only in a commit message: moving the check up beside the
`$ref` navigation reads as a tidy-up, leaves every test green, and silently drops those declared
properties. It is now pinned by a comment at the call site and by a regression test —
**negative control performed**: hoisting the check above the object check makes that test fail,
restoring it returns the suite to green.

This **changes the measurement above**: `dimensions` becomes three sentinels instead of one, so
Catalog goes from 23 to **25** and the corpus total from 44 to **46**. Orders (14) and Inventory
(7) are unchanged — verified by re-running `init` → `generate` → `fixtures repair` against all
three specs. The workload table and totals earlier in this section record the run **as it was
measured**, before the fix; they are left as-run rather than retconned.

Existing hand-filled fixtures are undisturbed: against the filled Catalog set the new composer
leaves `generate` at exit 0 and `repair` reporting `Nothing to repair`.

**One residual limitation, accepted deliberately.** OpenAPI 3.0's *composition* idiom —
`allOf: [{$ref: Base}, {type: object, properties: {…}}]` — has two non-null branches, so it is
ambiguous under the rule above and still composes to one opaque sentinel. Resolving it properly
means genuinely merging the branches' properties, not picking one, which is a different
operation from selecting a nullable union's single real branch. None of the three sample specs
uses it, so it is recorded here rather than guessed at; it is the natural follow-up if 3.0-style
composition shows up in a real document.

### F7 — the generated suite is not idempotent against a persistent store · **open, by construction**

Running the Catalog suite a second time against the same database, changing nothing:

```
Failed!  - Failed: 3, Passed: 6, Skipped: 0, Total: 9 - Catalog.ApiTests.dll (net10.0)
```

```
POST http://localhost:5081/api/categories → expected 201, got 409 (12ms)
Body: {"title":"A category named 'Accessories' already exists.","status":409}

POST http://localhost:5081/api/products → expected 201, got 409 (3ms)
Body: {"title":"A product with SKU 'ACC-0100' already exists.","status":409}
```

The third failure was `DeleteApiCategoriesId_Contract`, whose target the first run had already
deleted. The 9-of-9 above is therefore **9 of 9 on a freshly seeded database** — stated plainly
because the number is otherwise misleading.

This is inherent to literal fixture values plus a stateful API, not a coding error. What
matters is how much of it v1-a can already solve, which was measured rather than assumed:

- **`{{runId}}` fixes the free-form case.** Changing the category name to
  `"Accessories-{{runId}}"` and running the same test twice in a row passed both times.
- **It cannot fix a format-constrained unique field.** The SKU must match `^[A-Z]{3}-[0-9]{4}$`;
  no run id fits that pattern.
- **It cannot fix deleting a seeded row.** Nothing in v1-a creates the row to delete.

The designed answer to the remaining two is `{{fixture:…}}` with `IAssemblyFixture`, deferred to
**v1-b**, which now has a measured justification rather than a predicted one. Until then the
honest guidance is: a generated suite expects a reset database per run, and adopters should use
`{{runId}}` wherever the uniqueness constraint is free-form. That guidance is now written down,
with this second-run result as its evidence, under getting-started Phase 5.

### F8 — `ITestTokenProvider` has no consumers · **documented; code fix is v1-c**

The scaffold's `TestStartup.cs` says "Add configuration providers and an ITestTokenProvider
implementation here", and getting-started Phase 3 tells adopters to implement it. Nothing calls
it. Every reference to the interface in `src/`:

```
src/InTest.Cli/Commands/InitCommand.cs:113:  /// ITestTokenProvider implementation here.</summary>
src/InTest.Runtime/Neutral/ITestTokenProvider.cs:7:   public interface ITestTokenProvider
src/InTest.Runtime/Neutral/StaticTokenProvider.cs:4:  public sealed class StaticTokenProvider(…) : ITestTokenProvider
src/InTest.Runtime/Neutral/StaticTokenProvider.cs:15:  "Implement ITestTokenProvider with more than one identity…"
```

`GetTokenAsync` is declared and implemented, and called from nowhere. The generated template
sends `Client.SendAsync(request, …)` with no `Authorization` header, so **implementing the
interface has no effect on any generated request**.

Every Orders operation declares `security`. **Measured as a negative control** — the same suite,
same fixtures, same live server, with only the handler registration commented out:

```
GET    http://localhost:5082/api/customers      → expected 200, got 401 (3ms)
POST   http://localhost:5082/api/customers      → expected 201, got 401 (3ms)
GET    http://localhost:5082/api/orders         → expected 200, got 401 (1ms)
POST   http://localhost:5082/api/orders         → expected 201, got 401 (1ms)
DELETE http://localhost:5082/api/orders/dddddddd-…  → expected 204, got 401 (1ms)
…
Failed!  - Failed: 7, Passed: 0, Skipped: 0, Total: 7 - Orders.ApiTests.dll (net10.0)
```

Restoring the registration returns it to 7 of 7. So the entire Orders result rests on ~40 lines
of hand-written `DelegatingHandler` in `TestStartup.Register`. That handler is legitimate
team-owned code, but the adopter has no way to know it is required: the documented extension
point is a dead end, and the failure mode is a uniformly 401 suite.

Auth *tests* are correctly v1-c. **Reaching a secured endpoint at all is not an auth test** —
it is the precondition for every other test on a secured API, and v1-a generates suites for
such APIs today.

Documented rather than left as a trap: getting-started Phase 3 now opens with the fact that
nothing calls `GetTokenAsync`, and shows the `DelegatingHandler` that does work. The interface
still has no consumers — closing that is v1-c's job.

## v1-a actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | F6 — navigate `oneOf`/`anyOf`/`allOf` in `ComposeFromSchema`, choosing the single non-null branch | v1-a | **Closed** — `8d0367a` + `6952aeb`; suite 226 → 234, including a negative-controlled guard on the check's ordering |
| 2 | F8 — document that `ITestTokenProvider` is unwired and that a secured API needs a hand-written `DelegatingHandler` today | v1-a docs | **Closed** — getting-started Phase 3 |
| 3 | F8 — actually consume `ITestTokenProvider` from the generated template, so the documented extension point stops being a dead end | v1-c | Open |
| 4 | F7 — document that a generated suite assumes a reset environment, and that `{{runId}}` is the v1-a tool for free-form uniqueness | v1-a docs | **Closed** — getting-started Phase 5 |
| 5 | F7 — `{{fixture:…}}` / `IAssemblyFixture`, so create-then-delete and constrained-unique values stop depending on a reset database | v1-b | Open, now measured |
| 6 | `intest survey` should predict from **total request-body leaf properties + path parameters**, not operation count and not `required` count | v1-f | Open, input recorded above |
| 7 | Merge `allOf` composition (`[{$ref: Base}, {…}]`) rather than treating it as an ambiguous union | when a real spec needs it | Open, recorded under F6 |

## Carried forward — not covered by either run

Closed by v1-a:

- ~~Orders and Inventory were generated and compiled, but not run live.~~ Both now run live,
  7 of 7 and 6 of 6.
- ~~Operations with a request body cannot send one.~~ Closed — that was the point of v1-a.

Still open, stated rather than glossed:

- **No auth tests were generated**, because v1-a does not generate them. Orders declares
  `security` on all 7 operations, so it is ready for v1-c — but see F8: the token plumbing
  those tests will need does not exist yet either.
- **No pipeline run.** Both runs were local. "In a real pipeline" remains unmet.
- **`X-Test-Run-Id` was not verified in server-side telemetry.** The header is sent, but no
  sink was configured to confirm arrival.
- **The Duende trial-mode startup warning was not observed.** The identity server was exercised
  this time — it issued a client-credentials token that Orders accepted — but the run reused an
  already-running instance, so its startup output was never seen.
- **`survey`, `generate --check`, YAML input, and variation tests** are unbuilt, so nothing
  about them was exercised.
- **One sample was measured per producer.** The corpus is deliberate but small; nothing here
  says how the composer behaves on a large real-world document.
