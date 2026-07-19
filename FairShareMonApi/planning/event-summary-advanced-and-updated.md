# Event summary: expose total advanced + updated-at

## Objective

Add two read-only fields to the events list API so the web dashboard can render a "recent events"
card without extra round-trips:

1. `TotalAdvanced` — the event-level total "advanced" (đã ứng): the sum of every expense's total
   for the event. Per The-ideal.md §3.7, `Σ đã ứng == Σ tiền các phiếu` (the total of all expenses in
   the event), so this is exactly the sum of the amounts of all shares belonging to the event's
   expenses.
2. `UpdatedAt` — the event row's last-updated timestamp. The `Event` entity already has `UpdatedAt`;
   it is simply not surfaced on the response DTOs.

Both fields are added to `EventSummaryResponse` (returned by `GET /api/v1/events`) and — for
consistency — to `EventResponse` (returned by `GET /api/v1/events/{uuid}`). This is a purely additive,
read-only change: no new endpoints, no writes, no schema change.

## Background

Relevant existing behavior (verified against the code, 2026-07-19):

- **Endpoint / flow.** `GET /api/v1/events` → `EventsController.ListAsync` →
  `IEventsService.ListAsync(userId, filter, ct)` →
  `IEventRepository.ListByUserAsync(...)` → AutoMapper `Event → EventSummaryResponse`.
  Detail: `GET /api/v1/events/{uuid}` → `GetAsync` → `GetByUuidAsync` → `Event → EventResponse`.
  (`FairShareMonApi/Controllers/EventsController.cs`, `Services/Api/Events/EventsService.cs`.)
- **DTOs.** `Models/Events/EventSummaryResponse.cs` currently exposes `Uuid`, `Name`, `StartDate`,
  `EndDate`, `IsClosed`, `ClosedAt`, `ExpenseCount`, `CreatedAt`. `Models/Events/EventResponse.cs`
  adds `Description`. Neither exposes `UpdatedAt` or any advanced/total figure.
- **`ExpenseCount` derivation (the pattern to mirror).** `Mappings/EventProfile.cs` maps
  `ExpenseCount` from `src.Expenses.Count`; the repository `Include`s `evt.Expenses` on both the list
  and detail reads so the collection is materialized when AutoMapper counts it. There is no separate
  count query — it is derived in-memory from the loaded (Included) collection.
- **`UpdatedAt` mapping.** `Database/Entities/Partials/Event.cs` maps `updated_at` with
  `ValueGeneratedOnAddOrUpdate()` + `HasDefaultValueSql("current_timestamp(6) ON UPDATE
  current_timestamp(6)")`. It is DB-maintained: it bumps when the **event row** changes (create,
  info edit, close), **not** when an expense/share inside the event changes.
- **Where the money lives.** An expense has **no stored total column** — `Database/Entities/Expense.cs`
  states "the expense total is definitionally the sum of its shares (§2, OQ1) — there is no stored
  total column." `Share.Amount` is `decimal(18,2)` with the DB CHECK `ck_shares_amount_non_negative`
  (`amount >= 0`). Therefore any "total advanced" figure must be summed from `Share.Amount`.
- **The-ideal.md §3.7** (line 99): `đã ứng` = tổng tiền các phiếu mà thành viên đó là người trả;
  summing over all members gives the total of all expenses in the event. `StatsRepository`
  already computes the per-member `Advanced` exactly this way — summing `Share.Amount` over the
  event's expenses' shares (`Repositories/StatsRepository.cs`, `GetEventBalanceAsync`).

Conclusion pinned for the implementer: **`TotalAdvanced = Σ Share.Amount` over every share of every
expense whose `EventId` is this event.** Equivalent to `Σ (expense total)` and to `Σ (per-member
advanced)`; all three agree by construction (§3.7). Uses `decimal`, never a stored column.

## Requirements

- Add `decimal TotalAdvanced` and `DateTime UpdatedAt` to `EventSummaryResponse`.
- Add the same two fields to `EventResponse` (single-event detail), for contract consistency.
- `TotalAdvanced` is the sum of `Share.Amount` over all shares of the event's expenses; an event with
  no expenses (or expenses with only 0đ shares) yields `0` (never null).
