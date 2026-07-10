# Stack Analysis & Initialization Recommendation

A comparative analysis of the two reference codebases (`quick-ordering` and `GHM.RecommendationManagement.*` in HrmApi) to decide the code stack / infrastructure for initializing FairShareMonApi.

## Objective

Analyze the code spec of both reference projects, compare their advantages and disadvantages, and recommend the stack + project infrastructure to initialize this project with.

## Background

- The repo was reset (commit `3431eaf`): `The-ideal.md` and prior planning docs were emptied/deleted. The full domain spec is still recoverable at `git show 6b19f01:The-ideal.md`.
- The current `FairShareMonApi/` project is a bare `dotnet new webapi` template (net8.0, Swashbuckle only).
- Domain (from spec): personal/group expense ledger with debt splitting — users, opaque-token auth (whitelist, SHA-256 hash, Memcached + DB fallback), members, categories, tags, expense batches (OPEN/CLOSED lifecycle), vouchers + records, append-only audit logs, stats/export endpoints. MySQL/MariaDB + EF Core (Pomelo), .NET 8.

## Requirements

- Compare architecture, package stack, request flow, data access, auth, testing, and code style of both reference codebases.
- Weigh advantages/disadvantages of each against this project's scale and spec.
- Recommend a concrete initialization stack (projects, packages, folder layout, conventions).

## Open Questions

- Restore `The-ideal.md` from `6b19f01` (it is currently empty after the reset), or is a rewritten spec coming? CLAUDE.md still treats it as the source of truth. *(Still open — carried into [project-initialization.md](project-initialization.md).)*
- ~~Mapping~~ → **Decided 2026-07-10: AutoMapper 13.0.1** (last MIT release; never upgrade to 14+).
- ~~Validation~~ → **Decided 2026-07-10: FluentValidation** (GHM style, auto-validation).
- ~~Test DB~~ → **Decided 2026-07-10: real MariaDB integration tests** with per-test rollback isolation.

## Assumptions

- `The-ideal.md` content from commit `6b19f01` remains the authoritative domain spec despite the reset (the reset removed files but CLAUDE.md still references it).
- Recommendation only — no code initialization performed in this task unless requested.

## Analysis

### quick-ordering

**Shape:** .NET 9, single web project + one xUnit test project. Pragmatic folder-based layering (`Controllers → Services/Api → Repositories → AppDbContext`) enforced by convention, not by assembly boundaries.

**Stack:** EF Core 9 + Pomelo MySQL (DbContextPool, split queries), custom JWT auth (hand-rolled `AuthenticationHandler`, per-user secret mixed into the signing key), BCrypt, Redis (StackExchange), NLog, AutoMapper 16 (commercial license key), DiDecoration attribute DI, Swashbuckle + Asp.Versioning, SignalR + SSE dual-run, in-process `BackgroundService`s, MailKit, VNPay/QRCoder.

**Key conventions:**
- `AppController` base: `api/v{version}/[controller]`, `ApiResult<T>` envelope (implements `IActionResult`); `[ResponseWrapped]` governs *error* wrapping (middleware + MVC filter), successes are wrapped explicitly by services.
- `BaseRepository.Query<T>()` → `AsNoTracking` + soft-delete filter by default; repositories are thin `Query()` wrappers.
- `ExecuteTransactionAsync` + `TransactionContext.NoCommit()` centralizes commit/rollback; `ExecuteQueryAsync` wraps even reads in a transaction.
- Entity mappings live in `Database/Entities/Partials/*.ConfigureModel(ModelBuilder)`; `AppDbContext.partial.cs` holds `HasQueryFilter`s only.
- Primary constructors everywhere, nullable enabled, Vietnamese user-facing messages, `ulong` internal id + public UUIDv7.
- Tests: service-level xUnit + Moq + EF InMemory, hand-written fakes; no WebApplicationFactory.

**Strengths:** consistent low-ceremony conventions; thin controllers; excellent transaction ergonomics; real production concerns handled (idempotency, locks, money safety, per-user token invalidation); good Swagger/versioning/localization surface; service-level testability.

