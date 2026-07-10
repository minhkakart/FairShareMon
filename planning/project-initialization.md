# Project Initialization (Infrastructure Only)

Initialize the FairShareMonApi solution skeleton — foundation plumbing and abstractions only, per the stack decided in [stack-analysis-and-initialization.md](stack-analysis-and-initialization.md). **No business features**: no domain entities, no domain endpoints, no migrations for business tables. Auth/token mechanism is established at the **abstract level** (interfaces + pipeline wiring); where DI needs a concrete instance, an **empty stub** is registered.

## Objective

Turn the bare `dotnet new webapi` template into a runnable, tested infrastructure skeleton: response envelope, error handling, base controller, repository/transaction pattern, empty DbContext + EF-migration tooling, auth abstractions with stub implementations, Memcached/NLog/AutoMapper/FluentValidation/versioning/Swagger wiring, and the real-MariaDB test harness. Business implementation (Auth, Members, Categories, Tags, Vouchers, Batches, Stats) comes later, each with its own planning doc.

## Background

- Current state: `FairShareMonApi/` is a bare net8.0 template (weatherforecast endpoint), only DiDecoration + Swashbuckle referenced. No test project, no `global.json`.
- Architecture: quick-ordering's single-project layered shape adapted to .NET 8, with GHM lessons grafted in (uniform soft-delete policy, real HTTP statuses + stable error codes, real-DB tests). Rationale in the stack-analysis doc.
- Confirmed decisions (user, 2026-07-10): AutoMapper **13.0.1**, **FluentValidation**, **real MariaDB tests**, **EF Core migrations only**; this initialization covers **infrastructure only** — auth abstract, empty stubs where an instance is required.

## Requirements

- .NET 8 (`global.json` pinned to installed SDK 8.0.422), nullable + implicit usings.
- EF Core 8 + Pomelo wired (pooled DbContext, split query), **empty model** — no entities, no business migrations; design-time factory present so `dotnet ef` authoring works offline from day one.
- `ApiResult<T>` envelope with real HTTP statuses (404/400/401) and stable integer error codes.
- `AppController` base (route `api/v{version:apiVersion}/[controller]`, `[ResponseWrapped]`, `AuthenticatedUser` accessor) — treated as locked once written.
- `BaseRepository` + `ExecuteTransactionAsync`/`TransactionContext.NoCommit()`; `ExecuteQueryAsync` **without** opening a transaction; `Query<T>()` applying `AsNoTracking` + soft-delete filtering hooks (interface only for now).
- **Auth at abstract level:** authentication handler + token contracts wired into the pipeline; no real token issuance/validation logic, no `users`/`auth_tokens` entities. Concrete registrations are empty stubs.
- `Uuid.NewV7()` helper (manual UUIDv7 — never `Guid.CreateVersion7()`).
- Memcached (EnyimMemcachedCore), NLog, AutoMapper 13.0.1, FluentValidation auto-validation, Asp.Versioning, Swagger with Bearer scheme — all wired and boot-verified.
- Test project with the real-MariaDB harness (probe once, skip when unreachable, per-test transaction rollback) + pure-logic unit tests for the helpers that exist.
- Vietnamese for any user-facing message infrastructure produces (e.g. generic error text).

## Out of Scope (deferred to per-feature planning docs)

- All domain entities/tables/migrations (`users`, `auth_tokens`, `members`, `categories`, `tags`, `expense_batches`, `expense_vouchers`, `voucher_records`, `voucher_tags`, `audit_logs`).
- Real token issuance/validation/refresh/revocation, register/login endpoints, BCrypt usage.
- Resource-owned query scoping, batch lifecycle, audit logging, stats/export logic.
- Restoring `The-ideal.md` matters before *business* work starts; not a blocker for this infrastructure phase.

## Open Questions

