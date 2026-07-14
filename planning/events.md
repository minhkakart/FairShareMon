# Events (Milestone 6: Events)

The **event** (đợt chi tiêu) lifecycle: an OPEN/CLOSED grouping of expenses over a date range, with
CRUD, one-way close, expense↔event assign/remove, the "expense_time within the event range"
validation, and the **closed-event write block** (§4.4) woven into every M5 expense/share write path.
This milestone consumes the M5 expense seam (OQ8 deferred the `event_id` column to here) and the M5
dedicated `SetSettledAsync` method (the seam for the sole closed-event write exception).

## Objective

Implement `The-ideal.md` §3.6 (Đợt chi tiêu) and the cross-cutting §4.4 (đợt đã chốt là bất biến) on
top of the shipped Auth + Members + Categories + Tags + Expenses/Shares/Audit stack:

- **Event entity + CRUD:** create an event (name + date range + optional description); edit its info;
  list/get; delete. Owns the `events` table + `EventRepository`/`EventsService`/DTOs/validators/
  controller.
- **Open/closed lifecycle:** an event is OPEN or CLOSED. **Close is one-way** (never reopenable),
  **never automatic** (§5 lock — even past the end date, the owner must act).
- **Assign / remove expense ↔ event:** assign an expense to an event and remove it; the expense's
  `expense_time` **must fall within the event's date range** — validated **both when assigning AND when
  the expense_time is later edited** (this adds a check into the M5 expense-update path). An expense
  need not belong to any event (loose expense).
- **`event_id` on expenses (the deferred M5 seam, OQ8):** add the nullable `event_id` FK to `expenses`.
  Deleting an event does **not** delete its expenses — they become loose (`event_id` → null).
