# Implementation Overview — FairShareMonApi

## Objective
Build the "Sổ ghi nợ chi tiêu" expense-ledger & debt-splitting Web API described in `The-ideal.md`, following the conventions in `CLAUDE.md` / `AGENTS.md` / `.agents/rules/rules.md` (adapted from the quick-ordering project, targeting **.NET 8**).

## How these plans are organized
Each major phase has its own file under `/planning`. The hierarchy inside every phase file is:

```
Major phase  (the file)
  └─ Minor phase   (optional — only when an earlier phase is revisited/changed)
       └─ Stage     (a cohesive unit of work)
            └─ Step  (concrete, ordered actions)
```

> **Minor phases** appear when a later phase forces a change back into an earlier one (e.g. `register` is first built user-only, then revisited to seed the owner member and default categories). They are labelled `Minor phase N.x` and cross-reference the file they modify.

## Phase map & build order

| # | Major phase | File | Depends on |
|---|---|---|---|
| 1 | Foundation & Infrastructure | `01-foundation.md` | — |
| 2 | Authentication & Users | `02-auth-users.md` | 1 |
| 3 | Members | `03-members.md` | 1, 2 |
| 4 | Categories & Tags | `04-categories-tags.md` | 1, 2 |
| 5 | Vouchers, Records & Audit Logging | `05-vouchers-records-audit.md` | 1, 2, 3, 4 |
| 6 | Expense Batches & Lifecycle | `06-batches.md` | 1, 2, 5 |
| 7 | Statistics & Export | `07-stats-export.md` | 1–6 |

Cross-phase changes (minor phases):
- **2.x** Revisit `AuthService.Register` after phase 3 (seed `is_owner` member) and phase 4 (seed default categories).
- **6.x** Inject the batch-lifecycle (`CLOSED`) write-guard into the phase-5 voucher/record write paths.

## Conventions applied to every phase
- **Layering:** `Controller → Service → Repository → AppDbContext`; thin controllers off `AppController`, returning `ApiResult<T>`.
- **DI:** DiDecoration attributes (`[ScopedService(typeof(IFoo))]`); interface + impl in the same file; primary constructors.
- **Data access:** `repo.Query()` / `Query(true)`, `ExecuteQueryAsync` / `ExecuteTransactionAsync` + `TransactionContext.NoCommit()`; no redundant `SaveChanges`; post-commit side-effects after the delegate.
- **Entities:** `partial` POCO in `Database/Entities/` + `Database/Entities/Partials/` ctor (`Uuid = Uuid.NewV7()`, `CreatedAt = AppDateTime.Now`) and static `ConfigureModel(ModelBuilder)`.
- **IDs:** `ulong Id` (bigint unsigned) PK + separate `Uuid` string column. **Never `Guid.CreateVersion7()`** — use the `Uuid.NewV7()` helper.
- **Resource Owned:** scope every owned-resource query by `user_id`; return **404** (not 403) on miss; validate cross-user FK links.
- **Money:** `DECIMAL`/`decimal` or integer smallest-unit; never float. `amount >= 0` enforced by DB CHECK.
- **Auth:** opaque stateful token (SHA-256 hashed, Memcached whitelist + DB fallback).
- **DB Change Rule:** every schema change is **appended** to `FairShareMonApi/database-migration.sql` (end, outside the commented "applied" block, dated comment). Maintainer applies manually.
- **Process:** Clarification-First (ask, don't assume); keep each phase file's Progress Log + Final Outcome synced; a phase is not done until its doc is updated and acceptance criteria pass.

## Global acceptance (end state)
- `dotnet build .\FairShareMonApi.sln` and `dotnet test` pass.
- Swagger documents every endpoint group in `The-ideal.md` §4.
- All responses wrapped in `ApiResult<T>`; errors flow through the standard error handler.
- A user can: register → login → manage members/categories/tags → create batches/vouchers/records → read balance, category stats, and audit history → export voucher/batch.

## Open questions (global)
- Standard error-response format / `ErrorCode` catalogue — `The-ideal.md` §7 defers this to "the existing project". Resolve in phase 1 (proposed: mirror quick-ordering `ErrorException`/`ApiResult`/`AppError`).
- DB workflow: EF code-first migrations **and** the manual `database-migration.sql` both exist in quick-ordering. These plans treat `database-migration.sql` as the source of truth (per the DB Change Rule) and map entities to the resulting schema.

## Progress log
### (to be filled as phases complete)
- [ ] Phase 1 — Foundation
- [ ] Phase 2 — Auth & Users
- [ ] Phase 3 — Members
- [ ] Phase 4 — Categories & Tags
- [ ] Phase 5 — Vouchers, Records & Audit
- [ ] Phase 6 — Batches
- [ ] Phase 7 — Stats & Export