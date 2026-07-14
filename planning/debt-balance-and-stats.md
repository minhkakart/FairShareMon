# Debt Balance + Stats (Milestone 7: Cân bằng nợ + Thống kê)

Read-only aggregation over the shipped M5/M6 data: the **per-event debt balance** (cân bằng nợ, the
spec's "tính năng lõi" — §3.7) and the two **statistics** views (thống kê — §3.9: overview + by-category).
No new expenditure data is created; this milestone only reads and sums what M5 (expenses/shares) and M6
(events) already store. It also establishes the **DB-side `GROUP BY`/`SUM`/`COUNT` aggregation pattern**
that the recurring M5/M6 Future Improvement asked for (both prior docs recorded "DB-side SUM/COUNT
projections instead of Include-then-count-in-memory").

## Objective

Implement `The-ideal.md` §3.7 (Cân bằng nợ) and §3.9 (Thống kê) on top of the shipped Auth + Members +
Categories + Tags + Expenses/Shares/Audit + Events stack:

- **Per-event debt balance (§3.7):** for a given event, compute each participating member's `đã ứng`
  (advanced), `phải gánh` (owed), and `cân bằng` (balance = advanced − owed). Positive balance → others
  owe this member; negative → this member owes. **The sum of all members' balances in an event is always
  0** (holds by construction — see OQ1). Loose expenses (no event) do NOT participate. Read-only.
- **Overview stats (§3.9):** total spending over a time range, for the owner's whole ledger (loose +
  event expenses together). Balance is per-event only — overview does **not** aggregate balance across
  events (§3.9 "không tính gộp xuyên đợt").
- **By-category stats (§3.9):** per-category total spend + expense count (with the category's color/icon)
  over a time range OR within a single event — for pie/bar charts.
- **Resource-owned (§4.1):** every balance/stats query is scoped to the current user; an event/member/
  category miss must look exactly like non-existence (404, reusing `9000 EventNotFound`; never 403). No
  cross-user leakage.
- **Almost certainly no new tables / no migration** — pure aggregation over M5/M6 rows (see Impact
  Analysis; confirmed as an explicit non-change).

This milestone owns a new **`StatsController`** (+ the per-event balance route — placement is OQ10), a new
**`StatsService`**, a new read-only **`StatsRepository`** (DB-side aggregation), and the balance/stats
DTOs + a time-range validator. It reuses the M3 `members`, M4 `categories`, M5 `expenses`/`shares`, and M6
`events` tables and the established resource-owned / `ExecuteQueryAsync` / `BaseRepository.Query`
conventions. Export (§3.5 CSV) is M8, per-still-owing-member QR (§3.10) is M9 — read only for the
boundary.

## Background

- **M5 (`planning/expenses-shares-audit.md`)** shipped `expenses` + `shares` + `expense_tags` +
  `audit_logs`. Confirmed from the live code that M7 must respect:
  - **The expense total is DERIVED** — `SUM(shares.amount)`; there is **no `total`/`amount` column** on
    `expenses` (M5 OQ1a). `ExpenseProfile` computes `Total` as `src.Shares.Sum(share => share.Amount)`.
    So every "total" M7 needs is a sum over `shares`.
  - Money is **`decimal(18,2)`** with a DB CHECK `ck_shares_amount_non_negative` (`amount >= 0`); 0đ is
    valid. Never float.
  - Expenses/shares are **hard-deleted** (`Expense`/`Share` are `IEntity`, NOT `IEntityDeletable`), so a
    deleted expense genuinely no longer exists — nothing to filter out of aggregation.
  - The **owner-rep member always has a share** in every expense (0đ if not entered — §5 lock), so the
    owner-rep always participates in an event that has any expense.
  - `Member` and `Category` **are `IEntityDeletable`** (soft-deleted); `BaseRepository.Query<T>` excludes
    soft-deleted rows by default unless `includeDeleted: true`. Historical data must still display deleted
    members/categories (§4.7) — relevant to which members/categories appear in balance/stats (OQ3/OQ9).
  - `is_settled` (bool) + `settled_at` are payment metadata, not số liệu (§3.5) — relevant to OQ2.
- **M6 (`planning/events.md`)** shipped `events` + the nullable `expenses.event_id` FK (`ON DELETE SET
  NULL`; null = loose). Confirmed from the live code:
  - `Event` has `IsClosed`/`ClosedAt`; the date range is a **whole-day-inclusive UTC window**
    (`StartDate` at 00:00:00, `EndDate` at 23:59:59.999999). The M5/M6 list filters compare
    `expense_time` and `from`/`to` as **raw UTC datetimes, inclusive** — M7's stats time range must match
    that convention (OQ7).
  - `ExpenseRepository.ListByUserAsync` already Includes shares/category/payer/event and computes totals
    in memory via AutoMapper — the exact pattern the "DB-side projection" Future Improvement wants M7 to
    replace for aggregation.
  - The **UTC-day-boundary limitation** is an accepted, documented limitation (M6 OQ1) — M7 inherits it
    for its time-range filters; no new timezone handling is introduced.
- Conventions confirmed by reading the live code (identical across M2–M6):
  - Repositories: interface + sealed impl in one file, `[ScopedService(typeof(IX))]`, extend
    `BaseRepository`; reads via `ExecuteQueryAsync`; `Query<T>(tracking, includeDeleted)` applies
    `AsNoTracking` + soft-delete filtering. `ResolveUserIdAsync(db, userUuid, ct)` resolves the owner id.
  - Services: `[ScopedService(typeof(IX))]`, primary ctor injecting repos + `IMapper` + validators;
    map a resource-owned miss to an `ErrorException` (reuse `EventNotFound` 9000). Vietnamese messages.
  - Controllers derive from `AppController` (LOCKED); routes `api/v{version:apiVersion}/[controller]`;
    `[ResponseWrapped]` → `ApiResult<T>`; `AuthenticatedUser.Id` = current user's UUID; Vietnamese
    `[SwaggerOperation]`/`[SwaggerResponse]`.
  - Errors: `ErrorCodes` 1xxx infra, 2xxx auth, 3xxx members, 4xxx categories, 5xxx tags, 6xxx expenses,
    7xxx shares, 8xxx audit (reserved), 9xxx events. Next free block is **10xxx** (OQ13).
  - Validation: FluentValidation auto-registered by `AddValidatorsFromAssembly`; services call
    `ValidateAndThrowAsync` (→ 400 with `error.fields` camelCase).
  - `ExecuteQueryAsync` takes `Func<AppDbContext, CancellationToken, Task<TResult>>` and returns the
    result — a DB-side `GroupBy`/`Sum`/`Count` LINQ query fits cleanly inside it.
- **quick-ordering** has no reusable stats/reporting service to mirror (its matches for "stats/report"
  are incidental — billing, shift, docs). M7 designs its aggregation shape from the FairShareMon
  conventions above, not from a sibling exemplar.
- The dev DB holds no real product data beyond disposable smoke rows.

## Requirements

From `The-ideal.md` §2, §3.5 (money model), §3.6 (per-event scope), §3.7, §3.9, and cross-cutting
§4.1/§4.3/§4.7:

**Per-event debt balance (§3.7):**
- For a member M in event E: `advanced(M)` = total of expenses in E whose payer is M; `owed(M)` = sum of
  M's shares in E; `balance(M)` = `advanced(M) − owed(M)`.
- Because the expense total = SUM of its shares (M5 OQ1), `advanced(M)` = SUM of the amounts of all
  shares belonging to expenses in E paid by M (see OQ1 for the derivation + sum-to-zero proof).
- The balances of all members in E sum to exactly 0 (§3.7). Loose expenses are excluded — they carry no
  balance, only the per-expense settled flag (§3.5).
- Resource-owned: E must be owned by the current user (else 404 / `9000`). Read-only.

**Overview stats (§3.9):**
- Total spending over a time range for the owner's whole ledger (loose + event expenses together).
- Overview does NOT include per-member balance and does NOT aggregate balance across events (§3.9).

**By-category stats (§3.9):**
- Per-category total spend + expense count, with the category's color/icon, scoped to a time range OR to
  a single event — for pie/bar charts.

**Cross-cutting:**
- **Absolute privacy / resource-owned (§4.1):** every query scoped by the current user; an event/member/
  category miss looks like non-existence (404, never 403).
- **Money accuracy (§4.3):** all sums are `decimal` (no float); the DB `SUM` over `decimal(18,2)` returns
  `decimal`.
- **Soft-delete history (§4.7):** deleted members appear in a historical balance; deleted categories
  appear in historical stats — both still displaying their full info.
- **Conventions:** DB-side aggregation via `ExecuteQueryAsync`; `Async` suffix + `CancellationToken`;
  Vietnamese messages. No migration expected (confirm in OQ12).

## Open Questions

> **All 15 answered by the user at the 2026-07-14 checkpoint — every recommended option (a) was
> accepted.** The annotated questions below carry the binding answers inline; the full options/trade-offs
> are preserved for the record and mirrored in the Decision Log. No open questions remain — implementation
> can start. The Implementation Plan, endpoint table, DTO section, and test list below are already synced
> to these answers (option (a) was recommended throughout). Decisions locked in spec §5 and in the M5/M6
> planning docs (total = sum of shares; balance is per-event only; loose expenses excluded from balance;
> hard-delete of expenses/shares; UTC raw-datetime comparison; domain terms) were NOT reopened.

**OQ1 — Balance computation semantics + the sum-to-zero invariant.**
> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** `advanced(member)` = `SUM(share.amount)` over shares
> of the event's expenses grouped by `expense.payer_member_id`; `owed(member)` = `SUM(share.amount)`
> grouped by `share.member_id`; `balance = advanced − owed`. Sum-to-zero holds **by construction** (both
> aggregate the same single share-set once). **Verify via an integration test; no runtime assert.**
Confirm the derivation and whether to actively verify the invariant.
- **(a) [recommended]** `advanced(M)` = `SUM(share.amount)` over all shares whose `expense.event_id = E`
  **and** `expense.payer_member_id = M` (this equals "total of expenses in E paid by M" because
  total = sum of shares, M5 OQ1); `owed(M)` = `SUM(share.amount)` over all shares whose
  `expense.event_id = E` **and** `share.member_id = M`; `balance(M) = advanced(M) − owed(M)`. The
  sum-to-zero invariant holds **by construction**: `Σ advanced` = every share in E counted once (grouped
  by the expense's payer) = the event's grand total; `Σ owed` = every share in E counted once (grouped by
  the share's member) = the same grand total; so `Σ balance = 0`. Do **not** add a runtime assert to the
  production path (it can never fail given decimal exactness and non-negative amounts); instead **cover
  the invariant with an integration test** on real data. Trade-off: no defensive check in prod, but the
  invariant is guaranteed by the query shape and money is exact `decimal` (§4.3).
- **(b)** Same derivation but compute `advanced` from a separate per-expense total sub-aggregate
  (`SUM` over expenses' derived totals) rather than directly over shares grouped by payer. Trade-off:
  reads more literally like §3.7 ("tổng tiền các phiếu mà thành viên là người trả"), but needs a
  two-level aggregate (sum shares per expense, then sum per payer) where (a) collapses it to one
  `GROUP BY payer` over shares — same result, more query.
- **(c)** Compute in memory (load the event's expenses + shares, sum in C#) and add a
  `Debug.Assert(Σ balance == 0)`. Trade-off: easy to assert, but loads all rows and is exactly the
  in-memory pattern the M5/M6 Future Improvement asked M7 to move off (see OQ11).

**OQ2 — Does the settled flag affect the balance?**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** the balance **ignores `is_settled` entirely** —
> settled stays payment metadata (§3.5); a settled expense still contributes to advanced/owed.
`is_settled` is payment metadata, not số liệu (§3.5). Should a settled expense still count in the ledger
balance?
- **(a) [recommended] Balance ignores `is_settled` entirely.** The balance is the ledger truth (who
  advanced vs who bore); "settled" only records that a transfer happened outside the ledger. A settled
  expense still contributed to advanced/owed. Trade-off: the balance doesn't show "how much is still
  outstanding after partial settlements" — but §3.7 defines balance purely from advanced − owed with no
  mention of settled, and M9's per-still-owing-member QR is a separate concern.
- **(b) Balance excludes settled expenses** (only unsettled shares count). Trade-off: shows remaining
  debt, but contradicts §3.7's definition, and since settled is a whole-expense flag (not per-member,
  §6 future), excluding a fully-settled expense would silently drop it from both advanced and owed and
  the per-member picture would be misleading.
- **(c) Return BOTH a gross balance (ignore settled) AND a net-of-settled balance** per member.
  Trade-off: most information, but doubles the DTO and the aggregation, and no spec/UI requirement asks
  for the net figure at M7 (QR is M9).

**OQ3 — Which members appear in the balance (deleted members, zero-balance members)?**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** the balance includes **every participant** (payer of
> an expense in E or holder of a share in E) — the owner-rep even at 0đ and **soft-deleted members**
> (§4.7), each row flagged `isDeleted`; non-participants are omitted.
- **(a) [recommended] Every member who participated in the event** — i.e. is the payer of an expense in E
  OR holds a share in E — including the **owner-rep** (even at 0đ, since it always has a share) and
  **soft-deleted members** (§4.7 — deleted members still appear in historical data), each with its
  `isDeleted` flag. A participant whose advanced happens to equal owed appears with `balance = 0` (they
  participated). Members who never participated in E are omitted. Trade-off: the balance list may include
  a deleted member and a net-zero participant, but that is exactly the §3.7/§4.7 "historical report shows
  everyone who was in the event" semantics.
- **(b) Exclude members whose net balance is 0** (only show non-zero balances). Trade-off: leaner "who
  owes whom" list, but hides participants who netted out and makes the sum-to-zero harder to eyeball; the
  owner-rep at 0đ would vanish.
- **(c) Exclude soft-deleted members.** Trade-off: simpler membership query, but breaks §4.7 (a deleted
  member's historical debt would silently disappear and the balances would no longer sum to 0).

**OQ4 — Is the balance available for OPEN events, or only CLOSED?**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** the balance is available for **both OPEN and CLOSED**
> events (no lifecycle gate — §3.7 places none; only the M9 QR requires CLOSED).
- **(a) [recommended] Both OPEN and CLOSED.** The balance is meaningful while the trip is still running
  (the owner watches it accrue). §3.7 places no lifecycle gate on viewing balance; only the M9 event QR
  requires CLOSED (§3.10). Trade-off: none significant — an open event's balance simply changes as
  expenses are added.
- **(b) CLOSED only.** Trade-off: mirrors the QR-after-close gate, but §3.7 doesn't require it and it
  would block the common "how are we doing so far" use.

**OQ5 — Per-member net only, or also a pairwise "who pays whom" settlement suggestion?**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** **per-member net only** (advanced/owed/balance rows,
> §3.7); NO pairwise settlement plan — deferred (the M9 QR uses the per-owing-member net directly).
- **(a) [recommended] Per-member net balances only** (advanced/owed/balance rows, §3.7). Defer any
  pairwise debt-minimization ("Cường pays An 500k") to a future improvement. §3.7 defines only the
  per-member figures + sum-to-zero; M9's QR is per-still-owing-member (one QR per negative-balance member
  to the owner's account), not a min-transaction graph. Trade-off: the client must derive any
  who-pays-whom view itself, but M7 stays scoped to the spec and M9 already covers the QR direction.
- **(b) Also return a pairwise settlement plan** (a minimal set of transfers that zero the balances).
  Trade-off: nice UX, but it's an optimization problem not in §3.7, introduces a non-trivial algorithm +
  its own tests, and the "everyone pays the owner" model (M9 QR) may make a general graph unnecessary.

**OQ6 — Overview figures: which numbers does `/overview` return?**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** a **lean set — `totalSpending` + `expenseCount`**,
> echoing the `from`/`to` used; nothing else.
- **(a) [recommended] A lean set:** `totalSpending` (= `SUM(share.amount)` over all the owner's expenses
  in the range = sum of expense totals), `expenseCount` (distinct expenses in the range), and echo the
  `from`/`to` used. Nothing else. Trade-off: minimal, matches §3.9 ("tổng chi tiêu trong một khoảng thời
  gian") literally; if the UI later wants more, it's an additive change.
- **(b) Add `settledTotal` / `unsettledTotal`** (split totalSpending by `is_settled`). Trade-off: handy
  for a "how much is still unpaid" widget, but §3.9 doesn't ask for it and it pre-empts the M9 QR concern;
  and settled is a whole-expense flag so the split is coarse.
- **(c) Add `eventCount` / `looseExpenseTotal`** (distinct events touched, loose vs event split).
  Trade-off: more context for a dashboard, but beyond §3.9's stated overview and easy to add later.

**OQ7 — Overview time-range: required vs optional, inclusivity, UTC.**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** `from`/`to` **both optional** (omit = all-time),
> **inclusive `[from,to]`**, raw-UTC compare (matches M5/M6); `from > to` → **validation error (1001)**.
- **(a) [recommended]** `from`/`to` are **both optional**; when omitted the overview covers the owner's
  **entire ledger** (all time). The range is **inclusive `[from, to]`** and compared against
  `expense_time` as **raw UTC datetimes**, matching the M5/M6 list filter exactly. When both are present,
  a validator rejects `from > to` (`10001`/validation — see OQ13). Trade-off: an all-time default is
  convenient for a first dashboard load, and inclusivity/UTC stay consistent with the shipped filters
  (inheriting the accepted UTC-day-boundary limitation, M6 OQ1).
- **(b) Require both `from` and `to`.** Trade-off: forces the client to always pick a window (no
  accidental full-table scan), but breaks the "show me everything" default and diverges from the M5
  filter where `from`/`to` are optional.

**OQ8 — By-category scope: time-range vs event, mutually exclusive or combinable?**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** accept a **time-range OR `eventUuid`**; sending
> **both together is a validation error (1001)**; each alone is valid (event mode scopes to that owned
> event's expenses, miss → 404/`9000`).
§3.9 says by-category is "trong khoảng thời gian **hoặc** trong một đợt" (time range OR within an event).
- **(a) [recommended] Mutually exclusive scope, event wins if both are sent — but validate against
  ambiguity:** accept `from`/`to` (time-range mode) OR `eventUuid` (event mode); if `eventUuid` is
  present, ignore `from`/`to` and scope to that event's expenses (event must be owned, else 404/`9000`);
  if `eventUuid` is absent, use the (optional, inclusive-UTC) time range exactly as overview does. Send
  both → reject as a validation error to avoid a silent-precedence surprise. Trade-off: one validation
  rule, but matches the spec's "OR" and never silently drops a filter.
- **(b) Two separate endpoints** — `/stats/by-category?from&to` and `/stats/events/{uuid}/by-category`.
  Trade-off: unambiguous by construction, but two routes for one chart and a second controller shape.
- **(c) Allow both simultaneously** (event AND time range intersected). Trade-off: more flexible, but the
  spec says OR and an event already bounds its expenses by its own range, so intersecting is redundant.

**OQ9 — By-category: which categories appear + sort order.**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** only categories with **≥ 1 in-scope expense** appear,
> **including soft-deleted categories** that have historical expenses (§4.7), flagged `isDeleted`, each
> carrying its `color`/`icon`; sort **`total` DESC → `expenseCount` DESC → `name`**.
- **(a) [recommended]** Only categories that have **≥ 1 expense** in scope appear (non-empty), each with
  its `total` + `expenseCount` + `color`/`icon`, and this **includes soft-deleted categories** that have
  historical expenses in scope (§4.7), flagged `isDeleted`. Sort by **`total` DESC** (biggest slice
  first — natural for a pie/bar chart), tie-break `expenseCount` DESC then `name` A→Z. Trade-off: a
  zero-expense category is omitted (a pie chart has no zero slices anyway) and a deleted-but-used category
  is shown — both correct per §3.9/§4.7.
- **(b) Include all of the user's active categories, zero-expense ones at 0.** Trade-off: a stable full
  legend, but adds empty slices/bars and needs a separate "list all categories" join; the client can pad
  if it wants.
- **(c) Exclude soft-deleted categories.** Trade-off: simpler query, but breaks §4.7 (historical spend
  under a since-deleted category would vanish and the category totals would no longer sum to the
  overview total for the same range).

**OQ10 — Endpoint surface + placement (balance route + stats controller).**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a):** balance route `GET api/v1/events/{uuid}/balance` on
> the existing **`EventsController`**; overview + by-category on a new **`StatsController`**.
- **(a) [recommended]** Balance lives on the **event resource** as `GET api/v1/events/{uuid}/balance`
  (added to `EventsController`; it is event-scoped and reuses `EventNotFound` 9000). Stats live on a new
  **`StatsController`**: `GET api/v1/stats/overview?from&to` and
  `GET api/v1/stats/by-category?from&to&eventUuid`. Business logic in a new `IStatsService` (balance +
  both stats). Trade-off: balance sits with events (where the client already has the event uuid) while
  the two charts sit together under `stats`; one extra service method on the events controller path.
- **(b)** Put everything on `StatsController`: `GET /stats/events/{uuid}/balance`,
  `GET /stats/overview`, `GET /stats/by-category`. Trade-off: all read-aggregation under one controller,
  but the balance's 404 scope is the event, so it reads more naturally under `/events/{uuid}`; and it
  duplicates the event-uuid path segment.
- **(c)** A dedicated `BalanceController` (`GET /events/{uuid}/balance` semantics) separate from
  `StatsController`. Trade-off: clean separation of the "core feature" from charts, but two thin
  controllers where one service already unifies the logic.

**OQ11 — Aggregation implementation: DB-side `GROUP BY`/`SUM`/`COUNT` vs load-then-sum-in-memory.**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** **DB-side `GROUP BY`/`SUM`/`COUNT`** aggregation
> pushed into MariaDB via LINQ inside `ExecuteQueryAsync` — establishes the efficient server-side pattern
> the M5/M6 reviews flagged; no load-then-count-in-memory.
- **(a) [recommended] DB-side.** Push `GROUP BY` + `SUM(amount)` + `COUNT` into MariaDB via LINQ inside
  `ExecuteQueryAsync`, projecting straight into lightweight result records — never `Include` the whole
  object graph and sum in C#. This is exactly the recurring M5/M6 Future Improvement
  ("DB-side SUM/COUNT projections instead of Include-then-count-in-memory"), and M7 is where the pattern
  is established for the codebase. Query shapes are spelled out in the Implementation Plan (balance joins
  `shares → expense` and groups by `payer_member_id` and by `member_id`; overview sums `shares` over a
  time range; by-category groups `shares`/`expenses` by `category_id`). Trade-off: LINQ-to-SQL group
  projections need care (Pomelo translates `GroupBy(...).Select(g => g.Sum(...))` fine; the member/
  category display join is a second small query keyed by the grouped ids), but this is the scalable,
  convention-endorsed approach.
- **(b) Load-then-aggregate in memory.** Reuse `ExpenseRepository`-style Includes and sum with LINQ-to-
  objects. Trade-off: simplest to write and trivially correct, but loads every share/expense row for the
  scope and is precisely what the Future Improvement flagged; poor for large ledgers.

**OQ12 — New service/repo structure.**
> ~~**OQ12**~~ → **Answered 2026-07-14 (option a):** a new read-only **`IStatsRepository`** (DB-side
> aggregation) + a new **`IStatsService`** — not bolted onto the write-focused Expense/Event repos, and
> no parallel query abstraction (reuse `BaseRepository`/`ExecuteQueryAsync`).
- **(a) [recommended]** One new read-only `IStatsRepository` (`[ScopedService]`, extends `BaseRepository`)
  with three DB-side aggregation methods (`GetEventBalanceAsync`, `GetOverviewAsync`,
  `GetByCategoryAsync`) returning plain aggregate records; one new `IStatsService` orchestrating them,
  resolving resource-owned misses, and mapping to DTOs. Do **not** bolt aggregation onto
  `ExpenseRepository`/`EventRepository` (keeps their write-oriented surface focused) and do **not** add a
  parallel query abstraction (reuse `BaseRepository`/`ExecuteQueryAsync`). Trade-off: two new files, but a
  cohesive home for all read-aggregation and a clean seam for M8 export (which reuses the balance) and M9
  QR (which reuses the negative-balance rows).
- **(b)** Add aggregation methods to the existing `ExpenseRepository` + `EventRepository` and a thin
  `StatsService`. Trade-off: no new repo, but spreads aggregation across two write-focused repos and the
  balance needs both, muddying ownership.
- **(c)** Put aggregation directly in `StatsService` using `AppDbContext` via a repo passthrough.
  Trade-off: fewer files, but violates the Controllers → Services → Repositories → DbContext layering
  (services must not touch `DbContext`).

**OQ13 — Error codes for Stats.**
> ~~**OQ13**~~ → **Answered 2026-07-14 (option a):** **no new codes** — resource-owned miss reuses
> `EventNotFound` (9000), bad range = `ValidationFailed` (1001); **reserve the 10xxx Stats block** in
> `ErrorCodes.cs` with a comment (define nothing yet).
- **(a) [recommended] No new codes.** All three endpoints are reads; the only failure is a resource-owned
  miss on the event (balance / by-category?eventUuid) → reuse **`EventNotFound` 9000** (404). A bad time
  range (`from > to`) is a **validation** failure (`ValidationFailed` 1001, `error.fields`). Reserve the
  **10xxx** block for Stats in `ErrorCodes.cs` with a comment but define nothing yet. Trade-off: none;
  continues the one-block-per-feature reservation without inventing unused codes.
- **(b)** Claim `10000 StatsScopeInvalid` (400) for the "both `eventUuid` and `from`/`to` sent" case
  (OQ8) instead of a validation error. Trade-off: a distinct machine code for that case, but a validator
  message is simpler and consistent with how other bad-input cases are handled (1001).

**OQ14 — Balance DTO money + row fields; stats DTO shapes.**
> ~~**OQ14**~~ → **Answered 2026-07-14 (option a):** money serialized as **decimal**; a balance row carries
> `memberUuid`/`memberName`/`isOwnerRepresentative`/`isDeleted`/`advanced`/`owed`/`balance` (**denormalized
> names** so a deleted member still displays); the balance header echoes `eventUuid`/`eventName`/`isClosed`;
> category/overview DTOs per the recommended shapes.
- **(a) [recommended]** All money as `decimal` (serialized as JSON number, consistent with `ExpenseResponse.Total`).
  A balance row carries `memberUuid`, `memberName` (denormalized so a deleted member still displays),
  `isOwnerRepresentative`, `isDeleted`, `advanced`, `owed`, `balance`. The balance response also echoes
  `eventUuid`, `eventName`, `isClosed`. A category stat row carries `categoryUuid`, `categoryName`,
  `color`, `icon`, `isDeleted`, `total`, `expenseCount`. The overview carries `from`, `to`,
  `totalSpending`, `expenseCount`. Trade-off: denormalized names inflate the payload slightly, but match
  the M5/M6 "display full info incl. deleted" DTO idiom (`ExpenseSummaryResponse` embeds member/category).
- **(b)** Return only uuids and let the client resolve names from its member/category caches. Trade-off:
  smaller payload, but a deleted member/category isn't in the active lists the client caches, so the
  report would show blanks — breaks §4.7 display.

**OQ15 — Empty / edge cases.**
> ~~**OQ15**~~ → **Answered 2026-07-14 (option a):** an owned-but-empty event → **200 with empty rows**;
> an empty range/event → **zeroed overview / empty by-category rows**; a foreign/unknown `eventUuid` →
> **404 (`9000`)**.
- **(a) [recommended]** An event with **no expenses** → balance returns an **empty rows list** (200, not
  404) with the event header echoed. A time range / event with **no expenses** → overview returns
  `totalSpending = 0`, `expenseCount = 0`; by-category returns an **empty rows list**. A foreign/unknown
  `eventUuid` on balance or by-category → **404 `9000`** (resource-owned; leaks nothing). Trade-off: an
  owned-but-empty event returns 200-empty while a foreign event returns 404 — the standard resource-owned
  distinction. Confirm this split.
- **(b)** Return 404 for an owned event with zero expenses too. Trade-off: fewer "empty" shapes, but a
  freshly-created (owned) event legitimately has no expenses and should show an empty balance, not
  "not found".

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 15 Open Questions — these are
> now decisions, not vetoable assumptions. Each is derived from the spec, prior decisions, and the
> answered M5/M6 OQs.

- All balance/stats endpoints are **guarded** (valid access token required); no anonymous access.
- M7 is **read-only** — it creates/updates/deletes no rows and writes no audit entries.
- **No schema change / NO EF migration** is needed (OQ12/OQ13; called out prominently in Impact Analysis
  as an explicit non-change — M7 is pure aggregation over M5/M6 rows). If aggregation performance later
  warrants an index, that is a follow-up migration, not part of M7.
- The **owner** is always the current authenticated user (only the owner reads their own data);
  `actor`/sharing concerns (§6) are out of scope.
- Money sums use `decimal` end to end (§4.3); MariaDB `SUM` over `decimal(18,2)` yields `decimal`.
- Tier limits (§3.11) do not gate reads (§4.9 — limits only block create); M7 imposes none.
- The **UTC-day-boundary limitation** (M6 OQ1) is inherited by M7's time-range filters unchanged; no
  per-user timezone is introduced.
- The balance query includes soft-deleted **members** (via `includeDeleted: true` when joining member
  display info) and by-category includes soft-deleted **categories** (§4.7).

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New service/repo use DiDecoration
> `[ScopedService]`. All user-facing strings Vietnamese. Concrete names reflect the **accepted option (a)**
> for every Open Question (all confirmed at the 2026-07-14 checkpoint) — no further re-sync needed.
>
> **⚠ NO SCHEMA CHANGE / NO EF MIGRATION.** M7 adds no entity, no `DbSet`, no `ConfigureModel`, and runs
> no `dotnet ef migrations add`. It is pure read-aggregation over the existing `expenses` / `shares` /
> `events` / `members` / `categories` tables. Steps are: aggregate records → `StatsRepository` (DB-side
> `GROUP BY`/`SUM`/`COUNT`) → `StatsService` → DTOs/validators → controllers → tests.

### Step 1 — Aggregate result records (repository-facing)

`Repositories/Stats/StatsAggregates.cs` (plain records the repo returns; NOT DTOs):
- `MemberBalanceAggregate(ulong MemberId, decimal Advanced, decimal Owed)` — one per participating member
  (balance derived later). Plus the member display fields joined in: extend to
  `MemberBalanceAggregate(string MemberUuid, string MemberName, bool IsOwnerRepresentative, bool IsDeleted, decimal Advanced, decimal Owed)`.
- `OverviewAggregate(decimal TotalSpending, int ExpenseCount)`.
- `CategoryStatAggregate(string CategoryUuid, string CategoryName, string Color, string? Icon, bool IsDeleted, decimal Total, int ExpenseCount)`.

### Step 2 — `StatsRepository` (DB-side aggregation, read-only)

`Repositories/StatsRepository.cs` — `IStatsRepository : IBaseRepository` + sealed impl
(`[ScopedService(typeof(IStatsRepository))]`, extends `BaseRepository`). All methods read-only via
`ExecuteQueryAsync`, resource-owned by `userUuid`.

1. `Task<Event?> FindOwnedEventAsync(string userUuid, string eventUuid, CancellationToken)` — resolve +
   own the event (for the balance/by-category header and the 404 scope). Null on miss.
2. `Task<IReadOnlyList<MemberBalanceAggregate>> GetEventBalanceAsync(string userUuid, ulong eventId, CancellationToken)`:
   - **advanced per payer** (DB-side): over `db.Shares` where `share.Expense.EventId == eventId`
     (and `share.Expense.User.Uuid == userUuid` for defense-in-depth),
     `GroupBy(share => share.Expense.PayerMemberId).Select(g => new { MemberId = g.Key, Advanced = g.Sum(x => x.Amount) })`.
   - **owed per member** (DB-side): over the same share set,
     `GroupBy(share => share.MemberId).Select(g => new { MemberId = g.Key, Owed = g.Sum(x => x.Amount) })`.
   - **union the member ids**, left-join each figure (missing → 0m), then join member display info from
     `Query<Member>(includeDeleted: true).Where(m => union.Contains(m.Id))` (OQ3 — deleted members
     included). Materialize into `MemberBalanceAggregate` rows.
   - **Sum-to-zero guarantee (OQ1 — do not compromise):** advanced and owed MUST be resolved from the
     **same single share-set** — the shares of the event's expenses — grouped two ways (by
     `expense.payer_member_id` for advanced, by `share.member_id` for owed). Because every one of those
     shares is counted exactly once in each grouping, `Σ advanced == Σ owed ==` the event's grand total,
     so `Σ balance == 0` by construction. Never source advanced from a different query/scope than owed
     (that would break the invariant).
3. `Task<OverviewAggregate> GetOverviewAsync(string userUuid, DateTime? from, DateTime? to, CancellationToken)`:
   - `totalSpending` = `db.Shares.Where(s => s.Expense.User.Uuid == userUuid && time-range on s.Expense.ExpenseTime).Sum(s => (decimal?)s.Amount) ?? 0m`
     (inclusive `[from,to]`, either bound optional — OQ7).
   - `expenseCount` = `db.Expenses.Where(e => e.User.Uuid == userUuid && time-range).Count()`.
   - Return `new OverviewAggregate(totalSpending, expenseCount)`. (Loose + event expenses both counted —
     it's the whole ledger; §3.9.)
4. `Task<IReadOnlyList<CategoryStatAggregate>> GetByCategoryAsync(string userUuid, DateTime? from, DateTime? to, ulong? eventId, CancellationToken)`:
   - Build the expense scope: `e.User.Uuid == userUuid` AND (`eventId` set → `e.EventId == eventId`,
     else the inclusive time range on `e.ExpenseTime`) (OQ8).
   - **total per category** (DB-side): over `db.Shares` where `share.Expense` is in scope,
     `GroupBy(s => s.Expense.CategoryId).Select(g => new { CategoryId = g.Key, Total = g.Sum(x => x.Amount) })`.
   - **count per category** (DB-side): over the in-scope `db.Expenses`,
     `GroupBy(e => e.CategoryId).Select(g => new { CategoryId = g.Key, Count = g.Count() })`.
   - join both on `CategoryId` (only categories with ≥ 1 expense appear — OQ9), then join category
     display info from `Query<Category>(includeDeleted: true)` (deleted categories included — OQ9),
     ordered `Total` DESC then `Count` DESC then `Name`. Materialize `CategoryStatAggregate` rows.

> Query-translation note for the implementer: if Pomelo cannot translate the two-`GroupBy`-then-outer-
> join in a single expression tree, run each `GroupBy` as its own `ToListAsync` inside the one
> `ExecuteQueryAsync` and stitch them in memory over the (small) grouped result sets — still DB-side
> `SUM`/`COUNT`, only the tiny per-member/per-category stitch is in memory (OQ11a intent preserved).

### Step 3 — `StatsService`

`Services/Api/Stats/StatsService.cs` — `IStatsService` + sealed impl (`[ScopedService(typeof(IStatsService))]`,
primary ctor injecting `IStatsRepository`, `IMapper`, and `IValidator<StatsRangeRequest>`):
- `Task<EventBalanceResponse> GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken)` —
  `FindOwnedEventAsync` (miss → `EventNotFound` 9000); available for OPEN and CLOSED (OQ4); call
  `GetEventBalanceAsync(eventId)`; map rows → `MemberBalanceRow` with `balance = advanced − owed`; build
  the header from the event. Empty event → empty rows (OQ15).
- `Task<OverviewStatsResponse> GetOverviewAsync(string userUuid, StatsRangeRequest range, CancellationToken)` —
  validate the range (from ≤ to when both present); call `GetOverviewAsync`; map, echoing `from`/`to`.
- `Task<ByCategoryStatsResponse> GetByCategoryAsync(string userUuid, ByCategoryStatsRequest request, CancellationToken)` —
  validate (reject both `eventUuid` and a time range — OQ8); if `eventUuid` set, `FindOwnedEventAsync`
  (miss → 9000) and pass its id; call `GetByCategoryAsync`; map rows.

### Step 4 — DTOs + validators + mapping

`Models/Stats/`:
- `MemberBalanceRow { string MemberUuid; string MemberName; bool IsOwnerRepresentative; bool IsDeleted; decimal Advanced; decimal Owed; decimal Balance; }` (OQ14).
- `EventBalanceResponse { string EventUuid; string EventName; bool IsClosed; IReadOnlyList<MemberBalanceRow> Rows; }`.
- `OverviewStatsRequest`/`StatsRangeRequest { DateTime? From; DateTime? To; }` (bound `[FromQuery]`).
- `OverviewStatsResponse { DateTime? From; DateTime? To; decimal TotalSpending; int ExpenseCount; }` (OQ6).
- `ByCategoryStatsRequest { DateTime? From; DateTime? To; string? EventUuid; }` (`[FromQuery]`; OQ8).
- `CategoryStatRow { string CategoryUuid; string CategoryName; string Color; string? Icon; bool IsDeleted; decimal Total; int ExpenseCount; }`.
- `ByCategoryStatsResponse { string? EventUuid; DateTime? From; DateTime? To; IReadOnlyList<CategoryStatRow> Rows; }`.

`Validators/Stats/`:
- `StatsRangeRequestValidator` — when both `From` and `To` are present, `From <= To`
  ("Khoảng thời gian không hợp lệ: thời điểm bắt đầu phải trước hoặc bằng thời điểm kết thúc.").
- `ByCategoryStatsRequestValidator` — the same range rule, plus reject `EventUuid` present together with
  `From`/`To` ("Chỉ được lọc theo đợt hoặc theo khoảng thời gian, không dùng đồng thời.").
- Field keys camelCase (`from`, `to`, `eventUuid`); auto-registered by `AddValidatorsFromAssembly`.

`Mappings/StatsProfile.cs` — map `MemberBalanceAggregate` → `MemberBalanceRow` (compute `Balance`),
`CategoryStatAggregate` → `CategoryStatRow`, `OverviewAggregate` → `OverviewStatsResponse`. (Balance may
be computed in the service rather than the profile — keep it one place.)

### Step 5 — Controllers

**[M6-MOD]** `Controllers/EventsController.cs` — add the balance route (OQ10a). The controller is thin
and derives from `AppController` (LOCKED base — the derived controller is editable). Inject
`IStatsService` alongside `IEventsService`.

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/events/{uuid}/balance` | route → `ApiResult<EventBalanceResponse>` | resource-owned; miss → 9000; OPEN or CLOSED (OQ4); empty event → empty rows (OQ15) |

`Controllers/StatsController.cs` (new; derives from `AppController`). All actions guarded, Vietnamese
Swagger, `userUuid = AuthenticatedUser.Id`.

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/stats/overview` | `[FromQuery] StatsRangeRequest` → `ApiResult<OverviewStatsResponse>` | inclusive UTC range, both bounds optional (OQ6/OQ7) |
| `GET api/v1/stats/by-category` | `[FromQuery] ByCategoryStatsRequest` → `ApiResult<ByCategoryStatsResponse>` | time-range OR eventUuid (OQ8); eventUuid miss → 9000; sort total DESC (OQ9) |

### Step 6 — Error codes

**[M6-MOD]** `Constants/ErrorCodes.cs` — reserve the **10xxx Stats** block with a comment; define no new
codes (OQ13a). Balance / by-category?eventUuid reuse `EventNotFound` (9000); bad range → validation
(1001). No change to `ErrorException.GetDefaultHttpStatus`.

### Step 7 — Tests (owned by the test-engineer; definitive list)

Reuse the M5/M6 harness: `[Collection("AuthIntegration")]`; DB tests use the `ExpenseDbTestBase` /
`ExpenseApiTestBase` families (own connections / app DI + real HTTP), a unique lowercase username prefix
per class, dispose-time cascade cleanup; all DB-dependent tests `[SkippableFact]` (skip when MariaDB
unreachable), never EF InMemory.

**Unit (no DB):**
- `StatsRangeRequestValidator` / `ByCategoryStatsRequestValidator` — `from > to` rejected with the exact
  Vietnamese message + camelCase `error.fields`; both-scope (`eventUuid` + range) rejected; a valid range
  and an event-only request pass.
- `StatsService` (fake `IStatsRepository`) — balance: event miss → `EventNotFound` (9000); rows map with
  `balance = advanced − owed`; empty aggregate → empty rows. Overview: maps totals + echoes range.
  By-category: `eventUuid` miss → 9000; passes `eventId` when the event is owned; maps + orders rows.

**Integration (real MariaDB — `StatsRepositoryTests`):**
- **Balance sum-to-zero invariant (OQ1):** seed an event with several expenses (different payers, mixed
  shares incl. an owner-rep 0đ share) → `Σ balance == 0` exactly (decimal), and each member's
  advanced/owed/balance matches a hand-computed expectation (the §3.7 Bình +300k / Cường −500k scenario).
- **Loose expenses excluded (§3.7):** a loose expense (event_id null) by/for the same members does NOT
  change the event's balance.
- **Deleted member included (OQ3/§4.7):** an event whose payer or share member was soft-deleted still
  appears in the balance with `isDeleted = true` and its correct figures; balances still sum to 0.
- **Owner-rep at 0 appears (OQ3):** the owner-rep 0đ share puts the owner-rep in the balance rows.
- **Settled ignored (OQ2):** toggling `is_settled` on an expense in the event does not change the balance.
- **Open vs closed (OQ4):** the balance is returned for both an OPEN and a CLOSED event.
- **Empty event (OQ15):** an owned event with no expenses → empty rows; a foreign event → treated as a
  miss.
- **Resource-owned:** another user's event → `FindOwnedEventAsync` returns null (never the row); the
  other user's expenses never leak into overview/by-category totals.
- **Overview (OQ6/OQ7):** `totalSpending` = `SUM(shares)` across loose + event expenses in range;
  `expenseCount` correct; **time-range boundaries inclusive** (an expense exactly at `from` and exactly at
  `to` is counted; one just outside is not); omitting `from`/`to` covers all time; empty range → zeros.
- **By-category (OQ8/OQ9):** groups total + count per category; deleted category with historical expenses
  included (`isDeleted = true`); zero-expense category omitted; sort `total` DESC; time-range mode vs
  event mode produce the expected scoping; category totals for a range sum to the overview total for the
  same range.

**Endpoint (WebApplicationFactory — `StatsEndpointTests`, `EventBalanceEndpointTests`):**
- `GET /events/{uuid}/balance`, `GET /stats/overview`, `GET /stats/by-category` through real HTTP wrapped
  in `ApiResult`; anonymous → 401.
- Resource-owned 404 (`9000`) for another user's event on balance and by-category?eventUuid (never 403).
- `from > to` on overview/by-category → 400 (validation, `error.fields`); both `eventUuid` and a range →
  400.
- The §3.7 scenario end-to-end: create an event + expenses via the M5/M6 endpoints, then read the balance
  and assert the rows + sum-to-zero over HTTP.

### Step 8 — Wrap-up

- Update this planning doc's Progress Log + Final Outcome; record the answered OQs in the Decision Log.
- Confirm in the doc that **no migration** was produced (explicit non-change).
- Note any DB-side query-translation fallbacks actually needed (Step 2 note) for the M8/M9 follow-ons.

## Impact Analysis

**APIs:**
- **New:** `GET api/v1/stats/overview`, `GET api/v1/stats/by-category` (new `StatsController`).
- **New route on an existing controller:** `GET api/v1/events/{uuid}/balance` (`EventsController`,
  injecting `IStatsService`).
- No change to existing expense/event/share endpoints.

**Database:**
- **No schema change, no EF migration** (explicit non-change; OQ12). Pure read-aggregation over
  `expenses`, `shares`, `events`, `members`, `categories`. A future performance index (e.g. on
  `shares.expense_id` if not already covered, or `expenses(user_id, expense_time)`) would be a separate
  follow-up migration, not part of M7.

**Infrastructure:** none (no Redis, no background workers, no new packages).

**Services:**
- **New:** `IStatsService`/`StatsService` (`Services/Api/Stats/`), `IStatsRepository`/`StatsRepository`
  (`Repositories/`), `Repositories/Stats/StatsAggregates.cs`, `Mappings/StatsProfile.cs`.
- **Modified:** `EventsController` (add the balance route + `IStatsService` dependency); `ErrorCodes.cs`
  (reserve the 10xxx Stats block, no new codes).
- **New DTOs/validators:** `Models/Stats/*`, `Validators/Stats/*`.

**UI:** none (API only).

**Documentation:** this planning doc; Vietnamese Swagger annotations on the new endpoints. `CLAUDE.md`
already lists a `Stats` controller area — no edit needed.

## Decision Log

> **Resolved at the 2026-07-14 user checkpoint — all 15 Open Questions accepted at the recommended option
> (a).** One numbered point per OQ (binding decision + one-line reason). **Reason** and
> **Alternatives-Considered** for each are the full options/trade-offs preserved inline under the matching
> OQ above.

1. **OQ1 — Balance derivation + sum-to-zero (a):** advanced = SUM(shares) grouped by
   `expense.payer_member_id`, owed = SUM(shares) grouped by `share.member_id`, balance = advanced − owed;
   both from the **same single share-set** so the sum-to-zero invariant holds by construction; verified by
   an integration test, no runtime assert. *Reason:* one query shape guarantees the §3.7 invariant with
   exact decimal math.
2. **OQ2 — Settled ignored (a):** the balance ignores `is_settled`. *Reason:* §3.5 defines settled as
   payment metadata, not số liệu; §3.7 defines balance purely from advanced − owed.
3. **OQ3 — All participants incl. deleted (a):** every payer/share-holder appears, owner-rep at 0đ and
   soft-deleted members included with `isDeleted`. *Reason:* §4.7 historical display + preserves
   sum-to-zero.
4. **OQ4 — Open and closed (a):** balance available for both lifecycles. *Reason:* §3.7 sets no gate; the
   owner watches balance accrue during an open trip.
5. **OQ5 — Per-member net only (a):** no pairwise settlement plan. *Reason:* §3.7 defines only per-member
   figures; M9 QR is per-owing-member — a graph algorithm isn't needed at M7.
6. **OQ6 — Lean overview (a):** `totalSpending` + `expenseCount`, echoing `from`/`to`. *Reason:* matches
   §3.9 literally; additive if more is wanted later.
7. **OQ7 — Optional inclusive UTC range (a):** both bounds optional (omit = all-time), inclusive `[from,to]`,
   raw-UTC compare, `from > to` → 1001. *Reason:* consistent with the M5/M6 list filters + a useful
   all-time default.
8. **OQ8 — Time-range XOR event (a):** by-category accepts a range or `eventUuid`, both together → 1001.
   *Reason:* matches §3.9 "hoặc"; never silently drops a filter.
9. **OQ9 — Non-empty categories incl. deleted, sort total DESC (a):** only categories with ≥1 in-scope
   expense, soft-deleted-with-history included, sorted total DESC → count DESC → name, color/icon carried.
   *Reason:* pie/bar has no zero slices; §4.7 keeps historical spend visible.
10. **OQ10 — Balance on `EventsController`, stats on new `StatsController` (a).** *Reason:* balance is
    event-scoped (reuses the event 404); the two charts live together under `/stats`.
11. **OQ11 — DB-side aggregation (a):** `GROUP BY`/`SUM`/`COUNT` in MariaDB via `ExecuteQueryAsync`.
    *Reason:* realizes the recurring M5/M6 Future Improvement; scales for large ledgers.
12. **OQ12 — New `IStatsRepository` + `IStatsService` (a).** *Reason:* cohesive home for read-aggregation;
    keeps the write-focused repos clean; respects the layering.
13. **OQ13 — No new error codes; reserve 10xxx (a):** reuse `EventNotFound` 9000 + `ValidationFailed`
    1001. *Reason:* reads only; no new failure states beyond a miss and bad input.
14. **OQ14 — Decimal money + denormalized DTO rows (a).** *Reason:* matches the M5/M6 "display full info
    incl. deleted" idiom so deleted members/categories still render.
15. **OQ15 — 200-empty for owned-empty, 404 for foreign (a).** *Reason:* the standard resource-owned
    distinction; a freshly-created owned event legitimately shows an empty balance.

**Inherited decisions (locked upstream — NOT reopened):** expense total = derived `SUM(shares)` (M5 OQ1);
money `decimal(18,2)`, non-negative, no float (§4.3 / M5 OQ2); expenses/shares hard-deleted, members/
categories soft-deleted (M5 OQ3 / §4.7); balance is per-event only, loose expenses excluded (spec §3.7 /
§5); `event_id` nullable, loose = null (M6 OQ8/OQ2); event whole-day-inclusive UTC range + raw-UTC
comparison, accepting the UTC-day-boundary limitation (M6 OQ1); resource-owned 404-never-403 and
`EventNotFound` 9000 (§4.1 / M6 OQ12); domain terms expense/share/event/settled/Premium-Free (§5).

## Progress Log

### 2026-07-14

- Started planning M7 (Debt balance + Stats).
- Read the source of truth: `The-ideal.md` §3.5/§3.6/§3.7/§3.9 + §4.1/§4.3/§4.7 + §5 locks; `CLAUDE.md`;
  `.claude/rules/rule.md` template.
- Read the prior planning docs `expenses-shares-audit.md` (M5) and `events.md` (M6) — confirmed
  total = derived `SUM(shares)`, `decimal(18,2)`, hard-deleted expenses/shares, soft-deleted
  members/categories, `event_id` nullable (loose = null), the recurring DB-side-aggregation Future
  Improvement, and the closed/open event lifecycle.
- Grounded the plan in the live code: `ExpenseRepository`, `EventRepository`, `BaseRepository`,
  `ExpensesService`, `EventsService`, `ExpenseProfile`, `EventsController`/`ExpensesController`,
  `ErrorCodes`, the `Expense`/`Share`/`Event`/`Member`/`Category` entities, and the `Models/*` DTO idiom.
  Checked quick-ordering — no reusable stats/reporting exemplar.
- Drafted requirements, 15 Open Questions (each with options/trade-offs + recommended option a),
  assumptions, a no-migration implementation plan (aggregate records → `StatsRepository` DB-side
  GROUP BY/SUM/COUNT → `StatsService` → DTOs/validators → controllers → tests), impact analysis, and the
  definitive test list (incl. the sum-to-zero invariant, deleted-member/deleted-category inclusion,
  settled handling, time-range boundaries, resource-owned).
- Status: awaiting the user checkpoint on the Open Questions before implementation.

### 2026-07-14 (checkpoint — all Open Questions answered, plan unblocked)

- The user answered all 15 Open Questions — **every one accepted at the recommended option (a)**.
- Annotated each OQ inline with its binding "Answered 2026-07-14 (option a)" answer (options/trade-offs
  preserved beneath); added a consolidated Decision Log (one numbered point + reason per OQ) with a
  Reason / Alternatives-Considered pointer and an inherited-decisions block; moved the Assumptions intro
  to "confirmed at the checkpoint".
- Confirmed the Implementation Plan, endpoint table, DTO section, and test list already match option (a)
  (option (a) was recommended throughout — no re-sync required).
- Emphasized prominently: **NO schema change / NO EF migration** (pure aggregation), and that the balance
  query must resolve advanced + owed from the **same single share-set aggregation** to guarantee
  sum-to-zero.
- Status: **unblocked** — ready for implementation (aggregates → `StatsRepository` → `StatsService` →
  DTOs/validators → controllers → tests). Next: api-implementer builds Steps 1–6; test-engineer builds
  Step 7.

### 2026-07-14 (implementation — Steps 1–6, api-implementer)

- Built exactly to the answered plan (all OQs option a). **No EF migration, no schema change** — pure
  read-aggregation over existing M5/M6 tables (confirmed: no entity/DbSet/ConfigureModel added, no
  `migrations add` run).
- **Files created:**
  - `Repositories/Stats/StatsAggregates.cs` — `MemberBalanceAggregate` / `OverviewAggregate` /
    `CategoryStatAggregate` records (repo-facing, not DTOs).
  - `Repositories/StatsRepository.cs` — `IStatsRepository`/`StatsRepository`
    (`[ScopedService(typeof(IStatsRepository))]`, extends `BaseRepository`): `FindOwnedEventAsync`,
    `GetEventBalanceAsync`, `GetOverviewAsync`, `GetByCategoryAsync`.
  - `Services/Api/Stats/StatsService.cs` — `IStatsService`/`StatsService` (primary ctor injecting
    `IStatsRepository`, `IMapper`, `IValidator<StatsRangeRequest>`, `IValidator<ByCategoryStatsRequest>`).
  - `Models/Stats/*` — `MemberBalanceRow`, `EventBalanceResponse`, `StatsRangeRequest`,
    `OverviewStatsResponse`, `ByCategoryStatsRequest`, `CategoryStatRow`, `ByCategoryStatsResponse`.
  - `Validators/Stats/*` — `StatsRangeRequestValidator` (from ≤ to), `ByCategoryStatsRequestValidator`
    (from ≤ to + reject event-together-with-range).
  - `Mappings/StatsProfile.cs` — aggregate→DTO maps; `Balance = Advanced − Owed` computed here (one place).
  - `Controllers/StatsController.cs` — `GET /stats/overview`, `GET /stats/by-category`.
- **Files modified:**
  - `Controllers/EventsController.cs` — added `GET /events/{uuid}/balance`; injected `IStatsService`
    alongside `IEventsService` (base `AppController` untouched).
  - `Constants/ErrorCodes.cs` — reserved the **10xxx Stats block** with a comment only; no new codes.
- **LINQ→SQL aggregation shape (OQ11, no client-eval):** each figure is a `GroupBy(...).Select(g => new
  { Key, g.Sum(...) / g.Count() })` materialized with its own `ToListAsync` inside one
  `ExecuteQueryAsync` — Pomelo 8 translates these to SQL `GROUP BY`/`SUM`/`COUNT`. Only the tiny
  per-member / per-category stitch (dictionary join to the display-info query) and the final sort run in
  memory, exactly the Step-2 fallback the plan permits. **Sum-to-zero (OQ1):** advanced (grouped by
  `expense.payer_member_id`) and owed (grouped by `share.member_id`) are computed from the SAME single
  `IQueryable<Share>` scope (`share.Expense.EventId == eventId && share.Expense.User.Uuid == userUuid`),
  guaranteeing `Σ advanced == Σ owed`. Confirmed no client-side evaluation by inspecting the generated
  query shapes (no `Include`-then-sum; the only in-memory work is over the already-grouped small result
  sets) and by the live smoke below (totals/counts exact).
- **Overview** sums shares over `expense.ExpenseTime` in `[from,to]` (raw-UTC, inclusive, both optional)
  and counts expenses over the same scope; **by-category** scopes by `eventId` (wins) else the time range,
  counts per `CategoryId` over expenses and sums per `CategoryId` over shares, includes deleted categories
  via `Query<Category>(includeDeleted: true)`, sorts total DESC → count DESC → name.
- **Build:** `dotnet build FairShareMonApi.sln` clean (0 errors; only the pre-existing AutoMapper 13.0.1
  NU1903 warning + an unrelated test-project nullability warning). **Tests:** `dotnet test` **606/606
  passed, 0 skipped** (MariaDB reachable) — no existing test changed or broken.
- **Live smoke (real HTTP against localhost:5200 + MariaDB, 35/35 checks passed):** §3.7 scenario
  (An adv300/owed300/bal0, Binh adv400/owed300/bal+100, Cuong adv0/owed100/bal−100, owner-rep at 0đ) —
  **balances sum to exactly 0**; loose expense excluded from balance but present in overview; settled
  toggle leaves the balance identical; a soft-deleted member still appears (`isDeleted=true`) with correct
  figures and the sum stays 0; balance readable for OPEN and CLOSED; owned-empty event → 200 empty rows;
  overview total 750 / count 3 over range + all-time, empty range → zeros, inclusive boundary (expense
  exactly at `from`/`to` counted, one microsecond later excluded), `from > to` → 400 (1001); by-category
  time-range and event modes with correct totals/counts/colors and total-DESC sort, category totals sum to
  the overview total, both-scopes → 400 (1001), soft-deleted category with history still shown
  (`isDeleted=true`); resource-owned 404 (`9000`) for another user's event on balance and
  by-category?eventUuid + user isolation on overview; unknown event → 404 (`9000`); anonymous → 401. Smoke
  data fully removed afterward (ordered delete of shares/expenses/events/audit_logs/categories/members/
  auth_tokens/users for the two smoke accounts; 0 remaining).

### 2026-07-14 (tests — Step 7, test-engineer)

- Authored the M7 test suite (Step 7 list, plus judged edge cases) in `FairShareMonApi.Tests`; **only the
  test project was touched — no production code changed** (the tests exercise the shipped M7 code as-is).
- **Files added (58 new tests):**
  - `StatsValidatorsTests.cs` (13, pure unit) — `StatsRangeRequestValidator` (from ≤ to; both/each/neither
    bound; from > to → pinned Vietnamese message on `To`) and `ByCategoryStatsRequestValidator` (same
    range rule + the OQ8 both-scopes rejection on `EventUuid` for eventUuid-with-from / -with-to /
    -with-full-range; each mode alone valid).
  - `StatsServiceTests.cs` (10, pure unit, fake `IStatsRepository` + real `StatsProfile` + real
    validators) — balance maps rows with `Balance = Advanced − Owed` + event header, passes the resolved
    event id, event miss → 9000, empty aggregate → empty rows; overview maps totals + echoes range, omitted
    bounds pass nulls, **from > to short-circuits the repo** (`GetOverviewCalled` stays false); by-category
    time-range vs event mode (resolves + passes the owned event id, nulls the range), event miss → 9000,
    **both-scopes rejected before the ownership resolve** (`FindOwnedEventCalled`/`GetByCategoryCalled`
    false).
  - `StatsRepositoryTests.cs` (20, integration, real MariaDB, `[SkippableFact]`) — balance: the canonical
    §3.7 scenario (An +200k / Bình +300k / Cường −500k, **Σ balance == 0m exact**), a **fractional-cents
    split still summing to exactly 0m** (no decimal drift), owner-rep-at-0đ inclusion + non-participant
    omission, soft-deleted-member inclusion (`isDeleted`, figures, Σ = 0), settled-toggle neutrality (OQ2),
    loose-expense exclusion, OPEN and CLOSED both return balance (OQ4), owned-empty → empty rows (OQ15),
    resource-owned `FindOwnedEventAsync` null for a stranger; overview: loose+event sum in range,
    all-time (null bounds), **inclusive UTC boundary (expense at `from` and at `to` counted, `to`+1µs
    excluded)**, empty range → zeros, per-user isolation; by-category: per-category total+count sorted
    total DESC (zero-expense category omitted, color carried), deleted-category-with-history included
    (`isDeleted`), event-mode scoping, **category totals reconcile to the overview total for the same
    range**, empty scope → empty, per-user isolation.
  - `EventBalanceEndpointTests.cs` (6, HTTP via WebApplicationFactory) — `GET /events/{uuid}/balance`: the
    §3.7 scenario driven through the M5/M6 create endpoints then read over the wire (per-member balances +
    **Σ balance == 0m**), owner-rep auto-0đ-share row present, owned-empty → 200 empty rows, foreign event
    → 404/9000 (never 403), unknown event → 404/9000, anonymous → 401.
  - `StatsEndpointTests.cs` (9, HTTP) — `GET /stats/overview` (range + all-time figures, null bounds
    echoed, from > to → 400/1001 with camelCase `to`, per-user isolation, anonymous 401) and
    `GET /stats/by-category` (time-range mode sorted total DESC with color/icon, event mode scoping,
    both-scopes → 400/1001 with camelCase `eventUuid`, foreign eventUuid → 404/9000, anonymous 401).
- **Sum-to-zero verification approach:** for every seeded balance shape the test asserts
  `rows.Sum(r => r.Advanced − r.Owed) == 0m` as an exact `decimal` compare (no tolerance), including an
  uneven cents split, so any rounding/float drift would fail; over HTTP the same is asserted on the
  serialized `balance` field via `GetDecimal()`.
- **Datasets** are deliberately multi-member / multi-category / multi-expense (and mix loose vs event,
  active vs soft-deleted) so a client-eval / mis-scoped-aggregation bug would surface in the totals/counts.
- **Result:** `dotnet test .\FairShareMonApi.sln` → **664/664 passed, 0 failed, 0 skipped** (606 existing +
  58 new; MariaDB reachable so no skips). **Run twice — identical (deterministic).** Post-run DB sweep
  confirmed **0 rows** across users(test-prefix)/events/expenses/shares/members/categories/tags/
  expense_tags/audit_logs (the M5/M6 harness cleanup — expense-first delete, events + audit_logs sweeps,
  user cascade — leaves the real DB clean). **No production bug found; production code untouched.**

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

- **Verdict: APPROVE — 0 blocking findings, no material nits** (3 benign informational notes below).
  Full suite **664/664 passed, 0 failed, 0 skipped**, deterministic (re-run identical), DB swept clean.
- **Verified checks:**
  - **Sum-to-zero correct by construction:** `GetEventBalanceAsync` builds ONE `IQueryable<Share>` scoped
    to the event + user (`share.Expense.EventId == eventId && share.Expense.User.Uuid == userUuid`) and
    groups it two ways — advanced by `Expense.PayerMemberId`, owed by `Share.MemberId`. Every share is
    counted exactly once per grouping, so `Σ advanced == Σ owed == Σ shares` and the balances sum to
    exactly 0 in `decimal`. Confirmed no second scope feeds advanced, and no tag/`expense_tag` join
    inflates the share set. Members are stitched via the union of the two key sets, each member once.
  - **DB-side aggregation, no client-eval blowup:** every `GroupBy`/`Sum`/`Count` is applied before any
    `ToListAsync`; only a bounded per-member / per-category dictionary stitch plus the display-info
    `IN`-join run in memory over the already-grouped small result sets (the Step-2 fallback the plan
    permits). No `Include`-then-sum anywhere.
  - **Settled-neutral:** no `IsSettled` reference in the balance path (OQ2).
  - **Loose-vs-event scoping correct:** balance filters `EventId == eventId`; overview counts loose +
    event; by-category is event XOR range.
  - **Deleted inclusion (§4.7):** members via `Query<Member>(includeDeleted: true)`, categories via
    `Query<Category>(includeDeleted: true)`.
  - **Resource-owned / per-user isolation:** `FindOwnedEventAsync` returns 9000 for foreign/unknown
    events; every aggregate carries `.User.Uuid == userUuid`. The both-scope rejection (1001) is
    validated BEFORE the ownership resolve. Range is optional / inclusive / UTC, `from > to` → 1001.
  - **No schema change / no migration.** Conventions intact: `[ScopedService]`, primary ctors,
    `Async` + `CancellationToken`, read-only `AsNoTracking`, Vietnamese messages, `AppController`
    untouched, 10xxx reserved as a comment only.
- **3 informational notes (non-blocking, no action required):**
  1. Balance-row ordering is a deterministic in-latitude addition (rows sorted for stable output) — an
     improvement over an unordered result, not a defect.
  2. The member / category display joins are keyed by `Id`; safe because those ids only ever come from
     the user's own scoped aggregate rows (a defense-in-depth observation, not a leak).
  3. The by-category DTO echoes event-mode vs time-mode scope fields exactly per the OQ8 contract.
- **Final state confirmed:** `dotnet test` = **664 passed / 0 failed / 0 skipped**, deterministic, DB
  swept clean. Milestone 7 approved and ready to commit.

## Final Outcome

**Milestone 7 (Debt balance + Stats) COMPLETE — implemented, tested, and code-reviewed APPROVE (0
blocking).** Delivered a read-only aggregation stack over the existing M5/M6 data with **NO schema change
and NO EF migration** (pure aggregation over `expenses`/`shares`/`events`/`members`/`categories`):

- **Three read-only endpoints:** `GET api/v1/events/{uuid}/balance` on `EventsController`, and
  `GET api/v1/stats/overview` + `GET api/v1/stats/by-category` on the new `StatsController`.
- **Per-event debt balance (§3.7):** advanced/owed/balance per participating member, **sum-to-zero by
  construction** (advanced + owed resolved from the same single event-scoped share-set), settled-ignored,
  loose-expenses-excluded, deleted-member-inclusive (`isDeleted`, §4.7), available for OPEN and CLOSED
  events; owned-but-empty event → 200 empty rows, foreign/unknown event → 404 (`9000`).
- **Overview stats (§3.9):** `totalSpending` + `expenseCount` over an optional, inclusive, raw-UTC time
  range (omit = all-time; `from > to` → 1001); loose + event expenses together; no cross-event balance.
- **By-category stats (§3.9):** per-category `total` + `expenseCount` with color/icon, scoped to a time
  range XOR an event (both together → 1001), only non-empty categories, deleted-category-with-history
  included, sorted total DESC → count DESC → name.
- **Components:** `Repositories/Stats/StatsAggregates.cs`, `IStatsRepository`/`StatsRepository` (DB-side
  `GROUP BY`/`SUM`/`COUNT` — establishing the efficient server-side aggregation pattern the M5/M6 reviews
  flagged), `IStatsService`/`StatsService`, `Models/Stats/*`, `Validators/Stats/*`,
  `Mappings/StatsProfile.cs`, `StatsController`; `EventsController` gained the balance route +
  `IStatsService`; `ErrorCodes.cs` reserves the 10xxx Stats block (comment only) and reuses `EventNotFound`
  9000 + `ValidationFailed` 1001.
- **Quality gates:** build clean; **`dotnet test` = 664/664 passed, 0 failed, 0 skipped**, deterministic;
  35/35 live smoke passed; **code review APPROVE, 0 blocking**. No deviations from the doc; no new Open
  Questions. Milestone committed-ready.
- **Seams left for later milestones:** M8 export (CSV — reuses the balance/stats projections), M9
  wallet + per-owing-member QR (consumes the per-owing-member negative balances), M10 tiers.

## Future Improvements

- **Pairwise debt-simplification / min-transaction settlement** (OQ5b) — a "who pays whom" plan derived
  from the per-member balances.
- **Net-of-settled balance** and settled/unsettled overview split (OQ2c/OQ6b) if a "still outstanding"
  view is wanted (dovetails with M9's per-still-owing-member QR).
- **Timezone-aware time ranges** — remove the inherited UTC-day-boundary limitation (M6 OQ1) with a
  per-user timezone.
- **Additional overview figures** (event count, loose vs event split, top-category) for a richer
  dashboard.
- **Materialized/cached aggregates or covering indexes** if a large ledger makes the on-read aggregation
  slow (the DB-side pattern established here is the prerequisite).
- **More stats dimensions** — by-tag, by-member, by-payer, trend-over-time buckets — beyond the §3.9
  overview + by-category set.
