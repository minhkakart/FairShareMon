# Settled Per Member (Đánh dấu đã trả theo từng thành viên)

Extend the existing whole-expense **settled** (đã trả) flag with two finer layers so the owner can track
**who has actually paid** — not just "the whole expense is settled". This is the §6 future item
(`The-ideal.md` line 177: *"Đánh dấu đã trả theo từng thành viên: chi tiết hơn mức phiếu — theo dõi từng
người đã trả phần của mình hay chưa"*). Two layers ship together:

- **Layer A — per-Share settled.** Each `Share` (one member's portion of one expense) gains its own
  `is_settled` + `settled_at`. Answers "has this member paid their part of *this* bill" — the natural fit
  for **loose expenses** (§3.5) and for a detailed per-portion view of any expense.
- **Layer B — per-member-per-event net clearance.** For an expense **event**, track whether member X has
  cleared their **net** debt in event Z (the §3.7 balance). Drives the "còn nợ / đã trả" overlay and the
  §3.10 per-owing-member QR.

The existing whole-expense `Expense.IsSettled`/`SettledAt` stays and is **reconciled/derived** against the
new per-share flags where sensible (locked decision D1). The M7 debt **balance stays PURE** (sum-to-zero,
ignores settled — M7 OQ2 preserved verbatim); the new "outstanding" numbers are a **derived overlay**
surfaced alongside the balance, never a change to the balance figures (locked decision D2).

> **⚠ This feature REQUIRES an EF migration** (unlike M7 Stats, which was pure aggregation). Layer A adds
> two columns to `shares`; Layer B (if stored — OQ1) adds a new table; and a migration **data step**
> backfills existing settled expenses. This is called out prominently in the Implementation Plan and
> Impact Analysis.

## Objective

Implement `The-ideal.md` §6 "Đánh dấu đã trả theo từng thành viên" on top of the shipped Auth + Members +
Categories + Tags + Expenses/Shares/Audit + Events + Stats/Balance + Wallet/QR stack:

- **Per-share settled (Layer A):** mark/unmark an individual share settled; payment metadata, does not
  change any amount (§3.5). Available on any expense (loose or event), and on a **closed** event's
  expenses (the §4.4 sole-exception, extended).
- **Per-member-per-event net clearance (Layer B):** mark/unmark that a member has cleared their net debt
  in an event; drives the outstanding overlay and the event QR "who still owes".
- **Reconcile the whole-expense flag (D1):** keep `Expense.IsSettled`/`SettledAt`; toggling it cascades to
  its shares, and per-share toggles keep the expense-level flag consistent — one coherent "settled" story.
- **Outstanding overlay (D2):** the M7 balance's `advanced`/`owed`/`balance` are untouched; the balance
  response gains derived `outstanding` per member + an event-level rollup so the UI and the QR flow can
  show who still owes without recomputing debt.
- **Resource-owned (§4.1):** every read/write scoped to the current user; a miss looks like non-existence
  (404, reusing `ExpenseNotFound` 6000 / `ShareNotFound` 7000 / `EventNotFound` 9000 / `MemberNotFound`
  3000; never 403).
- **No audit for settled toggles** (mirrors M5 OQ11 — settled is payment metadata, not số liệu) (OQ10).
- **No tier gate** — settled is a Free/basic feature (§3.11 "Cơ bản (Free): … đánh dấu đã trả") (OQ11).

## Background

Confirmed against the live code (2026-07-21):

- **Whole-expense settled (M5).** `Expense.IsSettled` (bool) + `SettledAt` (DateTime?) —
  `Database/Entities/Expense.cs`. Write path: `ExpensesController` `PUT api/v1/expenses/{uuid}/settled`
  (body `SetSettledRequest { bool IsSettled }`) → `ExpensesService.SetSettledAsync` →
  `ExpenseRepository.SetSettledAsync` (one `ExecuteTransactionAsync`; sets `IsSettled` +
  `SettledAt = isSettled ? AppDateTime.Now : null`; **no closed-event guard** — the sole §4.4 exception;
  **no audit** — M5 OQ11). Comment in the repo confirms both exemptions. Miss → `ExpenseWriteStatus.ExpenseNotFound`.
- **Share (M5).** `Database/Entities/Share.cs` — `Id/Uuid/ExpenseId/MemberId/Amount/Note/CreatedAt/UpdatedAt`;
  **no settled fields today**. `decimal(18,2)` with DB CHECK `ck_shares_amount_non_negative` (`amount >= 0`);
  0đ valid; unique index `(expense_id, member_id)` (one share per member per expense); cascade-deleted with
  the expense; hard-deleted (not `IEntityDeletable`). `ShareResponse` exposes `Uuid/Member/Amount/Note/CreatedAt`.
  Share writes go through `ShareRepository.AddAsync/UpdateAsync/DeleteAsync` with the `EventWriteGuard`
  (closed-event block) and audit; there is **no `ShareRepository.SetSettledAsync` today**.
- **Balance / overlay consumer (M7).** `GET api/v1/events/{uuid}/balance` →
  `StatsService.GetEventBalanceAsync` → `StatsRepository.GetEventBalanceAsync`. Advanced/owed are summed
  from the **same single share-set** (advanced grouped by `expense.payer_member_id`, owed by
  `share.member_id`) so `Σ balance == 0` by construction (M7 OQ1). The balance **ignores `is_settled`
  entirely** (M7 OQ2 — LOCKED, not reopened). `MemberBalanceRow` carries
  `MemberUuid/MemberName/IsOwnerRepresentative/IsDeleted/Advanced/Owed/Balance`; `EventBalanceResponse`
  carries `EventUuid/EventName/IsClosed/Rows`. Available for OPEN and CLOSED events (M7 OQ4). Rows include
  the owner-rep at 0đ and soft-deleted members (M7 OQ3).
- **QR (§3.10, M9).** `WalletQrService.GenerateEventQrAsync` reuses the M7 balance
  (`statsService.GetEventBalanceAsync`), requires `balance.IsClosed`, then bills **one QR per member with
  `row.Balance < 0`** (amount `= -row.Balance`), composited into one PNG. Errors `EventNotClosedForQr`
  (12002) / `NoOutstandingDebtForQr` (12003). This is the natural consumer of the Layer B overlay.
- **Audit (M5).** `AuditLogFactory` builds `Expense`/`Share` Create/Update/Delete snapshots with no-op
  detection; the settled toggle is deliberately excluded (M5 OQ11). The `Share` audit snapshot does **not**
  include settled today (the field doesn't exist yet).
- **Closed-event guard (M6).** `Repositories/EventWriteGuard.cs` + `IsCurrentEventClosed(expense)` woven
  into every write path **except** `SetSettledAsync`. §4.4/§3.5: settled is the sole write allowed on a
  closed event.
- **Errors + messages.** `Constants/ErrorCodes.cs` blocks: 1xxx infra, 2xxx auth, 3xxx members, 4xxx
  categories, 5xxx tags, 6xxx expenses, 7xxx shares, 8xxx audit (reserved-empty), 9xxx events, 10xxx stats
  (reserved-empty), 11xxx export (reserved-empty), 12xxx wallet/QR, 13xxx tiers, 14xxx admin.
  **Next free block is 15xxx** (OQ12). User-facing strings come from the D1 localization subsystem:
  `Constants/MessageKeys.cs` keys resolved via `IStringLocalizer<StringResources>` against
  `Localization/Resources/StringResources.resx` (neutral vi-VN) + `StringResources.en-US.resx`.
- **Tiers (§3.11).** "Đánh dấu đã trả" is explicitly in the **Free** basic set; tier limits only block
  *create* (§4.9). No gate on settled (OQ11).
- The dev DB holds no real product data beyond disposable smoke rows — the migration data backfill is
  effectively a no-op there but must be correct for any future real data.

## Requirements

From `The-ideal.md` §2, §3.5 (settled = payment metadata, sole closed-event exception), §3.7 (balance is
per-event net), §3.10 (per-owing-member QR), §6 (this feature), and cross-cutting §4.1/§4.3/§4.4/§4.7:

- **Layer A — per-share settled:** toggle `is_settled`/`settled_at` on a single share; never alters
  `amount`; allowed on any expense including a closed event's expenses; resource-owned; not audited.
- **Layer B — per-member-per-event net clearance:** toggle whether a participating member has cleared
  their net debt in an event; resource-owned; allowed on OPEN and CLOSED events; not audited.
- **Whole-expense flag reconciled (D1):** `Expense.IsSettled`/`SettledAt` retained; kept consistent with
  the per-share flags per a documented predicate (see OQ3).
- **Balance purity (D2, §4.3, §3.7):** `advanced`/`owed`/`balance` unchanged and still sum to 0; the
  overlay (`outstanding`, per-member `isSettled`, event-level rollup) is derived and additive.
- **Money accuracy (§4.3):** all amounts stay `decimal`; settled flags never touch amounts.
- **Soft-delete history (§4.7):** a soft-deleted member still appears in the balance/overlay with its
  settled/outstanding figures.
- **Privacy (§4.1):** resource-owned everywhere; misses are 404, never 403.
- **Conventions:** entities per rules.md; schema via **EF migration only**; `Async` + `CancellationToken`;
  Vietnamese user-facing strings via the D1 localization keys; writes as single `ExecuteTransactionAsync`
  with `NoCommit()` on failure; the transaction stays in the repository.

## Open Questions

> **All 15 answered by the user at the 2026-07-21 checkpoint — every recommended option (a) was accepted.**
> The annotated questions below carry the binding answers inline; the full options/trade-offs are preserved
> for the record and mirrored in the Decision Log. No open questions remain — implementation can start. The
> Implementation Plan, endpoint/DTO tables, and test list below are synced to these answers (option (a) was
> recommended throughout). The **locked top-level decisions D1/D2** (both layers ship; balance stays pure
> with a derived overlay) are recorded in the Decision Log and were **not** reopened. Prior locked decisions
> (M7 OQ2 balance ignores settled; M5 OQ11 settled not audited; §4.4 settled is the sole closed-event write;
> domain terms) were not reopened either.

**OQ1 — Layer B storage: STORED (new table) vs DERIVED (all-shares-settled ⇒ member cleared). [High impact — this is the crux; drives the migration.]**
> ~~OQ1~~ → **Answered 2026-07-21 (option a):** Layer B is **STORED** in a new `event_member_settlements`
> table keyed `(event_id, member_id)` — net clearance is a distinct net-level fact that doesn't decompose
> into gross per-share flags.
The per-member-per-event "has X cleared their net debt" state can be a stored fact or derived from Layer A.
- **(a) [recommended] STORED — new `event_member_settlements` table** keyed `(event_id, member_id)` with
  `is_settled` + `settled_at`. **Justification:** the event clearance is a **net** fact (X owes the group
  `|balance|`, netting X's advances against X's shares), while per-share settled is **gross** (each of X's
  portions paid to its payer). The §3.10 QR bills the **net** amount and the UC marks "đã trả" **after**
  the net transfer ("Cường quét mã… chuyển xong An đánh dấu đã trả") — a net-level action that does not
  decompose into shares. Deriving from "all X's shares settled" over-counts whenever X both advanced and
  owed in the event (gross > net), so it would mark X as still-owing after X has actually paid their net
  debt. A stored flag models the real action cleanly and lets the overlay's per-member `isSettled` and
  `outstanding` be exact. Trade-off: one new table + a small write path; while the event is OPEN the stored
  flag can go stale if the balance later changes (documented limitation — see OQ9 + Future Improvements).
- **(b) DERIVED — member cleared ⇔ every one of X's shares in the event is settled** (Layer A only; no new
  table). Trade-off: no new table and "settled" has one meaning (gross per-share), but it **conflates gross
  with net** — a member who advanced on one expense and owes on another is only "cleared" after paying
  gross, contradicting the net QR the owner actually sends; and the overlay's `outstanding` (net) would
  disagree with its `isSettled` (gross). Also every share of a payer-heavy owner would need toggling to
  ever mark the owner cleared.
- **(c) STORED, but store the net amount snapshot at marking time** (`is_settled` + `cleared_amount` +
  `settled_at`) so the overlay can show "cleared 500k of 500k" and detect drift. Trade-off: richest and
  drift-aware, but more schema + write logic than the spec asks for at this stage; the snapshot is a
  Future Improvement candidate on top of (a).

**OQ2 — Layer A schema + migration name.**
> ~~OQ2~~ → **Answered 2026-07-21 (option a):** add `shares.is_settled` (bool, default 0) + `shares.settled_at`
> (nullable) via **one** migration `AddPerMemberSettlement` covering the whole feature (alter + table + backfill).
- **(a) [recommended]** Add `shares.is_settled` (bool, `NOT NULL DEFAULT 0`) + `shares.settled_at`
  (`datetime(6)` nullable) to the `Share` entity + mapping; EF migration **`AddPerMemberSettlement`** that
  (1) alters `shares`, (2) creates `event_member_settlements` (if OQ1a), and (3) runs the data backfill
  (OQ4) — **one** migration for the whole feature. Trade-off: none significant; mirrors the
  `is_settled`/`settled_at` pair already on `expenses`.
- **(b)** Two migrations (columns first, table second). Trade-off: smaller diffs, but the feature is one
  logical unit and the backfill spans both.

**OQ3 — Reconcile `Expense.IsSettled` with per-share settled.**
> ~~OQ3~~ → **Answered 2026-07-21 (option a):** **cascade + reconcile, keep the column** — toggling the
> whole-expense flag cascades to shares, and per-share toggles recompute `Expense.IsSettled = (all billable
> shares settled)` (billable = `amount > 0` and `member ≠ payer`).
- **(a) [recommended] Cascade + reconcile, keep the column.** Toggling `PUT /expenses/{uuid}/settled`
  true/false **cascades** to set every share's `is_settled`/`settled_at` (so "mark the whole bill paid"
  still works and is consistent); and after any **per-share** toggle, recompute
  `Expense.IsSettled = (all "billable" shares settled)` where a **billable** share is one with
  `amount > 0` and `member_id ≠ payer_member_id` (see OQ6 — the payer's own share and 0đ shares are
  settled-by-definition and excluded from the predicate). `Expense.SettledAt` is set to the latest share
  `settled_at` (or `AppDateTime.Now`) when it flips true, cleared when false. Trade-off: a single-share
  toggle can flip the expense-level flag as a side effect and needs a recompute, but yields one coherent
  "settled" story and is backward-compatible with the existing whole-expense endpoint/clients.
- **(b) Keep fully independent** — `Expense.IsSettled` is its own manual flag unrelated to shares.
  Trade-off: no cascade/recompute, but the two flags can disagree ("expense settled yet a share unsettled"),
  which is confusing and breaks the M9 export/QR "settled" story.
- **(c) Make `Expense.IsSettled` purely DERIVED** (drop the column, compute on read). Trade-off: cleanest
  conceptually, but it removes a stored column relied on by the M5 settled write path, list filter
  (`ExpenseFilter.Settled`), and `SettledAt`, and is a larger, riskier change than the spec needs.

**OQ4 — Migration data backfill of existing settled data.**
> ~~OQ4~~ → **Answered 2026-07-21 (option a):** backfill per-share settled from already-settled expenses
> (`settled_at = expenses.settled_at`); **no Layer B backfill** — net clearance is asserted by the owner
> going forward.
- **(a) [recommended]** In the `AddPerMemberSettlement` migration data step, for every existing
  `expenses` with `is_settled = 1`, set all its shares `is_settled = 1, settled_at = expenses.settled_at`
  (keeps the D1 reconciliation invariant true from day one). Do **NOT** backfill `event_member_settlements`
  (Layer B) — net clearance is a distinct concept the owner asserts going forward; a whole-expense flag
  from the past does not imply a member's net event debt was cleared. Trade-off: Layer B starts empty
  (every member shows still-owing until explicitly marked), which is the correct conservative default.
- **(b)** Backfill Layer B too (e.g. mark a member cleared when all their event shares are settled).
  Trade-off: fewer "still owing" rows on first load, but it fabricates a net-clearance assertion the owner
  never made and re-introduces the gross/net conflation of OQ1b.

**OQ5 — Closed-event exception scope (§4.4).**
> ~~OQ5~~ → **Answered 2026-07-21 (option a):** both new settled writes (per-share + per-member-per-event)
> are the §4.4 sole-exception and **bypass the `EventWriteGuard`** — allowed on a closed event's data.
- **(a) [recommended]** Both new settled writes are the §4.4 sole-exception and **bypass the
  `EventWriteGuard`**: the per-share settled toggle and the per-member-per-event settled toggle are allowed
  on a **closed** event's data (exactly as the existing whole-expense `SetSettledAsync` is). Indeed Layer B
  is *primarily* a post-close action (QR is closed-only, then mark paid). Trade-off: none — this is the
  literal §3.5/§4.4 rule extended to the finer flags.
- **(b)** Allow only Layer B on closed events, block per-share on closed. Trade-off: inconsistent — §4.4
  exempts "settled" categorically, and a per-share settled is still just payment metadata.

**OQ6 — Payer's own share and 0đ shares: settleable / settled-by-definition / n/a.**
> ~~OQ6~~ → **Answered 2026-07-21 (option a):** the payer's own share (`member == payer`) and 0đ shares are
> **settled-by-definition** in all derivations and excluded from `outstanding`; a toggle on them is a
> harmless no-op (no auto-set flag maintained on payer change).
- **(a) [recommended] Settled-by-definition in all derivations; toggle is a harmless no-op.** A share whose
  `member_id == expense.payer_member_id` (the payer paying part of what they themselves advanced) and any
  `amount == 0` share (e.g. the owner-rep 0đ share) represent nothing owed to anyone; they are treated as
  **settled** when computing the expense-level reconciliation predicate (OQ3) and the gross view, and are
  **excluded from `outstanding`**. We do **not** store an auto-set flag that must be re-maintained when the
  payer changes (that coupling is avoided); the derivation simply treats them as settled. A per-share
  toggle on them is accepted but has no derived effect. Trade-off: the stored `is_settled` on such a share
  may read `false` while derivations treat it as settled — documented; keeps the write path free of
  payer-change cascades.
- **(b)** No special-casing — the payer's own share and 0đ shares are ordinary settleable shares. Trade-off:
  simplest code, but semantically the payer "owes themselves" and the owner-rep must toggle a 0đ share to
  ever mark an expense fully settled.
- **(c)** Store an auto-settled flag on the payer's own share at create/update and maintain it on payer
  change. Trade-off: explicit, but adds cascade logic to the payer-change path for a purely derived fact.

**OQ7 — Overlay placement + toggle endpoint surface.**
> ~~OQ7~~ → **Answered 2026-07-21 (option a):** **extend the existing `GET api/v1/events/{uuid}/balance`**
> additively; toggle via `PUT api/v1/expenses/{expenseUuid}/shares/{shareUuid}/settled` and
> `PUT api/v1/events/{eventUuid}/members/{memberUuid}/settled`, both reusing `SetSettledRequest`.
- **(a) [recommended]** **Extend the existing `GET api/v1/events/{uuid}/balance`** response additively with
  the overlay (balance figures untouched — D2). Toggle routes: per-share
  `PUT api/v1/expenses/{expenseUuid}/shares/{shareUuid}/settled` (mirrors the M5 nested share routes +
  `/settled`); per-member-per-event `PUT api/v1/events/{eventUuid}/members/{memberUuid}/settled`
  (event-scoped, resource-owned). Both bodies reuse `SetSettledRequest { bool IsSettled }`. Trade-off: the
  balance DTO grows, but the overlay is intrinsically per-member alongside the balance and the UI/QR
  already call this endpoint.
- **(b)** A separate `GET api/v1/events/{uuid}/settlement` overlay endpoint. Trade-off: keeps the balance
  DTO lean, but the UI needs two calls to render balance + who-paid and the QR must join them.
- **(c)** Event-side per-share toggle (`.../events/{uuid}/...`). Trade-off: a share is owned via its
  expense; the nested expense route is the natural 404 scope.

**OQ8 — What drives `outstanding` in the overlay: Layer B (net) vs Layer A (gross)?**
> ~~OQ8~~ → **Answered 2026-07-21 (option a):** **Layer B (net)** — `outstanding = (balance < 0 && !isSettledB) ? -balance : 0`;
> Layer A per-share settled does not reduce the event overlay's `outstanding`.
- **(a) [recommended] Layer B (net).** For a member with `balance < 0`: `outstanding = isSettledB ? 0 :
  -balance` (where `isSettledB` is the stored per-member-per-event flag, OQ1a). For `balance >= 0`:
  `outstanding = 0` (they are owed, not owing). Layer A per-share settled does **not** reduce the event
  overlay's `outstanding` (gross ≠ net) — it is a separate finer view for loose expenses and the detailed
  expense screen. Trade-off: per-share settled and the event overlay are two axes the UI must present
  distinctly; but each is exact for its purpose and neither perturbs the pure balance.
- **(b)** Derive `outstanding` from unsettled gross shares. Trade-off: ties the two layers, but reintroduces
  the gross/net conflation (OQ1b) and can disagree with the net balance.

**OQ9 — Per-member settled write scope: which members, and OPEN-vs-CLOSED gate.**
> ~~OQ9~~ → **Answered 2026-07-21 (option a):** allow marking any **participating** member (else
> `MemberNotFound` 3000), on **both OPEN and CLOSED** events; open-event drift is an accepted, documented
> limitation.
- **(a) [recommended]** Allow marking any member who **participates** in the event (is a payer of, or holds
  a share in, one of the event's expenses); a non-participant or unknown/foreign member → **`MemberNotFound`
  3000** (resource-owned miss). Allowed on **both OPEN and CLOSED** events (no lifecycle gate — matches M7
  OQ4 for viewing balance and §3.5 for settled). While OPEN, a stored flag may become stale if the balance
  later changes — documented limitation (Future Improvements: re-validate/clear on balance change).
  Trade-off: an open-event flag can drift, accepted because the real workflow marks paid after close.
- **(b)** Restrict Layer B marking to CLOSED events only. Trade-off: no drift, but blocks marking during a
  running trip where partial settlements happen; §3.5/§3.7 place no such gate.

**OQ10 — Audit scope for the new settled toggles.**
> ~~OQ10~~ → **Answered 2026-07-21 (option a):** **no audit** for per-share or per-member-per-event settled
> toggles (mirrors M5 OQ11); the `Share` audit snapshot is not extended with `is_settled`.
- **(a) [recommended] No audit** for per-share or per-member-per-event settled toggles — consistent with
  M5 OQ11 (settled is payment metadata, not số liệu; the audit exists for expenditure disputes). The
  `Share` audit snapshot is **not** extended with `is_settled` (preserves the existing no-op detection).
  `settled_at` records the last toggle. Trade-off: settled history isn't in the audit log (acceptable;
  mirrors the whole-expense settled exclusion).
- **(b)** Audit the settled toggles as `Share`/`Update`. Trade-off: literal to §3.8 "mọi lần sửa", but
  pollutes the dispute log with non-expenditure toggles and forces `is_settled` into the snapshot +
  no-op canonicalization.

**OQ11 — Tier gating (§3.11).**
> ~~OQ11~~ → **Answered 2026-07-21 (option a):** **no tier gate** — settled is a Free basic feature; tier
> limits only block create (§4.9).
- **(a) [recommended] No tier gate.** "Đánh dấu đã trả" is in the Free basic set; tier limits only block
  *create* (§4.9). Trade-off: none.
- **(b)** Gate per-member settled behind Premium. Trade-off: contradicts §3.11's Free basic list.

**OQ12 — Error codes.**
> ~~OQ12~~ → **Answered 2026-07-21 (option a):** **no new codes**; reserve the **15xxx** block with a
> comment. Misses reuse `ExpenseNotFound` 6000 / `ShareNotFound` 7000 / `EventNotFound` 9000 /
> `MemberNotFound` 3000.
- **(a) [recommended] No new codes; reserve the 15xxx block.** All failures are resource-owned misses
  reusing `ExpenseNotFound` (6000) / `ShareNotFound` (7000) / `EventNotFound` (9000) / `MemberNotFound`
  (3000). Reserve **15xxx** in `ErrorCodes.cs` with a comment (define nothing yet). Confirmed next-free
  block: 10xxx/11xxx are reserved-empty; 12xxx–14xxx are claimed; 15xxx is free. Trade-off: none; continues
  the one-block-per-feature reservation.
- **(b)** Add a `MemberNotEventParticipant` code for the OQ9 non-participant case. Trade-off: a distinct
  machine code, but reusing `MemberNotFound` (404, resource-owned) leaks nothing and is simpler.

**OQ13 — Wire the overlay into the event QR flow now?**
> ~~OQ13~~ → **Answered 2026-07-21 (option a):** **yes** — `WalletQrService.GenerateEventQrAsync` bills only
> members with `outstanding > 0` (uncleared owing); all cleared → `NoOutstandingDebtForQr` (12003).
- **(a) [recommended] Yes — QR bills only members with `outstanding > 0`.** `WalletQrService.GenerateEventQrAsync`
  changes its owing filter from `row.Balance < 0` to `row.Outstanding > 0` (i.e. negative balance AND not
  yet cleared, OQ8a), so regenerating the QR after some members pay bills only the remainder; if everyone
  has cleared → `NoOutstandingDebtForQr` (12003). Matches the UC ("chuyển xong An đánh dấu đã trả").
  Trade-off: a small change to the M9 QR service (impact-analyzed below); the `NoOutstandingDebtForQr`
  semantics widen from "no negative balances" to "no uncleared negative balances".
- **(b)** Leave the QR billing all negative-balance members; the overlay is display-only for now.
  Trade-off: smaller blast radius, but a cleared member keeps getting a QR — a worse UX and a follow-up.

**OQ14 — Empty / edge cases + loose expenses.**
> ~~OQ14~~ → **Answered 2026-07-21 (option a):** loose expenses get **Layer A per-share settled only** (no
> Layer B rollup); empty event → empty overlay; non-negative balance → `outstanding = 0`; 0đ-share toggle
> is a no-op; the per-member toggle is valid only for a real-event participant.
- **(a) [recommended]** Loose expenses (no event): **Layer A per-share settled applies**; there is **no
  Layer B rollup** (no event) — loose-expense debt remains tracked by the whole-expense flag (§3.5) + the
  new per-share flags. The per-member-per-event toggle is only valid for a member participating in a real
  event. An event with no expenses → balance rows empty → overlay empty, `totalOutstanding = 0`. A member
  with `balance >= 0` → `outstanding = 0` regardless of any stored flag. Toggling settled on a 0đ share is
  a no-op derivation (OQ6). Trade-off: two settled axes with slightly different applicability (loose vs
  event) the UI must communicate; correct per the spec.
- **(b)** Also allow a Layer B toggle on loose expenses (per-member across all loose expenses). Trade-off:
  beyond §3.7 (balance is per-event only); the whole-expense/per-share flags already cover loose expenses.

**OQ15 — Overlay + toggle DTO shapes.**
> ~~OQ15~~ → **Answered 2026-07-21 (option a):** `ShareResponse` += `IsSettled`/`SettledAt`;
> `MemberBalanceRow` += `Outstanding`/`IsSettled`/`SettledAt`; `EventBalanceResponse` +=
> `TotalOutstanding`/`OwingMemberCount`/`SettledMemberCount`; toggle bodies reuse `SetSettledRequest`.
- **(a) [recommended]** `ShareResponse` gains `bool IsSettled` + `DateTime? SettledAt`. `MemberBalanceRow`
  gains `decimal Outstanding` + `bool IsSettled` (Layer B) + `DateTime? SettledAt`. `EventBalanceResponse`
  gains `decimal TotalOutstanding` + `int OwingMemberCount` + `int SettledMemberCount`. Toggle requests
  reuse `SetSettledRequest { bool IsSettled }`. All money `decimal`. Trade-off: additive growth of two
  shipped DTOs; consistent with the M5/M7 denormalized-row idiom.
- **(b)** New dedicated overlay DTOs separate from `MemberBalanceRow`. Trade-off: avoids touching the M7
  DTO, but duplicates the member identity fields and forces the client to zip two lists.

## Assumptions

> These are working assumptions pending the checkpoint; each is derived from the spec, the locked D1/D2
> decisions, and prior planning docs. If any is wrong the user should flag it at the checkpoint.

- All settled/overlay endpoints are **guarded** (valid access token); no anonymous access.
- The **owner** is always the current authenticated user; `actor`/sharing concerns (§6) are out of scope.
- Settled flags never alter any `amount`; the DB CHECK and decimal money model are untouched.
- The M7 balance figures (`advanced`/`owed`/`balance`) and their sum-to-zero invariant are **not changed**
  (D2 / M7 OQ2); only additive overlay fields are introduced.
- Writes are single `ExecuteTransactionAsync` blocks with `NoCommit()` on failure; the transaction stays
  in the repository (non-nesting convention).
- The new settled writes are **not audited** (OQ10) and **not tier-gated** (OQ11).
- The migration data backfill is effectively a no-op on the dev DB (no real data) but must be correct.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repos use `[ScopedService]`. All
> user-facing strings via the D1 localization keys (added to both resx files). Names reflect the
> **recommended option (a)** for every Open Question; steps re-sync if the user picks otherwise. Steps that
> modify shipped files are marked **[MOD]**.

### Step 1 — Entities

1. **[MOD]** `Database/Entities/Share.cs` — add `bool IsSettled` and `DateTime? SettledAt` (with XML docs:
   payment metadata, does not change `Amount`, §3.5). (OQ2a)
2. **[MOD]** `Database/Entities/Partials/Share.cs` — in `ConfigureModel`: map `is_settled`
   (`bool`, default `false`) and `settled_at` (`datetime(6)`, nullable). No new index needed (toggles are
   keyed by the existing `uuid`/`(expense_id, member_id)`).
3. `Database/Entities/EventMemberSettlement.cs` + `Partials/EventMemberSettlement.cs` (OQ1a). Lightweight
   state row, **not** `IEntity` (composite-PK, mirroring the `ExpenseTag` precedent for a state row keyed
   by a pair): `ulong EventId`, `ulong MemberId`, `bool IsSettled`, `DateTime? SettledAt`, `DateTime
   CreatedAt`, `DateTime UpdatedAt`; navs `Event Event`, `Member Member`. `ConfigureModel`: table
   `event_member_settlements`; **composite PK `(event_id, member_id)`**; FK `event_id` → `events.id`
   **cascade** (delete the event ⇒ drop its settlement rows), FK `member_id` → `members.id` restrict;
   `updated_at` computed default. (If the user prefers a full `IEntity` with `uuid` — OQ1 (b)/(c) or a
   surrogate — this step re-syncs.)
4. **[MOD]** `Database/AppDbContext.cs` — add `DbSet<EventMemberSettlement> EventMemberSettlements` and
   invoke `EventMemberSettlement.ConfigureModel(modelBuilder)` in `OnModelCreating`. `AppDbContext.partial.cs`
   untouched.

### Step 2 — EF migration (**REQUIRED**)

- `dotnet ef migrations add AddPerMemberSettlement --project .\FairShareMonApi\FairShareMonApi.csproj`
  (offline via the pinned design-time factory). **Migration name: `AddPerMemberSettlement`.**
- The one migration: (1) **ALTERs `shares`** adding `is_settled` (`NOT NULL DEFAULT 0`) + `settled_at`
  (nullable `datetime(6)`); (2) **CREATEs `event_member_settlements`** (composite PK, FKs, utf8mb4/unicode_ci);
  (3) a **data step** (raw SQL inside the migration `Up`) backfilling per-share settled from already-settled
  expenses (OQ4a):
  ```sql
  UPDATE shares s
  JOIN expenses e ON e.id = s.expense_id
  SET s.is_settled = 1, s.settled_at = e.settled_at
  WHERE e.is_settled = 1;
  ```
  `Down` drops the table + columns (the data step is not reversed — dropping the columns discards it).
- Review the generated migration + keep the model snapshot in sync; apply to the dev DB during the Test
  step per the orchestration protocol.

### Step 3 — Error codes + message keys

- **[MOD]** `Constants/ErrorCodes.cs` — reserve the **15xxx** block with a comment; **define no new codes**
  (OQ12a). Reuse `ShareNotFound` (7000), `ExpenseNotFound` (6000), `EventNotFound` (9000), `MemberNotFound`
  (3000).
- **[MOD]** `Constants/MessageKeys.cs` + both resx files (`StringResources.resx` vi-VN neutral +
  `StringResources.en-US.resx`) — add **success** keys:
  - `Success.ShareSettledUpdated` — vi "Đã cập nhật trạng thái đã trả của phần gánh." / en "Share settled status updated."
  - `Success.MemberSettledUpdated` — vi "Đã cập nhật trạng thái đã trả của thành viên." / en "Member settled status updated."
  No new error message keys (reused misses already have keys: `ShareNotFound`, `ExpenseNotFound`,
  `EventNotFound`, `MemberNotFound`).

### Step 4 — Repositories

- **[MOD]** `Repositories/ShareRepository.cs` — add
  `Task<ExpenseWriteStatus> SetSettledAsync(string userUuid, string expenseUuid, string shareUuid, bool isSettled, CancellationToken)`:
  one `ExecuteTransactionAsync`; resource-own the expense (miss → `ExpenseNotFound`) and its share (miss →
  `ShareNotFound`) with tracking, `Include(Shares)`; set `share.IsSettled` + `share.SettledAt`;
  **reconcile** `expense.IsSettled`/`SettledAt` per the OQ3a predicate (all billable shares settled);
  **no `EventWriteGuard`** (closed-event exception, OQ5a); **no audit** (OQ10a). Returns `Success` /
  `ShareNotFound` / `ExpenseNotFound`.
- **[MOD]** `Repositories/ExpenseRepository.cs` `SetSettledAsync` — extend to **cascade** to shares (OQ3a):
  load the expense with `Include(Shares)`; set `expense.IsSettled`/`SettledAt`; set every billable share's
  `is_settled`/`settled_at` to match (payer-own + 0đ shares treated per OQ6a). Still no guard, no audit.
- `Repositories/EventMemberSettlementRepository.cs` — new `IEventMemberSettlementRepository` + sealed impl
  (`[ScopedService]`, extends `BaseRepository`):
  - `Task<SettlementWriteStatus> SetMemberSettledAsync(string userUuid, string eventUuid, string memberUuid, bool isSettled, CancellationToken)`:
    one `ExecuteTransactionAsync`; resource-own the event (miss → `EventNotFound`); resolve the member as
    an **owned participant** of the event (payer of, or share-holder in, one of the event's expenses;
    else `MemberNotFound`); **upsert** the `(event_id, member_id)` row (`is_settled`,
    `settled_at = isSettled ? AppDateTime.Now : null`); no guard (OQ5a), no audit (OQ10a).
  - `Task<IReadOnlyDictionary<ulong, EventMemberSettlement>> GetByEventAsync(string userUuid, ulong eventId, CancellationToken)`
    — read-only, for the overlay (keyed by `member_id`).
  - `SettlementWriteStatus { Success, EventNotFound, MemberNotFound }` (small enum in the same file).
- **[MOD]** `Repositories/StatsRepository.cs` `GetEventBalanceAsync` — additively **load the event's
  settlement flags** (a second small `Query<EventMemberSettlement>().Where(s => s.EventId == eventId)`
  materialize keyed by `member_id`) and enrich each `MemberBalanceAggregate` with `IsSettled`/`SettledAt`.
  Advanced/owed/balance computation is **unchanged** (D2 — still the same single share-set, sum-to-zero
  preserved). Do not filter by settled.

### Step 5 — Aggregates + DTOs

- **[MOD]** `Repositories/Stats/StatsAggregates.cs` — extend `MemberBalanceAggregate` with
  `bool IsSettled` + `DateTime? SettledAt` (defaulting false/null for non-participants and members not in
  the settlement table).
- **[MOD]** `Models/Shares/ShareResponse.cs` — add `bool IsSettled` + `DateTime? SettledAt` (Vietnamese
  XML docs). (OQ15a)
- **[MOD]** `Models/Stats/MemberBalanceRow.cs` — add `decimal Outstanding`, `bool IsSettled`,
  `DateTime? SettledAt` (Vietnamese XML docs: outstanding = còn nợ ròng; isSettled = đã trả). (OQ15a)
- **[MOD]** `Models/Stats/EventBalanceResponse.cs` — add `decimal TotalOutstanding`, `int OwingMemberCount`,
  `int SettledMemberCount`. (OQ15a)
- Reuse `Models/Expenses/SetSettledRequest.cs { bool IsSettled }` for both new toggle bodies (OQ15a).

### Step 6 — Services + mappings

- **[MOD]** `Services/Api/Shares/SharesService.cs` — add
  `Task SetSettledAsync(string userUuid, string expenseUuid, string shareUuid, SetSettledRequest request, CancellationToken)`
  delegating to `ShareRepository.SetSettledAsync` and mapping `ExpenseWriteStatus` → `ErrorException`
  (`ShareNotFound` 7000 / `ExpenseNotFound` 6000).
- `Services/Api/Events/` (or a small new `ISettlementService`) — recommend extending **`IEventsService`**
  with `Task SetMemberSettledAsync(string userUuid, string eventUuid, string memberUuid, SetSettledRequest request, CancellationToken)`
  delegating to `IEventMemberSettlementRepository` and mapping `SettlementWriteStatus` → `ErrorException`
  (`EventNotFound` 9000 / `MemberNotFound` 3000). (Keeps event-scoped state on the events service.)
- **[MOD]** `Services/Api/Stats/StatsService.cs` `GetEventBalanceAsync` — after mapping rows, compute the
  overlay in one place: per row `Outstanding = (Balance < 0m && !IsSettled) ? -Balance : 0m`; set the
  response `TotalOutstanding = Σ Outstanding`, `OwingMemberCount = count(Outstanding > 0)`,
  `SettledMemberCount = count(Balance < 0 && IsSettled)`. Balance figures untouched.
- **[MOD]** `Mappings/StatsProfile.cs` — map the new `MemberBalanceAggregate` fields
  (`IsSettled`/`SettledAt`) onto `MemberBalanceRow`; `Outstanding` is computed in the service (single
  source), not in the profile.
- **[MOD]** `Mappings/ShareProfile.cs` — map `Share.IsSettled`/`SettledAt` → `ShareResponse`.

### Step 7 — Controllers

- **[MOD]** `Controllers/ExpensesController.cs` — add the per-share settled route (Vietnamese Swagger):

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `PUT api/v1/expenses/{expenseUuid}/shares/{shareUuid}/settled` | `SetSettledRequest` → `ApiResult` message | per-share toggle; allowed on closed events (OQ5a); no audit; miss → 7000/6000; success msg `Success.ShareSettledUpdated` |

- **[MOD]** `Controllers/EventsController.cs` — add the per-member-per-event settled route:

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `PUT api/v1/events/{eventUuid}/members/{memberUuid}/settled` | `SetSettledRequest` → `ApiResult` message | per-member net-clearance toggle; participant only (else 3000); event miss → 9000; allowed on closed events (OQ5a); no audit; success msg `Success.MemberSettledUpdated` |

- **[MOD]** `GET api/v1/events/{uuid}/balance` — response now carries the overlay fields (Step 5); route,
  verb, and resource-owned 404 (`9000`) unchanged.

### Step 8 — QR integration (OQ13a)

- **[MOD]** `Services/Api/Wallet/WalletQrService.cs` `GenerateEventQrAsync` — change the owing filter from
  `row.Balance < 0m` to `row.Outstanding > 0m` (bill only uncleared owing members); the per-member amount
  is `row.Outstanding` (equals `-row.Balance` for an uncleared member). If none remain →
  `NoOutstandingDebtForQr` (12003). No new codes. (If the user picks OQ13b, this step is dropped.)

### Step 9 — Tests (owned by the test-engineer; definitive list)

Reuse the shipped harness: `[Collection("AuthIntegration")]`; DB tests on the `ExpenseDbTestBase` /
`ExpenseApiTestBase` families (own connections / app DI + real HTTP), unique lowercase username prefix per
class, dispose-time cascade cleanup; all DB-dependent tests `[SkippableFact]` (skip when MariaDB
unreachable), never EF InMemory. The user-cascade cleanup already removes expenses/shares/events; the new
`event_member_settlements` rows drop via the `event_id` FK cascade when the event is removed (add a sweep
if a class leaves orphan events).

**Unit (no DB):**
- `SharesService.SetSettledAsync` (fake repo) — maps `Success`; `ShareNotFound` → 7000; `ExpenseNotFound`
  → 6000.
- `EventsService.SetMemberSettledAsync` (fake repo) — maps `Success`; `EventNotFound` → 9000;
  `MemberNotFound` → 3000.
- `StatsService.GetEventBalanceAsync` overlay math (fake `IStatsRepository`): `outstanding` = `-balance`
  for an uncleared negative-balance member, `0` when that member is `isSettled`, `0` for a non-negative
  balance; `totalOutstanding`/`owingMemberCount`/`settledMemberCount` correct; **balance/advanced/owed
  unchanged** vs the M7 expectations (regression guard on D2).
- `WalletQrService` (fake `IStatsService`, OQ13a) — bills only `outstanding > 0` members; all-cleared →
  `NoOutstandingDebtForQr` (12003).

**Integration (real MariaDB):**
- `ShareRepositoryTests` (settled): toggling a share sets `is_settled`/`settled_at`; **reconciliation** —
  after all billable shares settled the expense `is_settled` flips true (and `settled_at` set), unsettling
  one flips it back; the **payer's own share and 0đ shares are treated as settled-by-definition** (an
  expense with only a payer share + 0đ owner-rep share reconciles to settled without toggling them, OQ6a);
  a per-share toggle **writes no audit row** (OQ10a) and **does not change any `amount`**; toggling on a
  **CLOSED** event's expense **succeeds** (OQ5a). Resource-owned: another user's share/expense → miss
  (never the row).