1. **Target MariaDB server version** — needed to pin `ServerVersion` in `AppDbContextDesignTimeFactory` and for `ConnectionStrings:Default`. (Fallback assumption if unanswered: `MariaDbServerVersion 11.x` matching quick-ordering's `11.8.6`, adjustable later.)
2. **Test database** — dedicated `fairsharemon_test` database on the same server? Connection via `appsettings.tests.json` + `FSM_TEST_CONNECTION` env-var override (GHM pattern)?
3. **Memcached** — local instance at default `localhost:11211`?

## Assumptions

- Auth abstractions shaped for the spec's opaque-token design so the real implementation drops in later without reshaping contracts: token string → SHA-256 → whitelist lookup (cache first, DB fallback).
- Stub behavior: the stub token validator authenticates **nothing** (every `[Authorize]` request → 401); anonymous endpoints work. Stubs throw no exceptions and contain no logic.
- `BCrypt.Net-Core` is **not** added yet (belongs to the auth feature, not infrastructure).
- FluentValidation wired via `FluentValidation.AspNetCore` 11.x auto-validation; no validators yet beyond wiring verification in tests.
- Swagger stays on already-referenced Swashbuckle 6.6.2.

## Package Set

Main project (add to existing DiDecoration 1.1.0 / Swashbuckle 6.6.2):

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.EntityFrameworkCore` (+ `.Relational`, `.Design`, `.Tools`) | 8.0.x latest | ORM |
| `Pomelo.EntityFrameworkCore.MySql` | 8.0.x latest | MySQL/MariaDB provider |
| `AutoMapper` | **13.0.1** | mapping (last MIT license — never upgrade to 14+; 13.x includes `AddAutoMapper` DI registration) |
| `FluentValidation.AspNetCore` | 11.3.x | request validation (auto-validation) |
| `NLog.Extensions.Logging` | 5.3.x (net8-compatible) | logging |
| `EnyimMemcachedCore` | 3.x latest | Memcached client |
| `Asp.Versioning.Mvc` (+ `.ApiExplorer`) | 8.1.x | API versioning |
| `Swashbuckle.AspNetCore.Annotations` | 6.6.2 | Vietnamese Swagger summaries |

Test project: `xunit` 2.9.x, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` 17.11.x, `Xunit.SkippableFact` 1.4.x, reference to the web project.

## Implementation Plan

### Step 1 — Solution scaffolding

1. Add `global.json` (SDK 8.0.422, `rollForward: latestFeature`).
2. Add the package set; create `FairShareMonApi.Tests` (xUnit) and add it to `FairShareMonApi.sln`.
3. Delete the weatherforecast template code.

### Step 2 — Core plumbing (no auth, no DB model)

1. `Models/ApiResult.cs` — `ApiResult<T>.Success(...)`, `ApiResult.Failure(...)`, `ApiResult.SuccessMessage(...)`; serializes `{ data, isSuccess, error{code,message} }`; implements `IActionResult`; HTTP status derived from the attached error (404/400/401/500 — not always-200).
2. `Constants/ErrorCodes.cs` (stable explicit ints) + `Exception/ErrorException.cs` (code → default HTTP status mapping).
3. `Attributes/ResponseWrappedAttribute.cs`, `Middlewares/ErrorHandlerMiddleware.cs` (outermost catch → `ApiResult` for wrapped endpoints), `Attributes/MvcFilters/ErrorHandlerFilter.cs` (exception filter + ModelState/FluentValidation surfacing; suppress built-in invalid-model filter).
4. `Controllers/AppController.cs` — base controller as specified in CLAUDE.md. **Locked after this step.**
5. `Utils/Uuid.cs` — manual UUIDv7 `NewV7()`.
6. `Program.cs` bootstrap: NLog, DiDecoration `RegisterDecorators`, AutoMapper, FluentValidation auto-validation, API versioning, Swagger (Bearer scheme + Vietnamese descriptions), Memcached, pipeline `UseRouting → ErrorHandlerMiddleware → UseAuthentication → UseAuthorization → MapControllers`.
7. A minimal `Controllers/HealthController.cs` (anonymous `GET api/v1/health` returning `ApiResult`) so the envelope/pipeline is observable and testable.

### Step 3 — Data-access infrastructure (empty model)

1. `Database/AppDbContext.cs` — empty model (no `DbSet`s yet), `OnModelCreating` with UTF8MB4 defaults and the per-entity `ConfigureModel` call pattern documented; `Database/AppDbContext.partial.cs` reserved for future query filters.
2. `Database/Abstractions/` — `IEntity` (uuid PK, `CreatedAt`/`UpdatedAt`) and `IEntityDeletable` (soft-delete contract) interfaces only.
3. `Database/TransactionContext.cs`, `Extensions/DatabaseExtensions.cs` — `ExecuteTransactionAsync` (commit unless `NoCommit()`), `ExecuteQueryAsync` (no transaction).
4. `Repositories/Abstractions/` — `IBaseRepository`, `IQueryRepository<T>`, `BaseRepository` (`Query<T>()` with `AsNoTracking` + soft-delete filtering for `IEntityDeletable`).
5. `Database/AppDbContextDesignTimeFactory.cs` — pinned MariaDB `ServerVersion` (OQ1), reads `ConnectionStrings:Default`.
6. DbContext registration in `Program.cs` (`AddDbContextPool`, Pomelo, split query). **No migration generated** — first migration ships with the first entity (auth feature).
7. `appsettings.json` — `ConnectionStrings:Default`, `Memcached` section placeholders.

### Step 4 — Auth abstractions + empty stubs

1. `Auth/AuthenticatedUser.cs` — identity model (id, username) + claims mapping helpers; `IContextAuthenticated` contract used by `AppController`.
2. Contracts (interfaces only, shaped for the opaque-token spec):
   - `Auth/Abstractions/ITokenService.cs` — issue/refresh/revoke/revoke-all signatures.
   - `Auth/Abstractions/ITokenValidator.cs` — raw token → `AuthenticatedUser?`.
   - `Auth/Abstractions/ITokenWhitelistStore.cs` — hash-based add/lookup/remove (the future cache+DB composite sits behind this).
3. `Auth/OpaqueTokenAuthenticationHandler.cs` — real `AuthenticationHandler` wired as the default scheme: extracts the Bearer token, delegates to `ITokenValidator`, honors `[AllowAnonymous]`; contains no token logic itself.
4. Empty stubs registered via DiDecoration (`[ScopedService]`): `StubTokenService` and `StubTokenValidator` (validator returns null → 401; service methods return failure results). Marked clearly as placeholders to be replaced by the auth feature.
5. Authorization wiring: default policy requiring authentication; `[AllowAnonymous]` on health endpoint.

### Step 5 — Test infrastructure

1. `FairShareMonApi.Tests/Infrastructure/DatabaseFixture.cs` — probes MariaDB once (OQ2 connection: `appsettings.tests.json`, `FSM_TEST_CONNECTION` override); `SkipIfNoDb()`.
2. `IntegrationTestBase` — opens a real connection + transaction, builds `DbContextOptions<AppDbContext>` on that connection, rolls back on dispose.
3. Tests that exist at this stage:
   - `UuidTests` — format, version bits, time-ordering (pure unit).
   - `ApiResultTests` / error-mapping tests (pure unit).
   - `HealthEndpointTests` — envelope shape + anonymous access; a `[Authorize]`-guarded probe endpoint (or WebApplicationFactory call) verifying the stub auth yields 401.
   - `DatabaseFixtureSmokeTest` — skippable; verifies connect + rollback isolation works against real MariaDB.

### Step 6 — Verification & wrap-up

1. `dotnet build`, `dotnet test` (DB tests skip cleanly without MariaDB, pass with it), app boots, Swagger UI renders with Bearer scheme, health endpoint returns wrapped response, guarded endpoint returns 401 wrapped in `ApiResult`.
2. Update this doc (progress log, final outcome); confirm CLAUDE.md matches what was actually built.

## Impact Analysis

- **APIs:** only `GET api/v1/health` (+ a guarded probe endpoint for tests). No business endpoints.
- **Database:** none — no tables, no migrations. EF tooling ready via design-time factory.
- **Infrastructure:** MariaDB needed only to un-skip DB tests; Memcached wired but nothing depends on it yet.
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

## Progress Log

### 2026-07-10

- Full-featured plan written, then rescoped per user direction to infrastructure-only with abstract auth + empty stubs. Awaiting answers to Open Questions 1–3 (MariaDB version/connection, test DB, Memcached) — Step 1–2 can start without them; Steps 3 and 5 need OQ1/OQ2.

## Final Outcome

(pending)

## Future Improvements

- Business features in order: auth (real token service + entities), members, categories/tags, vouchers/records/audit, batches, stats/export — each with its own `/planning/*.md`.
- Restore `The-ideal.md` from `6b19f01` before the first business feature.