- **Remove-expense-from-event and delete-event only while OPEN** (§3.6/§5 lock).
- **Closed-event write block (§4.4 — the milestone's hardest invariant):** when an event is CLOSED,
  **every write to its expenses/shares is rejected** — update expense general info, delete expense,
  add/edit/delete a share, assign/remove the expense to/from the event — with a clear Vietnamese error.
  **The sole exception is the settled flag** (`SetSettledAsync`). The guard is woven into every M5
  write path.
- **Resource-owned (§4.1):** events are user-owned; every event query scoped by user; miss → **404,
  never 403**. An expense may only be assigned to an event owned by the **same user** (§4.2 link
  integrity).

Debt balance (§3.7), stats (§3.9), export (§3.5 CSV), and QR-after-close (§3.10) are later milestones
(M7/M8/M9) — read only to respect the boundary; M6 builds none of them, but leaves the `events` table
and the closed/`closed_at` timeline they consume.

## Background

- **M5 (`planning/expenses-shares-audit.md`)** shipped `expenses`/`shares`/`expense_tags`/`audit_logs`,
  the `ExpenseRepository`/`ShareRepository`/`AuditLogRepository`, `ExpensesService`/`SharesService`,
  and `ExpensesController` (ten `api/v1/expenses` routes). **It deliberately left the event seam:**
  - **OQ8 deferred the entire `event_id` relationship to M6** — the `Expense` entity has **no**
    `event_id` column today; M6 adds the nullable FK, the within-range validation, the closed-event
    write block, and the list-by-event filter together.
  - **`ExpenseFilter`** carries `from`/`to`/`categoryUuid`/`tagUuid`/`settled` but **no `eventUuid`**
    (its XML doc reads "Bộ lọc theo đợt được thêm ở M6"). M6 adds it.
  - M5 shipped a **dedicated `SetSettledAsync`** repository/service method precisely so M6's
    closed-event settled-exception (§3.5/§4.4) fits cleanly: the guard is added to every *other* write
    path and `SetSettledAsync` is left un-guarded.
  - Expenses/shares are **hard-deleted** (`Expense` is `IEntity`, not `IEntityDeletable`); the total is
    **derived** (`SUM(shares.amount)`, no column); money is `decimal(18,2)` with the codebase's first
    CHECK (`ck_shares_amount_non_negative`).
  - Writes are single `ExecuteTransactionAsync` blocks with `TransactionContext.NoCommit()` on failure;
    the transaction lives in the repository (non-nesting); repos return a typed `ExpenseWriteResult<T>`
    / `ExpenseWriteStatus` mapped to `ErrorException` in the service. `ExpenseRepository` loads the
    tracked expense in each write path (`UpdateGeneralInfoAsync`, `DeleteAsync`, `SetSettledAsync`);
    `ShareRepository` resolves the owning expense via `FindOwnedExpenseAsync` then the share.
  - Event membership is **not** currently reflected in the `ExpenseAuditSnapshot` (which carries
    name/description/expense_time/payer/category/tags/isSettled) — so leaving event out of the audit
    (see OQ6) keeps the existing snapshot + no-op detection untouched.
- Conventions confirmed by reading the live code (identical across M2–M5):
  - Entities: partial POCO `Database/Entities/<Name>.cs` + `Partials/<Name>.cs` (ctor sets `Uuid =
    Uuid.NewV7()`, `CreatedAt = AppDateTime.Now`; static `ConfigureModel(ModelBuilder)` invoked from
    `AppDbContext.OnModelCreating`). `IEntity` = `ulong Id`, `string Uuid` (unique, max 64),
    `CreatedAt`, `UpdatedAt` (`ValueGeneratedOnAddOrUpdate` + `current_timestamp(6) ON UPDATE
    current_timestamp(6)`). Snake_case columns; UTC timestamps (`AppDateTime.Now`). FK
    `HasOne(...).WithMany().HasForeignKey(...)`.
  - Soft delete via `IEntityDeletable`/`is_deleted` is for **member/category/tag only** (§4.7);
    expenses/shares are hard-deleted. The default-value/`closed_at` pattern mirrors `is_settled` +
    `settled_at` and `is_default`.
  - Repositories: interface + sealed impl in one file, `[ScopedService(typeof(IX))]`, extend
    `BaseRepository`; reads via `ExecuteQueryAsync`, writes via one `ExecuteTransactionAsync` with
    `NoCommit()`. `ResolveUserIdAsync(db, userUuid, ct)` resolves the owner id. A multi-table atomic
    write stages rows on the shared `AppDbContext` inside the one transaction (never nests another
    repo's transactional method).
  - Controllers derive from `AppController` (LOCKED); routes `api/v{version:apiVersion}/[controller]`;
    `[ResponseWrapped]` → `ApiResult<T>`; `AuthenticatedUser.Id` = current user's UUID; Vietnamese
    `[SwaggerOperation]`/`[SwaggerResponse]`.
  - Errors: `ErrorCodes` — 1xxx infra, 2xxx auth, 3xxx members, 4xxx categories, 5xxx tags, 6xxx
    expenses, 7xxx shares, **8xxx audit reserved**; next free block is **9xxx** for Events.
    `ErrorException(code, message)` → HTTP via `GetDefaultHttpStatus`. Vietnamese messages.
  - Validation: FluentValidation, auto-registered by `AddValidatorsFromAssembly`; services call
    `ValidateAndThrowAsync` (→ `ValidationException` → 400 with `error.fields` camelCase).
  - The money CHECK (`entity.ToTable(t => t.HasCheckConstraint(name, sql))`) is the precedent for a
    DB-level `end_date >= start_date` CHECK on `events`.
- The dev DB holds no real product data beyond disposable smoke rows.

## Requirements

From `The-ideal.md` §2 (concepts), §3.5 (expense↔event relationship), §3.6, §5 locks, and cross-cutting
§4.1/§4.2/§4.4, plus the conventions:

**Event (§2, §3.6):**
- An event groups expenses over a **date range**, with a status **đang mở / đã chốt** (OPEN/CLOSED).
- Create with a name + date range (+ optional description per OQ9); edit its info while OPEN.
- **Close is one-way** (§5 lock: "không mở lại được") and **never automatic** (§5 lock: "Hệ thống
  không bao giờ tự chốt đợt … chủ sổ phải chủ động chốt"). No end-date auto-close job.
- **Delete an event** and **remove an expense from an event** are allowed **only while OPEN**
  (§3.6/§5). Deleting an event does **not** delete its expenses — they become loose (`event_id` null).

**Expense↔event (§3.5, §3.6):**
- An expense may belong to **at most one** event, or none (loose).
- Assign/remove an expense to/from an event; on assign the expense's **`expense_time` must fall within
  the event's date range**, and the same rule is re-checked when the expense's `expense_time` is later
  edited (adds a check into the M5 expense-update path).
- Only an event **owned by the same user** may be assigned (§4.2 link integrity).

**Closed-event write block (§4.4):**
- When an event is CLOSED, **all writes to its expenses/shares are rejected** (update expense general
  info; delete expense; add/edit/delete a share; assign/remove the expense) with a clear Vietnamese
  error. **Sole exception:** the settled flag (`SetSettledAsync`) — payment metadata, not số liệu.
- Closed events are read/export/QR only (export/QR are later milestones).

**Cross-cutting:**
- **Absolute privacy / resource-owned (§4.1):** every event query scoped `WHERE uuid = :uuid AND
  user_id = :current_user_id`; ownership miss → **404, never 403**.
- **Atomicity (§4.5):** each event write and each assign/remove is one `ExecuteTransactionAsync`.
- **Conventions:** entity per rules.md; schema via **EF migration only**; `Async` suffix +
  `CancellationToken`; Vietnamese messages; claim the **9xxx** error block.

## Open Questions

> **All 16 answered by the user at the 2026-07-14 checkpoint — every recommended option (a) was
> accepted.** The struck questions below carry the binding answers inline; the full options/trade-offs
> are preserved for the record and mirrored in the Decision Log. No open questions remain —
> implementation can start. The Implementation Plan, entity/schema section, M5-files-modified list,
> error-code table, endpoint table, and test list below are synced to these answers. Decisions locked
> in spec §5 (event not reopenable; delete-event/remove-expense only while open; system never
> auto-closes; settled is the sole closed-event write) and in prior planning docs were **not** reopened.

**OQ1 — Date-range granularity + "expense_time within range" comparison + timezone.** *(High impact —
drives the column types and the core validation.)*
> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** event range = **two `DateTime` columns, whole-day
> inclusive, UTC-day** — `start_date` normalized to `00:00:00`, `end_date` to `23:59:59.999999` (UTC);
> validation `start_date <= expense_time <= end_date`; DB **CHECK `ck_events_date_range`
> (`end_date >= start_date`)** — the codebase's second CHECK. **The UTC-day-boundary caveat is
> accepted** (an expense near local UTC+7 midnight may land in the adjacent UTC day) and is recorded in
> Assumptions + Future Improvements as a known, documented limitation (a later timezone-aware
> refinement is possible).
`expense_time` is a UTC `DateTime`; the event range
must be comparable against it. The codebase stores everything in UTC (`AppDateTime.Now`) and has **no
per-user timezone**; M5's list filter compares `expense_time` to `from`/`to` as raw UTC datetimes,
inclusive.
- **(a) [recommended] Whole-day inclusive range, stored as two `DateTime` columns.** Client sends
  `startDate`/`endDate` as calendar dates; the service normalizes `start_date` to `00:00:00.000000` and
  `end_date` to `23:59:59.999999` **UTC** on write. Validation: `start_date <= expense_time <=
  end_date`. Trade-off: matches the domain ("Đà Lạt 3 ngày", "tháng 3") and spares the user picking
  times; but a "day" is a **UTC** day, so an expense near local midnight can fall on the neighbouring
  UTC day — acceptable pre-UI, and a proper per-user timezone is a Future Improvement. A DB CHECK
  `end_date >= start_date` (the codebase's second CHECK, after the money one) backs the validator.
- **(b) Datetime-precise range.** Client sends full `startAt`/`endAt` datetimes; comparison `startAt <=
  expense_time <= endAt`. Trade-off: unambiguous and most consistent with M5's raw-datetime filter, no
  day-boundary fudge; but the UI must expose times and a 3-day trip forces the user to pick 00:00 day1
  → 23:59 day3 themselves.
- **(c) Date-only columns (`DateOnly`/SQL `date`), compare `expense_time.Date`.** Trade-off: cleanest
  storage intent; but `expense_time.Date` is still a UTC date (same timezone caveat) and `DateOnly`
  round-tripping through EF/Pomelo is fiddlier than `DateTime`.

**OQ2 — `event_id` on-delete behavior (expenses go loose, never cascade-delete).**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** nullable `event_id` FK on `expenses` with DB-level
> **`OnDelete(SetNull)`** — deleting an event nulls its expenses (they go loose), never cascade-deletes.
Confirm deleting an event **nulls** its expenses' `event_id` and never deletes expenses.
- **(a) [recommended] DB-level `OnDelete(SetNull)`** on the nullable `event_id` FK → `events.id`.
  Deleting an event sets its expenses' `event_id` to null (loose) via `ON DELETE SET NULL`, robust for
  both tracked and untracked rows. Trade-off: the nulling is a DB side-effect outside the app/audit,
  but event membership isn't audited (OQ6) so there is nothing to record.
- **(b) Application-level nulling in the delete transaction.** Load the event's expenses, set
  `event_id = null`, then remove the event — all in one `ExecuteTransactionAsync`. Trade-off: explicit
  and app-visible, but loads potentially many expense rows where the DB can do it in one statement.

**OQ3 — Event deletion model: hard vs soft.**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** events are **hard-deleted** (NOT `IEntityDeletable`);
> delete allowed **only while OPEN** (a closed event → `EventClosed` 9001). Its expenses go loose (OQ2).
Events are **not** in §4.7's soft-delete list (member/category/tag only).
- **(a) [recommended] Hard delete** (Event **not** `IEntityDeletable`), mirroring the M5 expense
  hard-delete decision (OQ3a). Physically removes the event row; its expenses go loose (OQ2). Allowed
  **only while OPEN** (§5). Trade-off: no recovery, but an event carries no historical-display
  obligation like a member — and delete is only permitted before close, i.e. before the event becomes a
  finalized dispute record.
- **(b) Soft delete** (`is_deleted`). Trade-off: uniform with member/category/tag and recoverable, but
  contradicts the §4.7 scope; a soft-deleted event lingering in the table is semantically odd, and its
  expenses would still need `event_id` nulled (or kept pointing at a hidden event).

**OQ4 — How an expense is assigned/removed to/from an event.**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** assign/remove via **expense-side sub-routes** —
> `PUT api/v1/expenses/{uuid}/event` body `{ eventUuid }` (assign/move) and
> `DELETE api/v1/expenses/{uuid}/event` (remove → loose). Leaves the M5 `PUT /expenses/{uuid}`
> full-replace untouched.
- **(a) [recommended] Dedicated expense-side sub-routes:** `PUT api/v1/expenses/{uuid}/event` with body
  `{ eventUuid }` to assign (or move — OQ16), `DELETE api/v1/expenses/{uuid}/event` to remove. Mirrors
  the existing `/settled` and `/shares` nested-route pattern; concentrates the closed-event guard +
  within-range check in one focused path; leaves the M5 `PUT /expenses/{uuid}` (general-info
  full-replace) untouched so it never accidentally reassigns the event. Trade-off: two extra routes.
- **(b) An `eventUuid` field on the expense create/update body.** Trade-off: fewer routes, but M5's
  `PUT` is a full-replace, so an omitted `eventUuid` would silently remove the membership (the same
  footfgun as the M5 payer/category note I1); entangles the closed-event guard with a general-info
  edit.
- **(c) Event-side routes:** `POST api/v1/events/{uuid}/expenses/{expenseUuid}` +
  `DELETE .../expenses/{expenseUuid}`. Trade-off: reads naturally ("add expense to event"), but the
  owned resource being mutated is the **expense** (its `event_id`), and two 404 scopes (event +
  expense) muddy the resource-owned semantics.

**OQ5 — Create an expense directly into an event.**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** allow an **optional `eventUuid` on `POST /expenses`**
> — when present the event must be owned + **OPEN** and `expense_time` within range (else
> `EventNotFound` 9000 / `EventClosed` 9001 / `ExpenseTimeOutOfEventRange` 9002); absent → loose.
- **(a) [recommended] Allow an optional `eventUuid` on `POST /expenses`.** If present, validate the
  event is owned + **OPEN** and `expense_time` within range (same rules as assign); if absent, the
  expense is loose. Cannot create into a CLOSED event (`EventClosed`). Trade-off: one optional field +
  validation in the create path, but matches "mỗi lần chi tiêu An tạo một phiếu" during an active trip.
- **(b) No event at creation; membership only via the assign route afterward.** Trade-off: leaner
  create path, but forces a two-call create-then-assign for the common "log this into the current trip"
  flow.

**OQ6 — Is event assignment/removal audited?**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** **NOT audited** — `event_id` stays out of the
> `ExpenseAuditSnapshot`; assign/remove writes no audit row (mirrors the settled-flag exclusion, OQ11
> in M5). Keeps M5's snapshot + no-op detection untouched.
M5's audit scope is expense + shares (§3.8); changing an expense's `event_id` is a change to the
expense row.
- **(a) [recommended] No audit** for assign/remove, and event is **not** added to the
  `ExpenseAuditSnapshot`. Event membership is grouping metadata, not expenditure số liệu — like the
  settled flag, which §3.8/OQ11 excluded. Keeps the existing snapshot + no-op detection untouched.
  Trade-off: event-membership history is not in the audit log (acceptable; mirrors the settled
  exclusion).
- **(b) Yes** — assign/remove writes an `Expense`/`Update` audit row with `event`/`eventUuid` added to
  the snapshot. Trade-off: literal to §3.8 "mọi lần sửa," but pollutes the dispute-oriented log with
  membership churn and forces event fields into the snapshot (re-touching no-op canonicalization).

**OQ7 — Editing an event's date range when it would exclude already-assigned expenses.**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** **block the edit → `EventRangeExcludesAssignedExpenses`
> (9003)** when the new range would leave any assigned expense out of range; the invariant "every
> assigned expense is within its event's range" always holds (owner must move/remove first).
*(Only relevant while OPEN — a closed event can't be edited at all.)*
- **(a) [recommended] Block the edit** and return `EventRangeExcludesAssignedExpenses` (9003) when the
  new range would leave any currently-assigned expense out of range. Preserves the invariant "every
  assigned expense's time is within its event's range." Trade-off: the owner must first
  remove/re-time the offending expenses; strict but keeps the invariant always true.
- **(b) Allow the edit**, leaving assigned expenses out of range. Trade-off: simplest, but breaks the
  invariant, so "within range" holds only at assign-time — confusing and inconsistent.
- **(c) Auto-remove** out-of-range expenses (`event_id` → null) on the range edit. Trade-off:
  convenient, but silently unassigns expenses — a surprising data mutation.

**OQ8 — Preconditions to close.**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** **no preconditions** — closing is always allowed
> while OPEN, even with zero expenses.
- **(a) [recommended] None** — closing is always allowed while OPEN, even with zero expenses.
  Trade-off: an empty event can be closed (harmless); matches "chủ sổ phải chủ động chốt" with no
  gating.
- **(b) Require ≥ 1 expense to close.** Trade-off: avoids a pointless empty closed event, but the spec
  never requires it and it would block a legitimately empty closed trip.

**OQ9 — Event fields + status representation + field lengths.**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** `name` required max **200**; `description` optional
> max **1000**; range per OQ1; status = **`is_closed` bool (default false) + nullable `closed_at`**;
> derived `expenseCount` on the DTO. No enum.
- **(a) [recommended]** `name` required (max **200**, matching the expense name cap); `description`
  optional (max **1000**); the date range per OQ1; status as **`is_closed` (bool, default false) +
  nullable `closed_at`** (mirrors `is_settled`/`settled_at` and `is_default`). No enum. `EventResponse`
  also exposes a derived `expenseCount`. Trade-off: bool + timestamp is the established pattern here and
  records "when closed" cheaply (useful for the M9 QR-after-close timeline); an enum would be more
  extensible but there are only two, one-way states.
- **(b) A `status` int-enum column** (`Open`/`Closed`), like the audit enums. Trade-off: consistent
  with the audit enums and extensible, but overkill for a binary one-way flag and loses the cheap
  `closed_at`.

**OQ10 — Event list default sort + filtering.**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a):** sort **`start_date` DESC then `created_at` DESC**;
> optional `?closed=true|false` filter; **no pagination** this milestone.
- **(a) [recommended]** Sort `start_date` DESC then `created_at` DESC (current/recent trip first);
  optional `?closed=true|false` filter (open/closed); **no pagination** (mirrors M5/M4/M3 lists; tier
  limits cap volume in M10). Trade-off: consistent with shipped lists; `start_date` DESC is the most
  meaningful order for events.
- **(b)** Sort by `created_at` DESC, or `name` A→Z. Trade-off: created-order is simplest but less
  meaningful than the trip's start date.

**OQ11 — Endpoint surface (confirmation).**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** the six event routes
> (list/get/create/update/delete/**`PUT /{uuid}/close`** one-way) + the two expense-side assign/remove
> routes, as tabled in Step 9; **re-closing a closed event → `EventClosed` (9001)**.
- **(a) [recommended]** Events: `GET /events` (list, filter/sort), `GET /events/{uuid}` (detail),
  `POST /events` (create), `PUT /events/{uuid}` (edit info — OPEN only), `DELETE /events/{uuid}` (hard
  delete — OPEN only), `PUT /events/{uuid}/close` (one-way close); plus expense-side `PUT
  /expenses/{uuid}/event` + `DELETE /expenses/{uuid}/event` (assign/remove, per OQ4). All guarded,
  resource-owned. Re-closing an already-closed event → `EventClosed` (9001). Trade-off: none; confirms
  shapes before coding.
- **(b)** Any change — e.g. event-side assign routes (OQ4-c), or folding close into the `PUT` body.
  Trade-off: see OQ4; a status-in-PUT blurs the "edit info vs one-way close" distinction.

**OQ12 — Error-code block (9xxx Events).**
> ~~**OQ12**~~ → **Answered 2026-07-14 (option a):** **9xxx** = `9000 EventNotFound` (404) /
> `9001 EventClosed` (400) / `9002 ExpenseTimeOutOfEventRange` (400) /
> `9003 EventRangeExcludesAssignedExpenses` (400); range-invalid (end < start on input) stays a
> **1001** validation error; extend `ErrorException.GetDefaultHttpStatus`.
- **(a) [recommended]** Claim **9xxx** with: `9000 EventNotFound` (404, every resource-owned miss),
  `9001 EventClosed` (400 — the §4.4 write block: any write to a closed event's expense/share, plus
  remove/delete/edit/close attempted on a closed event), `9002 ExpenseTimeOutOfEventRange` (400 —
  expense_time outside the range on assign, create-into-event, or expense_time edit), `9003
  EventRangeExcludesAssignedExpenses` (400 — the OQ7 range-edit block). An invalid request payload
  (`endDate` before `startDate`, blank name) stays a **validation** failure (1001), not a 9xxx code.
  Extend `ErrorException.GetDefaultHttpStatus`. Trade-off: none; continues the one-block-per-feature
  pattern.
- **(b)** Fold `9002`/`9003` into `9001`, or reuse `1003 NotFound`. Trade-off: denser, but clients lose
  the closed-vs-out-of-range-vs-range-conflict distinction.

**OQ13 — Closed-event guard mechanics + where it lives.**
> ~~**OQ13**~~ → **Answered 2026-07-14 (option a):** a shared **repository-layer** `EventWriteGuard`
> invoked inside each M5 write transaction — `ExpenseRepository.UpdateGeneralInfoAsync`, `DeleteAsync`,
> `AssignEventAsync`, `RemoveEventAsync` and `ShareRepository.AddAsync`/`UpdateAsync`/`DeleteAsync`;
> **`SetSettledAsync` deliberately omits it** (the sole §4.4 exception).
- **(a) [recommended] A shared repository-layer guard** invoked inside each M5 write transaction, after
  the tracked expense is loaded: if `expense.EventId` is set, load the event (or `Include` it) and
  return `EventClosed` (aborting via `NoCommit()`) when `is_closed`. Woven into `ExpenseRepository`
  (`UpdateGeneralInfoAsync`, `DeleteAsync`, and the new `AssignEventAsync`/`RemoveEventAsync`) and
  `ShareRepository` (`AddAsync`, `UpdateAsync`, `DeleteAsync`). **`SetSettledAsync` deliberately omits
  the guard** — the sole §4.4 exception. A small shared helper (e.g. a static `EventWriteGuard.Check`)
  keeps the check DRY across the two repos. Trade-off: the guard is referenced from two repos, but it
  keeps the "transaction in the repository" convention and each write path self-guards atomically (a
  close racing a write can't slip through, because the check and the write share one transaction).
- **(b) Guard in the service layer** — `ExpensesService`/`SharesService` load the expense's event and
  reject before delegating. Trade-off: centralizes the check, but the read-then-write spans two
  transactions (a close racing a write could slip through) and spreads `DbContext` access into
  services — against convention.

**OQ14 — Expense list/detail DTO event info + activating the deferred M5 event filter.**
> ~~**OQ14**~~ → **Answered 2026-07-14 (option a):** add nullable `eventUuid` / `eventName` /
> `eventIsClosed` to **both** expense response DTOs (full + summary) and `eventUuid` + `looseOnly` to
> `ExpenseFilter` — activating M5's deferred event filter against the real `event_id`.
- **(a) [recommended]** Add nullable `eventUuid` + `eventName` (+ `eventIsClosed`) to
  **`ExpenseResponse`** and **`ExpenseSummaryResponse`**, mapped from the `Event` nav; add `eventUuid`
  to **`ExpenseFilter`** (the M5 seam) so the list filter resolves against the real `event_id`, plus a
  `looseOnly` (or `noEvent`) flag to list expenses belonging to no event. Trade-off: small DTO/mapping
  additions + one `Include`, but makes event membership visible inline and activates the deferred
  filter (needed by the M7 per-event balance UI).
- **(b) Minimal** — don't expose event on the expense DTOs; surface an event's expenses only via `GET
  /expenses?eventUuid=…`. Trade-off: less coupling, but a client can't see an expense's event inline
  and the loose-only listing is unavailable.

**OQ15 — Does the event detail embed its expenses?**
> ~~**OQ15**~~ → **Answered 2026-07-14 (option a):** event **detail** returns fields + derived
> `expenseCount` (no embedded expense list); an event's expenses are listed via
> `GET /expenses?eventUuid=…`.
- **(a) [recommended] No** — `GET /events/{uuid}` returns event fields + derived `expenseCount` only;
  the client lists an event's expenses via `GET /expenses?eventUuid=…` (OQ14). Debt balance is M7,
  out of scope. Trade-off: keeps the event endpoint lean and reuses the expense-list projection; one
  extra call to see the expenses.
- **(b) Embed the expense summaries** in the event detail. Trade-off: one call, but duplicates the
  expense-list projection and grows the payload; better served by the filter.

**OQ16 — Move semantics for `PUT /expenses/{uuid}/event` when the expense is already assigned.**
> ~~**OQ16**~~ → **Answered 2026-07-14 (option a):** allow a **direct reassign A→B** in one call when
> both the current and target events are OPEN and `expense_time` is within the target range; if the
> **source** event is CLOSED → `EventClosed` (9001, can't move an expense out of a closed event, §4.4).
- **(a) [recommended] Allow a direct reassign** (A → B) in one call when both the **current** event (if
  any) and the **target** event are OPEN and `expense_time` is within the target's range. If the
  current event is CLOSED → `EventClosed` (9001, can't move an expense out of a closed event, §4.4).
  Trade-off: one call to move; but the source-closed case must be guarded.
- **(b) Require explicit remove-then-assign** (assign only works on a loose expense; reject if already
  assigned). Trade-off: more explicit, but two calls for a move and an extra "already assigned" error
  state.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 16 Open Questions — these
> are now decisions, not vetoable assumptions. Each is derived from spec/prior decisions and the
> answered OQs.

- All event + assign/remove endpoints are **guarded** (valid access token); no anonymous operation.
- `users` are not soft-deletable; the event `user_id` FK cascade delete is inert in practice, kept for
  integrity (consistent with expenses/members/categories).
- Each event write and each assign/remove is one `ExecuteTransactionAsync` with `NoCommit()` on
  failure; the transaction stays in the repository (non-nesting convention).
- The **actor** and owner is always the current authenticated user (only the owner touches their own
  data).
- Event membership is **at most one event per expense** (a single nullable `event_id`), not many-to-many.
- **UTC-day-boundary limitation (accepted, OQ1):** the event range is a **UTC** whole-day window, so an
  expense logged near local (e.g. UTC+7) midnight can fall on the adjacent UTC day and thus just
  outside/inside a range the user thinks of in local time. This is a **known, documented limitation**,
  not a bug; a timezone-aware refinement is listed in Future Improvements. It is consistent with M5,
  where `expense_time` and the `from`/`to` list filter are already compared as raw UTC datetimes.
- Tier limits on events (§3.11 "M đợt đang mở") are **out of scope** (M10); M6 imposes no count limit.
- Debt balance (§3.7), stats (§3.9), export (§3.5), and QR (§3.10) are later milestones; M6 only leaves
  the `events` table + `is_closed`/`closed_at` timeline they will consume.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repos use DiDecoration
> `[ScopedService]`. All user-facing strings Vietnamese. Concrete names below reflect the **recommended
> option (a)** for every Open Question; if the user picks a different option at the checkpoint, the
> affected step is re-synced before implementation. Steps marked **[M5-MOD]** modify shipped M5 files.

### Step 1 — Entity

1. `Database/Entities/Event.cs` (POCO, `partial`, `IEntity`; **not** `IEntityDeletable` — hard delete
   per OQ3): `ulong Id`, `string Uuid`, `ulong UserId` (FK → `users.id`), `required string Name`,
   `string? Description`, `DateTime StartDate`, `DateTime EndDate`, `bool IsClosed`,
   `DateTime? ClosedAt`, `DateTime CreatedAt`, `DateTime UpdatedAt`; nav `User User`,
   `ICollection<Expense> Expenses`.
2. `Database/Entities/Partials/Event.cs`: ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt =
   AppDateTime.Now`; consts `NameMaxLength = 200`, `DescriptionMaxLength = 1000` (OQ9); static
   `ConfigureModel`: table `events`; `uuid` (max 64, unique index); `user_id` (indexed); `name`
   (max 200); `description` (max 1000); `start_date`, `end_date` (`datetime(6)`, per OQ1); `is_closed`
   (bool, default false); `closed_at`; `created_at`; `updated_at` computed default; composite index
   `(user_id, start_date)` for the default sort (OQ10); FK `HasOne(User).WithMany()
   .OnDelete(Cascade)`; **DB CHECK** `entity.ToTable(t => t.HasCheckConstraint("ck_events_date_range",
   "end_date >= start_date"))` (the codebase's second CHECK).
3. **[M5-MOD]** `Database/Entities/Expense.cs`: add `ulong? EventId` and nav `Event? Event`.
4. **[M5-MOD]** `Database/Entities/Partials/Expense.cs`: map `event_id` (indexed); FK
   `HasOne(Event).WithMany(Expenses).HasForeignKey(EventId).OnDelete(DeleteBehavior.SetNull)` (OQ2).
5. **[M5-MOD]** `Database/AppDbContext.cs`: add `DbSet<Event> Events => Set<Event>();` and invoke
   `Event.ConfigureModel(modelBuilder)` in `OnModelCreating`. `AppDbContext.partial.cs` untouched.

### Step 2 — EF migration

- `dotnet ef migrations add AddEvents --project .\FairShareMonApi\FairShareMonApi.csproj` (offline via
  the pinned design-time factory). **Migration name: `AddEvents`.**
- The one migration creates the **`events`** table AND **`ALTER`s `expenses`** to add the nullable
  `event_id` column + its FK (`ON DELETE SET NULL`) + index.
- Review: utf8mb4/unicode_ci; unique `uuid` index; `(user_id, start_date)` index; the `event_id` FK
  SetNull; the **`ck_events_date_range`** CHECK; bool/`closed_at`/`updated_at` defaults. Keep the model
  snapshot in sync. Apply to the dev DB during the Test step per the orchestration protocol.

### Step 3 — Error codes + messages

**[M5-MOD]** Append the **9xxx Events** block to `Constants/ErrorCodes.cs` (never renumber):

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `9000` | `EventNotFound` | 404 | "Không tìm thấy đợt chi tiêu." |
| `9001` | `EventClosed` | 400 | "Đợt chi tiêu đã chốt, không thể thay đổi." |
| `9002` | `ExpenseTimeOutOfEventRange` | 400 | "Thời điểm chi của phiếu không nằm trong khoảng thời gian của đợt." |
| `9003` | `EventRangeExcludesAssignedExpenses` | 400 | "Không thể đổi khoảng thời gian: có phiếu đã gán nằm ngoài khoảng thời gian mới." |

- **[M5-MOD]** Extend `Exception/ErrorException.GetDefaultHttpStatus`: `9000`→404, `9001/9002/9003`→400.
- `9001` may carry context-specific Vietnamese messages per call site (as M5's `7002` did for
  delete-vs-change): remove-from-closed → "Không thể gỡ phiếu khỏi đợt đã chốt."; delete-closed-event →
  "Không thể xóa đợt đã chốt."; edit-closed-event → "Không thể sửa đợt đã chốt.".
- Success messages: create "Thêm đợt chi tiêu thành công."; update "Cập nhật đợt chi tiêu thành
  công."; delete "Đã xóa đợt chi tiêu."; close "Đã chốt đợt chi tiêu."; assign "Đã gán phiếu vào đợt
  chi tiêu."; remove "Đã gỡ phiếu khỏi đợt chi tiêu.".

### Step 4 — Typed write results

- New `Repositories/EventWriteResult.cs`: `EventWriteStatus { Success, EventNotFound, EventClosed,
  RangeExcludesAssignedExpenses }` + `EventWriteResult<T>` (mirrors `ExpenseWriteResult<T>`) + data
  records `CreateEventData(Name, Description, StartDate, EndDate)` / `UpdateEventData(...)`.
- **[M5-MOD]** `Repositories/ExpenseWriteResult.cs`: add `EventNotFound`, `EventClosed`,
  `ExpenseTimeOutOfEventRange` to the existing `ExpenseWriteStatus` enum (so the shared expense/share
  write paths can return them); add optional `string? EventUuid` to `CreateExpenseData` (create-into-
  event, OQ5); add a `EventAssignData`/param for the assign method.

### Step 5 — EventRepository

`Repositories/EventRepository.cs` — `IEventRepository : IBaseRepository, IQueryRepository<Event>` +
sealed impl (`[ScopedService]`, extends `BaseRepository`):
- `Query(tracking, includeDeleted)` → `Query<Event>(...)` (includeDeleted inert — events aren't
  `IEntityDeletable`).
- `ListByUserAsync(userUuid, EventFilter filter, ct)` — resource-owned; optional `closed` filter; sort
  `start_date` DESC then `created_at` DESC (OQ10); project with `expenseCount` (`Expenses.Count`).
- `GetByUuidAsync(userUuid, eventUuid, ct)` — resource-owned; null on miss.
- `CreateAsync(userUuid, CreateEventData data, ct)` — resolve user; normalize the range per OQ1; insert;
  `is_closed = false`. One transaction.
- `UpdateAsync(userUuid, eventUuid, UpdateEventData data, ct)` — resource-owned tracked load; reject if
  `is_closed` → `EventClosed`; recompute the range; **OQ7 check** — if any currently-assigned expense
  falls outside the new range → `RangeExcludesAssignedExpenses`; else apply.
- `CloseAsync(userUuid, eventUuid, ct)` — resource-owned tracked load; if already `is_closed` →
  `EventClosed`; else set `is_closed = true`, `closed_at = AppDateTime.Now` (one-way). No preconditions
  (OQ8).
- `DeleteAsync(userUuid, eventUuid, ct)` — resource-owned load; reject if `is_closed` → `EventClosed`
  (delete only while OPEN, §5); else hard-delete (the FK `ON DELETE SET NULL` makes its expenses loose,
  OQ2). Miss → `EventNotFound`.

### Step 6 — Closed-event guard + expense↔event assignment (M5 repos)

> **Three invariants this step must realize (spelled out for the api-implementer):**
>
> 1. **Closed-event write block (§4.4, OQ13) — `EventWriteGuard` in every M5 write path.** The guard is
>    invoked inside the write transaction, immediately after the tracked expense is loaded, in **all**
>    of: `ExpenseRepository.UpdateGeneralInfoAsync`, `ExpenseRepository.DeleteAsync`,
>    `ExpenseRepository.AssignEventAsync`, `ExpenseRepository.RemoveEventAsync`,
>    `ShareRepository.AddAsync`, `ShareRepository.UpdateAsync`, `ShareRepository.DeleteAsync`. If the
>    expense's current event `is_closed` → `NoCommit()` + `EventClosed` (9001). **`SetSettledAsync` is
>    the only write path that deliberately does NOT invoke the guard** — the sole §4.4 exception.
> 2. **Within-range re-validation on the expense_time edit path (OQ1/OQ7).** In
>    `UpdateGeneralInfoAsync`, when the expense belongs to an (OPEN) event, the new `expense_time` must
>    still fall in `[start_date, end_date]`; otherwise `NoCommit()` + `ExpenseTimeOutOfEventRange`
>    (9002). (A CLOSED event is already rejected by invariant 1, so this check only runs for open
>    events.)
> 3. **Assign / create-into validation (§4.1/§4.2 + OQ5/OQ16).** Assigning to (or creating into) an
>    event validates the target event is **owned** (resource-owned, miss → `EventNotFound` 9000),
>    **OPEN** (closed target → `EventClosed` 9001), and `expense_time` **within its range**
>    (else `ExpenseTimeOutOfEventRange` 9002). On a **move**, the expense's current event must also be
>    OPEN (source closed → `EventClosed` 9001, invariant 1).

`Repositories/EventWriteGuard.cs` (new, static helper, OQ13): `Check(Expense expense) → bool isClosed`
given the loaded `expense.Event` (or a helper that loads `db.Events` by `expense.EventId`). Returns
whether the expense's current event is CLOSED, so each write path can `NoCommit()` + return
`EventClosed`.

**[M5-MOD]** `Repositories/ExpenseRepository.cs`:
- `CreateAsync` — after resolving defaults/links, if `data.EventUuid` is set: resolve the event
  (owned + **OPEN**, else `EventNotFound`/`EventClosed`); validate `expense_time` within its range (else
  `ExpenseTimeOutOfEventRange`); set `expense.EventId`. (OQ5)
- `UpdateGeneralInfoAsync` — **add the closed-event guard** (load the expense's event; if CLOSED →
  `EventClosed`) **before** applying changes; and if the expense belongs to an (open) event, re-validate
  the new `expense_time` against the event's range (`ExpenseTimeOutOfEventRange`). Include `Event` in
  the tracked load.
- `DeleteAsync` — **add the closed-event guard** (CLOSED → `EventClosed`, block delete). Include
  `Event`.
- `SetSettledAsync` — **unchanged / no guard** (the sole §4.4 exception).
- New `AssignEventAsync(userUuid, expenseUuid, eventUuid, ct)` — resource-own the expense (incl. its
  current `Event`); if the **current** event is CLOSED → `EventClosed` (can't move out of closed, OQ16);
  resolve the **target** event (owned + OPEN, else `EventNotFound`/`EventClosed`); validate
  `expense_time` within the target range (else `ExpenseTimeOutOfEventRange`); set `expense.EventId`. One
  transaction; **no audit** (OQ6).
- New `RemoveEventAsync(userUuid, expenseUuid, ct)` — resource-own the expense (incl. `Event`); expense
  miss → `ExpenseNotFound` (6000); if it has **no** event → **success no-op** (idempotent, mirrors the
  M5 idempotent soft-delete pattern); if the current event is CLOSED → `EventClosed` (9001 — remove only
  while open, §3.6/§4.4); else set `EventId = null`. No audit (OQ6).
- `ListByUserAsync` — **add `eventUuid` + loose filter** (OQ14): `filter.EventUuid` →
  `expense.Event.Uuid == …`; `filter.LooseOnly` → `expense.EventId == null`. `Include(Event)` for the
  DTO.
- `GetByUuidAsync` — `Include(Event)` for the detail DTO.

**[M5-MOD]** `Repositories/ShareRepository.cs`:
- `AddAsync`, `UpdateAsync`, `DeleteAsync` — after `FindOwnedExpenseAsync`, **add the closed-event
  guard** (load the expense's event; CLOSED → `EventClosed`, abort). `FindOwnedExpenseAsync` gains an
  `Include(Event)` (or the guard queries `db.Events` by `expense.EventId`).

### Step 7 — Services + mappings

`Services/Api/Events/EventsService.cs` — `IEventsService` + sealed impl (`[ScopedService]`, primary
ctor injecting `IEventRepository`, `IMapper`, the create/update validators): `ListAsync`, `GetAsync`
(miss → `EventNotFound`), `CreateAsync`, `UpdateAsync`, `CloseAsync`, `DeleteAsync` — each maps the
`EventWriteStatus` to the right `ErrorException` (`9000/9001/9003`). Range normalization (OQ1) lives
here or in the repo (keep it one place).

**[M5-MOD]** `Services/Api/Expenses/ExpensesService.cs`:
- `ThrowIfFailed` — add cases: `EventNotFound` → `9000`, `EventClosed` → `9001`,
  `ExpenseTimeOutOfEventRange` → `9002`.
- New `AssignEventAsync(userUuid, expenseUuid, AssignEventRequest, ct)` and
  `RemoveEventAsync(userUuid, expenseUuid, ct)` delegating to the repo and mapping statuses.
- `CreateAsync` — thread the optional `request.EventUuid` into `CreateExpenseData`.

**[M5-MOD]** `Services/Api/Shares/SharesService.cs`: add an `EventClosed` → `9001` case to the
`AddAsync`/`UpdateAsync`/`DeleteAsync` status switches.

`Mappings/EventProfile.cs` — `Event`→`EventResponse`/`EventSummaryResponse` (map `expenseCount` from
`Expenses.Count`).
**[M5-MOD]** `Mappings/ExpenseProfile.cs` — map `EventUuid`/`EventName`/`EventIsClosed` (nullable) from
the `Event` nav on both `ExpenseResponse` and `ExpenseSummaryResponse` (OQ14).

### Step 8 — DTOs + validators

- `Models/Events/`: `CreateEventRequest { string Name, string? Description, DateTime StartDate,
  DateTime EndDate }`; `UpdateEventRequest { string Name, string? Description, DateTime StartDate,
  DateTime EndDate }`; `EventResponse { string Uuid, string Name, string? Description, DateTime
  StartDate, DateTime EndDate, bool IsClosed, DateTime? ClosedAt, int ExpenseCount, DateTime
  CreatedAt }`; `EventSummaryResponse` (same minus description, or reuse); `EventFilter { bool?
  Closed }`.
- `Models/Expenses/AssignEventRequest.cs` (new): `{ string EventUuid }`.
- **[M5-MOD]** `Models/Expenses/ExpenseFilter.cs` — add `string? EventUuid` and `bool? LooseOnly` (drop
  the "added in M6" TODO comment).
- **[M5-MOD]** `Models/Expenses/ExpenseResponse.cs` + `ExpenseSummaryResponse.cs` — add `string?
  EventUuid`, `string? EventName`, `bool? EventIsClosed`.
- **[M5-MOD]** `Models/Expenses/CreateExpenseRequest.cs` — add `string? EventUuid` (OQ5).
- `Validators/Events/CreateEventRequestValidator.cs` + `UpdateEventRequestValidator.cs`: `Name`
  required + max 200 ("Tên đợt không được để trống." / "…không được vượt quá 200 ký tự.");
  `Description` max 1000; `StartDate`/`EndDate` required (non-default); **`EndDate >= StartDate`**
  ("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu."). `AssignEventRequest` — `EventUuid` required. Field
  keys camelCase. Auto-registered by the existing `AddValidatorsFromAssembly`.

### Step 9 — Controllers

`Controllers/EventsController.cs` (derives from `AppController`, LOCKED). All actions guarded,
Vietnamese Swagger, `userUuid = AuthenticatedUser.Id`.

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/events` | `[FromQuery] EventFilter` → `ApiResult<IReadOnlyList<EventSummaryResponse>>` | sort start_date DESC; optional `closed` (OQ10) |
| `GET api/v1/events/{uuid}` | route → `ApiResult<EventResponse>` | resource-owned; miss → 9000 |
| `POST api/v1/events` | `CreateEventRequest` → `ApiResult<EventResponse>` | validate range; is_closed=false |
| `PUT api/v1/events/{uuid}` | `UpdateEventRequest` → `ApiResult<EventResponse>` | OPEN only (9001); OQ7 range check (9003); miss → 9000 |
| `DELETE api/v1/events/{uuid}` | route → `ApiResult` message | OPEN only (9001); expenses go loose (OQ2); miss → 9000 |
| `PUT api/v1/events/{uuid}/close` | route → `ApiResult` message | one-way; re-close → 9001; miss → 9000 |

**[M5-MOD]** `Controllers/ExpensesController.cs` — add two routes (and the `eventUuid`/`looseOnly` query
params come free via `[FromQuery] ExpenseFilter`):

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `PUT api/v1/expenses/{uuid}/event` | `AssignEventRequest` → `ApiResult<ExpenseResponse>` | assign/move; target OPEN + within range; source CLOSED → 9001; out-of-range → 9002; event miss → 9000 |
| `DELETE api/v1/expenses/{uuid}/event` | route → `ApiResult` message | remove; OPEN only (9001); expense miss → 6000 |

### Step 10 — Tests (owned by the test-engineer; definitive list)

Reuse the M5 harness: `[Collection("AuthIntegration")]`; DB tests use the `ExpenseDbTestBase` /
`ExpenseApiTestBase` families (own connections / app DI + real HTTP) with a unique lowercase username
prefix per class and dispose-time cascade cleanup; all DB-dependent tests `[SkippableFact]`. Cleanup
note: deleting the prefix's users cascades to expenses (which now carry `event_id` SetNull) and the new
`events` rows via the `user_id` FK; the base classes gain an **events sweep** if needed.

**Unit (no DB):**
- `CreateEventRequestValidator` / `UpdateEventRequestValidator` — name required/max-200, description
  max-1000, dates required, `EndDate >= StartDate`; exact Vietnamese messages; camelCase `error.fields`.
- `AssignEventRequestValidator` — `eventUuid` required.
- `EventsService` (fake `IEventRepository`) — get/update/close/delete miss → 9000; update/delete/close
  on closed → 9001; range-conflict → 9003; create maps success.
- `ExpensesService` (extend the M5 fakes) — assign maps 9000/9001/9002; remove maps 9001/6000;
  create-into-event threads `eventUuid`; the M5 6001/6002/6003/7001/7003 mappings still hold.
- `SharesService` — add/edit/delete on a closed-event expense → 9001 (fake status).

**Integration (real MariaDB — `EventRepositoryTests`, `ExpenseEventAssignmentTests`, and closed-event
regressions on `ExpenseRepositoryTests`/`ShareRepositoryTests`):**
- Event CRUD: create sets uuid/UTC/user_id/is_closed=false; **resource-owned** — another user's event
  invisible to get/list/update/close/delete (null/status, never the row); default sort start_date DESC;
  the `closed` filter.
- Close is **one-way**: closes once (is_closed + closed_at set); re-close → 9001.
- Delete: OPEN event hard-deletes and its expenses **go loose** (`event_id` → null, expenses survive);
  CLOSED event delete → 9001.
- Range edit (OQ7): editing to a range that would exclude an assigned expense → 9003; a safe edit
  persists.
- **DB CHECK** `ck_events_date_range` rejects `end_date < start_date` at the DB.
- Assign: expense_time within range assigns; out-of-range → 9002; foreign/unknown target event → 9000;
  target CLOSED → 9001; move A→B when both open; move out of a CLOSED source → 9001. Create-into-event
  (OQ5): within range OK; closed target → 9001; out-of-range → 9002.
- Expense_time edit re-validation: editing an assigned expense's time out of its (open) event range →
  9002.
- **Closed-event write block regressions (the milestone's core):** with an expense in a CLOSED event,
  each of `UpdateGeneralInfoAsync`, `DeleteAsync`, share `AddAsync`/`UpdateAsync`/`DeleteAsync`, and
  `AssignEventAsync`/`RemoveEventAsync` → **9001** and leaves the data unchanged; **`SetSettledAsync`
  succeeds** (the sole exception) and toggles the flag.
- No audit rows are written by assign/remove (OQ6).

**Endpoint (WebApplicationFactory — `EventsEndpointTests`, `ExpenseEventEndpointTests`):**
- Create/list/get/update/close/delete an event through HTTP; resource-owned 404 (9000) on every route
  for another user's event (never 403); anonymous → 401.
- Close then attempt update/delete/re-close → 400 (9001); the event stays closed.
- Assign an expense to an event (`PUT /expenses/{uuid}/event`), see `eventUuid`/`eventName` on
  `GET /expenses/{uuid}` and in the list; filter `GET /expenses?eventUuid=…` and `?looseOnly=true`;
  remove (`DELETE …/event`) → the expense goes loose.
- **Closed-event block through HTTP:** close the event, then every M5 expense/share write route
  (`PUT /expenses/{uuid}`, `DELETE /expenses/{uuid}`, `POST|PUT|DELETE /expenses/{uuid}/shares…`,
  `PUT /expenses/{uuid}/event`, `DELETE /expenses/{uuid}/event`) → 400 (9001); **`PUT
  /expenses/{uuid}/settled` still succeeds**.
- Assign out-of-range → 400 (9002); range-conflict edit → 400 (9003); `endDate < startDate` create →
  400 (1001, validation).

### Step 11 — Wrap-up

- `dotnet build` clean; `dotnet test` green (DB tests skip only when MariaDB unreachable). Live smoke:
  register → create event → create-into-event + assign/move/remove → within-range + out-of-range →
  close (one-way) → closed-event block on every write route + settled exception → delete event leaves
  expenses loose → resource-owned 404s. `dotnet ef database update` per protocol. Update this doc's
  Progress Log + Final Outcome; note in `agent-dev-team.md` that M6 consumed the M5 event seam (OQ8)
  and the `SetSettledAsync` seam, and record the M7 boundary (debt balance consumes `event_id` +
  is_closed).

## Impact Analysis

- **APIs:** six new `api/v1/events` endpoints (list, get, create, update, delete, close) + two new
  expense-side routes (assign/remove event). `GET /expenses` gains `eventUuid`/`looseOnly` filters;
  `POST /expenses` gains an optional `eventUuid`; the expense response DTOs gain `eventUuid`/`eventName`/
  `eventIsClosed`. No existing route shape is removed.
- **Database:** new migration `AddEvents` — the `events` table (FK cascade to `users`, unique `uuid`,
  `(user_id, start_date)` index, the codebase's **second CHECK** `ck_events_date_range`) **and** an
  `ALTER` on `expenses` adding the nullable `event_id` FK (`ON DELETE SET NULL`) + index. No data
  migration.
- **Infrastructure:** no new hosted service (§5: the system never auto-closes — there is deliberately
  **no** end-date sweeper), no new packages, no Redis involvement.
- **Services:** new `EventsService`, `EventRepository`, `EventWriteResult`, `EventProfile`,
  `EventWriteGuard`, event validators, `EventsController`. **Modified M5 files:** `Expense.cs` +
  `Partials/Expense.cs`, `AppDbContext.cs`, `ExpenseWriteResult.cs`, `ExpenseRepository.cs`,
  `ShareRepository.cs`, `ExpensesService.cs`, `SharesService.cs`, `ExpensesController.cs`,
  `Mappings/ExpenseProfile.cs`, `Models/Expenses/ExpenseFilter.cs` /`ExpenseResponse.cs`
  /`ExpenseSummaryResponse.cs` /`CreateExpenseRequest.cs`, `Constants/ErrorCodes.cs`,
  `Exception/ErrorException.cs`. `AppController`, `ApiResult`, `AuditLogFactory`/snapshots, and the
  audit tables are **unchanged** (event membership isn't audited — OQ6).
- **UI:** none (API only).
- **Documentation:** this doc; `ErrorCodes` XML docs (9xxx); a note in `agent-dev-team.md`.

## Decision Log

### Decision
**User checkpoint 2026-07-14 — all 16 Open Questions resolved; every recommended option (a) accepted.**

1. **Date range (OQ1a):** two `DateTime` columns, **whole-day inclusive, UTC-day** — `start_date`→
   `00:00:00`, `end_date`→`23:59:59.999999` (UTC); validate `start_date <= expense_time <= end_date`;
   DB CHECK `ck_events_date_range` (`end_date >= start_date`) — the codebase's **second** CHECK. *Reason:*
   matches the trip/month domain and spares the user picking times; the UTC-day-boundary caveat is
   accepted + documented (Assumptions/Future Improvements); consistent with M5's raw-UTC-datetime filter.
2. **`event_id` on-delete (OQ2a):** nullable FK with DB-level `OnDelete(SetNull)` — deleting an event
   nulls its expenses (loose), never cascade-deletes. *Reason:* the DB enforces it for tracked and
   untracked rows; event membership isn't audited so nothing needs app-level recording.
3. **Event deletion (OQ3a):** hard delete (not `IEntityDeletable`); allowed **only while OPEN**.
   *Reason:* mirrors the M5 expense hard-delete; events aren't in §4.7's soft-delete list; delete is
   pre-close, before the event is a finalized record.
4. **Assign/remove endpoints (OQ4a):** expense-side sub-routes `PUT /expenses/{uuid}/event {eventUuid}`
   + `DELETE /expenses/{uuid}/event`. *Reason:* mirrors `/settled` and `/shares`; concentrates the guard
   + within-range check; leaves the M5 full-replace `PUT /expenses/{uuid}` untouched.
5. **Create-into-event (OQ5a):** optional `eventUuid` on `POST /expenses` (owned + OPEN + within range,
   else 9000/9001/9002). *Reason:* matches "log this into the current trip"; one optional field.
6. **Assign/remove not audited (OQ6a):** `event_id` stays out of the `ExpenseAuditSnapshot`; assign/
   remove writes no audit row. *Reason:* grouping metadata, not số liệu (mirrors the settled exclusion,
   M5 OQ11); keeps M5's snapshot + no-op detection untouched.
7. **Range-edit conflict (OQ7a):** block the edit → `EventRangeExcludesAssignedExpenses` (9003) when it
   would orphan an assigned expense. *Reason:* keeps "every assigned expense is within range" always
   true.
8. **Close preconditions (OQ8a):** none — always allowed while OPEN, even empty. *Reason:* matches "chủ
   sổ phải chủ động chốt" with no gating; the spec never requires ≥1 expense.
9. **Fields/status (OQ9a):** name max 200, description optional max 1000, range per OQ1, `is_closed`
   bool + nullable `closed_at`, derived `expenseCount`. *Reason:* mirrors the `is_settled`/`settled_at`
   pattern and records "when closed" cheaply; only two one-way states, so no enum.
10. **List sort/filter (OQ10a):** `start_date` DESC then `created_at` DESC; optional `?closed=` filter;
    no pagination. *Reason:* consistent with shipped lists; start_date DESC surfaces the current trip.
11. **Endpoint surface (OQ11a):** six event routes (list/get/create/update/delete/`PUT /{uuid}/close`
    one-way) + two expense-side assign/remove routes; re-close → 9001. *Reason:* confirms shapes; nested
    routes preserve resource-owned scoping; one-way close is a distinct route from edit.
12. **Error block (OQ12a):** 9xxx = 9000 EventNotFound (404) / 9001 EventClosed (400) / 9002
    ExpenseTimeOutOfEventRange (400) / 9003 EventRangeExcludesAssignedExpenses (400); end<start on input
    stays 1001 validation; extend `GetDefaultHttpStatus`. *Reason:* one 1000-block per feature area,
    consistent with 2xxx–7xxx; distinct codes let clients tell the cases apart.
13. **Closed-event guard (OQ13a):** a shared repository-layer `EventWriteGuard` inside each M5 write
    transaction (update/delete expense, add/edit/delete share, assign/remove); `SetSettledAsync`
    exempt. *Reason:* keeps the "transaction in the repository" convention; check + write share one
    transaction so a close racing a write can't slip through.
14. **Expense DTO event info + filter (OQ14a):** add `eventUuid`/`eventName`/`eventIsClosed` to both
    expense DTOs and `eventUuid` + `looseOnly` to `ExpenseFilter`. *Reason:* makes membership visible
    inline and activates M5's deferred event filter against the real `event_id`.
15. **Event detail (OQ15a):** fields + derived `expenseCount`, no embedded expense list; expenses listed
    via `GET /expenses?eventUuid=…`. *Reason:* keeps the event endpoint lean and reuses the expense-list
    projection.
16. **Move semantics (OQ16a):** `PUT /expenses/{uuid}/event` allows a direct reassign A→B when both
    current and target events are OPEN and expense_time is within the target range; source CLOSED → 9001.
    *Reason:* one call to move; the "can't move out of a closed event" rule falls straight out of §4.4.

### Reason
User answers at the Milestone-6 planning checkpoint (2026-07-14), brought by the orchestrator per the
Clarification-First protocol; recorded verbatim so the api-implementer needs no other source.

### Alternatives Considered
The full option sets (b)/(c) with trade-offs, as presented to the user, are preserved in the struck
Open Questions above.

### Decision (inherited — NOT reopened)
Spec §5 locks (event not reopenable; delete-event/remove-expense only while open; the system never
auto-closes an event — no end-date sweeper; settled is the sole write allowed on a closed-event
expense; domain terms expense/share/event/wallet/settled/Premium-Free); resource-owned 404 scoping
(§4.1); §4.2 same-owner link integrity; money `decimal(18,2)` + DB CHECK (§4.3); soft-delete via
`IEntityDeletable`/`is_deleted` for members/categories/tags only with inviolable history (§4.7/§4.8);
expenses/shares hard-deleted with derived total; UTC timestamps; entity/repo/controller conventions;
EF-migration-only schema with the pinned design-time factory; writes through `ExecuteTransactionAsync`
+ `NoCommit()` (non-nesting); the M5 OQ8 deferral that assigns the `event_id` column, the within-range
validation, the closed-event write block, and the list-by-event filter to this milestone; the M5
dedicated `SetSettledAsync` seam reused as the closed-event settled exception; the M3 `MemberRepository`,
M4 `CategoryRepository`/`TagRepository`, and M5 `ExpenseRepository`/`ShareRepository`/`AuditLogFactory`
reused/extended, not replaced.

## Progress Log

### 2026-07-14

- Feature-planner: required reading completed — `The-ideal.md` §2, §3.5, §3.6, §4.1/§4.2/§4.4, §5
  locks (event not reopenable; delete/remove only while open; system never auto-closes; settled is the
  sole closed-event write), plus §3.7/§3.9/§3.10 read only for the M7/M8/M9 boundary; `CLAUDE.md`
  (Critical conventions — Event lifecycle) + `.agents/rules/rules.md` (Domain Safety Rules — event
  lifecycle) + `.claude/rules/rule.md` (template); `planning/expenses-shares-audit.md` (M5 — the
  deferred `event_id` seam OQ8, the dedicated `SetSettledAsync` seam, the create/update/delete/share
  flows, the typed `ExpenseWriteResult` pattern, the audit model); `planning/members.md` +
  `planning/categories-and-tags.md` (exemplars — structure, OQ format, Decision Log, test-list
  breakdown, `[Collection("AuthIntegration")]` harness); `planning/agent-dev-team.md` (M6 line +
  protocol); and the live code: `Expense.cs`/`Partials/Expense.cs`, `ExpenseRepository.cs`,
  `ShareRepository.cs`, `ExpensesService.cs`, `SharesService.cs`, `ExpensesController.cs`,
  `ExpenseWriteResult.cs`, `AuditLogFactory.cs`/`AuditSnapshots.cs`, `ExpenseProfile.cs` + the expense
  DTOs (`ExpenseResponse`/`ExpenseSummaryResponse`/`ExpenseFilter`/`CreateExpenseRequest`),
  `ErrorCodes.cs`, `Exception/ErrorException.cs`, `AppDbContext.cs`, `CategoryRepository.cs` +
  `Partials/Category.cs` (repo/entity patterns), `CategoriesController.cs`.
- Drafted this plan: the `events` entity + `AddEvents` migration (new table + `ALTER expenses` adding
  the nullable `event_id` FK with `ON DELETE SET NULL`, plus the codebase's second CHECK
  `ck_events_date_range`); six `api/v1/events` endpoints + two expense-side assign/remove routes; the
  one-way close; the closed-event write guard woven into every M5 write path (`ExpenseRepository`
  update/delete/assign/remove + `ShareRepository` add/update/delete) with `SetSettledAsync` left as the
  sole exception; the 9xxx error block; `EventRepository`/`EventsService`/`EventWriteGuard`/`EventProfile`/
  validators/DTOs/controller; the expense DTO + filter additions; and the full test list incl. the
  closed-event write-block regressions.
- **16 Open Questions raised** (date-range granularity + timezone; `event_id` on-delete; hard-vs-soft
  event delete; assign/remove endpoint shape; create-into-event; audit-on-assign; range-edit-excludes-
  assigned; close preconditions; event fields/status representation; list sort/filter; endpoint-surface
  confirmation; 9xxx error block; closed-event guard mechanics/placement; expense DTO event info +
  activating the M5 filter; event-detail embeds expenses?; move semantics) — awaiting user answers at
  the checkpoint before implementation starts.

### 2026-07-14 (checkpoint — all Open Questions answered, plan unblocked)

- **User answered all 16 Open Questions; every recommended option (a) accepted.** See the consolidated
  Decision Log entry: OQ1 two `DateTime` whole-day UTC range + CHECK `ck_events_date_range` (2nd CHECK),
  UTC-day caveat accepted; OQ2 `event_id` FK `OnDelete(SetNull)`; OQ3 events hard-deleted, delete only
  while OPEN; OQ4 expense-side assign/remove sub-routes; OQ5 optional `eventUuid` on `POST /expenses`;
  OQ6 assign/remove not audited; OQ7 range-edit conflict → 9003; OQ8 no close preconditions; OQ9 name
  200 / description 1000 / `is_closed`+`closed_at` / `expenseCount`; OQ10 sort start_date DESC then
  created_at DESC, `?closed=` filter, no pagination; OQ11 six event routes + two assign/remove routes,
  re-close → 9001; OQ12 9000/9001/9002/9003, end<start stays 1001; OQ13 shared repo-layer
  `EventWriteGuard`, `SetSettledAsync` exempt; OQ14 `eventUuid`/`eventName`/`eventIsClosed` on both
  expense DTOs + `eventUuid`/`looseOnly` filter; OQ15 event detail = fields + `expenseCount` (no
  embedded list); OQ16 direct reassign A→B, source-closed → 9001.
- Plan synchronized with the answers: Open Questions struck + annotated with the binding answers;
  Assumptions promoted to confirmed (incl. the accepted UTC-day-boundary limitation); Step 6 now spells
  out the three explicit invariants (the `EventWriteGuard` placement in every M5 write path + the
  `SetSettledAsync` exemption; the within-range re-validation on the expense_time-edit path; the
  owned + OPEN + within-range validation on assign/create-into) and firms up `RemoveEventAsync`
  (no-event → idempotent no-op); the entity/schema section, M5-files-modified list, 9xxx error-code
  table, endpoint tables, and test list already match the recommendations; Decision Log recorded (16
  numbered points + Reason + Alternatives-Considered + inherited-decisions block). **No open questions
  remain — implementation can start.**

### 2026-07-14 (implementation — api-implementer)

Implemented Milestone 6 strictly to the Decision Log (all 16 OQs = option (a)). Steps 1–9 + 11
delivered; Step 10 (tests) is the test-engineer's.

- **Entity + schema (Step 1):** new `Database/Entities/Event.cs` + `Partials/Event.cs` (`IEntity`,
  NOT `IEntityDeletable`; `NameMaxLength=200`, `DescriptionMaxLength=1000`; table `events`, unique
  `uuid`, `(user_id)` + composite `(user_id, start_date)` indexes, `is_closed` default false,
  `closed_at`, computed `updated_at`, FK user cascade, **CHECK `ck_events_date_range`
  `end_date >= start_date`** — the codebase's 2nd CHECK). **[M5-MOD]** `Expense.cs` gained
  `ulong? EventId` + `Event? Event`; `Partials/Expense.cs` maps `event_id` (indexed) + FK
  `WithMany(Expenses).OnDelete(SetNull)`. `AppDbContext.cs` gained `DbSet<Event> Events` +
  `Event.ConfigureModel`.
- **Migration (Step 2):** `20260714074607_AddEvents` — creates `events` (CHECK, indexes, FK cascade)
  and ALTERs `expenses` (adds `event_id` + `IX_expenses_event_id` + FK SetNull). Snapshot in sync
  (verified `ck_events_date_range`, `EventId`, `OnDelete(SetNull)`). **Applied to the dev DB**
  (`database update`, MariaDB 11.7.2).
- **Errors (Step 3):** **[M5-MOD]** appended 9xxx to `ErrorCodes` (9000 EventNotFound / 9001
  EventClosed / 9002 ExpenseTimeOutOfEventRange / 9003 EventRangeExcludesAssignedExpenses) and
  extended `ErrorException.GetDefaultHttpStatus` (9000→404, 9001/9002/9003→400). Context 9001 messages
  per call-site (edit-closed / delete-closed / remove-from-closed).
- **Write results (Step 4):** new `EventWriteResult.cs` (`EventWriteStatus` +
  `EventWriteResult<T>` + `CreateEventData`/`UpdateEventData`). **[M5-MOD]** `ExpenseWriteResult.cs`
  gained `EventNotFound`/`EventClosed`/`ExpenseTimeOutOfEventRange` + optional `EventUuid` on
  `CreateExpenseData`.
- **Repositories (Steps 5–6):** new `EventRepository` (list sort start_date DESC then created_at DESC +
  `?closed`; get; create w/ range normalization `start→00:00:00`, `end→23:59:59.999999` via
  `AddTicks(-10)` to stay in `datetime(6)`; update w/ closed-guard + OQ7 range-exclusion check; one-way
  close; OPEN-only hard delete). New static `EventWriteGuard.IsCurrentEventClosed`. **[M5-MOD]**
  `ExpenseRepository`: create-into-event; `UpdateGeneralInfoAsync` + `DeleteAsync` now Include `Event`
  and run the closed guard (+ update re-checks the new expense_time against an open event's range);
  `SetSettledAsync` left un-guarded (the sole §4.4 exception); new `AssignEventAsync` (source-closed
  9001, target owned/open/in-range) + `RemoveEventAsync` (idempotent no-op when loose, closed 9001);
  list gains `eventUuid`/`looseOnly` + Include `Event`; get Includes `Event`. **[M5-MOD]**
  `ShareRepository`: `FindOwnedExpenseAsync` Includes `Event`; guard woven into
  `AddAsync`/`UpdateAsync`/`DeleteAsync`.
- **Services + mappings (Step 7):** new `EventsService` + `EventProfile` (expenseCount from
  `Expenses.Count`; create/update re-load via `GetByUuidAsync` for an accurate count). **[M5-MOD]**
  `ExpensesService`: `ThrowIfFailed` maps 9000/9001/9002; `DeleteAsync` now routes through
  `ThrowIfFailed` (fixes a bug found in smoke where a closed-event delete returned 6000 instead of
  9001); new `AssignEventAsync`/`RemoveEventAsync`; create threads `EventUuid`; ctor gained
  `IValidator<AssignEventRequest>`. **[M5-MOD]** `ExpenseProfile` maps
  `eventUuid`/`eventName`/`eventIsClosed`. **[M5-MOD]** `SharesService` add/update/delete map
  `EventClosed`→9001.
- **DTOs + validators (Step 8):** new `Models/Events/*` (Create/Update requests, Event(+Summary)
  Response, EventFilter) + `Models/Expenses/AssignEventRequest`. **[M5-MOD]** `ExpenseFilter`
  (+`EventUuid`/`LooseOnly`, dropped the "M6" TODO), `ExpenseResponse`/`ExpenseSummaryResponse`
  (+event fields), `CreateExpenseRequest` (+`EventUuid`). New validators
  `Create/UpdateEventRequestValidator` (name/description/dates + `EndDate.Date >= StartDate.Date`) and
  `AssignEventRequestValidator`.
- **Controllers (Step 9):** new `EventsController` (6 routes). **[M5-MOD]** `ExpensesController` gained
  `PUT/DELETE /expenses/{uuid}/event`.
- **Verification (Step 11):** `dotnet build` of `FairShareMonApi.csproj` is clean. Live smoke (39/39)
  covered create; create-into-event within/out-of-range (9002); assign/move/remove (+ idempotent
  remove); range-edit-excludes-assigned (9003) + safe edit; expense_time-edit re-validation (9002);
  one-way close + re-close 9001; the full closed-event write block (update/delete expense,
  add/update/delete share, move-out, remove → 9001) with the **settled toggle EXEMPT (200)**; delete
  closed 9001; delete OPEN event → its expense goes loose (survives, eventUuid null); resource-owned
  404/9000 for another user + anonymous 401; list sort + `?closed`; expense `?eventUuid`/`?looseOnly`;
  `endDate<startDate` create → 1001. Smoke rows cleaned from the dev DB (ordered child deletes +
  audit_logs sweep; 0 remaining).

**In-latitude choices (doc left the placement to the implementer):** range normalization lives in
`EventRepository` (one place, per Step 7); `EventWriteGuard` is a sync check on the Included `Event`
nav (the doc's "or Include it" option) rather than a per-call DB round-trip; `create`/`update` re-load
the event for the response so `expenseCount` is accurate.

**Deviation / bug fixed:** none from the doc. One pre-existing-shaped bug surfaced by smoke and fixed
within M6 scope: `ExpensesService.DeleteAsync` mapped every non-success to 6000; it now routes through
`ThrowIfFailed`, so a closed-event delete correctly returns 9001 (the M5 delete still returns 6000 on a
genuine miss via the same helper's default).

**Tests (Step 10 — NOT done by api-implementer, reported per protocol):** the `FairShareMonApi.Tests`
project no longer compiles because of two mechanical consequences of the spec-mandated
`IExpenseRepository`/`ExpensesService` extension — (1) `ExpensesServiceTests.FakeExpenseRepository`
must implement the new `AssignEventAsync`/`RemoveEventAsync` members, and (2) its `CreateService()`
must pass the new `IValidator<AssignEventRequest>` ctor arg. Per the "do not edit tests" rule these are
left for the test-engineer; consequently `dotnet test` could not be run to reconfirm 487/487 (the
production build is clean and the 487 M5 tests were not otherwise touched).

### 2026-07-14 (tests — test-engineer)

Wrote and ran the full Milestone-6 test suite (Step 10). **`dotnet test .\FairShareMonApi.sln` →
606 passed / 0 failed / 0 skipped**, run twice (deterministic); the DB was reachable so no test
skipped. Post-run DB sweep confirmed **0 leftover rows** in users/events/expenses/shares/expense_tags/
audit_logs/members/categories/tags.

- **Harness-plumbing fixes (test project only — the 2 mechanical breaks the implementer flagged):**
  `ExpensesServiceTests.FakeExpenseRepository` now implements `AssignEventAsync`/`RemoveEventAsync`
  (mirroring the other fake write methods), and `CreateService()` passes the new
  `IValidator<AssignEventRequest>` (`AssignEventRequestValidator`). No production code touched.
- **Shared harness extended (test project only):** `ExpenseDbTestBase` gained `CreateEventRepository()`,
  `SeedEventAsync(...)`, `ReloadEventAsync(...)`, and an explicit **events sweep** on dispose (events
  are hard-deleted and the `user_id` FK cascades on user delete, but the sweep guarantees no row
  survives). `ExpenseApiTestBase` gained `CreateEventAsync`/`CreateEventUuidAsync`/`CloseEventAsync`
  HTTP helpers and the same events sweep.
- **Unit (no DB):** `EventValidatorsTests` (21) — Create/Update event validators: name required
  (whitespace rejected) + max 200, description optional + max 1000, dates required, `EndDate>=StartDate`
  → pinned Vietnamese messages; plus `AssignEventRequestValidator` (eventUuid required).
  `EventsServiceTests` (18, fake `IEventRepository`) — create maps success + derived expenseCount;
  get/update/close/delete miss → 9000; update/close/delete on closed → 9001; range-conflict → 9003.
  `ExpensesServiceTests` extended (+8 methods, 2 theories) — assign maps 9000/9001/9002/6000, remove
  maps 9001/6000, create-into-event threads `eventUuid`, empty assign UUID → ValidationException.
  `SharesServiceTests` extended — add/update/delete on a closed-event expense → 9001.
- **Integration (real MariaDB, `[SkippableFact]`):** `EventRepositoryTests` (20) — create + whole-day
  UTC range normalization + is_closed=false; resource-owned get/list/update/close/delete (null/status,
  never the row); start_date DESC sort + `?closed` filter + derived expenseCount; one-way close
  (re-close → 9001); OPEN-only hard delete with expenses going loose (`event_id`→null, survive); closed
  delete → 9001; OQ7 range-edit-excludes-assigned → 9003 + safe edit; the DB CHECK
  `ck_events_date_range`. `ExpenseEventAssignmentTests` (27) — create-into-event (in-range/closed/
  out-of-range/foreign); whole-day-inclusive UTC **boundary** (exact start-of-day & end-of-day in range,
  just-before/just-after out → 9002); assign/move/remove (idempotent loose remove, unknown/foreign
  target → 9000, closed target → 9001, move A→B); expense_time-edit re-validation → 9002 + in-range
  success; the **closed-event write block on EVERY guarded path** (UpdateGeneralInfo, Delete, share
  Add/Update/Delete, AssignEvent move-out, RemoveEvent → 9001, data unchanged) with **`SetSettledAsync`
  succeeding** (sole exception); assign/move/remove write **no audit** (OQ6).
- **Endpoint (WebApplicationFactory, real HTTP, `[SkippableFact]`):** `EventsEndpointTests` (11) — the
  six event routes; GET returns fields + expenseCount, no embedded expense list (OQ15); list sort +
  `?closed`; one-way close blocks update/delete/re-close → 400 (9001) while staying closed; OPEN delete
  → subsequent 404; another user's event → 404 (9000) on every route, never 403; anonymous → 401;
  `endDate<startDate` → 400 (1001) with camelCase `error.fields`. `ExpenseEventEndpointTests` (6) —
  assign sets inline `eventUuid`/`eventName`, `?eventUuid=`/`?looseOnly=` filters, remove → loose;
  create-into-event; assign out-of-range → 400 (9002); another user's event on assign → 404 (9000);
  anonymous → 401; **closed event blocks all M5 expense/share write routes → 400 (9001) while
  `PUT /settled` still succeeds**.
- **Cleanup strategy:** unchanged unique-lowercase-prefix-per-class + dispose-time cascade delete;
  expenses deleted first (RESTRICT FKs), then the new events sweep, then the audit_logs no-FK sweep,
  then the base user-cascade. audit_logs swept explicitly by actor (no FK on entity_uuid/expense_uuid).
- **Production bugs found:** none. The two initial red tests were my own fixture mistakes (passing
  `default` to a nullable `DateTime?` helper param coalesced back to a valid date), corrected to
  `DateTime.MinValue`; not production issues. All 9xxx mappings, the closed-event guard on every path,
  the settled exemption, the range boundaries, and the OQ7/OQ6 rules behave exactly as specified.
  **No production code was modified.**

### 2026-07-14 (post-review — Nit 1 fix, api-implementer)

Code review: **APPROVE, 0 blocking.** Applied the one trivial fix requested before commit:

- **Nit 1 (fixed):** `ExpensesService.CreateAsync` now trims the optional `EventUuid`
  (`request.EventUuid?.Trim()`), matching `AssignEventAsync`'s `request.EventUuid.Trim()`. Previously a
  `POST /expenses` with a whitespace-padded `eventUuid` would miss the event lookup and return 9000
  instead of resolving it; the null-safe trim keeps it optional (empty/whitespace → loose, unchanged).
  One-line change; no other code touched.
- **Nit 2 (recorded, not fixed — non-blocking scaling nit):** see Future Improvements below.

Post-fix: `dotnet build .\FairShareMonApi.sln` clean; `dotnet test` from repo root → **606 passed / 0
failed / 0 skipped** (no regression).

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

- **Code-reviewer verdict: APPROVE, 0 blocking findings** (2 nits: Nit 1 — trim `EventUuid` on
  create-into-event — fixed by the implementer; Nit 2 — `ListByUserAsync` count-via-`Include` —
  recorded as a Future Improvement). All 16 accepted decisions and the in-latitude choices verified
  against the code; no silent deviations, no Open Questions reopened.
- **Verified checks:**
  - **§4.4 closed-event write block is airtight:** `EventWriteGuard.IsCurrentEventClosed` fires
    **before any mutation, inside the write transaction**, on **all 7 M5 write paths** —
    `ExpenseRepository.UpdateGeneralInfoAsync` / `DeleteAsync` / `AssignEventAsync` / `RemoveEventAsync`
    and `ShareRepository.AddAsync` / `UpdateAsync` / `DeleteAsync`. `SetSettledAsync` is the **only**
    exempt path (the sole §4.4 exception). **No null-nav false-negative** — every guarded path
    `Include`s the `Event` nav before the check, so a loose expense reads as "not closed" correctly and
    an assigned-to-closed expense is always caught.
  - **One-way close:** re-closing a closed event → 9001; no reopen path; **no auto-close hosted
    service / end-date sweeper** (§5 — the system never auto-closes).
  - **Within-range validation:** create-into-event, assign, and the expense_time-edit path all validate
    `start_date <= expense_time <= end_date` → 9002, with correct **whole-day-inclusive UTC** boundary
    math (start→`00:00:00`, end→`23:59:59.999999`, inclusive); range-edit-excludes-assigned → 9003.
  - **Resource-owned scoping (§4.1):** 9000/404 on every event route (never 403); **§4.2 link
    integrity** via server-side `expense.UserId` resolution (never trusting client-supplied UUIDs for
    ownership).
  - **`event_id` `OnDelete(SetNull)`** makes an OPEN event's expenses loose on delete (they survive,
    `eventUuid` null); hard delete blocked while closed (9001).
  - **Atomicity:** every write is one `ExecuteTransactionAsync` with `NoCommit()` on failure; no stray
    `SaveChanges`.
  - **Audit untouched (OQ6):** assign/remove stage no audit rows; the `ExpenseAuditSnapshot` signature
    is unchanged, so M5's no-op detection is intact.
  - **M5 regression surface is additive-only:** `ExpensesService.DeleteAsync` via `ThrowIfFailed`
    still returns 6000 for a genuine miss / 9001 for a closed-event delete; the new filters are a no-op
    when unset; `ExpenseProfile`'s event fields are null-safe for loose expenses.
  - **Migration + snapshot in sync:** `events` table (CHECK `ck_events_date_range`, unique `uuid`,
    `(user_id, start_date)` index, FK cascade) + `expenses` ALTER (`event_id` + `IX_expenses_event_id`
    + FK SetNull).
  - **Conventions:** `Uuid.NewV7`, snake_case columns, primary constructors, Vietnamese user-facing
    strings/Swagger; `AppController` and `AppDbContext.partial.cs` untouched.
- **1 informational note (recorded, not a defect):** the standalone `(user_id)` index alongside the
  composite `(user_id, start_date)` is redundant but **intentional** — it mirrors the `Expense`
  mapping's shape for consistency.
- **UTC-day boundary** is the known, accepted OQ1 limitation (documented in Assumptions + Future
  Improvements), not a defect.
- **Final green state:** `dotnet build` clean; `dotnet test` = **606 passed / 0 failed / 0 skipped**,
  deterministic across runs, DB swept clean (0 leftover rows). Milestone 6 complete.

## Final Outcome

Milestone 6 (Events) is **implemented, fully tested, and code-reviewed (APPROVE, 0 blocking)** per the
approved plan and all 16 accepted decisions. New surface: the `events` table + `Event` entity,
`EventRepository`/`EventsService`/`EventProfile`/`EventWriteGuard`, event validators, DTOs, and
`EventsController` (6 routes — list/get/create/update/delete/**one-way `PUT /{uuid}/close`**), plus the
two expense-side assign/remove routes (`PUT`/`DELETE /expenses/{uuid}/event`). Migration `AddEvents`
authored and applied to the dev DB — creates `events` (incl. the codebase's **second CHECK**
`ck_events_date_range` (`end_date >= start_date`), unique `uuid`, `(user_id, start_date)` index, FK
cascade) and ALTERs `expenses` (adds `event_id` + `IX_expenses_event_id` + FK **`OnDelete(SetNull)`**).
This **closes the M5 OQ8 event-seam deferral** (adds the `event_id` FK, the within-range validation, the
closed-event write block, and the list-by-event filter). The M5 expense/share stack was extended with:
the **§4.4 closed-event write block** (`EventWriteGuard` on all 7 write paths, with `SetSettledAsync`
the sole exempt path); the **whole-day-inclusive UTC** within-range validation (create-into / assign /
expense_time-edit → 9002; range-edit-excludes-assigned → 9003); create-into-event; the assign/remove
routes with **direct A→B move** (source-closed → 9001); the `eventUuid`/`looseOnly` filters + the
`eventUuid`/`eventName`/`eventIsClosed` expense-DTO fields; and the 9xxx error block. **Close is
one-way** (re-close → 9001; no reopen; no auto-close job — §5). Events are **hard-deleted** (OPEN-only;
deleting an event leaves its expenses **loose**, `event_id` → null; closed delete → 9001). **Event
membership is not audited** (OQ6 — assign/remove stage no audit rows; the M5 snapshot + no-op detection
are untouched). Production build clean; live smoke 39/39; test-engineer added the full M6 suite with 0
production bugs; the one trivial code-review fix (Nit 1 — trim `EventUuid` on create-into-event) applied
(Nit 2 recorded as a Future Improvement). Full suite green: `dotnet test` → **606 passed / 0 failed / 0
skipped**, deterministic across runs, DB swept clean. The M7 (debt balance + stats), M8 (export), and M9
(QR) seams remain — they consume the `events` table + `is_closed`/`closed_at` timeline built here. No
open questions remain; no unrecorded deviations.

## Future Improvements

- **Per-user timezone** so the event "day" range and `expense_time` comparison respect the owner's
  local day rather than UTC (resolves the OQ1 timezone caveat).
- **Reopen a closed event** (would require relaxing the §5 one-way lock — spec change, not a planning
  decision).
- **Event templates / recurring events** (e.g. a monthly event auto-created) — noting the spec
  forbids *auto-close*, not auto-*create*.
- **Audit expansion to events** (§6 future item): create/close/edit/delete an event and assign/remove
  membership, if event history becomes disputable.
- **Bulk assign** several loose expenses into an event in one call.
- **`EventRepository.ListByUserAsync` expenseCount projection (review Nit 2, non-blocking).** The list
  currently `.Include(evt => evt.Expenses)` to compute the derived `ExpenseCount`, materializing full
  expense rows just to count them; a `.Select`/correlated-count projection (`Expenses.Count` without
  loading the rows) would avoid that — matching the plan's stated approach. Volume is tier-capped in
  M10, so this is a scaling nit only; mirrors the M5 summary-list Future-Improvement note.
- **DB-level generated-column guards** if the app-level closed-event invariant ever needs hardening
  (parallels the M3/M5 "app-level invariant" future notes).