- `ExpenseRepositoryTests` (settled cascade): `SetSettledAsync(true)` marks all billable shares settled;
  `false` clears them; still no guard/no audit; closed event still allowed.
- `EventMemberSettlementRepositoryTests`: mark a participating member settled → row upserted; re-mark →
  idempotent update of `settled_at`; unmark → `is_settled=false`, `settled_at=null`; non-participant /
  foreign member → `MemberNotFound`; foreign/unknown event → `EventNotFound`; marking on a **CLOSED** event
  → **succeeds** (OQ5a); deleting the event cascades away the settlement rows.
- `StatsRepositoryTests` (overlay): the §3.7 Bình +300k / Cường −500k scenario — `outstanding` = 500k for
  uncleared Cường, `0` after Cường is marked settled, `0` for Bình (owed); `Σ balance == 0` **unchanged**
  by any settled flag (D2 / M7 OQ2 regression); soft-deleted participant still appears with its overlay.
- `MigrationBackfillTests` (or a seeded `[SkippableFact]`): an expense that was `is_settled=true` before
  the migration has all its shares `is_settled=true` with `settled_at = expense.settled_at`; `event_member_settlements`
  is empty (no Layer B backfill, OQ4a).

**Endpoint (WebApplicationFactory):**
- `PUT /expenses/{expenseUuid}/shares/{shareUuid}/settled` and
  `PUT /events/{eventUuid}/members/{memberUuid}/settled` through real HTTP wrapped in `ApiResult`;
  anonymous → 401; resource-owned 404 (`7000`/`6000`/`9000`/`3000`, never 403).
