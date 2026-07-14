# FairShareMonApi Coding Style & Process Rules

## Scope
- Applies to `FairShareMonApi.sln` (project `FairShareMonApi`), a **.NET 8** Web API.
- Conventions adapted from the sibling **quick-ordering** project. Domain spec: `The-ideal.md`. High-level guidance: `CLAUDE.md`, `AGENTS.md`.

## Language and Formatting
- Prefer **primary constructors** for any class with a single constructor (controllers, services, repositories, and others). Reference parameters directly, or use a computed `private readonly` field initializer when the value needs transforming. Skip types with multiple constructors, real ctor logic/validation, or a parameterless default-init ctor (entities, value types, `AppDbContext`).
- Use `var` when the type is obvious; explicit types where it reads better.
- Use guard clauses and early returns for input/state validation.
- Suffix async methods with `Async`; always thread `CancellationToken` through async application methods; always await `Task`/`ValueTask`.
- Naming: PascalCase for types/methods/properties/constants; camelCase for parameters/locals; interfaces start with `I`.
- Keep comments concise and only where logic is non-obvious.
- User-facing messages and Swagger summaries/descriptions are written in **Vietnamese**.

## Layering and API Shape
- Flow: `Controller -> Service -> Repository -> AppDbContext`.
- Controllers stay thin and delegate business logic to services. Keep business logic out of controllers and repositories.
- Controllers derive from `AppController`, return `ApiResult<T>`, and follow the `[ResponseWrapped]` pattern. Routes: `api/v{version:apiVersion}/[controller]`.
- Annotate endpoints with Swagger attributes (`[SwaggerOperation]`, `[SwaggerResponse]`, `[ResponseBody]`, `[RequestBody]`).
- Define a service's **interface and implementation in the same file** (interface above the class), as in quick-ordering's `AccountService.cs`.

## Dependency Injection
- Prefer attribute-driven registration via DiDecoration: `[ScopedService]`, `[SingletonService]`, `[TransientService]`.
- For multiple implementations of one interface, set `Multiple = true` (property initializer); otherwise `TryAdd` drops later registrations.
- Hosted background workers inherit from `Microsoft.Extensions.Hosting.BackgroundService` (override `ExecuteAsync`) and are registered with `[BackgroundService]`, picked up by the same `RegisterDecorators(...)` scan — no manual `AddHostedService`. Bare `[BackgroundService]` yields a dedicated instance; to share one singleton instance use `[SingletonService(typeof(self))]` + `[BackgroundService(typeof(self))]`. Never pair `[BackgroundService]` with a non-singleton lifetime attribute — DiDecoration throws.
- Avoid manual DI registration for app services unless there's a clear exception.

## Data Access and Transactions
- Use repository helpers: `ExecuteQueryAsync(...)` for reads, `ExecuteTransactionAsync(...)` for writes.
- Read via `repo.Query()` (AsNoTracking + soft-delete filter); mutate via `repo.Query(true)` (tracking enabled).
- Use `TransactionContext.NoCommit()` for validation/business failures inside a write.
- Inside transaction delegates, **do not** add a trailing `SaveChanges`/`SaveChangesAsync` that only duplicates the extension's commit. Keep an explicit save only when an intermediate flush is genuinely required (e.g. a generated Id needed immediately).
- Perform post-commit side-effects (cache invalidation, notifications) **after** the transaction delegate returns, not inside it.
- Default to soft-delete (`members.is_active`, and/or `IsDeleted` / `IEntityDeletable`); exclude deleted rows by default. Expose `?include_inactive=true` where stats/export need them.

## AppDbContext File Ownership Rule
- All EF model declarations/mappings live in `Database/AppDbContext.cs` — either inline `modelBuilder.Entity<T>(...)` blocks or per-entity static `ConfigureModel(ModelBuilder)` methods (in `Database/Entities/Partials/`) invoked from `OnModelCreating`.
- `Database/AppDbContext.partial.cs` is reserved for query filters only (`HasQueryFilter(...)`) — no entity model declaration blocks.

## Entity Conventions
- Each entity is a `partial class`: a clean POCO in `Database/Entities/<Name>.cs` (properties only) and a `Database/Entities/Partials/<Name>.cs` holding the constructor and the static `ConfigureModel(ModelBuilder)`.
- PK is `ulong Id` (bigint unsigned). Add a separate `string Uuid` column (non-nullable — the constructor always sets it; max length 64, unique index) for external references.
- The constructor sets `Uuid = Uuid.NewV7()` and `CreatedAt = AppDateTime.Now`. **Never use `Guid.CreateVersion7()`** (it is .NET 9-only) — use the `Uuid.NewV7()` helper.
- Timestamps `CreatedAt` / `UpdatedAt` on every table; `UpdatedAt` uses `ValueGeneratedOnAddOrUpdate()` with `current_timestamp()` default.