**Weaknesses:** no compile-time layering (discipline-only); in-memory JWT blacklist (revocation breaks on multi-instance/restart); business errors returned as HTTP 200 `isSuccess=false`; reads open transactions unnecessarily; inconsistent soft-delete (query filters for some entities, repo filtering for others, raw `DbSet` bypasses); doc drift (CLAUDE.md vs code); dual migration mechanism (EF migrations + hand-maintained `database-migration.sql`); error codes are enum hash codes; AutoMapper now commercial.

### GHM.RecommendationManagement.* (HrmApi)

**Shape:** .NET 8, classic 3-project onion (`Domain` = contracts/POCOs/DTOs, `Infrastructure` = services + Dapper repositories + validation, `Api` = host/controllers) + a real-DB `IntegrationTests` project. Service-oriented, not DDD — anemic entities, no aggregates/domain events, no MediatR/CQRS.

**Stack:** Dapper + `Microsoft.Data.SqlClient` (SQL Server, stored-procedure-centric — no EF Core anywhere), hand-rolled per-request unit-of-work (`IDbSession` owning one connection + optional transaction), Autofac convention modules **plus** DiDecoration attributes (two DI systems side by side), Serilog (console + MSSQL sink), FluentValidation with auto-validation, Kafka producer for notifications, custom OAuth token-introspection auth (ForgeRock), RESX localization (vi-VN default), Asp.Versioning + Swashbuckle. No AutoMapper — all mapping hand-written.

**Key conventions:**
- `GhmControllerBase` + `ActionResultResponse<T>` envelope with numeric result codes (`1` success, `-99` not found, `-7` concurrency race, …); controllers map `Code <= 0` → 400.
- Repositories call SPs via Dapper, pass child collections as JSON blobs, whitelist ORDER BY fields; tenant id threaded into every predicate; soft delete as `IsDeleted = 0` in every SQL read.
- Transaction discipline: read-only pre-checks → begin tx → writes + audit → commit → side-effects (notifications, HR mirror) strictly after commit, all best-effort (never fail the primary op).
- Optimistic concurrency via `Version` column + SP race codes; in-process keyed `ConcurrentLock` around approval flows.
- IntegrationTests: real SQL Server, outer transaction per test + `RollbackDbSession` mapping session semantics onto savepoints, `SkippableFact` when no DB, hand-written fakes — exercises real SPs with zero persistence.

**Strengths:** compile-time layering; excellent transactional discipline (post-commit side-effects); best-effort isolation of non-core concerns; highest-fidelity test strategy of the two; strong SQL injection/tenancy hygiene; superb inline documentation of business rules.

**Weaknesses:** two DI systems (Autofac + DiDecoration) split the wiring story; fine-grained permissions documented but not enforced in code; in-process lock isn't distributed; business logic split between C# and out-of-repo T-SQL SPs (module not self-contained, magic result codes duplicated); brittle cross-DB coupling to HR schema; lots of hand-mapping/UoW boilerplate; `SearchResult<T>.Data` is `dynamic` (weak typing at API boundary); redundant detail re-queries in notification helpers.

### Comparison