- `GET /events/{uuid}/balance` returns the overlay fields; `outstanding`/`totalOutstanding` reflect a
  member marked settled; the balance numbers are byte-for-byte the M7 values.
- Event QR (OQ13a) end-to-end: close an event, mark one of two owing members settled, generate the QR →
  only the remaining member is billed; mark both → `NoOutstandingDebtForQr` (12003).

### Step 10 — Wrap-up

- Update this doc's Progress Log + Final Outcome; move the answered OQs into the Decision Log.
- Record the migration name and that the data backfill ran (explicit change, unlike M7).
- Note any Pomelo query-translation fallbacks for the settlement-flags join.

## Impact Analysis

**APIs:**
- **New:** `PUT api/v1/expenses/{expenseUuid}/shares/{shareUuid}/settled` (per-share toggle);
  `PUT api/v1/events/{eventUuid}/members/{memberUuid}/settled` (per-member-per-event toggle).
- **Changed (additive):** `GET api/v1/events/{uuid}/balance` response gains overlay fields
  (`MemberBalanceRow.Outstanding/IsSettled/SettledAt`, `EventBalanceResponse.TotalOutstanding/OwingMemberCount/SettledMemberCount`);
  `ShareResponse` (returned inside `ExpenseResponse` and the share sub-routes) gains `IsSettled/SettledAt`.
