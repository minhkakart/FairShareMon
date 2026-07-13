# Project Initialization (Infrastructure Only)

Initialize the FairShareMonApi solution skeleton — foundation plumbing and abstractions only, per the stack decided in [stack-analysis-and-initialization.md](stack-analysis-and-initialization.md). **No business features**: no domain entities, no domain endpoints, no migrations for business tables. Auth/token mechanism is established at the **abstract level** (interfaces + pipeline wiring); where DI needs a concrete instance, an **empty stub** is registered.

## Objective

Turn the bare `dotnet new webapi` template into a runnable, tested infrastructure skeleton: response envelope, error handling, base controller, repository/transaction pattern, empty DbContext + EF-migration tooling, auth abstractions with stub implementations, Redis/NLog/AutoMapper/FluentValidation/versioning/Swagger wiring, and the real-MariaDB test harness. Business implementation (Auth, Members, Categories, Tags, Expenses, Events, Stats, Wallet, tiers) comes later, each with its own planning doc.

## Background

- Current state: `FairShareMonApi/` is a bare net8.0 template (weatherforecast endpoint), only DiDecoration + Swashbuckle referenced. No test project, no `global.json`.
- Architecture: quick-ordering's single-project layered shape adapted to .NET 8, with GHM lessons grafted in (uniform soft-delete policy, real HTTP statuses + stable error codes, real-DB tests). Rationale in the stack-analysis doc.
- Confirmed decisions (user, 2026-07-10): AutoMapper **13.0.1**, **FluentValidation**, **real MariaDB tests**, **EF Core migrations only**; this initialization covers **infrastructure only** — auth abstract, empty stubs where an instance is required.

## Requirements

- .NET 8 (`global.json` pinned to installed SDK **8.0.414**, `rollForward: latestFeature` — keeps 9.x out; the machine has 8.0.414 + 9.0.314), nullable + implicit usings.
- EF Core 8 + Pomelo wired (pooled DbContext, split query), **empty model** — no entities, no business migrations; design-time factory present so `dotnet ef` authoring works offline from day one.
- `ApiResult<T>` envelope with real HTTP statuses (404/400/401) and stable integer error codes.
- `AppController` base (route `api/v{version:apiVersion}/[controller]`, `[ResponseWrapped]`, `AuthenticatedUser` accessor) — treated as locked once written.
- `BaseRepository` + `ExecuteTransactionAsync`/`TransactionContext.NoCommit()`; `ExecuteQueryAsync` **without** opening a transaction; `Query<T>()` applying `AsNoTracking` + soft-delete filtering hooks (interface only for now).
- **Auth at abstract level:** authentication handler + token contracts wired into the pipeline; no real token issuance/validation logic, no `users`/`auth_tokens` entities. Concrete registrations are empty stubs.
- `Uuid.NewV7()` helper (manual UUIDv7 — never `Guid.CreateVersion7()`).
- Redis (StackExchange.Redis, `IConnectionMultiplexer` singleton), NLog, AutoMapper 13.0.1, FluentValidation (manual validation — validators registered via `AddValidatorsFromAssembly`), Asp.Versioning, Swagger with Bearer scheme — all wired and boot-verified.
- Test project with the real-MariaDB harness (probe once, skip when unreachable, per-test transaction rollback) + pure-logic unit tests for the helpers that exist.
- Vietnamese for any user-facing message infrastructure produces (e.g. generic error text).

## Out of Scope (deferred to per-feature planning docs)

- All domain entities/tables/migrations (`users`, `auth_tokens`, `members`, `categories`, `tags`, `expense_events`, `expenses`, `expense_shares`, `expense_tags`, `bank_accounts`, `audit_logs` — naming per the 2026-07-10 terminology decision: expense/share/event, never voucher/record/batch).
- Real token issuance/validation/refresh/revocation, register/login endpoints, BCrypt usage.
- Resource-owned query scoping, event lifecycle, audit logging, stats/export logic.
- Restoring `The-ideal.md` matters before *business* work starts; not a blocker for this infrastructure phase.

## Open Questions

New (raised during implementation, 2026-07-13 — implemented with a recorded default, awaiting confirmation):

4. **Field-error placement in the envelope.** The plan mandates "400 `ApiResult` with field errors" but the envelope spec is `{ data, isSuccess, error{code,message} }` and doesn't say where field errors go. Implemented as an optional `error.fields` property (`field name → string[] messages`, omitted from JSON when null) so the core shape is unchanged. Confirm, or direct a different shape.

All answered by user 2026-07-10:

1. ~~Target MariaDB server version~~ → **MariaDB 11.7.2** (local server; lowercase table names both plain and delimited). Pin `new MariaDbServerVersion(new Version(11, 7, 2))` in `AppDbContextDesignTimeFactory`.
2. ~~Test database~~ → **tests run against the real database** from the web project's `ConnectionStrings:Default`, overridable via the `FSM_TEST_CONNECTION` env var. No dedicated test DB, no `appsettings.tests.json` — per-test transaction rollback keeps the real data untouched.
3. ~~Memcached~~ → **replaced by Redis** (running locally under Docker). Cache client switches from EnyimMemcachedCore to StackExchange.Redis — see Decision Log. Default endpoint `localhost:6379` in appsettings.

## Assumptions

- Auth abstractions shaped for the spec's opaque-token design so the real implementation drops in later without reshaping contracts: token string → SHA-256 → whitelist lookup (cache first, DB fallback).
- Stub behavior: the stub token validator authenticates **nothing** (every `[Authorize]` request → 401); anonymous endpoints work. Stubs throw no exceptions and contain no logic.
- `BCrypt.Net-Core` is **not** added yet (belongs to the auth feature, not infrastructure).
- FluentValidation wired via the core `FluentValidation` package + `FluentValidation.DependencyInjectionExtensions` (**manual validation** — services inject `IValidator<T>` and validate explicitly; no `FluentValidation.AspNetCore`, no auto-validation); no validators yet beyond wiring verification in tests.
- Swagger stays on already-referenced Swashbuckle 6.6.2.
- Redis reachable at `localhost:6379` (Docker port mapping) — configurable via a `Redis` appsettings section.
- Concrete MariaDB credentials (database name, user, password) for `ConnectionStrings:Default` are filled in when Step 3 lands — user's local server, not known yet.

## Package Set

Main project (add to existing DiDecoration 1.1.0 / Swashbuckle 6.6.2):

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.EntityFrameworkCore` (+ `.Relational`, `.Design`, `.Tools`) | 8.0.x latest | ORM |
| `Pomelo.EntityFrameworkCore.MySql` | 8.0.x latest | MySQL/MariaDB provider |
| `AutoMapper` | **13.0.1** | mapping (last MIT license — never upgrade to 14+; 13.x includes `AddAutoMapper` DI registration) |
| `FluentValidation` (+ `.DependencyInjectionExtensions`) | 11.x latest | request validation (manual — `IValidator<T>` injected in services). License: **Apache-2.0**, free for commercial use — no pin needed (unlike AutoMapper). Upstream asks commercial users to voluntarily sponsor via GitHub Sponsors/OpenCollective |
| `NLog.Extensions.Logging` | 5.3.x (net8-compatible) | logging |
| `StackExchange.Redis` | 2.8.x latest | Redis client (token whitelist cache; matches quick-ordering) |
| `Asp.Versioning.Mvc` (+ `.ApiExplorer`) | 8.1.x | API versioning |
| `Swashbuckle.AspNetCore.Annotations` | 6.6.2 | Vietnamese Swagger summaries |

Test project: `xunit` 2.9.x, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` 17.11.x, `Xunit.SkippableFact` 1.4.x, `Microsoft.AspNetCore.Mvc.Testing` 8.0.x (WebApplicationFactory for the endpoint tests), reference to the web project. Requires `public partial class Program {}` at the bottom of `Program.cs` so the factory can see the entry point.

## Implementation Plan

### Step 1 — Solution scaffolding

1. Add `global.json` (SDK 8.0.414, `rollForward: latestFeature`).
2. Add the package set; create `FairShareMonApi.Tests` (xUnit) and add it to `FairShareMonApi.sln`.
3. Delete the weatherforecast template code.

### Step 2 — Core plumbing (no auth, no DB model)

