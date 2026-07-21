# Settled Per Member (Đánh dấu đã trả theo từng thành viên) — Web

Extend the shipped whole-expense **settled** (đã trả) UI with two finer layers so a user can track
**who has actually paid** — not just "the whole bill is settled". This is the FairShareMonWeb
counterpart to the backend plan `FairShareMonApi/planning/settled-per-member.md`
(`The-ideal.md` §6, line 177). Two surfaces ship together, mirroring the backend's two layers:

- **Layer A — per-Share settled.** A per-member settled toggle on each row of the expense-detail
  **shares** section, alongside the existing whole-expense `SettledToggle` in the header (which now
  cascades to shares on the backend). A derived **rollup indicator** communicates all / partial / none.
- **Layer B — per-member-per-event net clearance + outstanding overlay.** In the event **balance**
  table, a derived "còn nợ / đã trả" overlay: an `outstanding` amount per member, an đã-trả/còn-nợ
  status badge, a per-member **đánh dấu đã trả** toggle, and an event-level summary
  (`totalOutstanding`, X/Y đã trả). The M7 balance figures (`advanced`/`owed`/`balance`) stay PURE and
  unchanged — the overlay is additive (locked decision D2).

> **⚠ Backend contract CONFIRMED (option a throughout) but NOT yet shipped.** The API planning doc
> `FairShareMonApi/planning/settled-per-member.md` was **accepted at the 2026-07-21 user checkpoint at
> option (a) for every OQ** (OQ1 stored Layer B, OQ8 net-driven `outstanding`, OQ13 QR bills
> `outstanding > 0`, OQ15 additive DTO growth, etc.). This web plan is written against that confirmed
> contract. **No web implementation should begin until the backend is actually built and shipped** —
> the two new PUT routes, the additive `GET /balance` overlay fields, the `ShareResponse` growth, and
> the whole-expense cascade do not exist in the running API yet. The top-level decisions D1 (both
> layers) + D2 (pure balance + derived overlay) are user-confirmed (2026-07-21) and are NOT reopened.

## Objective

Implement the §6 "Đánh dấu đã trả theo từng thành viên" front-end on top of the shipped Expenses (M4),
Events (M5), Stats/Balance (M6/M7), and Wallet/QR features, consuming the extended API contract:

- **Per-share settled (Layer A):** a compact per-row toggle in `SharesSection` that flips one share's
  `isSettled`; available on any expense, **including a closed event's expenses** (the §4.4 sole
  write-exception, extended to the finer flag). Reconciles visually with the whole-expense header
  toggle/badge (which the backend now keeps consistent via cascade + recompute — backend OQ3a).
- **Per-member net clearance + outstanding overlay (Layer B):** in `EventBalanceTable`, render the new
  `MemberBalanceRow.outstanding` / `isSettled` / `settledAt` and the `EventBalanceResponse`
  event-level rollup (`totalOutstanding`, `owingMemberCount`, `settledMemberCount`); add a per-member
  **đánh dấu đã trả** toggle. Allowed on OPEN and CLOSED events (the sole closed-event write).
- **Pure balance (D2):** never client-recompute or perturb `advanced`/`owed`/`balance`; render the
  overlay fields verbatim from the API (money via `formatMoneyVnd`, never float math).
- **Resource-owned 404 (§4.1):** every read/write is the caller's; a miss (reusing `ExpenseNotFound`
  6000 / `ShareNotFound` 7000 / `EventNotFound` 9000 / `MemberNotFound` 3000) is treated as
  not-found — never leak existence.
- **QR "who still owes":** the event QR (backend OQ13a) bills only members with `outstanding > 0`; the
  web side surfaces the overlay so the user can see who has cleared before/after generating the QR.
- **No new error codes, no new tier gate:** settled is a Free/basic feature; the `errors.ts` mirror is
  unchanged (all reused codes already present).

## Background

Confirmed against the live SPA (2026-07-21):

- **Whole-expense settled UI (M4).** `src/features/expenses/components/SettledToggle.tsx` is a
  `role="switch"` button (color-independent: icon + "Đã trả"/"Chưa trả" text) that calls
  `useSetSettled` → `expensesApi.setSettled(uuid, { isSettled })` → `PUT /v1/expenses/{uuid}/settled`.
  It is mounted in the `ExpenseDetailPage` header actions and is deliberately **never disabled** by the
  closed-event guard (the sole write exception). On error → toast (verbatim server message); success →
  toast + `invalidateExpense(uuid)` refetch. i18n under `expenses:settled.*`.
- **Shares UI (M4).** `src/features/expenses/components/SharesSection.tsx` renders a `Table` of
  member / amount / note + a derived total row, with add/edit/delete controls that are **all
  hidden when `disabled`** (i.e. the owning event is closed, `expense.eventIsClosed === true`). The
  owner-representative row shows a `Badge` (lock/star) and no delete; soft-deleted members show
  "(đã xóa)". `ShareResponse` today = `{ uuid, member, amount, note, createdAt }` — **no settled
  fields yet** (`src/features/expenses/api/types.ts`).
- **Expense-side cache (M4).** `useExpenses.ts`: `expensesKeys = { all, lists, list, detail, history }`;
  `invalidateExpense(uuid)` invalidates `all` + `detail(uuid)` + `history(uuid)`. Mutations are
  refetch-based (no optimistic updates — M4 OQ14a). `useSetSettled` is the settled write. The
  expenses summary/detail DTOs already carry `isSettled`/`settledAt` for the **whole expense**.
- **Balance UI (M5/M7).** `src/features/events/components/EventBalanceTable.tsx` renders the §3.7
  debt-balance `Table` (member / advanced / owed / balance) with a `TableFoot` sum-to-zero total row,
  owner-rep + "(đã xóa)" markers, and a color-independent signed-balance polarity word
  (`BalanceAmount`). It reads `useEventBalanceQuery(uuid)` → `eventsApi.balance(uuid)` →
  `GET /v1/events/{uuid}/balance` → `EventBalanceResponse { eventUuid, eventName, isClosed, rows }`
  with `MemberBalanceRow { memberUuid, memberName, isOwnerRepresentative, isDeleted, advanced, owed,
  balance }` (`src/features/events/api/types.ts`) — **no overlay fields yet**. Rendered for OPEN and
  CLOSED events (M5 OQ8a). i18n under `events:balance.*` (already has `advanced`=Đã ứng, `owed`=Phải
  gánh, `balance`=Cân bằng, `ownerRep`, `deletedTag`, positive/negative/zero labels).