## Protected File Rule
- Once written, treat `Controllers/AppController.cs` as **locked** — do not modify it without explicit, file-specific permission in the current request.

## Domain Safety Rules
- **Resource Owned:** scope every owned-resource query by `WHERE ... AND user_id = :current_user_id`; return **404** (not 403) on miss. Validate cross-user FK links (an expense's `payer_member_id` and a share's `member_id` must match the expense's `user_id`).
- **Money:** store as `DECIMAL`/`decimal` or integer smallest-unit; never float/double. Enforce `amount >= 0` with a DB CHECK constraint.
- **Event lifecycle:** validate state transitions explicitly; a `CLOSED` event rejects all writes to its expenses/shares (sole exception: the settled flag). Closing is one-way and never automatic. Expense `expense_time` must fall within the event's date range.
- Create an expense and its shares in a single transaction.

## Auth
- Opaque stateful token (not JWT). Store `SHA-256(token)` in `auth_tokens` and Redis (TTL = expiry); return the raw token to the client once.
- Validate each request by hashing the incoming token and checking the whitelist (cache first, DB fallback). Issue access + refresh tokens.
- Logout/revoke removes from the whitelist; password change revokes all of a user's tokens.

## Testing Rules
- Use xUnit.
- Prefer focused tests asserting both the returned result (`ApiResult<T>`, error codes, state) and persisted EF Core data changes.
- Integration tests run against a real MariaDB: shared fixture probes the DB once and skips tests (`Xunit.SkippableFact`) when unreachable; each test runs inside a transaction rolled back on dispose so nothing persists. Use lightweight fakes/stubs for collaborators.
- EF InMemory only for pure-logic units with no DB-level behavior (e.g. helpers).

## Database Change Rule
- DB schema changes use EF Core migrations: `dotnet ef migrations add <Name>` then `dotnet ef database update` (both `--project .\FairShareMonApi\FairShareMonApi.csproj`).
- `migrations add` / `migrations script` run offline via `AppDbContextDesignTimeFactory` (pins the MariaDB `ServerVersion`); only `database update` / `migrations list` need a reachable MySQL/MariaDB (`ConnectionStrings:Default`).
- Bump the pinned version in that factory if the target server major/minor changes.
- Review the generated migration before applying; keep the model snapshot in sync.
- Do not create or append to a manual `database-migration.sql`. Data-only fixes with no schema change may be applied as ad-hoc SQL.

---

# Clarification-First Rule

The agent MUST NOT make assumptions.

Whenever information is missing, ambiguous, subjective, preference-dependent, or multiple valid solutions exist, the agent MUST stop and ask the user before proceeding. The agent MUST: identify the uncertainty, explain the options and trade-offs, ask the user to choose, and wait for confirmation. The agent MUST NOT guess requirements, invent context, infer preferences, choose defaults without approval, or continue when a user decision is required.

**Rule of thumb:** if a reasonable human engineer would ask the user before proceeding, the agent must also ask.

---

# Change Planning and Work Log Policy

Every feature, enhancement, bug fix, refactor, migration, or significant change MUST be documented in a planning log **before implementation begins**. The planning log is the source of truth for requirements, decisions, scope, progress, and outcomes.

## Planning Directory & Naming
- Store all planning docs under `/planning` (create it if missing).
- One Markdown file per work item: `/planning/[main-purpose].md` — lowercase, kebab-case, descriptive (e.g. `user-authentication.md`, `expense-export-feature.md`).

## Required Template
Each planning file MUST contain: **Title**, **Objective**, **Background**, **Requirements**, **Open Questions**, **Assumptions**, **Implementation Plan**, **Impact Analysis** (APIs / Database / Infrastructure / Services / Documentation), **Progress Log** (timestamped entries), **Final Outcome**, and optional **Future Improvements**. Record significant decisions with reason + alternatives considered.

## Ongoing Updates & Completion
Update the planning file whenever requirements/scope/decisions change, steps complete, or problems arise. A task is **not complete** until its Progress Log and Final Outcome are updated and the doc reflects the final implementation.