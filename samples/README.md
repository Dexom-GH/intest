# Sample APIs

Three ASP.NET Core APIs and an identity server, built as fixtures for InTest. They serve three
purposes at once: the target for the v0 acceptance run
([`../docs/v0-acceptance.md`](../docs/v0-acceptance.md)), the producer matrix required by §16 of
the design spec, and a corpus a contributor can regenerate against without needing a private
API.

Nothing here is in the dependency closure of `InTest.Cli` or `InTest.Runtime`. Installing
either package pulls none of it.

## The four projects

| Project | Auth | OpenAPI producer | Emits | Why it exists |
|---|---|---|---|---|
| `Catalog.Api` | none | built-in `Microsoft.AspNetCore.OpenApi` | **3.1.1** | Every primitive the OpenAPI type system distinguishes, plus the nullable variant of each. No `operationId`, so synthesis is exercised |
| `Orders.Api` | Duende, client-credentials | Swashbuckle | **3.0.4** | Declares `security` per operation with the scope each needs, so auth contract tests have something to assert |
| `Inventory.Api` | none | NSwag | **3.0.0** | `{Controller}_{Action}` operationIds — stable-looking, but they churn when an action is renamed |
| `Identity.Server` | — | — | — | Two clients, one full-access and one read-only, so a genuine multi-identity token provider exists |

Three producers, three different OpenAPI versions. That was not contrived — it is simply what
each emits by default, and it is worth knowing that a single organisation can meet all three.

## Deliberate variations

- **`Catalog.Api`**: `ProductsController` has no `DELETE` (products are deactivated, never
  removed); `CategoriesController` has one. Generation must follow the spec, not an assumed
  CRUD shape.
- **`Orders.Api`**: `OrdersController` has `DELETE`; `CustomersController` does not. Reads need
  `orders.read`, writes need `orders.write`, so a read-only token receives 403 on writes.
- **`Inventory.Api`**: `StockController` has `DELETE`; `WarehousesController` is read-only. Its
  route parameters are strings and ints rather than GUIDs, so percent-encoding and empty-segment
  behaviour differ from the other two.
- **Status coverage**: 200, 201 with `Location`, 204 (bodiless), 400, 401, 403, 404, and 409
  from real unique indexes and restricted foreign keys.
- **Parameter positions**: route, query and header — the variation catalog is per-position.

## Persistence

File-backed SQLite, created and seeded on startup. A real relational provider is the point:
duplicate SKUs and referenced categories produce genuine 409s from database constraints. The EF
Core InMemory provider enforces neither, so error-path endpoints would return 200 where a real
deployment returns 409.

Seed data uses fixed GUIDs (`aaaaaaaa-…`, `11111111-…`) so tests can reference known rows
without fixtures — which is what made a v0 acceptance run possible before fixtures exist.

## Running them

None of the four sets a port in source, an `appsettings.json`, or a `launchSettings.json` (there
is none). Run any one of them exactly as written below and it binds to the ASP.NET Core
default, `http://localhost:5000` — confirmed by running each and reading its own "Now listening
on" line, not assumed. Since all four share that same default, running more than one at a time
needs an explicit, distinct `ASPNETCORE_URLS` per project — and more than one at a time is the
ordinary case, since `Orders.Api` needs `Identity.Server` reachable to validate tokens:

```bash
ASPNETCORE_URLS="http://localhost:5081" dotnet run --project samples/Catalog.Api
ASPNETCORE_URLS="http://localhost:5084" dotnet run --project samples/Identity.Server    # required only by Orders.Api
ASPNETCORE_URLS="http://localhost:5082" dotnet run --project samples/Orders.Api
ASPNETCORE_URLS="http://localhost:5083" dotnet run --project samples/Inventory.Api
```

Confirmed: all four stay up and answer `/health/ready` with the ports above set concurrently.
Pick different ports freely — nothing below depends on these specific numbers — but each
project's `Api:BaseUrl` (or `Identity:Authority`/`IdentityServer:IssuerUri` for the identity
pair) must then point at whatever you actually chose.

Each exposes `GET /health/ready`. Each writes its OpenAPI document beside its project file at
build time, so `intest` can read an artifact rather than needing a running instance.

### Configuring a generated suite against these

Two things the acceptance run got wrong first, both recorded in
[`../docs/v0-acceptance.md`](../docs/v0-acceptance.md):

- **`Api:BaseUrl` must be the origin** — `http://localhost:5081/`, not `http://localhost:5081/api/`.
  These specs' paths already begin `/api/`, and InTest appends them. Getting this wrong gives
  every test a 404 against configuration that looks right.
- **`readiness.path` must be absolute** here, because `/health/ready` sits at the host root
  rather than under `/api`.

## A note on Duende

`Duende.IdentityServer` requires a paid licence for **production** use. Development, testing and
personal projects are free, which is what this is — and it is never in the dependency closure of
a shipped InTest package.

If you lift `Identity.Server` into anything that serves real users, that changes: get a licence.
Running without a key logs a startup warning and does not otherwise restrict the application,
so it is easy to miss.