- **Event-side cache (M5).** `useEvents.ts`: `eventsKeys = { all, lists, list, detail, balance }`;
  `invalidateEvent(uuid)` invalidates `all` + `detail(uuid)` + `balance(uuid)`;
  `invalidateEventAndExpenses` also reaches `expensesKeys.all`. Cross-feature invalidation between
  expenses ↔ events is already an established pattern (`useAssignExpenseEvent`, `useCreateExpense`).
- **Event detail (M5).** `src/features/events/pages/EventDetailPage.tsx` `DetailView` gates every
  write control on `!closed`; when closed it shows a warning `Alert` and hides Edit/Delete/Close/Assign
  and keeps Export + the QR button (QR only appears when closed) + the balance table. `EventBalanceTable`
  is mounted unconditionally.
- **QR (M7).** `src/features/wallet/components/QrDialog.tsx` (shared expense/event modal) fetches the
  event QR PNG via `useEventQrQuery`; the backend decides who is billed. It maps
  `NoOutstandingDebtForQr` (12003) → an informational "nobody owes" `Alert`, `EventNotClosedForQr`
  (12002) → warning, Premium-gate `13003` → `UpgradePrompt`, ownership 404 (`6000`/`9000`) →
  toast + close. The QR button is closed-only on the event detail.
- **Error mirror.** `src/lib/api/errors.ts` already has `MemberNotFound 3000`, `ExpenseNotFound 6000`,
  `ShareNotFound 7000`, `EventNotFound 9000`, and the `notFound` classification for `6000/7000/9000`
  via `classifyError`. Backend reserves 15xxx but defines nothing (OQ12a) — **no `errors.ts` change**.
- **Domain terms (locked).** expense (phiếu chi tiêu), share (phần gánh), event (đợt), settled (đã
  trả), đã ứng / phải gánh / cân bằng (advanced / owed / balance). New overlay term: **còn nợ**
  (outstanding).

### Proposed API contract this UI consumes (from the API doc, PENDING confirmation)

| Verb + path | Request → Response `data` | Notes |
| --- | --- | --- |
| `PUT /v1/expenses/{expenseUuid}/shares/{shareUuid}/settled` | `SetSettledRequest { isSettled }` → `MessageResponse` | per-share toggle (Layer A); allowed on closed events; no audit; miss → `7000`/`6000` |
| `PUT /v1/events/{eventUuid}/members/{memberUuid}/settled` | `SetSettledRequest { isSettled }` → `MessageResponse` | per-member net-clearance toggle (Layer B); participant only (else `3000`); event miss → `9000`; allowed on closed events; no audit |
| `GET /v1/events/{uuid}/balance` (EXTENDED, additive) | — → `EventBalanceResponse` | `MemberBalanceRow` gains `outstanding: number`, `isSettled: boolean`, `settledAt: string\|null`; response gains `totalOutstanding: number`, `owingMemberCount: number`, `settledMemberCount: number`. `advanced`/`owed`/`balance` unchanged (D2). |
| `ShareResponse` (inside `ExpenseResponse`) EXTENDED | — | gains `isSettled: boolean`, `settledAt: string\|null` |
| `PUT /v1/expenses/{uuid}/settled` (existing) | unchanged shape | now **cascades to shares** on the backend (OQ3a) — the whole-expense toggle marks every billable share |

## Requirements

### Functional

1. **R1 — Per-share settled toggle (Layer A).** In `SharesSection`, add a per-row settled control
   (color-independent switch: icon + đã trả/chưa trả text) that flips `share.isSettled` via
   `PUT /v1/expenses/{expenseUuid}/shares/{shareUuid}/settled`. It is **exempt from the closed-event
   `disabled` gate** (the sole write allowed on a closed event) — unlike the add/edit/delete controls
   that stay hidden when `disabled`.
2. **R2 — Whole-expense ↔ per-share reconciliation (D1).** The existing header `SettledToggle` stays as
   the "mark the whole bill paid" action (backend cascades it to all billable shares). Add a derived
   **rollup indicator** in the shares section communicating all / partial (X/Y) / none settled, so the
   two controls tell one coherent story. The header badge already reflects the backend-reconciled
   `expense.isSettled` (all billable shares settled).
3. **R3 — Payer's own share & 0đ shares.** Per backend OQ6a these are "settled-by-definition" in every
   derivation (excluded from `outstanding`, treated as settled in the rollup), and a toggle on them is
   a harmless no-op. The UI must communicate this so users are not confused by a payer/0đ share that
   reads "chưa trả" yet doesn't block the whole-expense rollup (see OQ3).
4. **R4 — Outstanding overlay (Layer B).** In `EventBalanceTable`, render per `MemberBalanceRow`:
   `outstanding` (VND, còn nợ), an **đã trả / còn nợ** status badge (color-independent), and
   (`settledAt` on hover/title where relevant). Add an event-level summary using `totalOutstanding` +
   `owingMemberCount` + `settledMemberCount` (e.g. "Đã trả 2/5 thành viên · Còn nợ 300.000 đ"). The
   `advanced`/`owed`/`balance` columns and the sum-to-zero footer are **untouched**.
5. **R5 — Per-member đánh dấu đã trả toggle.** Add a per-member settled toggle in the balance overlay
   calling `PUT /v1/events/{eventUuid}/members/{memberUuid}/settled`. It is enabled on OPEN **and**
   CLOSED events (the sole closed-event write). Only meaningful for members who owe (`balance < 0`);
   see OQ5 for whether/how to present it for owed/settled members.