| Dimension | quick-ordering | GHM.Recommendation.* | Better fit for FairShareMonApi |
|---|---|---|---|
| Layering | 1 web project, folder convention | 3 projects, compile-time onion | **quick-ordering** — solo, small domain (~10 tables); multi-project ceremony buys little here |
| Data access | EF Core 9 + Pomelo MySQL, code-first | Dapper + SPs + SQL Server, schema external | **quick-ordering** — spec mandates EF Core + Pomelo/MySQL; SP-centric layer would make the repo not self-contained |
| Transactions | `ExecuteTransactionAsync` + `NoCommit()` | manual `IDbSession`, side-effects after commit | **quick-ordering pattern**, + adopt GHM's "side-effects after commit" discipline for future notifications |
| DI | DiDecoration only | Autofac + DiDecoration (dual) | **quick-ordering** — one mechanism; GHM's dual DI is its clearest smell |
| Response envelope | `ApiResult<T>` (but business errors → HTTP 200; enum-hash error codes) | `ActionResultResponse<T>` numeric codes → proper 400 | **hybrid** — keep `ApiResult<T>` shape, but return real HTTP statuses (404/400 per spec) and stable explicit error codes |
| Auth | custom JWT + in-memory blacklist (revocation breaks multi-instance) | OAuth introspection (external IdP) | **neither verbatim** — spec's opaque whitelist token (SHA-256 in DB + Memcached) already fixes quick-ordering's revocation gap without an IdP |
| Soft delete | inconsistent (some query filters, some repo-level, raw DbSet bypasses) | uniform `IsDeleted = 0` in every query | **GHM's uniformity, EF-style**: `HasQueryFilter` on *every* soft-deletable entity in `AppDbContext.partial.cs`, no exceptions |
| Validation | DataAnnotations + custom filter | FluentValidation auto-validation | either; DataAnnotations suffices for this domain's simple payloads (fewer packages) |
| Mapping | AutoMapper 16 (commercial license) | hand-written | AutoMapper pinned to **13.0.1** (last MIT version) or hand mapping — avoid the license cost |
| Testing | xUnit + Moq + EF InMemory (service-level) | xUnit real-DB, savepoint rollback, skippable | **GHM's approach adapted** — real MariaDB + per-test transaction rollback; InMemory can't verify CHECK constraints, `GROUP_CONCAT`, cascade rules the spec depends on |
| Logging | NLog | Serilog | NLog (spec/CLAUDE.md already chose it) |
| Migrations | EF migrations (as of `aeaed04` — previously dual with hand-maintained SQL) | external SQL only | **EF Core migrations** with an offline design-time factory (pinned MariaDB `ServerVersion`); repo rule updated 2026-07-10 to drop the manual `database-migration.sql` requirement, following quick-ordering's switch |

### Recommendation

**Base the skeleton on quick-ordering's architecture (it's what CLAUDE.md conventions were adapted from), downgraded to .NET 8, and graft in four GHM lessons.**

Solution layout — 2 projects:
- `FairShareMonApi/` (existing web project): `Controllers/` (Auth, Members, Categories, Tags, Vouchers, Batches, Stats + `AppController` base), `Services/Api/<Area>/`, `Repositories/` + `Repositories/Abstractions/BaseRepository`, `Database/` (`AppDbContext` + `AppDbContext.partial.cs` + `Entities/` with per-entity `ConfigureModel`), `Auth/` (opaque-token `AuthenticationHandler`), `Middlewares/`, `Models/` (`ApiResult<T>`, requests/DTOs), `Extensions/` (`DatabaseExtensions`), `Utils/` (`Uuid.NewV7()`), `Attributes/`, `Enums/`, `Constants/`.
- `FairShareMonApi.Tests/`: xUnit; service-level tests against **real MariaDB with per-test transaction rollback** (GHM style, adapted to EF Core: open connection + transaction, pass to `DbContextOptions`, roll back on dispose), `SkippableFact` when DB unreachable; EF InMemory only for pure-logic units.

Packages (all 8.x-compatible): `Microsoft.EntityFrameworkCore` 8.x + `Pomelo.EntityFrameworkCore.MySql` 8.x (+ Design/Tools), `DiDecoration` 1.1.0 (+ Analyzers) — *only* DI mechanism, `BCrypt.Net-Core`, `EnyimMemcachedCore` (Memcached client), `NLog.Extensions.Logging`, `AutoMapper` **13.0.1** (last MIT), `Swashbuckle.AspNetCore` + `Asp.Versioning.Mvc`, xUnit + `Xunit.SkippableFact` for tests.

