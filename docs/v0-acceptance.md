# v0 acceptance run

**Date:** 2026-08-17 · **Commit:** `bec4ee1` plus the fix recorded as F1 below
**Task:** Plan Task 22 — point InTest at real deployed APIs and record what happens.

The v0 plan's acceptance criterion was a run against "one real API in a real pipeline". This
run used three purpose-built sample APIs instead (`samples/`), one per OpenAPI producer, so the
producer matrix and the acceptance run are the same exercise. They are committed, so every
finding below is reproducible.

---

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
| Orders, Inventory live runs | **Not run** — see "Not covered" |

**16 of 22 operations (73%) ran on synthesized operationIds.** The decision to treat synthesis
as a first-class path rather than a fallback (§6) is load-bearing, not defensive: on this
corpus most of the suite depends on it.

---

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

Fixed in `InitCommand` by adding `appsettings*.json` to the copied content. **A regression test
that runs a generated suite — not merely compiles it — is still missing.**

### F2 — readiness path is resolved against the API base URL · **open**

Readiness probed `http://localhost:5081/api/health/ready` and got 404, because the base URL was
`…/api/` and the probe path is relative. Health endpoints conventionally live at the **host
root**, not under the API prefix — the sample follows that convention, as most services do.

Setting `readiness.path` to an absolute URL works and is a usable workaround. But the default
configuration puts a relative path against an API-prefixed base, so the out-of-the-box shape is
wrong for the common case.

*Options:* document that `readiness.path` may be absolute; or give readiness its own base URL;
or resolve relative readiness paths against the origin rather than the base URL.

### F3 — base URL and spec path prefix silently duplicate · **open, highest value**

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

*Recommended:* detect it. At `AssemblyInitialize`, if the base URL's path is a prefix of every
generated path, fail with a message naming both. That is cheap, and it converts a wall of 404s
into one sentence. §7 should also state plainly that `Api:BaseUrl` substitutes for
`servers[0].url`.

### F4 — readiness burns the full timeout on a 404 · **open, minor**

F2 took the full 120 seconds to fail. A 404 or 405 on the probe path is a misconfiguration, not
a cold start, and no amount of waiting fixes it. Treating those two statuses as terminal would
have surfaced F2 in three seconds rather than two minutes.

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

---

## Known v0 gaps confirmed

The three Catalog failures are all `POST`/`PUT`:

```
POST http://localhost:5081/api/products → expected 201, got 415 (2ms)
Body: {"title":"Unsupported Media Type","status":415,…}
```

No request body is sent, because v0 has no fixtures — `TestData` covers path parameters only.
This is the documented v0 boundary and the entire subject of plan **v1-a**. It is not a defect.

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

## Not covered

Stated rather than glossed:

- **Orders and Inventory were generated and compiled, but not run live.** Orders needs the
  identity server running and a multi-identity `ITestTokenProvider`, which is v1-c work.
- **No auth tests were generated**, because v0 does not generate them. Orders declares
  `security` on all 7 operations, so it is ready for v1-c.
- **No pipeline run.** The plan said "in a real pipeline"; this was local only.
- **`X-Test-Run-Id` was not verified in server-side telemetry.** The header is sent, but no
  sink was configured to confirm arrival.
- **The Duende trial-mode startup warning was not observed**, since the identity server was
  never started.

## Actions

| # | Action | Where |
|---|---|---|
| 1 | F3 — detect base-URL/path-prefix duplication and fail loudly; document `Api:BaseUrl` as substituting for `servers[0].url` | §7, `TestHost` |
| 2 | F2 — decide how readiness resolves its path | §13, `Readiness` |
| 3 | Add a test that **runs** a generated suite, not merely compiles it | §16 |
| 4 | F4 — treat 404/405 on the readiness probe as terminal | `Readiness` |
| 5 | F5 — document the route-constraint / path-signature trap | `docs/getting-started.md` |