6. **R6 — Closed-event read-only exception.** On a closed event, `EventDetailPage` hides every write
   control **except** the settled surfaces: the per-member overlay toggle stays enabled, and (on the
   closed event's expenses) the per-share toggle stays enabled. This is the sole write exception —
   enforced on the web by not gating these two controls behind the closed flag.
7. **R7 — QR "who still owes".** The event QR bills only `outstanding > 0` members (backend OQ13a);
   `NoOutstandingDebtForQr` (12003) now means everyone has cleared. The overlay in the balance table
   is the web surface for "who still owes"; see OQ8 for whether to add an explicit remaining-owing cue
   near the QR button.
8. **R8 — Cache invalidation across expenses ↔ balance.** A per-share toggle refetches the expense
   detail (the backend may recompute `expense.isSettled`) and the expenses list; a per-member toggle
   refetches the event balance overlay. Invalidation scope is specified per OQ7 (whether a per-share
   toggle should also refresh the event overlay).

### Non-functional / conventions

- One centralized `api` client; feature-local types mirroring the extended DTOs. Branch on numeric
  `code`; render `error.message` verbatim (`resolveErrorMessage`).
- Money: render `outstanding`/`totalOutstanding` via `<Money>`/`formatMoneyVnd`; **never float math**,
  never client-sum or re-derive the balance or the overlay totals (the API is authoritative).
- Datetimes: `settledAt` via `formatDateTime` in the viewer's zone (only if surfaced).
- i18n: extend the existing `expenses` and `events` namespaces (vi-VN authoritative + en-US parity);
  reuse the fixed domain terms; new term **còn nợ** = outstanding.
- a11y: toggles are `role="switch"` + `aria-checked` with a labelled accessible name; status conveyed
  by icon + text, never color alone; the overlay columns have proper `scope`/`numeric` cells.
- No `errors.ts` change (reused codes already present); append-only if a genuine gap surfaces (flag it).

## Open Questions

> **RESOLVED — all 10 OQs accepted at recommended option (a) at the 2026-07-21 user checkpoint; the
> backend contract was likewise confirmed at option (a) throughout.** Each OQ below carries its
> resolved annotation. The Implementation Plan is already synced to option (a). The top-level D1 (both
> layers) + D2 (pure balance + derived overlay) are locked and NOT reopened. Implementation still waits
> on the backend being built + shipped (see the ⚠ dependency at the top).

### ~~OQ1~~ — How to build the two new toggles vs the shipped `SettledToggle`

> **Answered 2026-07-21 (option a): extract a presentational `SettledSwitch` and build the shipped
> whole-expense toggle + the new per-share + per-member toggles on top of it.**

There are three settled toggles now: whole-expense (shipped), per-share (new), per-member-event (new).

- **(a) [recommended] Extract a presentational `SettledSwitch`** (the `role="switch"` button + icon +
  label markup + `SettledToggle.module.css`) and build all three on top of it: refactor the shipped
  `SettledToggle` to render `SettledSwitch` (behavior unchanged — same mutation/toast/aria), and add
  thin wrappers `ShareSettledToggle` (per-share) and `MemberSettledToggle` (per-member) that own their
  own mutation + toast + accessible name. Trade-off: a small refactor of a shipped component (guarded
  by its existing tests + gitnexus impact), but avoids three near-duplicate switch implementations and
  keeps the color-independent a11y contract in one place.
- **(b)** Leave `SettledToggle` as-is and add two independent new components duplicating the switch
  markup/CSS. Trade-off: zero touch to shipped code, but three copies drift over time (a11y/visual).
- **(c)** Generalize `SettledToggle` with a `variant`/`onToggle` prop instead of extracting a
  presentational primitive. Trade-off: one component with three code paths — more conditional logic
  than a clean presentational split.

### ~~OQ2~~ — How the per-share rollup relates to the whole-expense header toggle

> **Answered 2026-07-21 (option a): keep the header toggle as "mark the whole bill" (cascades) + the
> per-share column, plus a derived rollup chip (all / partial X/Y / none).**

- **(a) [recommended] Header toggle = "mark the whole bill", shares column = per-portion, plus a
  derived rollup chip.** Keep the header `SettledToggle` (cascades on the backend). In the
  `SharesSection` header show a derived chip: **all billable settled** → "Đã trả toàn bộ";
  **some** → "Đã trả một phần (X/Y phần)"; **none** → "Chưa trả". The chip is display-only, derived
  from the shares' `isSettled` (billable shares only, per R3). Trade-off: two settled controls on one
  screen (header + per-row) the copy must disambiguate — mitigated by the rollup chip + hint text.
- **(b)** Replace the header toggle with a tri-state control driven by the shares. Trade-off: richer,
  but tri-state switches are an a11y/interaction hazard and the backend keeps `expense.isSettled`
  boolean (reconciled) — a derived read-only chip matches the contract better than a tri-state writer.
- **(c)** Drop the per-share column and keep only the whole-expense toggle. Trade-off: abandons Layer A
  (contradicts D1).

### ~~OQ3~~ — Presentation of the payer's own share & 0đ shares in the per-share column

> **Answered 2026-07-21 (option a): no interactive toggle — show a muted "Không nợ" label for the
> payer's own share and 0đ shares, and exclude them from the rollup X/Y count.**

Per backend OQ6a these are settled-by-definition (excluded from `outstanding` and the rollup); a stored
`isSettled` may read `false` on them while derivations treat them as settled.

- **(a) [recommended] No interactive toggle; show a muted "Không nợ" (not-owed) label** for a share
  whose member is the expense payer or whose `amount === 0`, and **exclude these rows from the rollup
  X/Y count**. Trade-off: the UI diverges slightly from the raw stored flag (which the backend itself
  documents as intentional), but it avoids a confusing toggle that appears to do nothing.
- **(b)** Show a normal toggle on every share (backend accepts the no-op). Trade-off: simplest, but a
  payer/0đ share stuck at "chưa trả" that never affects the rollup is confusing.
- **(c)** Hide payer/0đ shares from the per-share settled column entirely. Trade-off: cleanest column,
  but the shares table still lists them (with amount/note) so a missing settled cell is inconsistent.

> Note: the FE cannot always identify the payer's own share purely from `ShareResponse`
> (`ExpenseResponse.payer` gives the payer member uuid; a share's member uuid is on `share.member.uuid`
> — so payer-share detection IS possible client-side). 0đ detection is `share.amount === 0`. If the
> backend prefers to signal "settled-by-definition" explicitly on the DTO, that is a backend OQ; this
> plan derives it client-side under (a).

### ~~OQ4~~ — Outstanding overlay layout in `EventBalanceTable`

> **Answered 2026-07-21 (option a): add a "Còn nợ" + a "Trạng thái" (badge + toggle) column to the
> existing balance table, and extend the `TableFoot` with `totalOutstanding` + a summary line (with a
> responsive stack on narrow widths).**

- **(a) [recommended] Add two columns to the existing balance table** — a "Còn nợ" (outstanding,
  numeric `<Money>`) column and a "Trạng thái" column holding the đã-trả/còn-nợ badge + the per-member
  toggle — and extend the `TableFoot` with a `totalOutstanding` cell + a summary line
  ("Đã trả {{settled}}/{{total}} · Còn nợ {{amount}}"). Trade-off: the balance table grows from 4 to
  ~6 columns (dense on phones — needs a responsive pass, possibly stacking the status/toggle under the
  member on narrow widths), but keeps balance + overlay in one coherent, sum-to-zero-anchored view.
- **(b)** A separate "Ai đã trả" (who has paid) panel/card beneath the balance table. Trade-off: keeps
  the balance table lean and phone-friendly, but splits one member's figures across two tables and
  duplicates the member identity column.
- **(c)** A compact per-row expander (click a member to reveal outstanding + toggle). Trade-off: least
  visual weight, but hides the primary Layer-B action behind an interaction.

### ~~OQ5~~ — Per-member toggle for members who do NOT owe (`balance >= 0`)

> **Answered 2026-07-21 (option a): render the per-member toggle only for owing members
> (`balance < 0`); owed/zero members show a muted "—" and no toggle.**

`outstanding` is `0` for a member with `balance >= 0` (they are owed, not owing).

- **(a) [recommended] Only render the toggle for members with `balance < 0`.** Owed/zero members show
  no toggle and no "còn nợ" figure (a muted "—" or "được nhận lại" from the existing polarity word).
  Trade-off: the backend accepts marking any participant settled, but marking a non-owing member has no
  overlay effect (`outstanding` stays `0`) so a toggle there is noise.
- **(b)** Render the toggle for every participant. Trade-off: matches the backend surface exactly, but
  a "đánh dấu đã trả" on someone who is owed money is meaningless to the user.
- **(c)** Render a disabled/settled-by-definition indicator for `balance >= 0` members. Trade-off:
  explicit, but adds a state most users won't need.

### ~~OQ6~~ — Optimistic update vs refetch for the two new toggles

> **Answered 2026-07-21 (option a): refetch (invalidate-on-success), matching the shipped
> `SettledToggle` and M4 OQ14a; no optimistic updates.**

- **(a) [recommended] Refetch (invalidate-on-success), matching the shipped `SettledToggle` and the M4
  OQ14a decision.** Toggle → mutate → on success toast + invalidate → the switch reconciles from the
  refetched data; the switch is `disabled` while pending. Trade-off: a brief round-trip before the UI
  reflects the flip (acceptable; consistent with every existing settled/write in the app).
- **(b)** Optimistic update with rollback on error. Trade-off: snappier for rapid per-share toggling,
  but introduces optimistic-cache plumbing the codebase has deliberately avoided so far; better as a
  cross-cutting Future Improvement once the pattern is adopted app-wide.

### ~~OQ7~~ — Cache-invalidation scope of a per-share toggle

> **Answered 2026-07-21 (option a): a per-share toggle invalidates the expenses caches only (not the
> event balance), because `outstanding` is Layer-B (net) driven per the confirmed backend OQ8a.**

- **(a) [recommended] A per-share toggle invalidates the expenses caches only**
  (`invalidateExpense(expenseUuid)` → `all` + `detail` + `history`), NOT the event balance. Rationale:
  per backend OQ8a the event overlay `outstanding` is driven by **Layer B (net)**, not by per-share
  (gross) flags, so a per-share toggle does not change the balance overlay. The whole-expense
  `isSettled` recompute the backend may perform is surfaced by the expense-detail refetch. Trade-off:
  if the backend later resolves OQ8 toward gross-derived outstanding, this must also invalidate
  `eventsKeys.balance`.
- **(b)** Always also invalidate `eventsKeys.all`/`balance` when the toggled expense has an event.
  Trade-off: defensive/safe against a contract change, but an unnecessary refetch under the confirmed
  (a) contract. (The task brief notes "per-share settled can change … the event overlay" — under the
  proposed OQ8a it does not; flagging this explicitly so the user can confirm which model ships.)

### ~~OQ8~~ — QR "who still owes" web wiring

> **Answered 2026-07-21 (option a): display-only overlay — rely on the backend QR filter
> (`outstanding > 0`); only refine the `12003` copy to "đã trả hết", no `QrDialog` logic change.**

- **(a) [recommended] Display-only overlay; rely on the backend QR filter.** The balance-table overlay
  (outstanding + đã-trả badges + summary) is the "who still owes" surface; the QR itself is filtered
  server-side to `outstanding > 0` (backend OQ13a), and `QrDialog` already maps `12003` to a friendly
  "nobody owes" state. No `QrDialog` change beyond copy (optionally refine the 12003 message to
  "mọi người đã trả"). Trade-off: none material; the overlay + existing QR states cover it.
- **(b)** Add an explicit "còn nợ: N thành viên" hint next to the closed-event QR button (from
  `owingMemberCount`), and/or list remaining owing members inside `QrDialog`. Trade-off: more
  discoverable, but duplicates what the balance overlay already shows and adds QR-dialog scope.

### ~~OQ9~~ — Show the Layer-B overlay + toggle on OPEN events, or CLOSED only

> **Answered 2026-07-21 (option a): show the overlay + enable the toggle on OPEN and CLOSED events
> (the drift on OPEN events is an accepted, documented limitation).**

- **(a) [recommended] Show on OPEN and CLOSED** (backend allows both; balance is already shown for
  both — M5 OQ8a). Marking mid-trip is a real workflow. Note the documented drift limitation (an
  OPEN-event stored flag can go stale if the balance later changes — backend OQ9a); optionally show a
  subtle hint that clearance reflects the balance at marking time. Trade-off: an open-event flag can
  drift (accepted; the primary workflow marks paid after close/QR).
- **(b)** Show the overlay read-only on OPEN, enable the toggle only when CLOSED. Trade-off: no drift,
  but blocks partial in-trip settlement the backend permits.

### ~~OQ10~~ — `SetSettledRequest` type location for the events member toggle

> **Answered 2026-07-21 (option a): define a feature-local `SetSettledRequest { isSettled: boolean }`
> in `features/events/api/types.ts` (mirror, not a cross-feature import).**

- **(a) [recommended] Define a feature-local `SetSettledRequest { isSettled: boolean }` in
  `features/events/api/types.ts`** (mirrors the identical expenses type) rather than importing across
  features, keeping feature-local DTO ownership. Trade-off: a two-line duplicate type (acceptable;
  matches the feature-first convention where each feature mirrors its own DTOs).
- **(b)** Import the expenses `SetSettledRequest`. Trade-off: fewer types, but couples the events
  feature to the expenses feature's DTO module.

## Assumptions

> Working assumptions pending the checkpoint (and the backend contract landing). Flag any that are wrong.

- The backend ships the proposed contract at option (a) for OQ1/OQ8/OQ13/OQ15 (stored Layer B, net
  `outstanding`, QR bills uncleared owing members, additive DTO growth). If not, re-sync.
- `ShareResponse` gains `isSettled`/`settledAt` **everywhere it is returned** (inside `ExpenseResponse`
  on the detail page). The expenses summary DTO already exposes the whole-expense `isSettled`.
- `MemberBalanceRow.outstanding` is `-balance` for an uncleared owing member, `0` once that member is
  marked settled, and `0` for a member with `balance >= 0` — the UI renders it verbatim and never
  derives it.
- The two new toggles are not audited and not tier-gated (backend OQ10a/OQ11a) — no upgrade affordance
  on these controls.
- The FE can detect the payer's own share (`share.member.uuid === expense.payer.uuid`) and 0đ shares
  (`share.amount === 0`) for the R3 presentation without a new DTO field.
- No new route or page; all UI lands inside the shipped `ExpenseDetailPage` (shares section) and
  `EventDetailPage` (balance table). No `errors.ts` change (reused codes present).
- MSW handlers (`src/test/msw/handlers.ts`) will be extended for the two new routes + the overlay
  fields so component tests can exercise them.

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. Concrete names reflect the recommended OQ options; re-sync if the
> user chooses otherwise. Steps marked **[MOD]** modify shipped files. **Do not start until the backend
> contract is confirmed + shipped** (the ⚠ dependency).

### Step 1 — API types (mirror the extended DTOs)

- **[MOD]** `features/expenses/api/types.ts` — add to `ShareResponse`:
  `isSettled: boolean;` and `settledAt?: string | null;` (Vietnamese doc: payment metadata, does not
  change `amount`). `SetSettledRequest` already exists and is reused for the per-share body.
- **[MOD]** `features/events/api/types.ts` —
  - `MemberBalanceRow` gains `outstanding: number;` (còn nợ ròng, VND, verbatim), `isSettled: boolean;`
    (Layer B net clearance), `settledAt?: string | null;`.
  - `EventBalanceResponse` gains `totalOutstanding: number;`, `owingMemberCount: number;`,
    `settledMemberCount: number;`.
  - Add `export interface SetSettledRequest { isSettled: boolean }` (OQ10a).

### Step 2 — API client methods

- **[MOD]** `features/expenses/api/expensesApi.ts` — add:
  ```ts
  setShareSettled: (expenseUuid: string, shareUuid: string, body: SetSettledRequest) =>
    api.put<MessageResponse>(`/v1/expenses/${expenseUuid}/shares/${shareUuid}/settled`, body),
  ```
- **[MOD]** `features/events/api/eventsApi.ts` — add:
  ```ts
  setMemberSettled: (eventUuid: string, memberUuid: string, body: SetSettledRequest) =>
    api.put<MessageResponse>(`/v1/events/${eventUuid}/members/${memberUuid}/settled`, body),
  ```

### Step 3 — Hooks + cache invalidation (R8, OQ6a, OQ7a)

- **[MOD]** `features/expenses/hooks/useExpenses.ts` — add `useSetShareSettled`:
  ```ts
  export function useSetShareSettled() {
    return useMutation({
      mutationFn: ({ expenseUuid, shareUuid, body }:
        { expenseUuid: string; shareUuid: string; body: SetSettledRequest }) =>
        expensesApi.setShareSettled(expenseUuid, shareUuid, body),
      onSuccess: (_data, { expenseUuid }) => invalidateExpense(expenseUuid), // OQ7a: expenses only
    });
  }
  ```
- **[MOD]** `features/events/hooks/useEvents.ts` — add `useSetMemberSettled`:
  ```ts
  export function useSetMemberSettled() {
    return useMutation({
      mutationFn: ({ eventUuid, memberUuid, body }:
        { eventUuid: string; memberUuid: string; body: SetSettledRequest }) =>
        eventsApi.setMemberSettled(eventUuid, memberUuid, body),
      onSuccess: (_data, { eventUuid }) => {
        void queryClient.invalidateQueries({ queryKey: eventsKeys.balance(eventUuid) });
        void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
      },
    });
  }
  ```
  (No `expensesKeys` reach — Layer B does not change expense/share data.)

### Step 4 — Presentational `SettledSwitch` + toggle wrappers (OQ1a)

- **New** `features/expenses/components/SettledSwitch.tsx` — extract the `role="switch"` button + icon
  (`CheckIcon`/`ClockIcon`) + label markup from the current `SettledToggle`, reusing
  `SettledToggle.module.css` (rename to `SettledSwitch.module.css` or share). Props:
  `{ isSettled, onToggle, pending, accessibleName, labelOn, labelOff }`. Pure, no data.
- **[MOD]** `features/expenses/components/SettledToggle.tsx` — re-implement on top of `SettledSwitch`
  (behavior identical: `useSetSettled`, toast, aria via `expenses:settled.*`). Run
  `gitnexus_impact({ target: "SettledToggle", direction: "upstream" })` before editing and report the
  blast radius (expected: `ExpenseDetailPage` + its tests).
- **New** `features/expenses/components/ShareSettledToggle.tsx` — wraps `SettledSwitch`; calls
  `useSetShareSettled`; toast on success/error (verbatim server message); accessible name
  `expenses:shares.settledAriaNamed` (member name). Props `{ expenseUuid, share }`.
- **New** `features/events/components/MemberSettledToggle.tsx` — wraps `SettledSwitch`; calls
  `useSetMemberSettled`; toast; accessible name `events:balance.settledAriaNamed` (member name).
  Props `{ eventUuid, memberUuid, memberName, isSettled }`.

### Step 5 — Shares section: per-share column + rollup (R1, R2, R3, R6)

- **[MOD]** `features/expenses/components/SharesSection.tsx`:
  - Add an **"Đã trả"** column (`TableHeaderCell scope="col"`) to the shares table.
  - Per data row: if the share is billable (not the payer's own share and `amount > 0`, per R3/OQ3a)
    render `<ShareSettledToggle expenseUuid={expense.uuid} share={share} />`; otherwise render a muted
    **"Không nợ"** label (`expenses:shares.notOwed`). This column is rendered **regardless of
    `disabled`** (the closed-event settled exception, R6) — only the add/edit/delete controls stay
    gated on `disabled`.
  - Add a derived **rollup chip** in the `CardHeader` action area (or a sub-row): compute over billable
    shares → all settled = `expenses:shares.rollupAll`; some = `expenses:shares.rollupPartial`
    (with `{{settled}}`/`{{total}}`); none = `expenses:shares.rollupNone`. Display-only (a `Badge`).
  - The derivation excludes payer-own + 0đ shares from the X/Y count (R3).

### Step 6 — Balance table: outstanding overlay + per-member toggle (R4, R5, R7, R9)

- **[MOD]** `features/events/components/EventBalanceTable.tsx` (OQ4a):
  - Extend `useEventBalanceQuery` consumption: the query now returns the overlay fields; thread the
    full `EventBalanceResponse` (not just `rows`) so the summary can read
    `totalOutstanding`/`owingMemberCount`/`settledMemberCount`.
  - Add a **"Còn nợ"** numeric column: `<Money amount={row.outstanding} format={formatMoneyVnd} />`
    (muted "—" when `0`).
  - Add a **"Trạng thái"** column: for `row.balance < 0` (owing) → an đã-trả/còn-nợ `Badge`
    (color-independent: icon + text, `events:balance.statusSettled`/`statusOwing`) **plus** a
    `<MemberSettledToggle>` (OQ5a — only for owing members); for `balance >= 0` → muted "—" (owed).
    The toggle is enabled on OPEN and CLOSED events (never gated by `isClosed`, R6).
  - Extend the `TableFoot`: a `totalOutstanding` cell (`<Money>`) + a summary line
    `events:balance.summary` ("Đã trả {{settled}}/{{total}} thành viên · Còn nợ {{amount}}"), read
    verbatim from the API totals (never client-summed).
  - `COLUMN_COUNT` bumps 4 → 6; update the skeleton + `TableEmpty colSpan`.
  - Responsive: on narrow widths, stack the status badge + toggle under the member row-header (a small
    CSS-module change in `EventBalanceTable.module.css`, tokens-only) so the 6-column table remains
    legible on phones (ui-designer pass — see Impact Analysis).

### Step 7 — Event QR copy (OQ8a)

- **[MOD]** (copy only) `features/wallet` i18n: refine the `wallet:qr.noDebtTitle`/`noDebtBody` (12003)
  wording to reflect "everyone has cleared" (đã trả) rather than "no negative balances". No logic
  change to `QrDialog` — the backend filters the QR to `outstanding > 0`.

### Step 8 — i18n (both locales, parity preserved)

- **[MOD]** `i18n/locales/{vi-VN,en-US}/expenses.json` — under `shares`:
  - `settledLabel` ("Đã trả" / "Settled"), `notOwed` ("Không nợ" / "Not owed"),
    `settledAriaNamed` ("Trạng thái đã trả phần gánh của {{name}}" / …),
    `settledToastOn`/`settledToastOff` (toasts), `rollupAll` ("Đã trả toàn bộ" / "Fully settled"),
    `rollupPartial` ("Đã trả một phần ({{settled}}/{{total}})" / "Partially settled ({{settled}}/{{total}})"),
    `rollupNone` ("Chưa trả" / "Not settled").
- **[MOD]** `i18n/locales/{vi-VN,en-US}/events.json` — under `balance`:
  - `outstanding` ("Còn nợ" / "Outstanding"), `statusSettled` ("Đã trả" / "Settled"),
    `statusOwing` ("Còn nợ" / "Owing"), `markSettled` ("Đánh dấu đã trả" / "Mark settled"),
    `settledAriaNamed` ("Trạng thái đã trả của {{name}}" / …),
    `totalOutstanding` ("Tổng còn nợ" / "Total outstanding"),
    `summary` ("Đã trả {{settled}}/{{total}} thành viên · Còn nợ {{amount}}" / "{{settled}}/{{total}} members settled · {{amount}} outstanding"),
    `settledToastOn`/`settledToastOff`.
- **[MOD]** `wallet.json` (both locales) — refine `qr.noDebtTitle`/`noDebtBody` (OQ8a copy).
- Keep strict key parity (the repo's i18n parity tests — `expensesI18n`/`eventsI18n` style — fail CI on
  any missing leaf).

### API endpoints consumed (summary)

| Screen/flow | Verb + path | Request → `data` | Codes handled |
| --- | --- | --- | --- |
| Shares section per-share toggle | `PUT /v1/expenses/{expenseUuid}/shares/{shareUuid}/settled` | `SetSettledRequest` → `MessageResponse` | `7000`/`6000` → toast (stale); network → toast |
| Balance per-member toggle | `PUT /v1/events/{eventUuid}/members/{memberUuid}/settled` | `SetSettledRequest` → `MessageResponse` | `3000`/`9000` → toast (stale); network → toast |
| Balance overlay read | `GET /v1/events/{uuid}/balance` | — → extended `EventBalanceResponse` | `9000` → shared NotFound (existing) |
| Whole-expense toggle (existing, cascades) | `PUT /v1/expenses/{uuid}/settled` | unchanged | `6000` → toast (existing) |

All through the centralized client; envelope unwrapped to `data`; failures throw `ApiError` (branch on
numeric `code`, render `error.message` verbatim).

### Loading / empty / error states

- **Shares section:** table renders from the already-loaded `expense` (no separate query). Per-share
  toggle: `disabled` while its mutation is pending; error → toast (verbatim). Payer/0đ rows show the
  muted "Không nợ" label. Rollup chip is derived (no async).
- **Balance table:** existing pending-skeleton / error+retry / empty-rows states unchanged; the new
  columns join them (skeleton cells added for outstanding + status). Empty `rows` → existing empty
  note (no overlay, `totalOutstanding = 0`). Per-member toggle: `disabled` while pending; error →
  toast; success → toast + overlay refetch reconciles the badge + outstanding.
- **QR:** unchanged states; `12003` now reads "đã trả hết" (OQ8a copy).

### Form validation rules

- No forms — both toggles submit a single boolean (`{ isSettled }`). No client-side validation beyond
  the switch state; the backend is authoritative on participant/ownership (mapped to toasts).

### Accessibility

- All three toggles are `role="switch"` + `aria-checked`, with a distinct accessible name
  (member/share/expense context), color-independent (icon + đã trả/chưa trả text). Reuses the shipped
  `SettledSwitch` contract (OQ1a).
- New table columns: `Còn nợ` numeric right-aligned; `Trạng thái` badge conveys state by icon + text
  (not color); the per-member toggle is keyboard-operable; the summary line is plain text.
- The rollup chip is a `Badge` with text (all/partial/none), not color alone; "Không nợ" is a labelled
  muted text cell, not an empty cell.
- On closed events the settled toggles remain keyboard-reachable (the sole write exception); the
  closed-event `Alert` copy should mention that marking đã trả is still allowed.

### Tests (web-test-engineer — Vitest + RTL, MSW at the client boundary; pinned TZ + vi-VN)

**MSW** (`src/test/msw/handlers.ts`): add `PUT /v1/expenses/:e/shares/:s/settled` and
`PUT /v1/events/:e/members/:m/settled` (flip stored flags; resource-owned 404s); emit
`isSettled`/`settledAt` on `ShareResponse`; emit `outstanding`/`isSettled`/`settledAt` on
`MemberBalanceRow` and `totalOutstanding`/`owingMemberCount`/`settledMemberCount` on the balance
response (compute `outstanding = balance < 0 && !isSettled ? -balance : 0`); make the whole-expense
settled cascade to shares; make the event QR bill only `outstanding > 0` (12003 when all cleared).

**`SettledSwitch` / `SettledToggle` (regression):** the shipped whole-expense toggle still flips, toasts,
invalidates, stays enabled on a closed-event expense, and is color-independent (icon + text).

**`ShareSettledToggle` / `SharesSection`:** per-share toggle flips `share.isSettled` via the correct PUT
(assert MSW request path/body); toast on success + error (`7000`); the toggle is present + enabled on a
**closed** event's expense while add/edit/delete stay hidden; payer-own + 0đ shares show "Không nợ" (no
toggle) and are excluded from the rollup; the rollup chip reads all / partial (X/Y) / none from the
shares.

**`MemberSettledToggle` / `EventBalanceTable`:** the overlay renders `outstanding` via `formatMoneyVnd`,
the đã-trả/còn-nợ badge (color-independent), and the summary line from
`totalOutstanding`/`owingMemberCount`/`settledMemberCount`; the toggle appears only for `balance < 0`
members (OQ5a); marking a member settled hits the correct PUT, toasts, and refetches so `outstanding` →
`0` and the badge flips; the toggle is enabled on OPEN and CLOSED events; the balance/advanced/owed
columns + sum-to-zero footer are **unchanged** by any settled flip (D2 regression guard); soft-deleted
owing member still renders with its overlay.

**QR (OQ8a):** with one of two owing members marked settled, the QR flow bills only the remainder; all
cleared → the `12003` "đã trả hết" state.

**i18n:** vi-VN↔en-US parity for the new `expenses.shares.*` + `events.balance.*` + `wallet.qr.noDebt*`
keys; no empty leaves; fixed domain terms (đã trả, còn nợ, phần gánh).

## Impact Analysis

- **APIs:** none authored here — consumes the PROPOSED backend contract (two new PUT routes + the
  additive `GET /balance` growth + the `ShareResponse` growth + the whole-expense cascade). **Blocked
  on the backend landing.**
- **Frontend (files):**
  - **[MOD]** `features/expenses/api/types.ts` (`ShareResponse` +settled), `api/expensesApi.ts`
    (+`setShareSettled`), `hooks/useExpenses.ts` (+`useSetShareSettled`),
    `components/SettledToggle.tsx` (refactor onto `SettledSwitch`),
    `components/SharesSection.tsx` (per-share column + rollup + closed-exception).
  - **[MOD]** `features/events/api/types.ts` (`MemberBalanceRow`/`EventBalanceResponse` +overlay,
    +`SetSettledRequest`), `api/eventsApi.ts` (+`setMemberSettled`), `hooks/useEvents.ts`
    (+`useSetMemberSettled`), `components/EventBalanceTable.tsx` (+overlay columns/summary/toggle),
    `components/EventBalanceTable.module.css` (responsive stack).
  - **New:** `features/expenses/components/SettledSwitch.tsx` (+ CSS),
    `features/expenses/components/ShareSettledToggle.tsx`,
    `features/events/components/MemberSettledToggle.tsx`.
  - **[MOD]** i18n: `locales/{vi-VN,en-US}/{expenses,events,wallet}.json`.
  - **No `errors.ts` change** (3000/6000/7000/9000 present; `classifyError` covers them).
  - **No new route / page.** All UI lands inside `ExpenseDetailPage` (shares) + `EventDetailPage`
    (balance).
- **Design system:** a **modest ui-designer pass** — the extracted `SettledSwitch` (already visually
  defined by the shipped toggle), the balance-table overlay columns + responsive stack, and the
  status/rollup badges. Reuses `Table`/`TableFoot`/`Badge`/`Money`/`Alert`; flag if the 6-column table
  reveals a genuine primitive gap.
- **Data-fetching:** `useSetShareSettled` invalidates expenses caches only (OQ7a); `useSetMemberSettled`
  invalidates the event balance + `eventsKeys.all`. No new query keys.
- **Tests:** MSW handler extensions + new component/interaction specs (above).
- **Infrastructure / Services / DB:** none (FE only).

## Decision Log

> **Locked at the 2026-07-21 user checkpoint (top-level) — do NOT reopen:**

1. **D1 — BOTH layers ship on the web.** Per-share settled toggles (Layer A) in the shares section AND
   the per-member-per-event net-clearance overlay (Layer B) in the event balance table.
2. **D2 — Balance stays PURE; overlay is derived.** `advanced`/`owed`/`balance` + the sum-to-zero
   footer are rendered verbatim and never perturbed; the "còn nợ / đã trả" overlay (outstanding per
   member + who cleared + event totals) is additive and read verbatim from the API.

> **Inherited (locked upstream — NOT reopened):** balance ignores settled + sums to zero (M7); settled
> is the sole write allowed on a closed event (§3.5/§4.4); resource-owned 404-never-403 (§4.1); money
> rendered verbatim, never float math (R3); refetch-based writes, no optimistic updates (M4 OQ14a);
> branch on numeric `code`, render `error.message` verbatim; domain terms fixed.

> **Resolved at the 2026-07-21 user checkpoint — all 10 OQs accepted at recommended option (a);
> backend contract also confirmed at option (a).** Binding feature-level decisions:

3. **OQ1a — Shared `SettledSwitch`.** Extract the presentational switch and build the whole-expense,
   per-share, and per-member toggles on it. *Reason:* one color-independent a11y contract, no
   three-way drift.
4. **OQ2a — Header toggle + per-share column + derived rollup chip.** Keep the whole-expense toggle
   (cascades on the backend) and add a display-only all/partial(X/Y)/none chip. *Reason:* one coherent
   settled story; matches the boolean, reconciled `expense.isSettled` contract.
5. **OQ3a — "Không nợ" for payer-own + 0đ shares.** No toggle on them; exclude from the rollup count.
   *Reason:* they are settled-by-definition; a no-op toggle would confuse.
6. **OQ4a — Overlay as two extra columns on the balance table + `TableFoot` summary.** Add "Còn nợ" +
   "Trạng thái" columns and a `totalOutstanding`/X-of-Y summary; responsive stack on phones. *Reason:*
   keeps balance + overlay in one sum-to-zero-anchored view.
7. **OQ5a — Per-member toggle only for owing members (`balance < 0`).** Owed/zero members show "—".
   *Reason:* marking a non-owing member has no overlay effect.
8. **OQ6a — Refetch, not optimistic.** Invalidate-on-success, disabled-while-pending. *Reason:*
   consistent with the shipped `SettledToggle` and M4 OQ14a.
9. **OQ7a — Per-share toggle invalidates expenses caches only.** Not the event balance. *Reason:*
   `outstanding` is Layer-B (net) driven per the confirmed backend OQ8a; per-share (gross) does not
   change it.
10. **OQ8a — Display-only QR "who still owes".** Rely on the server `outstanding > 0` QR filter; only
    refine the `12003` copy to "đã trả hết". *Reason:* the overlay + existing QR states already cover
    it.
11. **OQ9a — Overlay + toggle on OPEN and CLOSED events.** *Reason:* mid-trip settlement is a real
    workflow; OPEN-event drift is an accepted, documented limitation.
12. **OQ10a — Feature-local `SetSettledRequest` in the events types module.** *Reason:* feature-first
    DTO ownership; avoids cross-feature coupling.

## Progress Log

### 2026-07-21

- Started planning the web feature "Đánh dấu đã trả theo từng thành viên" (settled per member, §6).
- Read the PROPOSED backend contract `FairShareMonApi/planning/settled-per-member.md` (two new PUT
  routes; additive `GET /balance` overlay fields; `ShareResponse` +settled; whole-expense cascade;
  no new codes; no tier gate; QR bills `outstanding > 0`), noting it is itself awaiting a user
  checkpoint (OQ1–OQ15 at recommended option (a)).
- Read the prior web planning docs `m5-events.md`, `expense-event-linkage.md`,
  `dashboard-recent-events-card.md` for depth/style + locked conventions.
- Grounded the plan in the live SPA: `SettledToggle` (whole-expense `role="switch"` + `useSetSettled`,
  never disabled on closed), `SharesSection` (shares table, write controls gated on `disabled`),
  `EventBalanceTable` (§3.7 table + sum-to-zero `TableFoot`), `EventDetailPage` (closed-event control
  gating + QR button), `QrDialog` (12003/12002/13003 states), the expenses/events `api/types.ts` +
  `expensesApi`/`eventsApi` + `useExpenses`/`useEvents` cache factories + cross-feature invalidation,
  `errors.ts` (3000/6000/7000/9000 present, `notFound` classification), and the existing
  `expenses:settled.*` / `events:balance.*` i18n keys.
- Recorded the locked D1/D2; drafted 10 Open Questions with recommended option (a) + trade-offs; wrote
  the Implementation Plan (types → client → hooks → `SettledSwitch` extraction + two toggle wrappers →
  shares column/rollup → balance overlay/summary → QR copy → i18n), the endpoint/state/a11y tables,
  and the test list.
- Status: **awaiting (1) the backend contract checkpoint + ship, and (2) this doc's user checkpoint on
  OQ1–OQ10** before implementation.

### 2026-07-21 (checkpoint resolution)

- **All 10 web Open Questions accepted at recommended option (a)** at the user checkpoint; the
  **backend contract was likewise confirmed at option (a) throughout** (stored Layer B, net-driven
  `outstanding`, QR bills `outstanding > 0`, additive DTO growth, no new codes, no tier gate).
- Annotated each OQ resolved, added one numbered Decision Log entry per OQ (OQ1a–OQ10a), and updated
  the top-of-doc dependency note: the backend contract is now **CONFIRMED (option a) but NOT yet
  shipped** — the two new PUT routes, the `GET /balance` overlay fields, the `ShareResponse` growth,
  and the whole-expense cascade do not exist in the running API yet.
- Status: **plan finalized; implementation blocked ONLY on the backend being built + shipped.** No
  web code to be written until the extended API is live.

## Final Outcome

(pending)

## Future Improvements

- **Optimistic per-share toggling** (OQ6b) once an app-wide optimistic-update pattern is adopted —
  snappier for marking many portions in a row.
- **Drift indicator for OPEN-event Layer B** (backend OQ9/Future): surface a subtle "clearance may be
  stale — balance changed since marking" hint if the backend adds a snapshot/re-validation.
- **Partial per-member settlement UI** ("đã trả 300k / 500k") if the backend adds a cleared-amount
  snapshot (backend OQ1c/Future).
- **Reminders (§6 "Nhắc nợ"):** a "nhắc" affordance next to each còn-nợ member driven off the overlay.
- **Unify the three settled notions in copy/help** (whole-expense, per-share gross, per-member-event
  net) with a short inline explainer so users aren't confused by the two axes (mirrors backend
  Future Improvement).
- **Balance-table overlay on a dedicated "who has paid" view** (OQ4b) if the 6-column table proves too
  dense on phones despite the responsive stack.