1. `Models/ApiResult.cs` — `ApiResult<T>.Success(...)`, `ApiResult.Failure(...)`, `ApiResult.SuccessMessage(...)`; serializes `{ data, isSuccess, error{code,message} }`; implements `IActionResult`; HTTP status derived from the attached error (404/400/401/500 — not always-200).
2. `Constants/ErrorCodes.cs` (stable explicit ints) + `Exception/ErrorException.cs` (code → default HTTP status mapping).
3. `Attributes/ResponseWrappedAttribute.cs`, `Middlewares/ErrorHandlerMiddleware.cs` (outermost catch → `ApiResult` for wrapped endpoints), `Attributes/MvcFilters/ErrorHandlerFilter.cs` (exception filter — maps `FluentValidation.ValidationException` from manual validation to 400 `ApiResult` with field errors, plus ModelState/binding-error surfacing; suppress built-in invalid-model filter).
4. `Controllers/AppController.cs` — base controller as specified in CLAUDE.md. **Locked after this step.**
5. `Utils/Uuid.cs` — manual UUIDv7 `NewV7()`.
6. `Program.cs` bootstrap: NLog, DiDecoration `RegisterDecorators`, AutoMapper, FluentValidation validator registration (`AddValidatorsFromAssembly` — no auto-validation), API versioning, Swagger (Bearer scheme + Vietnamese descriptions), Redis (`IConnectionMultiplexer` singleton), pipeline `UseRouting → ErrorHandlerMiddleware → UseAuthentication → UseAuthorization → MapControllers`. `ErrorHandlerMiddleware` sits deliberately *after* routing (it needs endpoint metadata to see `[ResponseWrapped]`); it is the outermost catch for endpoint execution, not for routing itself. Ends with `public partial class Program {}` for WebApplicationFactory.
7. A minimal `Controllers/HealthController.cs` (anonymous `GET api/v1/health` returning `ApiResult`) so the envelope/pipeline is observable and testable.

### Step 3 — Data-access infrastructure (empty model)

1. `Database/AppDbContext.cs` — empty model (no `DbSet`s yet), `OnModelCreating` with UTF8MB4 defaults and the per-entity `ConfigureModel` call pattern documented; `Database/AppDbContext.partial.cs` reserved for future query filters.
2. `Database/Abstractions/` — `IEntity` (uuid PK, `CreatedAt`/`UpdatedAt`) and `IEntityDeletable` (soft-delete contract) interfaces only.
3. `Database/TransactionContext.cs`, `Extensions/DatabaseExtensions.cs` — `ExecuteTransactionAsync` (commit unless `NoCommit()`), `ExecuteQueryAsync` (no transaction).
4. `Repositories/Abstractions/` — `IBaseRepository`, `IQueryRepository<T>`, `BaseRepository` (`Query<T>()` with `AsNoTracking` + soft-delete filtering for `IEntityDeletable`).
5. `Database/AppDbContextDesignTimeFactory.cs` — pinned `new MariaDbServerVersion(new Version(11, 7, 2))` (OQ1 answer), reads `ConnectionStrings:Default`.
6. DbContext registration in `Program.cs` (`AddDbContextPool`, Pomelo, split query). **No migration generated** — first migration ships with the first entity (auth feature).
7. `appsettings.json` — `ConnectionStrings:Default` (real local MariaDB 11.7.2 — concrete credentials provided by user at this step), `Redis` section (`localhost:6379`).

### Step 4 — Auth abstractions + empty stubs

1. `Auth/AuthenticatedUser.cs` — identity model (id, username) + claims mapping helpers; `IContextAuthenticated` contract used by `AppController`.
2. Contracts (interfaces only, shaped for the opaque-token spec):
   - `Auth/Abstractions/ITokenService.cs` — issue/refresh/revoke/revoke-all signatures.
   - `Auth/Abstractions/ITokenValidator.cs` — raw token → `AuthenticatedUser?`.
   - `Auth/Abstractions/ITokenWhitelistStore.cs` — hash-based add/lookup/remove (the future cache+DB composite sits behind this).
3. `Auth/OpaqueTokenAuthenticationHandler.cs` — real `AuthenticationHandler` wired as the default scheme: extracts the Bearer token, delegates to `ITokenValidator`, honors `[AllowAnonymous]`; contains no token logic itself.
4. Empty stubs registered via DiDecoration (`[ScopedService]`): `StubTokenService` and `StubTokenValidator` (validator returns null → 401; service methods return failure results). Marked clearly as placeholders. **The auth feature must *delete* these stub files, not just add real implementations** — DiDecoration uses `TryAdd`, so a leftover stub registration silently wins over the real one.
5. Authorization wiring: default policy requiring authentication; `[AllowAnonymous]` on health endpoint.

### Step 5 — Test infrastructure