GHM lessons grafted onto the quick-ordering base:
1. **Uniform soft delete** — `HasQueryFilter` on every `is_active`/`IsDeleted` entity in `AppDbContext.partial.cs`; `BaseRepository.Query` remains a second guard, never the only one. No raw `DbSet` reads in services.
2. **Real HTTP semantics + stable error codes** — `ApiResult` failure maps to the spec's statuses (404 for ownership miss, 400 for business violations, 401); error codes are explicit constants, not `GetHashCode()`.
3. **Transaction discipline** — audit-log rows written inside the same `ExecuteTransactionAsync` delegate as the mutation (spec 5.5); any future side-effects (export files, reminders) run after commit, best-effort.
4. **Real-DB integration tests** with rollback isolation, since the spec leans on DB CHECK constraints, unique indexes, and MySQL `GROUP_CONCAT` aggregation SQL.

quick-ordering weaknesses deliberately *not* inherited: in-memory token blacklist (replaced by the spec's whitelist in Memcached+DB — revocation is just deletion), business-errors-as-200, inconsistent soft delete, dual DI ambitions, SignalR/SSE/localization subsystems (out of scope for this domain).

Migrations: **EF Core migrations only**, plus an `AppDbContextDesignTimeFactory` pinning the MariaDB `ServerVersion` so `migrations add`/`script` run offline (pattern adopted from quick-ordering commit `aeaed04`). No manual `database-migration.sql`.

Deviation from quick-ordering to flag: **reads should not open a DB transaction** — implement `ExecuteQueryAsync` without `BeginTransactionAsync` (pure query path), unlike quick-ordering's version.

## Decision Log

### Decision
Single web project + test project (quick-ordering shape), not 3-project onion (GHM shape).

### Reason
Solo-maintained, ~10-table domain; compile-time layering ceremony costs more than it protects here, and CLAUDE.md conventions are already written against the quick-ordering shape.

### Alternatives Considered
- GHM-style Domain/Infrastructure/Api split — better boundaries but 3× csproj overhead and the reference implementation of every convention (BaseRepository, ApiResult, DiDecoration wiring) is single-project.
- Dapper + stored procedures — rejected: spec mandates EF Core/Pomelo; SPs would put business logic outside the repo.

## Impact Analysis

- Documentation only at this stage; the recommendation will shape the initial solution layout, packages, and conventions of FairShareMonApi.

## Progress Log

### 2026-07-10

- Started planning; recovered domain spec from git history (`git show 6b19f01:The-ideal.md`); launched parallel code analyses of both reference codebases.
- Recorded quick-ordering analysis (single-project layered, EF Core 9/Pomelo, custom JWT, Redis, DiDecoration).
- Recorded GHM.Recommendation analysis (3-project onion, Dapper+SPs/SQL Server, Autofac+DiDecoration, Kafka, real-DB savepoint tests).
- Wrote comparison matrix, recommendation, and decision log.
- Reviewed quick-ordering's newly pulled commit `aeaed04` (shift carry-over audit + switch to EF migrations); per user decision, updated CLAUDE.md, AGENTS.md, and `.agents/rules/rules.md` to replace the manual `database-migration.sql` rule with EF Core migrations (+ offline design-time factory), and aligned this doc's recommendation.
- User decided remaining stack questions: AutoMapper 13.0.1, FluentValidation, real MariaDB tests. Wrote the initialization plan ([project-initialization.md](project-initialization.md)); aligned CLAUDE.md and testing rules.

## Final Outcome

Analysis and recommendation complete (documentation only — no code initialized). Recommendation: quick-ordering's single-project layered architecture downgraded to .NET 8 (EF Core 8 + Pomelo MySQL, DiDecoration-only DI, NLog, BCrypt, EnyimMemcachedCore, Swashbuckle + Asp.Versioning), replacing its JWT/in-memory-blacklist auth with the spec's opaque whitelist token (SHA-256 in `auth_tokens` + Memcached), and grafting four GHM lessons: uniform `HasQueryFilter` soft delete, real HTTP statuses + stable error codes, audit-in-transaction / side-effects-after-commit discipline, and real-DB rollback-isolated integration tests. Open questions above (AutoMapper licensing, validation library, test DB, restoring `The-ideal.md`) need user decisions before initialization begins.
