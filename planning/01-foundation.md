# Phase 1 — Foundation & Infrastructure

## Objective
Stand up the .NET 8 project skeleton and all cross-cutting primitives every later phase depends on: build config, base abstractions (`AppController`, `ApiResult`, `ErrorException`), the EF Core data layer (`AppDbContext`, `BaseRepository`, transaction helpers), the Memcached cache abstraction, DI/AutoMapper/versioning/Swagger wiring, and the `database-migration.sql` baseline.

## Background
The repo currently holds only the default `Program.cs` template (`net8.0`). Conventions are in `CLAUDE.md` / `.agents/rules/rules.md`, mirrored from quick-ordering (.NET 9) with .NET 8 substitutions.

## Requirements
- No domain logic yet — only reusable infrastructure.
- Everything compiles, runs, and serves Swagger; a trivial wrapped endpoint proves the response/error pipeline.
- Package versions pinned to .NET 8-compatible releases.

## Dependencies
None (first phase).

---

## Stage 1.1 — Project & build setup
1. Pin SDK: create `global.json` with `sdk.version` `8.0.x` (`rollForward: latestFeature`).
2. Edit `FairShareMonApi/FairShareMonApi.csproj`: confirm `<TargetFramework>net8.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.
3. Add NuGet packages (.NET 8-compatible):
   - `Microsoft.EntityFrameworkCore` 8.0.x, `Microsoft.EntityFrameworkCore.Design` 8.0.x, `Microsoft.EntityFrameworkCore.Relational` 8.0.x
   - `Pomelo.EntityFrameworkCore.MySql` 8.0.x
   - `AutoMapper` 13.x (+ DI extension)
   - `DiDecoration` + `DiDecoration.Analyzers`
   - `Asp.Versioning.Mvc` 8.1.x + `Asp.Versioning.Mvc.ApiExplorer` 8.1.x
   - `Swashbuckle.AspNetCore` 6.6.x (+ `.Annotations`)
   - `BCrypt.Net-Next` 4.x
   - `EnyimMemcachedCore` (Memcached client) + `Microsoft.Extensions.Caching.Abstractions`
   - `NLog.Extensions.Logging` 5.x
4. Create the folder skeleton under `FairShareMonApi/`: `Controllers/`, `Services/Api/`, `Repositories/`, `Repositories/Abstractions/`, `Database/`, `Database/Entities/`, `Database/Entities/Partials/`, `Database/Abstractions/`, `Models/`, `Models/Dtos/`, `Models/Requests/`, `Enums/`, `Exception/`, `Attributes/`, `Attributes/MvcFilters/`, `Extensions/`, `Auth/`, `Abstractions/Caching/`, `Utils/`, `MapProfiles/`.
5. `dotnet restore` + `dotnet build` to confirm a clean baseline.

**Acceptance:** solution builds with all packages restored.

---

## Stage 1.2 — Core primitives
1. `Utils/Uuid.cs` — `Uuid.NewV7()` returning a UUIDv7 `string` (manual implementation: 48-bit Unix-ms timestamp + version/variant bits + random; `.NET 8`-compatible). Unit-testable for monotonic ordering.
2. `Utils/AppDateTime.cs` — `AppDateTime.Now` returning a single, consistent timezone value (UTC or fixed VN offset — decide & document).
3. `Enums/ErrorCode.cs` — `SUCCESS`, `BAD_REQUEST`, `UN_AUTHORIZED`, `FORBIDDEN`, `NOT_FOUND`, `CONFLICT`, `INTERNAL_ERROR` (+ HTTP-status mapping).
4. `Exception/ErrorException.cs` — carries `ErrorCode` + `StatusCode` + message; thrown by services/middleware.
5. `Database/Abstractions/IEntity.cs`, `IEntityDeletable.cs` (exposes `IsDeleted`).

**Acceptance:** `Uuid.NewV7()` produces valid, time-ordered v7 strings (covered by a unit test).

---

## Stage 1.3 — API response shape & error handling
1. `Models/AppError.cs` — wraps an `ErrorException` + optional message.
2. `Models/ApiResult.cs` — `ApiResult<T>` (implements `IActionResult`, sets status from error) with `Success` / `Failure`; static `ApiResult` helpers `Success<T>`, `Failure<T>`, `SuccessMessage`.
3. `Attributes/ResponseWrappedAttribute.cs` — result filter that wraps raw returns into `ApiResult<T>`.
4. `Attributes/MvcFilters/ErrorHandlerFilter.cs` + `Middlewares/ErrorHandlerMiddleware.cs` — convert thrown `ErrorException` (and unhandled exceptions) into `ApiResult.Failure`.
5. `Controllers/AppController.cs` — `[ApiController]`, route `api/v{version:apiVersion}/[controller]`, `[ResponseWrapped]`, `AuthenticatedUser` accessor, `MustAuthenticated()`. **Mark as locked once written.**

**Acceptance:** a thrown `ErrorException(NOT_FOUND)` returns a 404 `ApiResult` with `IsSuccess=false`.

---

## Stage 1.4 — Database layer
1. `Database/AppDbContext.cs` — pooled context; `OnModelCreating` invokes each entity's static `ConfigureModel(modelBuilder)` (registered as entities are added in later phases). Split-query behavior.
2. `Database/AppDbContext.partial.cs` — reserved for `HasQueryFilter` only.
3. `Database/Abstractions/TransactionContext.cs` — with `NoCommit()`.
4. `Extensions/DbContextExtensions.cs` — `ExecuteQuery(Async)` and `ExecuteTransaction(Async)` (open transaction, run delegate, commit unless `NoCommit()`, rollback on exception).
5. `Repositories/Abstractions/IBaseRepository.cs` + `BaseRepository.cs` — `Query<T>(tracking, includeDeleted)` applying `AsNoTracking` + soft-delete filter; pass-through `ExecuteQueryAsync` / `ExecuteTransactionAsync`.

**Acceptance:** a smoke repository can run a query and a transaction against a test DB.

---

## Stage 1.5 — Caching (Memcached) abstraction
1. `Abstractions/Caching/ICache.cs` — `GetAsync`, `SetAsync(key, value, ttl)`, `RemoveAsync`.
2. `Services/Caching/MemcachedCache.cs` — `[SingletonService(typeof(ICache))]` wrapping the Enyim client.
3. Config binding for Memcached endpoints in `appsettings.json`.

**Acceptance:** set/get/remove round-trips against a local Memcached (or a fake in tests).

---

## Stage 1.6 — Composition root (`Program.cs`)
1. `RegisterDecorators(...)` (DiDecoration attribute scan).
2. `AddDbContextPool<AppDbContext>(UseMySql(connectionString, ServerVersion.AutoDetect, o => o.UseQuerySplittingBehavior(SplitQuery)))`.
3. AutoMapper registration + `IMapConfigurationProvider` for `ProjectTo`.
4. API versioning (default v1) + Swagger gen (with annotations, auth header).
5. NLog wiring.
6. Register error-handling middleware + Memcached client.
7. Map controllers.

**Acceptance:** `dotnet run` serves Swagger UI at `/swagger`.

---

## Stage 1.7 — DB migration baseline
1. Create `FairShareMonApi/database-migration.sql` with a header comment explaining the append-only rule and an empty/commented "— APPLIED —" block.
2. Add `CREATE DATABASE`/charset notes (`utf8mb4`) as guidance.

**Acceptance:** file exists and documents the workflow; no tables yet.

---

## Stage 1.8 — Verification
1. Add `Controllers/HealthController.cs` → `GET /api/v1/health` returning `ApiResult.SuccessMessage("ok")`.
2. `dotnet build` + `dotnet run`; hit the endpoint; confirm wrapped success and a forced-error path returns wrapped failure.
3. Add a minimal xUnit test project `FairShareMonApi.Tests` (if not present) and one test for `ApiResult`/`Uuid`.

**Acceptance:** build + test green; health endpoint wrapped correctly.

---

## Impact Analysis
- **APIs:** health endpoint only.
- **Database:** `database-migration.sql` created (no tables).
- **Infrastructure:** packages, DI, cache, logging, Swagger.
- **Services:** base abstractions only.
- **Documentation:** this file; resolve the error-format open question.

## Open questions / Assumptions
- Confirm `AppDateTime` timezone (assume **UTC**, format in API layer).
- Confirm Memcached client choice (assume `EnyimMemcachedCore`).
- Assume DiDecoration is .NET 8-compatible; if not, fall back to a small reflection-based attribute scanner.

## Progress log
- (pending)

## Final outcome
- (to be completed)