1. `FairShareMonApi.Tests/Infrastructure/DatabaseFixture.cs` — probes MariaDB once (connection: web project's `ConnectionStrings:Default`, overridden by `FSM_TEST_CONNECTION` env var when set — OQ2 answer); `SkipIfNoDb()`.
2. `IntegrationTestBase` — opens a real connection + transaction, builds `DbContextOptions<AppDbContext>` on that connection, rolls back on dispose.
3. Tests that exist at this stage:
   - `UuidTests` — format, version bits, time-ordering (pure unit).
   - `ApiResultTests` / error-mapping tests (pure unit).
   - `HealthEndpointTests` — via WebApplicationFactory: envelope shape + anonymous access; a minimal `[Authorize]`-guarded probe endpoint (hidden from Swagger with `[ApiExplorerSettings(IgnoreApi = true)]`, removed once the first real guarded endpoint lands) verifying the stub auth yields 401 wrapped in `ApiResult`.
   - `DatabaseFixtureSmokeTest` — skippable; verifies connect + rollback isolation works against real MariaDB.

### Step 6 — Verification & wrap-up

1. `dotnet build`, `dotnet test` (DB tests skip cleanly without MariaDB, pass with it), app boots, Swagger UI renders with Bearer scheme, health endpoint returns wrapped response, guarded endpoint returns 401 wrapped in `ApiResult`.
2. Update this doc (progress log, final outcome); confirm CLAUDE.md matches what was actually built.

## Impact Analysis

- **APIs:** only `GET api/v1/health` (+ a guarded probe endpoint for tests). No business endpoints.
- **Database:** none — no tables, no migrations. EF tooling ready via design-time factory.
- **Infrastructure:** MariaDB (11.7.2, local) needed only to un-skip DB tests; Redis (Docker) wired but nothing depends on it yet.
- **Services:** auth stubs registered — every `[Authorize]` request 401s until the real auth feature lands.
- **Documentation:** CLAUDE.md/AGENTS.md already aligned; business phases will get their own planning docs.

## Decision Log

### Decision
Initialization delivers infrastructure only; auth exists as contracts + pipeline wiring with empty stub registrations (validator authenticates nothing → 401).

### Reason
User direction 2026-07-10 ("init basic infrastructures only, no business thing; auth/token at abstract level, empty instances where needed"). Keeps the skeleton reviewable and lets each business feature land with its own planning doc and migration.

### Alternatives Considered
- Full phased build-out (previous version of this plan) — deferred; the phase list is preserved in git history and the features remain mapped in the stack-analysis doc.
- Skipping auth wiring entirely — rejected: retrofitting the authentication pipeline later would touch `Program.cs`, `AppController`, and middleware ordering, which are meant to be locked/stable after initialization.

### Decision
Cache is **Redis** (Docker), client **StackExchange.Redis** — replacing the spec's Memcached/EnyimMemcachedCore.

### Reason
User decision 2026-07-10 (OQ3): a Redis container is what's actually running locally. StackExchange.Redis matches quick-ordering, whose conventions this repo was adapted from — the "Redis → Memcached" adaptation row disappears. CLAUDE.md, AGENTS.md, and `.agents/rules/rules.md` updated accordingly.

### Alternatives Considered
- EnyimMemcachedCore (original plan, per the spec) — dropped: no Memcached instance exists locally.
- `Microsoft.Extensions.Caching.StackExchangeRedis` (`IDistributedCache`) — simpler surface, but the token-whitelist store benefits from direct key TTL control, and quick-ordering's reference patterns use `IConnectionMultiplexer` directly.

### Decision
Use the core `FluentValidation` package (+ `FluentValidation.DependencyInjectionExtensions` for `AddValidatorsFromAssembly`) with **manual validation** — not `FluentValidation.AspNetCore` auto-validation. *(Supersedes the earlier "keep FluentValidation.AspNetCore despite deprecation" decision.)*

### Reason
User decision 2026-07-13. `FluentValidation.AspNetCore` is deprecated/maintenance-mode and the FluentValidation team explicitly recommends manual validation. Services inject `IValidator<T>` and validate explicitly; failures throw `ValidationException`, which `ErrorHandlerFilter` maps to a 400 `ApiResult` with field errors — so the response shape stays identical to what auto-validation would have produced.

Licensing note (2026-07-13): FluentValidation is **Apache-2.0** — free for commercial use with no version ceiling (contrast AutoMapper, pinned at 13.0.1 as the last MIT release). The project displays a request that commercial users voluntarily sponsor it (GitHub Sponsors / OpenCollective); this is a courtesy ask, not a license obligation.

### Alternatives Considered
- `FluentValidation.AspNetCore` 11.3.x auto-validation (previous decision, GHM reference pattern) — dropped: deprecated upstream, and adopting the recommended path now avoids a later migration.
- Custom auto-validation action filter over `IValidator<T>` — recreates the deprecated machinery; not worth owning for this domain's simple payloads.

## Progress Log

### 2026-07-10

- Full-featured plan written, then rescoped per user direction to infrastructure-only with abstract auth + empty stubs. Awaiting answers to Open Questions 1–3 (MariaDB version/connection, test DB, Memcached) — Step 1–2 can start without them; Steps 3 and 5 need OQ1/OQ2.
- Plan reviewed. Fixes applied: SDK pin corrected 8.0.422 → **8.0.414** (actual installed SDK; 8.0.422 would fail `rollForward: latestFeature`), added `Microsoft.AspNetCore.Mvc.Testing` + `public partial class Program` for WebApplicationFactory, documented why `ErrorHandlerMiddleware` sits after `UseRouting`, committed the 401 test to WebApplicationFactory with a Swagger-hidden probe endpoint, recorded the FluentValidation.AspNetCore deprecation trade-off, and made stub deletion by the auth feature explicit (DiDecoration `TryAdd` hazard).
- User answered all Open Questions: MariaDB **11.7.2** (pin ServerVersion), tests hit the **real DB** (`ConnectionStrings:Default`) with `FSM_TEST_CONNECTION` env override, and cache is **Redis under Docker** (StackExchange.Redis replaces EnyimMemcachedCore). CLAUDE.md / AGENTS.md / rules.md synced to Redis. Plan is unblocked — implementation can start.

### 2026-07-13

- User decision: use the core **`FluentValidation`** package, not `FluentValidation.AspNetCore`. Plan switched from auto-validation to **manual validation** (`AddValidatorsFromAssembly` registration; services inject `IValidator<T>`; `ValidationException` → 400 `ApiResult` via `ErrorHandlerFilter`). Package set, requirements, Step 2, and Decision Log updated; the stack-analysis doc's validation decision line annotated accordingly.
- Recorded FluentValidation licensing status (user flagged the project's sponsorship notice): Apache-2.0, free for commercial use, no version pin needed; upstream requests voluntary sponsorship from commercial users (GitHub Sponsors / OpenCollective).

### 2026-07-13 (implementation — Steps 1–4 complete)

- **Step 1 done.** `global.json` pinned SDK `8.0.414` / `rollForward: latestFeature`. Environment note: the machine now reports only SDK **8.0.422** installed (the 2026-07-10 note assumed 8.0.414 + 9.x); the pin still resolves cleanly to 8.0.422 via `latestFeature`, so no change needed. Package set added to `FairShareMonApi.csproj` — resolved versions: EF Core (`Microsoft.EntityFrameworkCore` + `.Relational`, `.Design`, `.Tools`) **8.0.28**, Pomelo **8.0.3**, AutoMapper **[13.0.1]** (exact pin; NuGet emits the known NU1903 advisory for it — accepted, the pin is a recorded user decision), FluentValidation + DependencyInjectionExtensions **11.12.0**, NLog.Extensions.Logging **5.3.15**, StackExchange.Redis **2.8.58**, Asp.Versioning.Mvc + .ApiExplorer **8.1.1**, Swashbuckle.AspNetCore.Annotations **6.6.2**. `FairShareMonApi.Tests` created (scaffold only, zero tests — Step 5 belongs to the test-engineer): xunit **2.9.3**, xunit.runner.visualstudio **2.8.2**, Microsoft.NET.Test.Sdk **17.11.1**, Xunit.SkippableFact **1.4.13**, Microsoft.AspNetCore.Mvc.Testing **8.0.28**, project reference to the web project; added to the sln. Weatherforecast template code deleted (`Program.cs` rewritten; `FairShareMonApi.http` now targets `GET /api/v1/health`).
- **Step 2 done.** `Models/ApiResult.cs` (envelope `{ data, isSuccess, error{code,message} }`, implements `IActionResult`, status derived from the error — plus optional `error.fields` for validation errors, see OQ4), `Constants/ErrorCodes.cs` (1xxx infrastructure block: `InternalError=1000`, `ValidationFailed=1001`, `Unauthorized=1002`, `NotFound=1003`; feature areas claim their own blocks later), `Exception/ErrorException.cs` (code → default HTTP status; folder is `Exception/` per convention but the **namespace is `FairShareMonApi.Exceptions`** — a namespace named `Exception` would shadow `System.Exception` across all project namespaces), `Attributes/ResponseWrappedAttribute.cs` (endpoint metadata **and** result filter that auto-wraps plain DTO returns into `ApiResult<T>`, per CLAUDE.md "auto-wrapped"), `Middlewares/ErrorHandlerMiddleware.cs` (after `UseRouting`; rethrows for non-wrapped endpoints), `Attributes/MvcFilters/ErrorHandlerFilter.cs` (ModelState surfacing + `ValidationException`/`ErrorException` mapping; built-in invalid-model filter suppressed), `Controllers/AppController.cs` (**locked from now on**), `Utils/Uuid.cs` (manual RFC 9562 UUIDv7), `Program.cs` bootstrap per plan ending in `public partial class Program`, `Controllers/HealthController.cs`. Template `UseHttpsRedirection` dropped — the pipeline is exactly the plan's order plus dev-only Swagger.
- **Step 3 done.** Empty-model `Database/AppDbContext.cs` (UTF8MB4 + `utf8mb4_unicode_ci` defaults, `ConfigureModel` call pattern documented, `ConfigureQueryFilters` partial hook) + `AppDbContext.partial.cs` (query filters only), `Database/Abstractions/IEntity.cs` (reconciled with rules.md Entity Conventions: `ulong Id` PK **and** `string Uuid` + `CreatedAt`/`UpdatedAt` — the plan's "(uuid PK, ...)" shorthand), `IEntityDeletable.cs`, `Database/TransactionContext.cs`, `Extensions/DatabaseExtensions.cs` (`ExecuteTransactionAsync` saves+commits unless `NoCommit()`; `ExecuteQueryAsync` opens no transaction), `Repositories/Abstractions/{IBaseRepository,IQueryRepository,BaseRepository}.cs` (`Query<T>()` = AsNoTracking + `EF.Property` soft-delete filter for `IEntityDeletable`), `Database/AppDbContextDesignTimeFactory.cs` (pinned `MariaDbServerVersion(11.7.2)`; verified offline via `dotnet ef dbcontext info` → `ServerVersion=11.7.2-mariadb`). `AddDbContextPool` + Pomelo + split query in `Program.cs`. **No migration generated.** `appsettings.json`: `ConnectionStrings:Default` (local MariaDB, `fairsharemon`/root), `Redis` section (`localhost:6379`), `NLog` section (colored console).
- **Step 4 done.** `Auth/AuthenticatedUser.cs` (id + username, claims round-trip) with `IContextAuthenticated` + its real claims-mapping implementation `ContextAuthenticated` (`[ScopedService]`, used by `AppController` — this one is infrastructure, not a stub); contracts `Auth/Abstractions/{ITokenService(+TokenPair),ITokenValidator,ITokenWhitelistStore(+TokenWhitelistEntry)}.cs`; `Auth/OpaqueTokenAuthenticationHandler.cs` as default scheme (Bearer extraction → `ITokenValidator`; `HandleChallengeAsync` writes the 401 **in the ApiResult envelope** so guarded endpoints fail wrapped); stubs `Auth/StubTokenService.cs` + `Auth/StubTokenValidator.cs` (`[ScopedService]`, all-failure, no logic, marked MUST-DELETE for the auth feature — DiDecoration `TryAdd` hazard); authorization **FallbackPolicy** = require authenticated user (so `[AllowAnonymous]` on health is meaningful, per plan); Swagger-hidden `[Authorize]` probe `Controllers/AuthProbeController.cs` (`[ApiExplorerSettings(IgnoreApi = true)]`, temporary until the first real guarded endpoint).
- **Verification:** `dotnet build .\FairShareMonApi.sln` clean (0 errors; only the accepted AutoMapper NU1903 warnings). App booted (`dotnet run`, Kestrel on :5200, NLog console live, no Redis/MariaDB required to boot — `AbortOnConnectFail=false`, lazy multiplexer): `GET /api/v1/health` → 200 `{"data":{"message":"Hệ thống hoạt động bình thường."},"isSuccess":true,"error":null}`; `GET /api/v1/authprobe` → 401 `{"data":null,"isSuccess":false,"error":{"code":1002,"message":"Phiên đăng nhập không hợp lệ hoặc đã hết hạn."}}` (with and without a bogus Bearer token); `/swagger/v1/swagger.json` renders with Bearer scheme + Vietnamese info, probe endpoint hidden. `dotnet test` runs the empty scaffold without failure. Steps 5 (tests) and 6 (wrap-up) remain.

### 2026-07-13 (test infrastructure — Step 5 complete)

- **Step 5 done** (test-engineer). Test harness: `FairShareMonApi.Tests/Infrastructure/DatabaseFixture.cs` — probes MariaDB **once per run** (static `Lazy`, 3s connect timeout, `SELECT 1`); connection string from `FSM_TEST_CONNECTION` env var, falling back to the web project's `ConnectionStrings:Default` (appsettings.json copied to test output via the project reference, with a source-path fallback); `SkipIfNoDb()` via `Skip.If`. `Infrastructure/IntegrationTestBase.cs` — per test: skip-if-no-DB, open real `MySqlConnection` + `BeginTransaction`, `DbContextOptions<AppDbContext>` built **on that connection** (Pomelo, pinned `MariaDbServerVersion(11.7.2)`); `CreateContext()` enlists via `UseTransaction`, `CreateCommand()` binds raw commands to the transaction; **rollback + dispose on `DisposeAsync`** — the real DB is never dirtied. No packages added to the test csproj (Pomelo/MySqlConnector/EF flow transitively through the project reference).
- Tests added (35 total): `UuidTests` (6 — canonical 36-char lowercase format, version nibble `7`, RFC 9562 variant `10xx`, 48-bit ms timestamp matches current UTC, sequential generations time-ordered as strings, 1000 generations unique), `ApiResultTests` (14 — Success/SuccessMessage/Failure envelope fields, custom status codes, code→status derivation theory incl. unknown-code→500, explicit-status override, serialized camelCase envelope shape `{data,isSuccess,error{code,message}}` with `statusCode` excluded and `error.fields` present-when-given/omitted-when-null, `Failure(ErrorException)` copy semantics), `ErrorExceptionTests` (7 — `GetDefaultHttpStatus` mapping theory 1000→500/1001→400/1002→401/1003→404/unknown→500, ctor default-vs-explicit status), `HealthEndpointTests` (5, `WebApplicationFactory<Program>` — health 200 success envelope; AuthProbe 401 error envelope with `error.code=1002` both without token and with a bogus Bearer token; swagger.json hides `authprobe` while exposing health; Bearer security scheme present), `DatabaseFixtureSmokeTest` (3, skippable — harness connection scalar SELECT, `AppDbContext` on the harness transaction via `SqlQueryRaw`, and insert-then-rollback isolation on a session-scoped TEMPORARY InnoDB table: row visible inside the transaction, gone after rollback, schema-free).
- `dotnet test .\FairShareMonApi.sln`: **Failed: 0, Passed: 32, Skipped: 3, Total: 35** — the 3 DB smoke tests skip cleanly (local MariaDB unreachable), everything else passes. No production code touched. Known gap: the DB smoke tests have not yet executed against a live MariaDB on this machine — re-run once the local server is up (or set `FSM_TEST_CONNECTION`) to un-skip them. Step 6 (wrap-up) remains.

### 2026-07-13 (code-review fixes applied — milestone approved, 0 blocking)

- **SHOULD-FIX applied — `Attributes/ResponseWrappedAttribute.cs`:** the result filter now auto-wraps **only 2xx** object results; non-2xx `ObjectResult`s (e.g. a future `BadRequest(dto)`) pass through untouched instead of being mislabeled `isSuccess: true`. Covered by new pure unit tests `FairShareMonApi.Tests/ResponseWrappedAttributeTests.cs` (7 tests: wrap-200-default, wrap-201-keeps-status, 400/404/500/302 untouched theory, already-`ApiResult` untouched) — no new production endpoints needed.
- **`Attributes/MvcFilters/ErrorHandlerFilter.cs`:** both `CollectFields` overloads now camelCase field keys via `JsonNamingPolicy.CamelCase.ConvertName` (grouped after conversion to stay collision-safe), so `error.fields` matches the envelope's camelCase contract. `ApiResultTests.Failure_WithFields_ExposesFieldErrorsUnderErrorFields` updated to the camelCase contract (`"name"`).
- **`Extensions/DatabaseExtensions.cs`:** `ExecuteQueryAsync`'s delegate changed to `Func<TContext, CancellationToken, Task<TResult>>` so the token actually reaches the query (matching how `ExecuteTransactionAsync` threads it into Begin/Save/Commit). Callers updated: `Repositories/Abstractions/IBaseRepository.cs` + `BaseRepository.cs` (only callers; no test callers exist yet).
- **`Middlewares/ErrorHandlerMiddleware.cs`:** an `OperationCanceledException` with `context.RequestAborted.IsCancellationRequested` is now rethrown instead of being logged as `LogError` and written as a 500 to an aborted connection.
- **`Utils/Uuid.cs`:** doc comment now states ordering is guaranteed only across millisecond boundaries (no intra-ms monotonicity counter; same-ms values are mutually random).
- **`FairShareMonApi.sln`:** stray bare `# ` line (artifact of today's `dotnet sln add`) removed; CRLF + BOM preserved — the diff vs HEAD is now purely the test-project addition.
- **`FairShareMonApi/FairShareMonApi.csproj`:** unused `Microsoft.AspNetCore.OpenApi` template leftover removed (Swashbuckle owns doc generation; nothing referenced it after the weatherforecast deletion).
- **Recorded reconciliation (review item 8):** `Database/Abstractions/IEntity.cs` keeps **non-nullable `string Uuid`** although rules.md's Entity Conventions text says "`string? Uuid`" — the constructor always sets `Uuid = Uuid.NewV7()`, so non-nullable is the truthful contract and avoids needless null-handling at every read. Deliberate, not a silent deviation; if rules.md should be updated to match, that is a one-word doc fix for the orchestrator/user.
- **Deferred per coordinator:** Swagger padlock on anonymous-vs-guarded endpoints (auth feature), `HandleForbiddenAsync` envelope wrapping (auth feature), `error.fields` placement itself (OQ4 — user checkpoint pending).
- **Verification:** `dotnet build .\FairShareMonApi.sln` — 0 errors (only the accepted AutoMapper NU1903 advisory warnings). `dotnet test .\FairShareMonApi.sln` — **Failed: 0, Passed: 39, Skipped: 3, Total: 42** (3 expected MariaDB skips).

### 2026-07-13 (Step 6 — orchestrator verification, milestone closed)

- Delta re-review by the code-reviewer: **APPROVE**, all fixes correct, no new blocking findings (one residual nit noted for later: `JsonNamingPolicy.CamelCase` only camel-cases the leading segment of nested validation paths like `Address.Street` → `address.Street`; irrelevant until nested DTO validation exists).
- Orchestrator verification: build 0 errors; `dotnet test` **Failed: 0, Passed: 39, Skipped: 3, Total: 42**; live boot on :5200 — `GET /api/v1/health` → 200 success envelope (Vietnamese message), `GET /api/v1/authprobe` → 401 wrapped envelope (`error.code=1002`), `swagger/v1/swagger.json` → 200.
- Committed as the infrastructure-initialization milestone.

## Final Outcome

Infrastructure skeleton delivered per plan (Steps 1–6), reviewed and approved with zero blocking findings.

- **Solution:** `global.json` (SDK 8.0.414 pin, latestFeature), full package set (EF Core 8.0.28 + Pomelo 8.0.3, AutoMapper [13.0.1], FluentValidation 11.12.0 manual, NLog, StackExchange.Redis, Asp.Versioning 8.1.1, Swashbuckle), `FairShareMonApi.Tests` project.
- **Plumbing:** `ApiResult<T>` envelope with real HTTP statuses + stable 1xxx error codes + optional `error.fields`; `[ResponseWrapped]` (2xx-only auto-wrap); `ErrorHandlerMiddleware` + `ErrorHandlerFilter`; locked `AppController`; `Uuid.NewV7()`; Vietnamese user-facing messages.
- **Data access:** empty `AppDbContext` (UTF8MB4), `IEntity`/`IEntityDeletable`, `ExecuteTransactionAsync`/`NoCommit()`/`ExecuteQueryAsync`, `BaseRepository.Query<T>()` (AsNoTracking + soft-delete filter), design-time factory pinned to MariaDB 11.7.2 (offline EF authoring verified). No migrations — first one ships with the auth feature.
- **Auth:** opaque-token contracts + `OpaqueTokenAuthenticationHandler` (default scheme, wrapped 401 challenge), FallbackPolicy requiring authentication, MUST-DELETE stubs (`StubTokenService`, `StubTokenValidator`), hidden `[Authorize]` probe endpoint.
- **Tests:** 42 total — 39 pass, 3 MariaDB smoke tests skip (server not running on this machine during the milestone; un-skip by starting MariaDB or setting `FSM_TEST_CONNECTION`).

**Known limitations / handoffs:** OQ4 (`error.fields` placement) awaits user confirmation at the checkpoint; DB smoke tests never executed against a live server yet; Redis wired but unused; auth feature must delete the stubs, own the Swagger padlock filter and `HandleForbidAsync` wrapping; `ResponseWrapped` auto-wrap of plain DTOs and `ValidationException`→400 mapping lack end-to-end coverage until the first real feature endpoint exists.

## Future Improvements

- Business features in order: auth (real token service + entities), members, categories/tags, expenses/shares/audit, events, stats/export, wallet/QR, tiers — each with its own `/planning/*.md`.
- ~~Restore `The-ideal.md` from `6b19f01` before the first business feature.~~ Done 2026-07-10 — restored, then rewritten as a feature-only spec (see [the-ideal-feature-spec-rewrite.md](the-ideal-feature-spec-rewrite.md)).