- **Behavior change:** `PUT api/v1/expenses/{uuid}/settled` now cascades to shares (OQ3a) — request/response
  shape unchanged. Event QR bills uncleared owing members only (OQ13a) — same route/DTO.

**Database (REQUIRES MIGRATION — `AddPerMemberSettlement`):**
- **ALTER `shares`:** add `is_settled` (`NOT NULL DEFAULT 0`) + `settled_at` (nullable `datetime(6)`).
- **CREATE `event_member_settlements`** (composite PK `(event_id, member_id)`, FKs `event_id` cascade /
  `member_id` restrict, `is_settled`, `settled_at`, `created_at`, `updated_at`).
- **Data step:** backfill per-share settled from already-settled expenses (OQ4a); no Layer B backfill.
- Money model (`decimal(18,2)`, CHECK, non-negative) untouched.

**Infrastructure:** none (no Redis/workers/packages).

**Services:**
- **New:** `IEventMemberSettlementRepository`/`EventMemberSettlementRepository` (+ `SettlementWriteStatus`);
  `Database/Entities/EventMemberSettlement.cs` (+ Partials).
- **Modified:** `ShareRepository` (+`SetSettledAsync`), `ExpenseRepository.SetSettledAsync` (cascade),
  `StatsRepository.GetEventBalanceAsync` (load flags), `StatsService.GetEventBalanceAsync` (overlay),
  `SharesService`, `EventsService` (+ member-settled method), `WalletQrService` (OQ13a), `AppDbContext`,
  `StatsAggregates`, `ShareResponse`, `MemberBalanceRow`, `EventBalanceResponse`, `StatsProfile`,
  `ShareProfile`, `ExpensesController`, `EventsController`, `ErrorCodes.cs` (reserve 15xxx), `MessageKeys.cs`
  + both resx files.