- `UpdatedAt` is the event row's `updated_at` value, surfaced verbatim (UTC, `datetime(6)` precision),
  consistent with how `CreatedAt` is already exposed.
- No N+1: the aggregate must be produced without one query per event.
- No change to the list ordering (see the explicit constraint below).
- No schema migration (both the source column and the derived aggregate already exist).
- Follow existing conventions: AutoMapper entity→DTO mapping, `AsNoTracking` reads via
  `BaseRepository.Query`, resource-owned scoping unchanged, Vietnamese XML doc comments on new DTO
  members and updated Swagger descriptions.

### Explicitly out of scope / do NOT touch

- **List ordering stays exactly as-is:** `ORDER BY start_date DESC, created_at DESC`
  (`ListByUserAsync`). The dashboard's "open → closed, then recently updated" ordering is done
  **client-side**. The implementer must **not** change the backend sort, add an `UpdatedAt` sort, or
  reorder by `IsClosed`.
- No new filter parameters on `EventFilter`.
- No new endpoints; `GET /balance`, `/export`, `/qr` are untouched.

## Open Questions

1. **`UpdatedAt` semantics vs. the dashboard's "recently updated" intent (non-blocking — a default is
   recommended so implementation can proceed).** The `Event.updated_at` column bumps only when the
   **event row itself** is written (info edit, close), *not* when an expense or share inside the event
   is added/edited/deleted. If the dashboard sorts "recently updated" purely on this field, an event
   whose expenses changed today but whose row last changed a month ago will sort as stale.
   - **Option A (recommended, matches the task brief):** expose the existing `Event.updated_at`
     verbatim. Zero cost, no schema change, honest to the entity. The "recently updated" card is a
     best-effort recency hint, not a guarantee it reflects expense edits.
   - **Option B:** define event "updated" as `GREATEST(event.updated_at, MAX(child expense/share
     updated_at))`, computed on read. More intuitive for the dashboard, but adds another aggregate to
     the list query and is a semantic change beyond "expose the existing field."
   - **Option C:** bump `event.updated_at` from expense/share writes (touch the parent event on child
     change). Most faithful long-term, but a write-path behavior change touching the expenses/shares
     services — out of proportion to this read-only task and risks disturbing the CLOSED-event write rules.
   - Recommendation: **Option A** now; capture B/C under Future Improvements. Proceeding with A unless
     the checkpoint says otherwise.
   - **RESOLVED (user decision, checkpoint):** **Option B.** The exposed `UpdatedAt` must be the
     event's *effective last-activity* timestamp = `GREATEST(event.updated_at, MAX(expense.updated_at
     within the event), MAX(share.updated_at within the event's expenses))`, computed on read. This is
     read-only (no write-path change, no migration) and makes "adding an expense" bubble the event up
     the dashboard card. Implementation notes:
     - Compute it in the same materialized child graph already used for `TotalAdvanced`/`ExpenseCount`
       (`event.Expenses` + `.Shares`) — no extra round-trip. In `EventProfile`, map `UpdatedAt` from a
       max over `{ event.UpdatedAt } ∪ Expenses.Select(e => e.UpdatedAt) ∪ Expenses.SelectMany(e =>
       e.Shares).Select(s => s.UpdatedAt)`. Guard the empty-children case (fall back to
       `event.UpdatedAt`).
     - **Precondition to verify first:** confirm `Expense` and `Share` entities actually have an
       `UpdatedAt`/`updated_at` column (check `Database/Entities/*` + their `Partials`). If either does
       NOT track `updated_at`, fall back to the max over whatever child timestamps DO exist (e.g.
       `CreatedAt`), and if no child timestamp exists at all, fall back to **Option A** (`event.UpdatedAt`
       verbatim) and note the limitation in the Progress Log. Do not add columns/migrations for this.
     - Since `UpdatedAt` now carries a *derived* meaning, no longer map it by name convention — it needs
       an explicit `ForMember`. Apply the same derivation on both `EventSummaryResponse` and
       `EventResponse` for consistency.

(No other blocking questions. The computation strategy and the EventResponse decision are resolved
below with recommendations, per the "same way ExpenseCount is computed" guidance in the brief.)

## Assumptions

- The JSON field casing follows the project-wide serializer convention already applied to
  `expenseCount`/`createdAt` (camelCase on the wire): `totalAdvanced`, `updatedAt`. No per-property
  serialization attributes exist on the current DTOs, so none are added.
- `TotalAdvanced` is serialized as a JSON number with 2-decimal precision (VND uses whole numbers, but
  the column is `decimal(18,2)`; the existing money DTOs elsewhere serialize `decimal` directly and we
  match them).
- Events per user are bounded (a personal/group ledger), and the list read already materializes every
  expense of every event to derive `ExpenseCount`; adding the shares to that same materialized graph is
  an acceptable incremental cost (see Decision D1).
- No pagination change: the events list is currently unpaged and stays unpaged.

## Implementation Plan

### 1. DTOs — add the two fields

`FairShareMonApi/Models/Events/EventSummaryResponse.cs`

- Add `public decimal TotalAdvanced { get; set; }` with a Vietnamese doc comment, e.g.
  `/// <summary>Tổng tiền đã ứng của đợt (tổng tất cả phiếu chi tiêu thuộc đợt = tổng các phần gánh). 0 khi đợt chưa có phiếu.</summary>`
- Add `public DateTime UpdatedAt { get; set; }` with
  `/// <summary>Thời điểm cập nhật gần nhất của đợt (UTC).</summary>`

`FairShareMonApi/Models/Events/EventResponse.cs`

- Add the same two properties + doc comments (consistency; the detail read already Includes expenses).

### 2. AutoMapper — derive `TotalAdvanced` and the effective `UpdatedAt`

`FairShareMonApi/Mappings/EventProfile.cs` — for **both** maps (`Event → EventResponse` and
`Event → EventSummaryResponse`) add:

```csharp
.ForMember(dest => dest.TotalAdvanced,
    opt => opt.MapFrom(src => src.Expenses.SelectMany(e => e.Shares).Sum(s => s.Amount)))
```

`UpdatedAt` is now the **effective last-activity** value per the RESOLVED Open Question (Option B), so
it needs an explicit `ForMember` on **both** maps — it does NOT map by convention. Compute the max over
the event row's own `UpdatedAt` plus every child expense/share timestamp available. Since these live on
the already-loaded `Expenses`/`Shares` graph, the derivation is in-memory (no extra query). Sketch
(adapt to the actual child timestamp columns confirmed in the precondition check — prefer each child's
`UpdatedAt`, else `CreatedAt`):

```csharp
.ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src =>
    new[] { src.UpdatedAt }
        .Concat(src.Expenses.Select(e => e.UpdatedAt))
        .Concat(src.Expenses.SelectMany(e => e.Shares).Select(s => s.UpdatedAt))
        .Max()))
```

Keep the existing `ExpenseCount` mapping untouched. Add a code comment noting both `TotalAdvanced` and
the effective `UpdatedAt` are derived from the loaded `Expenses.Shares` graph and that the repository
must Include those shares.

### 3. Repository — Include shares so the in-memory aggregate has data

`FairShareMonApi/Repositories/EventRepository.cs`

- `ListByUserAsync`: change `.Include(evt => evt.Expenses)` to
  `.Include(evt => evt.Expenses).ThenInclude(exp => exp.Shares)`.
- `GetByUuidAsync`: change `.Include(evt => evt.Expenses)` to
  `.Include(evt => evt.Expenses).ThenInclude(exp => exp.Shares)`.
- Update the XML doc on both interface methods to note they also Include the shares for the derived
  `TotalAdvanced` (alongside the existing "Includes Expenses for the derived expenseCount" note).
- **Do not change** the `OrderByDescending(StartDate).ThenByDescending(CreatedAt)` sort, the closed
  filter, or the resource-owned `Where(evt => evt.User.Uuid == userUuid)` scope.
- Consider `.AsSplitQuery()` on `ListByUserAsync` to avoid a cartesian row blow-up from the
  event→expenses→shares Include across many events (see Decision D1 note). This is an
  implementation-detail optimization, not a contract change.

### 4. Service — no code change

`EventsService.ListAsync`/`GetAsync` already return the AutoMapper projection; the new fields flow
through automatically once the map and Includes are in place. No edit needed (call out in the PR that
the service is intentionally untouched).

### 5. Swagger / doc comments

- The `[SwaggerOperation]` description on `ListAsync` mentions "kèm số lượng phiếu suy ra"; extend it to
  mention the derived total advanced + last-updated timestamp (Vietnamese), and likewise on `GetAsync`.
  Do not restate ordering incorrectly — the sort description stays "ngày bắt đầu giảm dần rồi thời điểm
  tạo giảm dần".

### Endpoints (contract summary — no route/verb/DTO-shape changes beyond the two added fields)

| Route | Verb | Request | Response (changed) |
|---|---|---|---|
| `/api/v1/events` | GET | `EventFilter` (`?closed=`) — unchanged | `ApiResult<IReadOnlyList<EventSummaryResponse>>` — each item gains `totalAdvanced` (number) + `updatedAt` (datetime) |
| `/api/v1/events/{uuid}` | GET | route `uuid` — unchanged | `ApiResult<EventResponse>` — gains `totalAdvanced` + `updatedAt` |

### Entities / EF mapping / migration

- **No entity changes.** `Event.UpdatedAt` and `Share.Amount` already exist and are mapped.
- **No migration.** No column added or altered; `TotalAdvanced` is computed, `UpdatedAt` already
  persisted. The EF model snapshot is unchanged. (Confirmed against
  `Database/Entities/Partials/Event.cs`, `.../Expense.cs`, `.../Share.cs`.)

### Validators / services / repositories to create

- **None created.** This is additive read-only. No new validator (no request body), no new
  service/repository. Only the DTOs, the mapping profile, and two `Include` calls are edited.

### Vietnamese user-facing message keys

- **None.** No new success/error path — no `MessageKeys` additions. User-facing text is limited to the
  Vietnamese XML doc comments on the new DTO members and the extended Swagger descriptions above.

### Tests the test-engineer should write

Unit (no DB), extend `FairShareMonApi.Tests/EventsServiceTests.cs` (uses the real `EventProfile`):

- `ListAsync_MapsTotalAdvanced_FromSumOfSharesAcrossEventExpenses` — seed a stored event with two
  expenses, each carrying shares (e.g. expense A: 200+200+200+200=800; expense B: 50+50=100), assert
  the mapped `TotalAdvanced == 900`.
- `ListAsync_TotalAdvanced_IsZero_WhenNoExpenses` and `..._WhenAllSharesZero`.
- `ListAsync_MapsUpdatedAt_FromEntity` — set `Event.UpdatedAt` on the stub, assert it round-trips.
- Mirror the two into `GetAsync_*` for `EventResponse`.
- Update the existing `StoredEvent()` helper (currently adds expenses with no shares) so the advanced
  assertions have share data; keep the existing `ExpenseCount == 2` assertions passing.

Integration (real MariaDB, skippable), extend `FairShareMonApi.Tests/EventRepositoryTests.cs` and/or
`EventsEndpointTests.cs`:

- `ListByUserAsync_ProjectsTotalAdvanced_AsSumOfEventExpenseShares` — seed a user, an event, two
  expenses with shares (across different payer members), assert the returned/mapped `TotalAdvanced`
  equals the summed share amounts and equals `Σ` of the M7 balance `Advanced` for the same event
  (cross-check against `StatsRepository.GetEventBalanceAsync` to prove the §3.7 identity).
- `..._TotalAdvanced_ExcludesOtherEventsAndLooseExpenses` — a second event and a loose expense
  (`EventId == null`) for the same user must not leak into the first event's total.
- `..._TotalAdvanced_ResourceOwned` — another user's event/expenses never contribute.
- `ListByUserAsync_ExposesUpdatedAt` — after an info edit via `UpdateAsync`, the listed `UpdatedAt`
  reflects the bump and is `>= CreatedAt`.
- `ListByUserAsync_OrderingUnchanged` — a regression guard asserting the sort is still
  `start_date DESC, created_at DESC` (proves the ordering constraint was honored).
- No-N+1 guard: for two events each with several expenses/shares, one `ListByUserAsync` call returns
  correct totals for all events (behavioral proof the aggregate is not per-event fetched).

## Impact Analysis

- **APIs.** Additive, backward-compatible: two new fields on `EventSummaryResponse` and `EventResponse`.
  Existing consumers ignore unknown fields; no breaking change. No new routes/verbs.
- **Database.** None. No migration, no snapshot change, no new index. (`TotalAdvanced` derived;
  `UpdatedAt` already stored. The existing `(user_id, start_date)` index still serves the unchanged
  sort.)
- **Infrastructure.** None.
- **Services.** `EventsService` unchanged. `EventRepository` reads now Include `Expenses.Shares` (one
  extra join / split-query segment); `StatsRepository` unaffected.
- **UI.** Enables the web "recent events" dashboard card (client-side ordering + display of advanced
  total + updated-at). Frontend work is tracked separately (`FairShareMonWeb/planning/*`).
- **Documentation.** Swagger descriptions for `ListAsync`/`GetAsync` extended; new DTO doc comments;
  this planning doc.
- **Risk.** Low. Read-only, additive, no schema/write-path change. Main watch-item is the query shape
  (Decision D1) — mitigated by `AsSplitQuery` and the no-N+1 integration guard.

## Decision Log

### D1 — Compute `TotalAdvanced` by Including `Expenses.Shares` and summing in the mapper (mirror `ExpenseCount`)

**Decision.** Derive `TotalAdvanced` the same way `ExpenseCount` is derived today: Include the child
graph on the repository read and let AutoMapper sum `Expenses.SelectMany(e => e.Shares).Sum(s =>
s.Amount)` in-memory. Add `.AsSplitQuery()` on the list read to avoid the expense×share cartesian
row multiplication.

**Reason.** The task brief instructs computing the aggregate "the same way ExpenseCount is … to avoid
N+1." Keeping the AutoMapper entity→DTO pattern is the smallest, lowest-risk change: no reshaping of
the repository return type, no service rewrite, no new projection DTO, and it stays uniform with the
already-shipped `ExpenseCount`. The read already materializes all expenses per event; adding their
shares is incremental. It is a single query (or split into two with `AsSplitQuery`), never per-event.

**Alternatives considered.**
- *DB-side projection* (repository returns a lightweight projection computing `ExpenseCount` +
  `TotalAdvanced` as SQL aggregates, no entity materialization). Cleaner for large data and closer to
  `StatsRepository`'s "never Include-then-sum-in-memory" note, but reshapes the repository contract and
  ripples into the mapper, service, and existing tests for both the list and detail reads — larger blast
  radius than this read-only task warrants. Recorded as a Future Improvement.
- *Include `Shares` without `AsSplitQuery`* — correct but risks a large cartesian result set when an
  event has many expenses each with many shares. Rejected in favor of split query.

### D2 — Add the fields to `EventResponse` too (not only `EventSummaryResponse`)

**Decision.** Add `TotalAdvanced` + `UpdatedAt` to both DTOs.

**Reason.** The detail read (`GetByUuidAsync`) already Includes the same graph and derives
`ExpenseCount` identically, so the marginal cost is zero and the two DTOs stay consistent (avoids a
confusing asymmetry where the summary exposes more than the detail). The dashboard only needs the
summary, but contract consistency is worth the two extra lines.

### D3 — Do not change list ordering

**Decision.** Leave `ORDER BY start_date DESC, created_at DESC` untouched; "open→closed then recently
updated" is client-side.

**Reason.** Explicit constraint from the task brief; avoids touching the indexed sort and the
`EventFilter` contract.

## Progress Log

### 2026-07-19

- Read `The-ideal.md` §3.6/§3.7, `CLAUDE.md`, `AGENTS.md`, `.claude/rules/rule.md`, and the events
  code (`EventsController`, `EventSummaryResponse`, `EventResponse`, `EventFilter`, `EventRepository`,
  `EventsService`, `EventProfile`, `Event`/`Expense`/`Share` entities + partials, `StatsRepository`,
  existing event tests).
- Pinned the `TotalAdvanced` definition: sum of `Share.Amount` over the event's expenses' shares
  (expenses have no stored total column; matches §3.7 and `StatsRepository.GetEventBalanceAsync`).
- Confirmed no migration is needed (`UpdatedAt` already mapped; aggregate derived) and that ordering
  must not change.
- Drafted this plan. Recorded D1–D3 and one non-blocking Open Question (UpdatedAt semantics).

### 2026-07-19 — implementation (Option B, per RESOLVED user decision)

- **Precondition check (Expense/Share `updated_at`): PASSED.** Both `Database/Entities/Expense.cs`
  (line 50) and `Database/Entities/Share.cs` (line 33) declare a non-nullable `DateTime UpdatedAt`.
  Therefore the full Option-B derivation applies: `UpdatedAt` = max over `{ event.UpdatedAt }` ∪
  `Expenses.Select(e => e.UpdatedAt)` ∪ `Expenses.SelectMany(e => e.Shares).Select(s => s.UpdatedAt)`.
  No `CreatedAt` fallback and no Option-A fallback were needed; no columns/migrations added.
- DTOs: added `decimal TotalAdvanced` and `DateTime UpdatedAt` (with Vietnamese XML doc comments) to
  both `Models/Events/EventSummaryResponse.cs` and `Models/Events/EventResponse.cs`.
- `Mappings/EventProfile.cs`: on both maps added an explicit `ForMember` for `TotalAdvanced`
  (`Expenses.SelectMany(e => e.Shares).Sum(s => s.Amount)`) and for the effective `UpdatedAt` (the max
  described above — no longer mapped by name convention). `event.UpdatedAt` is always in the max set, so
  the empty-children case falls back to it naturally (no explicit guard required). `ExpenseCount`
  mapping left untouched.
- `Repositories/EventRepository.cs`: added `.ThenInclude(exp => exp.Shares)` to the `Include(Expenses)`
  in both `ListByUserAsync` and `GetByUuidAsync`; added `.AsSplitQuery()` to `ListByUserAsync` (D1) to
  avoid the event→expenses→shares cartesian blow-up. Interface XML docs updated. ORDER BY, the closed
  filter, and the resource-owned scope were NOT changed.
- `Controllers/EventsController.cs`: extended the Vietnamese Swagger `Description` on `ListAsync` and
  `GetAsync` to mention the derived total-advanced + last-activity timestamp; ordering description
  unchanged.
- `Services/Api/Events/EventsService.cs`: intentionally untouched (fields flow through the AutoMapper
  projection).
- No entity change and **no EF migration** (confirmed: source columns already exist, aggregate is
  derived; the model snapshot is unchanged).
- Build: `dotnet build .\FairShareMonApi.sln` → succeeded, 0 errors (pre-existing warnings only: the
  pinned AutoMapper 13.0.1 NU1903 advisory and an unrelated test-project CS8619). Tests:
  `dotnet test` → 690 passed, 0 failed, 475 skipped (DB-backed integration tests, DB unreachable in
  this environment). New feature tests are the test-engineer's follow-up.

### 2026-07-19 — tests (test-engineer)

Added coverage for the two derived fields. Full suite `dotnet test .\FairShareMonApi.sln`:
**708 passed, 0 failed, 486 skipped** (skips are the DB-backed integration tests — MariaDB unreachable
in this environment; they skip cleanly per convention). Net new vs. the prior baseline (690/0/475):
**+18 pure unit tests** (all passing) and **+11 integration tests** (all skipped without a DB). No
product bug found; product code untouched.

Files added/changed (test project only):
- **`FairShareMonApi.Tests/EventProfileTests.cs` (new, 11 pure unit tests).** Exercises the real
  `EventProfile` directly for BOTH `Event → EventSummaryResponse` and `Event → EventResponse`:
  `TotalAdvanced` = Σ share amounts across multiple expenses × multiple shares (800+100 ⇒ 900), = 0 with
  no expenses, = 0 when every share is 0đ (0đ expense still counted); effective `UpdatedAt` = max over
  event + expense + share timestamps — child-later-than-event, event-is-latest, empty-children fallback
  to `event.UpdatedAt`, and an expense-with-no-shares still contributing its own timestamp. Plus
  `AssertConfigurationIsValid()` (no unmapped members).
- **`FairShareMonApi.Tests/EventsServiceTests.cs` (extended, +7 unit tests).** Updated the shared
  `StoredEvent()` helper to carry shares (800+100 ⇒ 900) and child timestamps peaking after the event
  row (still `ExpenseCount == 2`, existing assertions intact). New list-path tests: total-advanced sum,
  zero-when-no-expenses, zero-when-all-shares-zero, effective-updatedAt-from-latest-child,
  updatedAt-fallback-to-event; mirrored onto the get-path (`EventResponse`) for populated and empty
  events.
- **`FairShareMonApi.Tests/EventRepositoryTests.cs` (extended, +8 integration tests, skippable).**
  `ListByUserAsync`/`GetByUuidAsync` materialize `Include(Expenses).ThenInclude(Shares)` and are mapped
  through the real `EventProfile`: total-advanced as Σ event-expense-shares **cross-checked equal to Σ
  per-member `Advanced` from `StatsRepository.GetEventBalanceAsync`** (proves the §3.7 identity);
  excludes other events + loose (`event_id == null`) expenses; resource-owned (another user's expenses
  never contribute); effective updatedAt bubbles from child activity (and ≥ CreatedAt); updatedAt after
  an info edit; correct totals for multiple events in one call (behavioral no-N+1); an ordering
  regression guard (`start_date DESC, created_at DESC`); and the detail-read projection.
- **`FairShareMonApi.Tests/EventsEndpointTests.cs` (extended, +3 integration tests, skippable).**
  Full HTTP/`ApiResult` envelope: GET with no expenses ⇒ `totalAdvanced == 0` + `updatedAt` present and
  ≥ `createdAt`; GET with expenses ⇒ `totalAdvanced` equals the summed shares (150 000) + updatedAt
  bubbles; list ⇒ per-event `totalAdvanced` (populated vs. empty).

Extra edge cases beyond the plan's list: the AutoMapper `AssertConfigurationIsValid` guard, an
expense-with-no-shares timestamp contribution, and the endpoint-level zero/empty `totalAdvanced` +
`updatedAt` assertions.

Coverage note: the integration tests could not be executed here (no reachable MariaDB), so their
green/red status is unverified against a live DB — they compile and skip cleanly. The pure unit tests
(which fully cover the mapping logic per the task) run and pass. The `created_at`-tiebreak assertion in
`ListByUserAsync_OrderingUnchanged_...` relies on two sequential inserts landing on distinct
`datetime(6)` microseconds (overwhelmingly the case, but a theoretical tie exists).

## Final Outcome

Implemented as planned under the RESOLVED Open Question (Option B). Two read-only fields —
`totalAdvanced` (JSON number) and `updatedAt` (JSON datetime) — were added to both
`EventSummaryResponse` (`GET /api/v1/events`) and `EventResponse` (`GET /api/v1/events/{uuid}`):

- `totalAdvanced` = Σ `Share.Amount` over every share of every expense in the event (0 when none),
  derived in-memory by AutoMapper from the loaded `Expenses.Shares` graph — same pattern as
  `expenseCount`.
- `updatedAt` = effective last-activity timestamp = max of the event's own `UpdatedAt` and every child
  expense/share `UpdatedAt` (the precondition confirmed both children track `updated_at`).

The events list read now Includes `Expenses.Shares` with `AsSplitQuery()`; the detail read Includes
`Expenses.Shares`. No ordering/filter/scope change, no entity change, no migration. Additive and
backward-compatible. Build green; existing tests green.

Files changed:
- `FairShareMonApi/Models/Events/EventSummaryResponse.cs`
- `FairShareMonApi/Models/Events/EventResponse.cs`
- `FairShareMonApi/Mappings/EventProfile.cs`
- `FairShareMonApi/Repositories/EventRepository.cs`
- `FairShareMonApi/Controllers/EventsController.cs`

## Future Improvements

- **DB-side projection.** If the events list grows or profiling shows the Include graph is heavy, move
  `ExpenseCount` + `TotalAdvanced` to a single SQL aggregate projection in the repository (drop entity
  materialization), consistent with `StatsRepository`'s aggregate approach.
- **Truer "updated" semantics (Open Question B/C).** Either compute event "updated" as the max of the
  event row and its expense/share `updated_at` on read, or bump `event.updated_at` when child
  expenses/shares change — so the dashboard's "recently updated" reflects content edits, not just row
  edits. Requires a separate planning doc (touches the write path and/or the read aggregate).
- **Pagination** for the events list if the "recent events" surface ever needs server-side limiting.
