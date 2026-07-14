# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FairShareMonApi — a personal/group **expense ledger & debt-splitting** Web API ("Sổ ghi nợ chi tiêu"). Each user owns their own data (members, expense events, expenses, shares); the system tracks who paid and who owes. Domain terms (decided 2026-07-10): **expense** (phiếu chi tiêu), **share** (phần gánh), **event** (đợt), **wallet/bank account** (ví), **settled** (đã trả), tiers **Premium/Free** — never "voucher"/"record"/"batch". Single solution `FairShareMonApi.sln` with the Web API project `FairShareMonApi/`.

The feature specification lives in **`The-ideal.md`** (features, use cases, business rules — no implementation detail) — treat it as the source of truth for *what* to build. This file describes *how* to build it (conventions). The old technical version of the spec (schema/endpoints/SQL) is at commit `6b19f01`.

**Stack (.NET 8):** `net8.0` (SDK pinned in `global.json`), EF Core 8 + Pomelo MySQL (MySQL/MariaDB), AutoMapper **13.0.1 pinned** (last MIT license — never upgrade to 14+), FluentValidation for request validation, NLog, BCrypt for password hashing, **opaque stateful tokens** (whitelist in Redis, not JWT), Redis via StackExchange.Redis, DiDecoration for attribute-driven DI, Swagger. Nullable + ImplicitUsings enabled. Integration tests run against a **real MariaDB** with per-test transaction rollback (skip when unreachable), not EF InMemory.

> These conventions were adapted from the sibling project **quick-ordering** (a .NET 9 codebase). Where quick-ordering uses .NET 9-only or domain-specific features, this repo substitutes a .NET 8-compatible equivalent — see **".NET 8 adaptations"** below.

## Commands

```powershell
dotnet build .\FairShareMonApi.sln                                       # build
dotnet run --project .\FairShareMonApi\FairShareMonApi.csproj            # run API locally (Swagger UI at /swagger)
dotnet test .\FairShareMonApi.sln                                        # run all tests
```

EF Core migrations (from repo root):

```powershell
dotnet ef migrations add <Name> --project .\FairShareMonApi\FairShareMonApi.csproj
dotnet ef database update --project .\FairShareMonApi\FairShareMonApi.csproj
```

Requires running MySQL/MariaDB (`ConnectionStrings:Default`, local MariaDB 11.7.2) and Redis (Docker, `localhost:6379`) — configured in `FairShareMonApi/appsettings.json`.

## Architecture

**Request flow:** `Controllers/* → Services/Api/* → Repositories/* → AppDbContext`. Controllers stay thin and delegate to services; business logic never lives in controllers or repositories.

Core building blocks to establish/orient (see `The-ideal.md` for the domain):
- `Program.cs` — bootstrap, middleware pipeline, auth/DI/DbContext/Redis wiring.
- `Controllers/AppController.cs` — base controller. All controllers derive from it; routes are `api/v{version:apiVersion}/[controller]` and responses are auto-wrapped via `[ResponseWrapped]` into `ApiResult<T>`. **Treat as locked** once written — don't edit without explicit per-request permission.
- `Repositories/Abstractions/BaseRepository.cs` — `ExecuteQueryAsync` (reads), `ExecuteTransactionAsync` (writes), and `Query<T>(...)` which applies `AsNoTracking` and soft-delete filtering by default.
- `Models/ApiResult.cs` — `ApiResult<T>.Success(...)` / `ApiResult.Failure(...)` / `ApiResult.SuccessMessage(...)`.

**Controllers** map to the feature areas in `The-ideal.md`: Auth, Members, Categories, Tags, Expenses (+ shares sub-routes), Events, Stats, Wallet (bank accounts + bank-transfer QR generation). User tiers (Premium/Free usage limits) cut across features. Endpoint design is decided per-feature in its planning doc. Service implementations live under `Services/Api/<Area>/`.

**Dependency injection** is attribute-driven via the DiDecoration package. Annotate services with `[ScopedService]`, `[SingletonService]`, or `[TransientService]` instead of registering manually. For multiple implementations of one interface, set `Multiple = true` (property initializer), otherwise `TryAdd` silently drops later ones. Hosted background workers inherit from `Microsoft.Extensions.Hosting.BackgroundService` (override `ExecuteAsync`) and are registered with `[BackgroundService]` — the single `RegisterDecorators(...)` scan in `Program.cs` picks them up; never call `AddHostedService` manually.