**UI:** none in this repo (API only). The web planner consumes the endpoint contract below; the FairShareMonWeb
`SettledToggle`, `EventBalanceTable`, and expenses/events types will need updates (separate web plan).

**Documentation:** this planning doc; Vietnamese Swagger annotations on the two new routes; the D1
message keys.

## Decision Log

> **Locked at the 2026-07-21 user checkpoint (top-level) — do NOT reopen:**

1. **D1 — BOTH layers ship.** Per-Share settled (`shares.is_settled`/`settled_at`) AND a
   per-member-per-event net-clearance rollup. The existing whole-expense `Expense.IsSettled`/`SettledAt`
   stays and is reconciled/derived against the per-share flags where sensible.
2. **D2 — Balance stays PURE; overlay is derived.** The M7 debt balance (`advanced`/`owed`/`balance`,
   sum-to-zero) is unchanged and still ignores settled (M7 OQ2 preserved verbatim). A derived "còn nợ / đã
   trả" overlay (outstanding per member + which members cleared) is surfaced alongside the balance and must
   not perturb the balance numbers.

> **Inherited decisions (locked upstream — NOT reopened):** balance ignores `is_settled` and sums to zero
> (M7 OQ1/OQ2); balance is per-event only, loose expenses excluded (§3.7/§5); settled is not audited (M5
> OQ11); settled is the sole write allowed on a closed event (§3.5/§4.4); money `decimal(18,2)`,
> non-negative, DB CHECK (§4.3/M5 OQ2); expenses/shares hard-deleted, members soft-deleted (§4.7);
> resource-owned 404-never-403 (§4.1); domain terms expense/share/event/settled (§5).