**Database (`Database/`):**
- All EF entity mappings go in `AppDbContext.cs` (or per-entity static `ConfigureModel(ModelBuilder)` methods invoked from it — see Style).
- `AppDbContext.partial.cs` is reserved for query filters (`HasQueryFilter`) only — no entity declaration blocks.
- Soft-delete is standard: `members.is_active` per the spec, and/or an `IsDeleted` / `IEntityDeletable` flag; deleted rows excluded by default by `BaseRepository.Query`.
- Use `AsNoTracking()` for reads; enable tracking only when mutating.

**Auth:** opaque stateful tokens. On login, generate a random token, store its **SHA-256 hash** in `auth_tokens` (the whitelist) and in Redis with TTL = `expires_at`; return the raw token once. Each request hashes the incoming token and checks the whitelist (cache first, DB fallback). Access + refresh token pair. Logout/revoke removes from whitelist; password change revokes all of a user's tokens.

## Critical conventions

- **Resource Owned authorization (mandatory):** every query for an owned resource is scoped `WHERE id = :id AND user_id = :current_user_id`. On miss return **404 Not Found**, never 403 (don't leak existence). Validate cross-user links too: an expense's `payer_member_id` and a share's `member_id` must belong to the same `user_id`.
- **Money safety:** store money as `DECIMAL`/`decimal` or as integer smallest-unit (VND). **Never `float`/`double`.** Enforce `amount >= 0` with a DB CHECK constraint, not just app validation.
- **Event lifecycle:** when an `expense_events.status = CLOSED`, disable all writes to expenses in that event (create/update/delete shares, adding/removing expenses) — read/export/QR only; sole exception is the settled flag (payment metadata). Closing is one-way and never automatic.
- **Transactions:** writes go through `ExecuteTransactionAsync`; use `TransactionContext.NoCommit()` to abort on validation/business failure. Don't add a trailing `SaveChangesAsync` that just duplicates the extension's commit (keep explicit saves only for a genuinely needed intermediate flush). Creating an expense + its shares is one transaction.
- **Async:** suffix methods `Async`, thread `CancellationToken` through, always await `Task`/`ValueTask`.
- **Style:** prefer primary constructors for any class with a single constructor. Skip types with multiple constructors, real ctor logic, or a parameterless default-init ctor (entities, value types, `AppDbContext`). Guard clauses + early returns. Vietnamese for user-facing messages and Swagger summaries.

## .NET 8 adaptations (differences from quick-ordering)

| quick-ordering (.NET 9) | FairShareMonApi (.NET 8) |
|---|---|
| `Guid.CreateVersion7().ToString()` | `Uuid.NewV7()` helper (manual UUIDv7, time-ordered, .NET 8-compatible) — **never** call `Guid.CreateVersion7()` |
| EF Core 9 / Pomelo 9 / `Microsoft.*` 9.x | EF Core 8 / Pomelo 8 / `Microsoft.*` 8.x package versions |
| JWT auth | opaque stateful token (hashed in DB + Redis) per `The-ideal.md` |

## Process rules (mandatory)

- **Clarification-First:** do NOT assume. When information is missing, ambiguous, preference-dependent, or has multiple valid solutions, stop and ask the user before proceeding. If a reasonable engineer would ask first, ask first.
- **Planning doc before code:** every feature/fix/refactor/migration gets a Markdown file under `/planning/[main-purpose].md` (lowercase kebab-case) before implementation begins — objective, requirements, open questions, assumptions, implementation plan, impact analysis, progress log, final outcome. Keep it synchronized with the actual work; a task isn't done until its planning doc is updated.
- **DB migration rule:** schema changes go through **EF Core migrations** (`dotnet ef migrations add <Name>` then `dotnet ef database update`, both `--project .\FairShareMonApi\FairShareMonApi.csproj`). Provide an `AppDbContextDesignTimeFactory` that pins the MariaDB `ServerVersion` so authoring commands (`migrations add`, `migrations script`) run offline; only `database update` / `migrations list` need a reachable DB. Review the generated migration before applying and keep the model snapshot in sync. Do not create or append to a manual `database-migration.sql`; data-only fixes with no schema change may be ad-hoc SQL.

## More detail

`AGENTS.md` (architecture map) and `.agents/rules/rules.md` (full coding-style + process rules) carry the complete conventions. `The-ideal.md` carries the domain design.