> **Resolved at the 2026-07-21 user checkpoint — all 15 OQs accepted at the recommended option (a).** One
> numbered entry per OQ (binding decision + one-line reason); the full options/trade-offs are preserved
> inline under each matching OQ above.

1. **OQ1 — Layer B STORED (a):** new `event_member_settlements` table keyed `(event_id, member_id)`.
   *Reason:* net clearance is a net-level fact the QR/UC acts on; deriving from gross per-share flags
   over-counts when a member both advanced and owed.
2. **OQ2 — Layer A columns + one migration (a):** `shares.is_settled`/`settled_at` via
   `AddPerMemberSettlement`. *Reason:* mirrors the `expenses` pair; the feature is one logical unit.
3. **OQ3 — Cascade + reconcile, keep the column (a):** whole-expense toggle cascades to shares; per-share
   toggles recompute `Expense.IsSettled` over billable shares. *Reason:* one coherent settled story,
   backward-compatible with the M5 endpoint.
4. **OQ4 — Backfill shares only (a):** settled expenses → shares settled; no Layer B backfill. *Reason:*
   keeps the reconciliation invariant true; net clearance is asserted going forward, not fabricated.
5. **OQ5 — Both settled writes bypass the guard (a):** allowed on closed events (§4.4 sole exception).
   *Reason:* settled is payment metadata; Layer B is primarily a post-close action.
6. **OQ6 — Payer-own + 0đ shares settled-by-definition (a):** excluded from `outstanding`; toggle is a
   no-op; no payer-change cascade. *Reason:* nothing is owed on them; avoids write coupling.
7. **OQ7 — Extend `/events/{uuid}/balance` + two `/settled` toggle routes (a).** *Reason:* the overlay is
   intrinsic to the balance the UI/QR already read; nested routes give natural 404 scopes.
8. **OQ8 — `outstanding` driven by Layer B net (a):** Layer A gross does not reduce it. *Reason:* the QR
   bills net; ties the overlay to the exact net debt without perturbing the pure balance.
9. **OQ9 — Any participant, OPEN or CLOSED (a):** non-participant → `MemberNotFound` 3000; open-drift
   accepted. *Reason:* §3.5/§3.7 place no lifecycle gate; the real workflow marks paid after close.
10. **OQ10 — No audit (a):** neither settled toggle is audited; `Share` snapshot unchanged. *Reason:*
    settled is payment metadata, not số liệu (mirrors M5 OQ11).
11. **OQ11 — No tier gate (a).** *Reason:* settled is a Free basic feature; limits only block create (§4.9).
12. **OQ12 — No new codes; reserve 15xxx (a):** reuse 6000/7000/9000/3000. *Reason:* only resource-owned
    misses; continues one-block-per-feature reservation.
13. **OQ13 — QR bills only `outstanding > 0` (a):** all cleared → `NoOutstandingDebtForQr` 12003.
    *Reason:* matches the UC (mark paid after transfer); a cleared member stops being billed.
14. **OQ14 — Loose = Layer A only; documented edge cases (a).** *Reason:* balance is per-event only
    (§3.7); loose debt already tracked by the whole-expense + per-share flags.
15. **OQ15 — Additive DTO fields, reuse `SetSettledRequest` (a).** *Reason:* consistent with the M5/M7
    denormalized-row idiom; avoids duplicate identity fields.

## Progress Log

### 2026-07-21

- Started planning "Đánh dấu đã trả theo từng thành viên" (settled per member, §6 future item).
- Read the source of truth `The-ideal.md` (§2, §3.5, §3.7, §3.10, §4.1/§4.3/§4.4/§4.7, §5, §6) and
  `CLAUDE.md` + `.claude/rules/rule.md`.
- Read prior planning docs `debt-balance-and-stats.md` (M7 balance — OQ1/OQ2 sum-to-zero + ignore-settled),
  `expenses-shares-audit.md` (M5 — settled = payment metadata, not audited; hard delete; money model),
  and `events.md` (M6 — closed-event guard, settled the sole exception) to inherit conventions and locked
  decisions.
- Grounded the plan in the live code: `Expense`/`Share` entities, `ExpenseRepository.SetSettledAsync`
  (no guard/no audit), `ShareResponse`, `StatsRepository.GetEventBalanceAsync` + `MemberBalanceRow` +
  `EventBalanceResponse`, `WalletQrService.GenerateEventQrAsync` (bills `Balance < 0`), `ErrorCodes.cs`
  (confirmed next-free block is **15xxx**), `MessageKeys.cs` + the D1 resx localization subsystem.
- Recorded the locked top-level decisions D1 (both layers) and D2 (pure balance + derived overlay).
- Drafted 15 Open Questions with recommended option (a) + trade-offs; wrote the Implementation Plan,
  endpoint/DTO tables, migration (`AddPerMemberSettlement`), and test list.
- **Checkpoint resolved 2026-07-21:** the user accepted **all 15 Open Questions at the recommended option
  (a)**. Annotated each OQ with its binding answer, added one Decision Log entry per OQ, and confirmed the
  Implementation Plan / endpoint tables / DTO section were already synced to option (a). No open questions
  remain — implementation can start.

## Final Outcome

(pending)

## Future Improvements

- **Drift-aware Layer B (OQ1c/OQ9):** store the net amount cleared (or clear the flag automatically) when
  an OPEN event's balance changes after a member was marked settled, so a stale "đã trả" can't linger.
- **Partial per-member settlement:** track an amount paid (< net debt) so the overlay can show "đã trả
  300k / 500k" instead of a boolean.
- **Reminders (§6 "Nhắc nợ"):** drive automated debt reminders off the per-member outstanding overlay.
- **Audit the settled timeline** (§6 "Audit mở rộng") if disputes over *payment* (not just số liệu) ever
  need a trail — currently deliberately excluded (OQ10).
- **Unify the three "settled" notions in the UI** (whole-expense, per-share gross, per-member-event net)
  with clear labels so users aren't confused by the two axes (OQ8/OQ14).
