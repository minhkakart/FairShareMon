# Event ↔ Expense Settlement Sync — Web

> Downstream of the finalized BA doc `planning/ba/event-expense-settlement-sync-business-analysis.md`
> (all 10 Decision Log entries locked 2026-08-25) and the finalized API planning doc
> `FairShareMonApi/planning/event-expense-settlement-sync.md` (all 5 implementation-level Open Questions
> locked 2026-08-25). Both docs are zero-open-questions and treated here as the authoritative contract —
> **the backend is not yet built**, so this doc plans against the contract, not the running API (mirrors
> how `FairShareMonWeb/planning/settled-per-member.md` was written and gated before its own backend
> shipped). This is the web counterpart of the BA doc's Handoff Summary "Web" workstream and its own
> Milestone 1 / Milestone 2 split.
>
> **Updated 2026-08-25 (checkpoint) — ZERO Open Questions remain.** All six items below (OQ1-OQ6) were
> put to `feature-planner`/`ui-designer`/the user and answered at the same checkpoint; each is annotated
> inline in the same `~~OQ-X~~ → Answered` style the two upstream docs used, with the original question
> text preserved for the record. See the Decision Log for the binding answer + rationale on each.
> `ui-designer` also **shipped the resolved design-system primitives ahead of this checkpoint** —
> `HelpHint` (`src/components/ui/HelpHint/`), the `Badge` `partial` tone (`src/components/ui/Badge/`),
> `SettlementStatusBadge` and `SettlementMeter` (`src/features/expenses/components/`), and
> `HalfCheckIcon` (`src/features/expenses/components/icons.tsx`) all exist in the tree today, confirmed by
> reading each file directly — this doc's Implementation Plan below wires them in, it does not design or
> build them. **This doc is now the final planning deliverable for this feature cycle** — planning-only;
> implementation is a separate future step, gated on each milestone's backend actually shipping (per the
> Assumptions section, unchanged).

## Objective

Wire the finalized two-way settlement sync into the already-shipped Layer A (per-share settled) / Layer B
(per-member-event net clearance) web surface built in `settled-per-member.md`, split into the same two
milestones the backend ships in:

- **Milestone 1 (Direction 1 — event settle → cascade to expenses).** No new backend response shape and
  no new stored field, but the balance overlay (`GET /events/{uuid}/balance`) gains
  `IsEligibleForAutoCascade` on each row. The web side must (a) fix `useSetMemberSettled`'s cache
  invalidation, which today never reaches the expenses caches even though a member-settle can now cascade
  to N shares across M expenses, and (b) give creditor rows (`balance >= 0`) a settle affordance for the
  first time — today `StatusCell` renders literally nothing but a muted "—" for them.
- **Milestone 2 (Direction 2 — expense/share settle → partial credit + Story C/QR).** The balance overlay
  gains `ClearedAmount` (decimal) and `SettlementStatus` (`Unsettled`/`PartiallySettled`/`Settled`) per
  row, and `PartiallySettledMemberCount` on the response. The web side must (a) fix
  `useSetSettled`/`useSetShareSettled`'s cache invalidation, which today explicitly asserts (in a code
  comment) that a per-share toggle never needs to invalidate the event balance — an assumption this
  feature makes false — and (b) render the new 3-state status instead of the current boolean
  đã-trả/còn-nợ badge, plus a partial-amount money display ("300.000đ / 500.000đ" via the new
  `SettlementMeter` component — resolved, OQ4).

None of this changes how `advanced`/`owed`/`balance` are rendered (still verbatim, still the sum-to-zero
footer, D2 — unchanged, not reopened here). The three settled-toggle PUT routes keep their existing
request/response shape (`{ isSettled }` → plain `ApiResult` success message) — **no cascade/credit counts
are returned**, so every UI reaction to a toggle's side effects comes from refetching/invalidating the
right caches, never from reading the mutation response (API doc OQ3, Decision Log entry 3).

## Background

Confirmed against the live SPA (2026-08-25) and the two upstream planning docs:

- **Layer A/B shipped surface** (`settled-per-member.md`, Final Outcome). `SettledSwitch` (presentational)
  backs three toggles: `SettledToggle` (whole-expense), `ShareSettledToggle` (per-share),
  `MemberSettledToggle` (per-member-event). All three are refetch-based (invalidate-on-success, no
  optimistic updates, OQ6a) and stay enabled on closed events/expenses (the sole write exception).
- **`MemberBalanceRow`/`EventBalanceResponse`** (`src/features/events/api/types.ts`) today: `advanced`,
  `owed`, `balance`, `outstanding`, `isSettled`, `settledAt` per row; `totalOutstanding`,
  `owingMemberCount`, `settledMemberCount` on the response. No `clearedAmount`, no `settlementStatus`, no
  `isEligibleForAutoCascade` yet.
- **`ShareResponse`** (`src/features/expenses/api/types.ts`) already carries `isSettled`/`settledAt`
  (Layer A) — unchanged by this feature (neither milestone touches the `Share` DTO shape).
- **The two now-concretely-broken invalidation assumptions** (the task brief's core ask, confirmed by
  reading the code):
  - `useSetSettled`/`useSetShareSettled` (`features/expenses/hooks/useExpenses.ts:98-125`) call
    `invalidateExpense(uuid)` only — no `eventsKeys` reach at all. `useSetShareSettled`'s doc comment
    reads: *"the event overlay `outstanding` is Layer-B (net) driven, so a per-share (gross) flip does
    not change the balance overlay"* (OQ7a, `settled-per-member.md`). Milestone 2 (Direction 2) makes
    this **false**: a per-share (or whole-expense) settle now credits `ClearedAmount` on every eligible
    debtor member's event balance row.
  - `useSetMemberSettled` (`features/events/hooks/useEvents.ts:101-116`) invalidates
    `eventsKeys.balance(eventUuid)` + `eventsKeys.all` only — no `expensesKeys` reach. Milestone 1
    (Direction 1) makes this incomplete: marking a member's event-level flag can now cascade
    `Share.isSettled` across every expense that member has a share in, in the same event.
  - **Neither hook currently carries an `eventUuid`/knows which expenses to invalidate** — this is the
    concrete plumbing gap the Implementation Plan below closes (Step 3).
- **`EventBalanceTable.tsx`'s `StatusCell`** (~line 149-178): renders **nothing but a muted "—"** for any
  row with `balance >= 0` (owed/zero members) — no `Badge`, no toggle, no way to interact. Milestone 1
  makes a gross-pure net creditor eligible for the Direction-1 auto-cascade; Milestone 2's
  `ClearedAmount`/`SettlementStatus` only ever apply to owing rows (`balance < 0`) since a creditor's
  `Outstanding` is floored at 0 regardless (API doc, self-protecting). So the creditor-row affordance
  change is **Milestone-1-only** work; the 3-state badge/partial-amount work is **Milestone-2-only**
  work, on the existing owing-row branch.
- **`ExpenseResponse`/`ExpenseSummaryResponse`** already carry `eventUuid?: string | null` — every call
  site that renders `SettledToggle`/`ShareSettledToggle` (`ExpenseDetailPage`, `ExpensesTable`,
  `SharesSection`) already has the owning event's uuid in scope; it is simply not threaded down to the
  toggle components or the mutation hooks today.
- **Badge component** (`src/components/ui/Badge/Badge.tsx`) — **resolved (OQ3).** A `partial` tone now
  exists in the tone union and `Badge.module.css` (`background-color: var(--fs-color-partial-surface)`
  etc.), confirmed by reading both files. Tone set today: `neutral | success | warning | danger | info |
  settled | partial | premium | free`.
- **`SettlementStatusBadge`** (`src/features/expenses/components/SettlementStatusBadge.tsx`) — **new,
  already built.** Renders all 3 states off a local `SettlementTriState = "unsettled" | "partial" |
  "settled"` union (deliberately decoupled from the wire enum's PascalCase names, mirroring how
  `SettledSwitch` takes a plain `isSettled: boolean` rather than a raw API shape): `unsettled` → `warning`
  tone + `ClockIcon`, `partial` → `partial` tone + the new `HalfCheckIcon`, `settled` → `settled` tone +
  `CheckIcon`. Color-independent by construction (distinct icon silhouette per state, never color alone).
  Callers pass the three label strings (i18n-sourced) as props.
- **`SettlementMeter`** (`src/features/expenses/components/SettlementMeter.tsx` +
  `SettlementMeter.module.css`) — **new, already built.** Renders the "300.000đ / 500.000đ" fraction plus
  a two-segment fill bar (`role="progressbar"`, numeric fraction as the primary channel, bar as
  reinforcement). Props: `clearedAmount`, `netOwed`, `format` (inject the shared VND formatter), and
  `accessibleLabel`. Its doc comment is explicit and load-bearing: `netOwed` **must** be the server's
  net-owed figure verbatim — never a client-side sum of the member's individually-settled bills — because
  a member can hold both a payer-share and a debtor-share across different expenses in the same event,
  making a locally-summed "total of their bills" diverge from the true net-owed amount (the OQ-L
  legibility trap this doc's OQ4 flagged). Since the wire has no standalone `NetOwed` field, the caller
  must compose it as `row.clearedAmount + row.outstanding` — the one client-side arithmetic composition
  already sanctioned by M2-R5 ("safe to display, never to persist/compare against"), not a new
  derivation.
- **`HelpHint`** (`src/components/ui/HelpHint/`) — **new, already built, dependency-free by design.** A
  small CSS-only (`:focus-within`, no JS state, no Radix Tooltip — this codebase has none installed, and
  adding one remains a foundation-level Open Question, not something `ui-designer` could decide
  unilaterally) info-glyph button that reveals a short explanatory bubble on hover/focus. Props: `label`
  (accessible name for the trigger) and `children` (the hint body). Built specifically to explain, inline,
  why a control looks the way it does (e.g. why an ineligible creditor's row has no settle toggle) without
  a modal or help page — used verbatim here, no further design work needed.
- **Enum wire format — resolved (OQ1).** `UserResponse.Tier`/`UserResponse.Role` are `string` at the DTO
  level (not raw C# enums) and the FE types them as uppercase literal unions (`"FREE" | "PREMIUM"`,
  `"USER" | "ADMIN"` — confirmed in `src/test/msw/handlers.ts`). The API doc's own OQ-WF confirms the
  `UserResponse.Tier`/`Role` precedent holds for `MemberBalanceRow.SettlementStatus` too: the DTO property
  is `string` (`"Unsettled" | "PartiallySettled" | "Settled"`, PascalCase values matching the C#
  `EventSettlementStatus` enum member names, camelCase wire key `settlementStatus`), backed internally by
  a service-only, never-stored `EventSettlementStatus` enum that is never the DTO's own type (no
  `JsonStringEnumConverter` is registered anywhere in `Program.cs`, confirmed by grep — a raw enum would
  have serialized as `0|1|2`). See `FairShareMonApi/planning/event-expense-settlement-sync.md`, Decision
  Log entry 6 ("OQ-WF").
- **i18n — resolved (OQ6).** Vietnamese copy is locked by the API doc: "số tiền đã tất toán" (cleared
  amount), "chưa trả" (unsettled), "đã trả một phần" (partially settled), "đã trả" (fully settled). The
  existing `events:balance.statusOwing` key (currently "Còn nợ" / en-US "Owing") describes the exact same
  "owing, not yet settled" member state as the new locked "chưa trả" term — renamed to match rather than
  kept as parallel-but-distinct terminology on the same screen. Two adjacent keys that also read "Còn nợ"
  — `events:balance.outstanding` (the amount column header) and `summary`'s "Còn nợ {{amount}}" phrase —
  label an **amount**, not a per-member status adjective, and are deliberately left unchanged (see Open
  Questions/Decision Log for the full key list and reasoning, including a related-but-out-of-scope finding
  in `share:public.statusOwing`).

## Requirements

### Milestone 1 — Direction 1 (event settle → cascade to expenses)

- **M1-R1 — Cross-invalidate on member-settle.** `useSetMemberSettled`'s `onSuccess` must additionally
  invalidate the expenses caches (`expensesKeys.all`, which by TanStack's default prefix/fuzzy match also
  covers every `detail(uuid)`/`history(uuid)` under it) so that any expense whose shares were cascaded by
  Direction 1 refetches its `isSettled`/`isSettled`-per-share state on next view.
- **M1-R2 — Creditor-row settle affordance — resolved (OQ2).** `EventBalanceTable`'s `StatusCell` must
  render a settle affordance for `balance >= 0` rows now that a net creditor can be Direction-1-eligible —
  gated by `row.isEligibleForAutoCascade` (not a plain `balance >= 0` check, since a creditor holding a
  debtor-share elsewhere in the event is NOT eligible, OQ-L amendment). Locked treatment: an **eligible**
  creditor (`balance >= 0 && isEligibleForAutoCascade`) gets the existing `SettledSwitch`/
  `MemberSettledToggle` unmodified (no new props), paired with a `HelpHint` reading "Đánh dấu đã trả sẽ tự
  động đánh dấu tất cả phần gánh liên quan của thành viên này là đã trả."; an **ineligible** creditor
  (`balance >= 0 && !isEligibleForAutoCascade`) gets **no toggle at all** (hidden, not disabled — confirmed
  by the user over "always show, disabled"), with the muted "—" replaced by a `HelpHint` reading "Thành
  viên này vừa có khoản được nhận vừa có khoản phải trả trong đợt, nên không thể tự động đồng bộ — hãy
  đánh dấu từng phiếu/phần gánh riêng."; a net-zero balance stays exactly as today (muted "—", no hint).
  `MemberSettledToggle` needs **no new props** for this — the branch lives entirely in `StatusCell`.
- **M1-R3 — `MemberBalanceRow.isEligibleForAutoCascade` typed and threaded.** Add the field to the
  `MemberBalanceRow` type and read it verbatim (never client-derived — the gross-purity classification is
  the exact duplication risk the API doc's Decision Log calls out; the web must never reimplement it).
- **M1-R4 — Toast/copy for the now-side-effecting event-level toggle — resolved (OQ5), draft copy.**
  `MemberSettledToggle`'s success toast, when `row.isEligibleForAutoCascade` is true, communicates the
  cascade: "Đã đánh dấu {name} đã trả — các phần gánh liên quan của họ trong đợt cũng đã được tự động đánh
  dấu đã trả." (settle) / "Đã bỏ đánh dấu đã trả cho {name} — và các phần gánh liên quan đã được đánh dấu
  lại là chưa trả." (un-settle, mirrored). When not eligible, the toast is unchanged from today's plain
  generic copy (`settledToastOn`/`settledToastOff`) — consistent with the API doc's own choice not to
  report cascade counts. Exact final strings are still to be locked in `i18n/locales/{vi-VN,en-US}` at
  implementation time (draft Vietnamese only, above; en-US mirror + any copy-editing pass is
  implementation-time work, not blocked on this doc).

### Milestone 2 — Direction 2 (expense/share settle → partial credit) + Story C (QR)

- **M2-R1 — Cross-invalidate on expense/share-settle.** `useSetSettled` and `useSetShareSettled`'s
  `onSuccess` must additionally invalidate `eventsKeys.balance(eventUuid)` (and, for parity with every
  other cross-feature invalidation in this codebase, `eventsKeys.all` so the balance-derived counts used
  elsewhere refresh too) — **only when the expense belongs to an event** (Direction 2 never applies to a
  loose expense, API doc Assumptions). This requires threading `eventUuid` through the mutation call
  (Implementation Plan Step 3/4). **Toast copy — resolved (OQ5), draft copy:** the share/expense-level
  toggle is always attempted (Direction 2 has no eligibility gate the web needs to check first — the
  server self-protects per Decision Log 5's clamp finding), so its toast changes unconditionally when
  `eventUuid` is present: "Đã cập nhật đã trả — số dư còn nợ của đợt đã được đồng bộ tương ứng." (applies
  to both settle and un-settle since Direction 2's credit/claw-back is symmetric); a loose expense
  (`eventUuid` absent) keeps today's plain toast. Exact final strings still to be locked in i18n at
  implementation time.
- **M2-R2 — `MemberBalanceRow.clearedAmount`/`settlementStatus` typed and rendered.** Add both fields;
  render `clearedAmount` and the derived tri-state status verbatim (never re-derived client-side — the
  BA doc's single biggest risk is exactly this kind of duplicated gross/net logic).
- **M2-R3 — `EventBalanceResponse.partiallySettledMemberCount` typed and surfaced** in the footer summary
  alongside the existing `settledMemberCount`/`owingMemberCount`.
- **M2-R4 — 3-state status replaces the binary đã-trả/còn-nợ badge — resolved (OQ3).** For owing rows
  (`balance < 0`, unchanged branch — creditor rows stay Milestone-1's affordance, not this), `StatusCell`
  swaps its inline `Badge` for the already-built `SettlementStatusBadge`
  (`features/expenses/components/SettlementStatusBadge.tsx`), mapping `row.settlementStatus` → the
  component's local `SettlementTriState` (`"Unsettled" → "unsettled"`, `"PartiallySettled" → "partial"`,
  `"Settled" → "settled"`) and passing the three locked labels
  (`labelUnsettled`/`labelPartial`/`labelSettled`) from i18n. No new `Badge` tone/CSS work remains — the
  `partial` tone already exists (Background).
- **M2-R5 — Partial-amount display — resolved (OQ4).** The already-built `SettlementMeter`
  (`features/expenses/components/SettlementMeter.tsx`) renders the "300.000đ / 500.000đ" fraction + fill
  bar, its one fill color reusing `--fs-color-partial` (the same hue as `SettlementStatusBadge`'s
  `partial` tone, so badge and meter read as one visual language). `EventBalanceTable` passes
  `clearedAmount={row.clearedAmount}` and `netOwed={row.clearedAmount + row.outstanding}` (the one
  sanctioned client-side composition — safe to display, never to persist/compare against) plus the shared
  VND `format` function. Layout: rendered inside the existing "Còn nợ" column (replacing the current
  single `<Money>` cell with the meter for rows with `settlementStatus === "PartiallySettled"`; `Settled`/
  `Unsettled` rows keep the plain `<Money>` cell — no `COLUMN_COUNT` change). A **one-time `HelpHint` near
  the column header** (not per-row, to avoid a table-wide accessibility/visual-noise repeat) explains the
  net-model behavior: "Số tiền đã tất toán tính theo số dư ròng của đợt, có thể khác tổng các phần gánh đã
  đánh dấu đã trả nếu thành viên vừa là người trả vừa là người nợ trong đợt."
- **M2-R6 — QR needs zero web changes.** Confirmed by the API doc (`WalletQrService` needs zero backend
  changes; `QrDialog` already renders `Outstanding` bills and the `12003` "nobody owes" state
  verbatim) — no `QrDialog`/`useEventQrQuery` change is required. Only a **regression test** confirming
  the existing QR flow still bills the correct (now-partial-aware) remainder is needed (Tests section).

### Cross-cutting (both milestones)

- **No new routes, no new DTOs beyond the additive `MemberBalanceRow`/`EventBalanceResponse` growth** —
  the three settled-toggle PUT routes keep their exact existing request/response shape (API doc OQ3).
- **No `errors.ts` change** — no new error codes are introduced by either milestone (API doc, confirmed).
- **No new tier gate** — the sync is Free-tier, consistent with settled being Free today (API doc,
  confirmed) — no `<UpgradePrompt>`/`13003` handling is added to any of the three toggles.
- **Closed-event exception unchanged** — both cascades fire identically on OPEN and CLOSED events; the
  web side's existing "settled toggles are never gated on `disabled`" contract is unchanged (no new code
  needed here — it already isn't gated).
- **Money/i18n conventions unchanged** — VND via `formatMoneyVnd`, verbatim from the API, never float
  math; Vietnamese-first copy through `useT()`; vi-VN + en-US parity maintained.

## Open Questions

> **Updated 2026-08-25 (checkpoint) — ZERO Open Questions remain.** All six items below (OQ1-OQ6) were
> put to `feature-planner`/`ui-designer`/the user and answered at the same checkpoint; each is annotated
> inline in the same `~~OQ-X~~ → Answered` style the two upstream docs used, with the original question
> text preserved for the record. See the Decision Log below for the binding answer + rationale on each.
> This doc is now ready to hand back to the user as the final planning deliverable for this feature cycle.

~~**OQ1 — Wire format of `MemberBalanceRow.SettlementStatus`.**~~ → **Answered 2026-08-25 (by
`feature-planner`): option (a).** The API doc types this as a C# enum (`EventSettlementStatus {
Unsettled, PartiallySettled, Settled }`) but did not originally state whether the DTO property itself is
`string` (mirroring the `UserResponse.Tier`/`Role` precedent) or a raw enum serialized by
System.Text.Json's default integer encoding. `feature-planner` confirmed, via the API doc's own OQ-WF
Decision Log entry, that the `UserResponse.Tier`/`Role` precedent holds: `SettlementStatus` is `string`
on the wire (`"Unsettled" | "PartiallySettled" | "Settled"`, camelCase key `settlementStatus`), backed by
an internal, never-stored, service-only `EventSettlementStatus` enum that is never the DTO's own type. Web
types `SettlementStatus: "Unsettled" | "PartiallySettled" | "Settled"`. See Decision Log entry 1.

~~**OQ2 — Creditor-row settle-affordance visual/interaction design (Milestone 1).**~~ → **Answered
2026-08-25 (`ui-designer`'s design, confirmed by the user): a variant of option (b), with a `HelpHint`
added to both branches.** For an eligible creditor (`balance >= 0 && isEligibleForAutoCascade`): show the
existing `SettledSwitch`/`MemberSettledToggle` as-is (no new component/props), paired with a `HelpHint`
reading "Đánh dấu đã trả sẽ tự động đánh dấu tất cả phần gánh liên quan của thành viên này là đã trả." For
an ineligible creditor (`balance >= 0 && !isEligibleForAutoCascade`): **no toggle** (hidden — the user
confirmed this over "always show, disabled," rejecting the original option (a)'s disabled-toggle
treatment), with the muted "—" replaced by a `HelpHint` reading "Thành viên này vừa có khoản được nhận vừa
có khoản phải trả trong đợt, nên không thể tự động đồng bộ — hãy đánh dấu từng phiếu/phần gánh riêng."
Net-zero balance: unchanged, muted "—", no hint. `MemberSettledToggle` needs no new props — simpler than
either original option (a) or (b) once `HelpHint` (a new, dependency-free primitive) existed to fill the
explanation gap option (b) originally left open. See Decision Log entry 2.

~~**OQ3 — Badge tone / visual language for `PartiallySettled` (Milestone 2).**~~ → **Answered 2026-08-25
(`ui-designer`): option (a).** A new `partial` tone was added to `Badge`/`Badge.module.css` (confirmed in
the tree). A new `SettlementStatusBadge` component
(`FairShareMonWeb/src/features/expenses/components/SettlementStatusBadge.tsx`) renders all 3 states:
`Unsettled` → existing `warning` tone + `ClockIcon`, `PartiallySettled` → the new `partial` tone + a new
`HalfCheckIcon` (`features/expenses/components/icons.tsx`), `Settled` → existing `settled` tone +
`CheckIcon`. Use this component everywhere the old binary badge was rendered. See Decision Log entry 3.

~~**OQ4 — Money-metaphor for partial clearance (Milestone 2).**~~ → **Answered 2026-08-25 (`ui-designer`):
lives in the existing "Còn nợ" column.** A new `SettlementMeter` component
(`FairShareMonWeb/src/features/expenses/components/SettlementMeter.tsx` + `.module.css`) renders
"300.000đ / 500.000đ" plus a two-segment fill bar, fill color reusing `--fs-color-partial`. Its doc
comment mandates the `netOwed` prop always be the server's `NetOwed`/`ClearedAmount`-basis figures
verbatim, never client-recomputed (the OQ-L legibility trap) — composed here as
`row.clearedAmount + row.outstanding`, the one sanctioned display-only arithmetic. A one-time `HelpHint`
near the relevant column header (not per-row) explains the net-model behavior: "Số tiền đã tất toán tính
theo số dư ròng của đợt, có thể khác tổng các phần gánh đã đánh dấu đã trả nếu thành viên vừa là người trả
vừa là người nợ trong đợt." See Decision Log entry 4.

~~**OQ5 — Toast copy for the now-side-effecting toggles (both milestones).**~~ → **Answered 2026-08-25,
draft copy provided (exact final strings still to be locked in i18n at implementation time).**
Event-member toggle when `isEligibleForAutoCascade`: "Đã đánh dấu {name} đã trả — các phần gánh liên quan
của họ trong đợt cũng đã được tự động đánh dấu đã trả." (un-settle mirrors: "...và các phần gánh liên quan
đã được đánh dấu lại là chưa trả."). Not eligible: keep the existing plain toast unchanged. Share/expense-
level toggle (Direction 2, always attempted): "Đã cập nhật đã trả — số dư còn nợ của đợt đã được đồng bộ
tương ứng." See Decision Log entry 5.

~~**OQ6 — "Còn nợ" copy vs. the locked "chưa trả" copy (Milestone 2).**~~ → **Answered 2026-08-25: rename
to match (the original option (a), not the parallel-but-distinct option (b)).** The shipped
`events:balance.statusOwing` key (vi-VN "Còn nợ", en-US "Owing") is renamed to "Chưa trả" / "Unsettled" to
align with the newly-locked settlement terminology, since both describe the exact same member state on
the same screen — this also updates `MemberSettledToggle`'s `labelOff` for free (it reads the same key).
Traced every i18n key/string reading "Còn nợ" for this state: the test fixture
`src/features/events/memberSettled.test.tsx` (`expect(within(an).getAllByText("Còn nợ"))`, line ~177)
asserts this literal text and must be updated to "Chưa trả" alongside the key rename. Two adjacent keys
were traced and deliberately **not** renamed: `events:balance.outstanding` (the amount column header,
vi-VN "Còn nợ" / en-US "Outstanding") and `summary`'s "Còn nợ {{amount}}" phrase — both label an **amount**
("the money still owed"), a different grammatical role than `statusOwing`'s per-member status adjective,
and "Chưa trả {{amount}}" is not the intended reading of either surface. A related-but-out-of-scope
finding surfaced while tracing: `share:public.statusOwing` (the public, unauthenticated settlement-report
page, `PublicBalanceTable.tsx`) carries the identical "Còn nợ" wording for the identical state — left
untouched here since that page is a separate feature surface this doc's Assumptions already scope out
("No new page/route" — the `share` feature isn't in this doc's file list), but recorded under Future
Improvements below so it isn't silently lost. See Decision Log entry 6.

## Assumptions

- **The web implementation does not start until the backend milestone it depends on is actually built and
  shipped** — mirrors the `settled-per-member.md` precedent exactly (that doc's own top-of-file warning).
  Milestone 1 web work can start once the API's Milestone 1 (Direction 1, no migration) ships; Milestone 2
  web work needs the API's Milestone 2 (the `ClearedAmount` migration + overlay math change).
- **`isEligibleForAutoCascade`/`clearedAmount`/`settlementStatus` are additive-only** on `MemberBalanceRow`
  — no existing field is removed or renamed (API doc, confirmed additive-only DTO growth, consistent with
  the shipped feature's own OQ15a precedent).
- **`expensesKeys.all` invalidation cascades to every `detail`/`history` sub-key** via TanStack Query's
  default prefix (fuzzy) match on `invalidateQueries({ queryKey: [...] })` — confirmed by reading
  `invalidateExpense`'s own existing redundant explicit `detail`/`history` calls (belt-and-suspenders, not
  strictly required) and TanStack Query v5's documented default `exact: false` matching. This is why
  `useSetMemberSettled`'s fix (M1-R1) can be a single `expensesKeys.all` invalidation rather than needing
  to enumerate the (unknown, cascade-determined) set of affected expense uuids client-side.
- **`eventUuid` is threaded through `SettledToggle`/`ShareSettledToggle` as a new optional prop**, sourced
  from the already-in-scope `ExpenseResponse.eventUuid`/`ExpenseSummaryResponse.eventUuid` at every call
  site (`ExpenseDetailPage`, `ExpensesTable`, `SharesSection`) — no new query is needed to obtain it.
- **Milestone 2's cross-invalidation only fires when `eventUuid` is present** (a loose expense has no
  event balance to invalidate) — mirrors the API doc's own Assumption that Direction 2 never applies to a
  loose expense.
- **No optimistic updates are introduced** — both milestones stay refetch-based/invalidate-on-success,
  consistent with the shipped OQ6a decision; not reopened here.
- **The QR flow (`QrDialog`, `useEventQrQuery`) needs no code change** — confirmed additive-only by the
  API doc (Story C, `WalletQrService` unchanged); only a regression test is planned.
- **No new page/route.** All Milestone 1/2 UI lands inside the already-shipped `EventDetailPage`
  (`EventBalanceTable`) and `ExpenseDetailPage` (`SharesSection`, header `SettledToggle`) — mirrors the
  shipped feature's own scope.

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. Split into the same two milestones the backend ships in (per the BA
> doc's Handoff Summary and the API doc's own milestone split) — **Milestone 1 web work should not start
> before the API's Milestone 1 ships**, and likewise for Milestone 2. Steps marked **[MOD]** touch shipped
> files (run `gitnexus_impact` upstream before editing any shared/shipped symbol, e.g. `SettledSwitch`,
> `EventBalanceTable`, per `CLAUDE.md`'s GitNexus rule).

### Milestone 1 — Direction 1 (event settle → cascade to expenses)

**Step M1.1 — Types.**

- **[MOD]** `features/events/api/types.ts` — add to `MemberBalanceRow`:
  ```ts
  /**
   * Direction-1 auto-cascade eligibility (event-expense settlement sync): true if
   * marking this member's event-level settled flag will automatically cascade to
   * all of their shares in the event (a net debtor, or a net creditor who holds no
   * debtor-share elsewhere in the event). Read verbatim — never re-derived
   * client-side (the gross/net classification is the API's single canonical
   * helper; duplicating it here is the exact drift risk the backend design
   * exists to prevent).
   */
  isEligibleForAutoCascade: boolean;
  ```

**Step M1.2 — Cache invalidation fix (the core Milestone-1 web task).**

- **[MOD]** `features/events/hooks/useEvents.ts` `useSetMemberSettled` — add, alongside the existing
  `eventsKeys.balance`/`eventsKeys.all` invalidation:
  ```ts
  import { expensesKeys } from "@/features/expenses/hooks/useExpenses";
  // ...
  onSuccess: (_data, { eventUuid }) => {
    void queryClient.invalidateQueries({ queryKey: eventsKeys.balance(eventUuid) });
    void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
    // Direction 1 (event-expense settlement sync): a member-level settle/un-settle
    // can now cascade Share.isSettled across every expense the member has a share
    // in, in this event — invalidate the expenses caches too (fuzzy-matches every
    // `detail`/`history` sub-key, not just the list).
    void queryClient.invalidateQueries({ queryKey: expensesKeys.all });
  },
  ```
  Update the function's doc comment (currently asserts "it does NOT reach the expenses caches — Layer B
  does not change expense/share data") to state the opposite, dated to this feature.
  Run `gitnexus_impact({ target: "useSetMemberSettled", direction: "upstream" })` first and report the
  blast radius (expected: `MemberSettledToggle` only) before editing.

**Step M1.3 — Creditor-row settle affordance (per OQ2's resolution — locked, no further design work).**

- **[MOD]** `features/events/components/EventBalanceTable.tsx` `StatusCell` — branch on
  `row.balance >= 0` into three cases instead of the current single early-return-to-muted-dash:
  1. `balance < 0` (owing, unchanged from today in Milestone 1 — Milestone 2 replaces this branch's badge
     per M2-R4/Step M2.3): existing `Badge` + `MemberSettledToggle`.
  2. `balance >= 0 && row.isEligibleForAutoCascade` (eligible creditor): the same `Badge` +
     `MemberSettledToggle` combination an owing row gets today (`MemberSettledToggle` needs no new props —
     `isSettled` still drives it identically for a creditor), plus a `HelpHint` reading "Đánh dấu đã trả sẽ
     tự động đánh dấu tất cả phần gánh liên quan của thành viên này là đã trả."
  3. `balance >= 0 && !row.isEligibleForAutoCascade` (ineligible creditor): **no toggle** — the muted "—"
     is replaced by a `HelpHint` reading "Thành viên này vừa có khoản được nhận vừa có khoản phải trả
     trong đợt, nên không thể tự động đồng bộ — hãy đánh dấu từng phiếu/phần gánh riêng."
  4. Net-zero balance (`balance === 0` and, per case 3's classification, `isEligibleForAutoCascade` is
     always `false` for a true net-zero member per the API's own rule): falls into case 3's branch by the
     condition above, but the resolved design keeps net-zero visually identical to today (muted "—", no
     hint) — so `StatusCell` must distinguish "net-zero, nothing to explain" from "gross-mixed, needs the
     hint" using `row.balance === 0` as a fourth explicit branch, not folded into case 3.
  `MemberSettledToggle` (`features/events/components/MemberSettledToggle.tsx`) needs **no new props** —
  confirmed by OQ2's resolved design; the branching lives entirely in `StatusCell`.
- Update `COLUMN_COUNT`/skeleton if the creditor row's cell shape changes width materially (unlikely — it
  reuses the existing "Trạng thái" column; `HelpHint` is a small inline trigger, not a new column).

**Step M1.4 — i18n.**

- **[MOD]** `i18n/locales/{vi-VN,en-US}/events.json` under `balance`: add
  `creditorEligibleHint` ("Đánh dấu đã trả sẽ tự động đánh dấu tất cả phần gánh liên quan của thành viên
  này là đã trả." / en-US mirror) and `creditorIneligibleHint` ("Thành viên này vừa có khoản được nhận vừa
  có khoản phải trả trong đợt, nên không thể tự động đồng bộ — hãy đánh dấu từng phiếu/phần gánh riêng." /
  en-US mirror) for the two `HelpHint`s in Step M1.3. Per OQ5's resolution, also update
  `settledToastOn`/add a new eligible-cascade toast key (e.g. `settledToastOnCascade`/
  `settledToastOffCascade`) so `MemberSettledToggle` can pick the cascade-aware copy when
  `row.isEligibleForAutoCascade` is true, falling back to the existing `settledToastOn`/`settledToastOff`
  otherwise (Step M1.5 below).

**Step M1.5 — Toast copy for the cascading event-level toggle (per OQ5's resolution).**

- **[MOD]** `features/events/components/MemberSettledToggle.tsx` — thread `isEligibleForAutoCascade:
  boolean` as a new prop (sourced from `row.isEligibleForAutoCascade`, already reaching `StatusCell` per
  M1-R3) and branch the success toast: when `true`, use the cascade-aware copy ("Đã đánh dấu {name} đã trả
  — các phần gánh liên quan của họ trong đợt cũng đã được tự động đánh dấu đã trả." / un-settle mirror);
  when `false`, keep today's plain `settledToastOn`/`settledToastOff`. Run `gitnexus_impact` on
  `MemberSettledToggle` first (expected callers: `StatusCell` only) before editing, per `CLAUDE.md`.

**Step M1.6 — Tests (web-test-engineer).**

Extend existing files (per the task brief's named starting set) rather than only adding new ones:

- `src/features/events/memberSettled.test.tsx` — extend:
  - `MemberSettledToggle_MarkSettled_AlsoInvalidatesExpensesCache`: after marking a member settled,
    assert the expenses cache is refetched (e.g. spy/assert a `GET /v1/expenses/*` or
    `GET /v1/expenses/{uuid}` request occurs post-toggle, or assert `queryClient` staleness via a
    `useIsFetching`/refetch probe) — the concrete regression for the fixed hook.
  - `MemberSettledToggle_CreditorRow_RendersAffordanceWhenEligible`: a `balance >= 0` row with
    `isEligibleForAutoCascade: true` renders a toggle (not the muted "—") plus the `creditorEligibleHint`
    `HelpHint`.
  - `MemberSettledToggle_CreditorRow_IneligibleGrossMixed_HidesToggleShowsHint`: a `balance > 0` row with
    `isEligibleForAutoCascade: false` renders **no** toggle, with the muted "—" replaced by the
    `creditorIneligibleHint` `HelpHint` (per OQ2's resolved design).
  - `MemberSettledToggle_CreditorRow_NetZero_UnchangedMutedDash`: a `balance === 0` row renders the plain
    muted "—" with no `HelpHint` (regression that the net-zero case isn't folded into the ineligible-hint
    branch — Step M1.3's explicit fourth branch).
  - `MemberSettledToggle_EligibleCascade_ToastCommunicatesCascade`: toggling an eligible creditor/debtor
    shows the cascade-aware toast copy (Step M1.5); toggling an ineligible row keeps the plain toast.
- `src/features/events/eventBalanceTable.test.tsx` — extend: a fixture row with `balance > 0` and
  `isEligibleForAutoCascade: true` renders the same status-cell shape a debtor row does (regression that
  the eligible-creditor branch doesn't silently regress the owing-row rendering).
- MSW (`src/test/msw/handlers.ts`): `computeBalance` gains `isEligibleForAutoCascade` per row (a
  test-double classification mirroring the API's four-way rule: net debtor → true; net creditor with no
  debtor-share elsewhere in the event → true; net creditor with a debtor-share elsewhere → false;
  net-zero → false); the per-member settled PUT handler cascades `Share.isSettled` across every expense
  the member has a share in within that event, mirroring Direction 1's forward + reversal behavior
  (needed for `expenseSettledReconcile.test.tsx`-style end-to-end assertions and for M1.6's own fixtures).

### Milestone 2 — Direction 2 (expense/share settle → partial credit) + Story C (QR)

> Do not start until Milestone 1 is merged and the API's Milestone 2 (the `ClearedAmount` migration +
> overlay math change) has shipped. **OQ1 (wire format) is resolved** (Decision Log entry 1) — Step
> M2.1's types below are final, not a guess — but still re-confirm the actual shipped DTO's property name
> casing/exact JSON key at implementation time as an ordinary "read the real API before wiring" check, not
> because the wire-format ambiguity itself is still open.

**Step M2.1 — Types.**

- **[MOD]** `features/events/api/types.ts` — add to `MemberBalanceRow`:
  ```ts
  /**
   * Cumulative amount credited via Direction 2 (expense/share settle → partial
   * event-level credit), VND, capped at this member's net owed amount. Rendered
   * verbatim (D2 — never client-derived).
   */
  clearedAmount: number;
  /**
   * Service-derived tri-state overlay status (Unsettled/PartiallySettled/Settled),
   * computed by the backend from `clearedAmount`/net owed/`isSettled` — never
   * re-derived client-side. Wire format: `string` (OQ1, resolved — see planning
   * doc Decision Log entry 1), not the raw C# enum.
   */
  settlementStatus: "Unsettled" | "PartiallySettled" | "Settled";
  ```
  and to `EventBalanceResponse`:
  ```ts
  /** Count of owing members whose net debt is partially, but not fully, cleared. */
  partiallySettledMemberCount: number;
  ```

**Step M2.2 — Cache invalidation fix (the core Milestone-2 web task).**

- **[MOD]** `features/expenses/hooks/useExpenses.ts` — thread `eventUuid` through both mutations'
  variables (sourced from the caller, which already has `ExpenseResponse.eventUuid`/
  `ExpenseSummaryResponse.eventUuid` in scope) and cross-invalidate when present:
  ```ts
  import { eventsKeys } from "@/features/events/hooks/useEvents";
  // ...
  export function useSetSettled() {
    return useMutation({
      mutationFn: ({ uuid, body }: { uuid: string; body: SetSettledRequest }) =>
        expensesApi.setSettled(uuid, body),
      onSuccess: (_data, { uuid }, context) => invalidateExpense(uuid), // unchanged base
    });
  }
  ```
  Concretely: add an optional `eventUuid?: string | null` field to both mutations' variable objects
  (`{ uuid, body, eventUuid }` / `{ expenseUuid, shareUuid, body, eventUuid }`) and, in `onSuccess`, when
  `eventUuid` is truthy, additionally call
  `queryClient.invalidateQueries({ queryKey: eventsKeys.balance(eventUuid) })` and
  `queryClient.invalidateQueries({ queryKey: eventsKeys.all })`. Update both functions' doc comments
  (delete the now-false "OQ7a: expenses only" / "does not change the balance overlay" claims, dated to
  this feature superseding OQ7a).
  Run `gitnexus_impact` on `useSetSettled`/`useSetShareSettled` first (expected callers:
  `SettledToggle`, `ShareSettledToggle`) and report the blast radius.
- **[MOD]** `features/expenses/components/SettledToggle.tsx` — add an `eventUuid?: string | null` prop,
  pass it through `setSettled.mutateAsync({ uuid, body, eventUuid })`.
- **[MOD]** `features/expenses/components/ShareSettledToggle.tsx` — add an `eventUuid?: string | null`
  prop, pass it through similarly.
- **[MOD]** call sites — thread `expense.eventUuid` into both toggles:
  - `features/expenses/pages/ExpenseDetailPage.tsx` (`<SettledToggle uuid=... eventUuid={expense.eventUuid} />`).
  - `features/expenses/components/ExpensesTable.tsx` (row-level `<SettledToggle ... eventUuid={row.eventUuid} />`).
  - `features/expenses/components/SharesSection.tsx` (`<ShareSettledToggle expenseUuid=... eventUuid={expense.eventUuid} />`).

**Step M2.3 — 3-state status + partial amount (per OQ1/OQ3/OQ4's resolutions — components already built,
this step is wiring only).**

- **[MOD]** `features/events/components/EventBalanceTable.tsx` `StatusCell` (owing-row branch,
  `balance < 0`, unchanged from Milestone 1's creditor-row branch): replace the inline `Badge` +
  `tone={row.isSettled ? "settled" : "warning"}` with the already-built `SettlementStatusBadge`
  (`import { SettlementStatusBadge } from "@/features/expenses/components/SettlementStatusBadge"`),
  mapping `row.settlementStatus` → its local `SettlementTriState` (`"Unsettled" → "unsettled"`,
  `"PartiallySettled" → "partial"`, `"Settled" → "settled"`) and passing
  `labelUnsettled={t("events:balance.statusOwing")}` (post-OQ6 rename, now "Chưa trả"),
  `labelPartial={t("events:balance.statusPartial")}` (new key, "đã trả một phần"),
  `labelSettled={t("events:balance.statusSettled")}`. No `Badge`/CSS work remains — `partial` tone and
  the icon set (`HalfCheckIcon` etc.) already exist (Background).
- **[MOD]** the "Còn nợ" column: for a row with `settlementStatus === "PartiallySettled"`, replace the
  plain `<Money value={row.outstanding} />` cell with the already-built `SettlementMeter`
  (`clearedAmount={row.clearedAmount}`, `netOwed={row.clearedAmount + row.outstanding}`, `format=
  {formatMoneyVnd}`, `accessibleLabel={t("events:balance.clearedAriaNamed", { name: row.memberName })}`
  — a new aria-label key); `Unsettled`/`Settled` rows keep the existing plain `<Money>` cell unchanged. No
  new query, no client-side recomputation of the net-owed figure beyond the one sanctioned
  `clearedAmount + outstanding` composition. No `COLUMN_COUNT` change (same column, conditional cell
  content).
- **[MOD]** column header: add a single `HelpHint` next to the "Còn nợ" `TableHeaderCell` (not per-row)
  reading `events:balance.clearedModelHint` ("Số tiền đã tất toán tính theo số dư ròng của đợt, có thể
  khác tổng các phần gánh đã đánh dấu đã trả nếu thành viên vừa là người trả vừa là người nợ trong đợt.").
- **[MOD]** `TableFoot` summary: extend the existing `events:balance.summary` sentence (or add a second
  line) to surface `balance.partiallySettledMemberCount` alongside `settledMemberCount`/
  `owingMemberCount`, verbatim from the API.
- **[MOD]** `i18n/locales/{vi-VN,en-US}/events.json` under `balance`: add `statusPartial` ("đã trả một
  phần" / "Partially settled"), `clearedAriaNamed` ("Đã tất toán của {{name}}" / "Cleared amount for
  {{name}}"), `clearedModelHint` (copy above / en-US mirror). Rename `statusOwing` per OQ6's resolution
  ("Còn nợ" → "Chưa trả" vi-VN, "Owing" → "Unsettled" en-US) — done once here, read by both the M1 binary
  badge (Step M1.3, unaffected in wording) and this step's `SettlementStatusBadge`.

**Step M2.4 — QR regression (no code change, Story C).**

- No `QrDialog`/`useEventQrQuery`/`wallet` code change (confirmed additive-only, API doc Story C). Add a
  regression test only (Tests section below).

**Step M2.5 — Tests (web-test-engineer).**

Extend existing files (per the task brief's named starting set):

- `src/features/expenses/settledToggle.test.tsx` — extend:
  `SettledToggle_MarkSettled_OnEventExpense_AlsoInvalidatesEventBalance`: with `eventUuid` set, toggling
  the whole-expense settled flag triggers a refetch of `GET /v1/events/{eventUuid}/balance` (assert via a
  request-count spy or a `MemberBalanceRow.clearedAmount` change reflected in a co-rendered
  `EventBalanceTable`, mirroring the existing `expenseSettledReconcile.test.tsx` cross-component pattern);
  a **loose** expense (`eventUuid` undefined) does NOT trigger any `/balance` request (regression for "no
  event, no invalidation").
- `src/features/expenses/shareSettled.test.tsx` — extend: a per-share toggle on an expense that belongs
  to an event also invalidates/refetches the event balance (same pattern as above, share-level trigger).
- `src/features/expenses/expenseSettledReconcile.test.tsx` — extend: mount both `SharesSection`/
  `ExpenseDetailPage` and `EventBalanceTable` against the same MSW-backed event; toggling a share updates
  `clearedAmount`/`settlementStatus` on the co-rendered balance table without a manual re-render trigger
  (proves the cross-cache invalidation wiring end-to-end, not just that the hook function invalidates the
  right key in isolation).
- `src/features/events/eventBalanceTable.test.tsx` — extend: renders `PartiallySettled` via
  `SettlementStatusBadge` with the locked copy + `partial` tone; a `PartiallySettled` row renders
  `SettlementMeter` (fraction text + `role="progressbar"`) in place of the plain `<Money>` cell, with
  `netOwed` equal to `clearedAmount + outstanding` (regression for the OQ-L composition rule); `Unsettled`/
  `Settled` rows keep the plain `<Money>` cell; the column-header `HelpHint` is present once, not per-row;
  the footer surfaces `partiallySettledMemberCount`.
- `src/features/events/settledQrFilter.test.ts` — extend: a member with `clearedAmount` between `0` and
  their net owed amount is billed exactly the remainder (`outstanding`) on the event QR, not the full
  amount and not zero; reaching full clearance drops them from the QR exactly as today; all-cleared still
  yields `12003`.
- MSW (`src/test/msw/handlers.ts`): `computeBalance` gains `clearedAmount`/`settlementStatus` per row
  (mirroring the API's `max(0, netOwed - clearedAmount)` formula and the tri-state derivation); the
  whole-expense and per-share settled PUT handlers apply/claw back credit to every eligible debtor
  member's `clearedAmount` on settle/un-settle (the "one shared code path" both triggers fire, mirrored as
  one test-double helper function shared by both MSW handlers, matching the API's own OQ-D-residual
  architecture); `partiallySettledMemberCount` computed alongside the existing count rollups.

## Impact Analysis

- **APIs:** none authored here — consumes the two finalized upstream contracts. **Blocked on each
  milestone's backend landing** (mirrors the shipped feature's own gating).
- **Design system — already shipped, no further work.** `ui-designer` built every primitive this feature
  needs ahead of this checkpoint, confirmed present in the tree: `src/components/ui/HelpHint/` (new),
  `src/components/ui/Badge/Badge.tsx` + `.module.css` (+`partial` tone, OQ3),
  `src/features/expenses/components/SettlementStatusBadge.tsx` (new, OQ3),
  `src/features/expenses/components/SettlementMeter.tsx` + `.module.css` (new, OQ4),
  `src/features/expenses/components/icons.tsx` (+`HalfCheckIcon`, OQ3). Nothing under
  `components/ui/*`/`features/expenses/components/*` needs to change for this feature — the Implementation
  Plan only *imports and wires* these into `EventBalanceTable`/`MemberSettledToggle` (Milestone 1 Steps
  M1.3/M1.4/M1.5; Milestone 2 Step M2.3).
- **Frontend (files), Milestone 1:**
  - **[MOD]** `features/events/api/types.ts` (+`isEligibleForAutoCascade`), `hooks/useEvents.ts`
    (`useSetMemberSettled` cross-invalidates `expensesKeys.all`), `components/EventBalanceTable.tsx`
    (`StatusCell` four-way branch: owing / eligible-creditor+`HelpHint` / ineligible-creditor+`HelpHint` /
    net-zero, per OQ2), `components/MemberSettledToggle.tsx` (+`isEligibleForAutoCascade` prop, cascade-
    aware toast copy, per OQ5 — **no** `disabled`/`disabledReason` prop; OQ2 resolved to hiding the toggle
    entirely for ineligible creditors, not disabling it).
  - **[MOD]** i18n `locales/{vi-VN,en-US}/events.json` (+`creditorEligibleHint`, `creditorIneligibleHint`,
    cascade-aware toast keys, per OQ2/OQ5).
- **Frontend (files), Milestone 2:**
  - **[MOD]** `features/events/api/types.ts` (+`clearedAmount`/`settlementStatus`/
    `partiallySettledMemberCount`), `features/expenses/hooks/useExpenses.ts` (`useSetSettled`/
    `useSetShareSettled` gain `eventUuid` in variables + cross-invalidate `eventsKeys.balance`, plus the
    OQ5-resolved toast copy), `features/expenses/components/{SettledToggle.tsx,ShareSettledToggle.tsx}`
    (+`eventUuid` prop), `features/expenses/pages/ExpenseDetailPage.tsx`,
    `features/expenses/components/{ExpensesTable.tsx, SharesSection.tsx}` (thread `eventUuid` at call
    sites), `features/events/components/EventBalanceTable.tsx` (owing-row branch swaps to
    `SettlementStatusBadge`/`SettlementMeter`, per OQ1/OQ3/OQ4 — both components imported, not authored,
    here).
  - **[MOD]** i18n `locales/{vi-VN,en-US}/events.json`: rename `statusOwing` ("Còn nợ"→"Chưa trả" vi-VN,
    "Owing"→"Unsettled" en-US, per OQ6); add `statusPartial`, `clearedAriaNamed`, `clearedModelHint`
    (OQ3/OQ4); `outstanding`/`summary` keys explicitly **unchanged** (OQ6 — different grammatical role,
    see Open Questions).
  - **No change:** `features/wallet/*` (`QrDialog`, `useEventQrQuery`) — confirmed additive-only.
- **Test fixtures:** `src/features/events/memberSettled.test.tsx` line ~177
  (`expect(within(an).getAllByText("Còn nợ"))`) must be updated to assert `"Chưa trả"` alongside the
  `statusOwing` rename (OQ6) — the one identified pre-existing literal-text assertion this rename breaks.
- **Related-but-out-of-scope (not touched):** `src/i18n/locales/{vi-VN,en-US}/share.json`
  (`public.statusOwing`, same "Còn nợ" wording, on the public unauthenticated settlement-report page,
  `src/features/share/components/PublicBalanceTable.tsx`) — a parallel terminology instance discovered
  while tracing OQ6, deliberately left out of this doc's scope (the `share` feature isn't in this
  feature's file list; see Future Improvements).
- **Data-fetching:** no new query keys; `expensesKeys`/`eventsKeys` cross-invalidation grows in both
  directions (previously one-directional or absent). No optimistic updates introduced.
- **Tests:** extends all six named existing files
  (`features/events/memberSettled.test.tsx`, `features/events/eventBalanceTable.test.tsx`,
  `features/expenses/settledToggle.test.tsx`, `features/expenses/shareSettled.test.tsx`,
  `features/expenses/expenseSettledReconcile.test.tsx`, `features/events/settledQrFilter.test.ts`) plus
  the MSW handler mock-classification/credit logic described per milestone above.
- **Infrastructure / Services / DB:** none (FE only).

## Decision Log

> Inherited, locked, NOT reopened here: every BA-doc and API-doc decision (the eligibility gate, cascade
> scope, symmetric capped reversal, both triggers share one code path, fully automatic/no confirmation,
> no audit, allowed on closed events, Free-tier, the plain-success-message response shape, the
> `ClearedAmount`/`EventSettlementStatus` representation and naming). See those two docs for full
> rationale; nothing here relitigates them.

> Entries below record THIS doc's own implementation-level decisions, confirmed at the 2026-08-25
> checkpoint. **Zero Open Questions remain in this doc.** Do not reopen these six without a new explicit
> user decision.

1. **OQ1 — Option (a): `SettlementStatus` is `string` on the wire.** Confirmed by `feature-planner`
   against the API doc's own OQ-WF Decision Log entry: `MemberBalanceRow.SettlementStatus` is a `string`
   DTO property (`"Unsettled" | "PartiallySettled" | "Settled"`, camelCase wire key `settlementStatus`),
   backed internally by a service-only `EventSettlementStatus` enum that is never the DTO's own CLR type
   (no `JsonStringEnumConverter` registered — a raw enum would have serialized as `0|1|2`). *Reason:*
   matches the established `UserResponse.Tier`/`Role` precedent for typing status/category fields as
   `string` at the DTO boundary; avoids the web ever needing to special-case an integer enum. Web types
   `SettlementStatus = "Unsettled" | "PartiallySettled" | "Settled"`.

2. **OQ2 — A hide-and-explain variant (not originally-listed option (a) or (b) verbatim): show the
   existing toggle + a `HelpHint` for eligible creditors; hide the toggle entirely + a different `HelpHint`
   for ineligible creditors; net-zero unchanged.** `ui-designer` designed this once `HelpHint` (a new,
   dependency-free primitive) existed to fill the explanation gap the original option (b) left open,
   avoiding option (a)'s need for new `disabled`/`disabledReason` props on `MemberSettledToggle`. The user
   explicitly confirmed **hiding** the toggle for ineligible creditors over "always show, disabled."
   *Reason:* a disabled control invites a click-and-see-why interaction this codebase doesn't otherwise
   use for settle toggles; a `HelpHint` explains the "why" upfront without a control that visually implies
   "you can enable this eventually." *Copy locked:* eligible — "Đánh dấu đã trả sẽ tự động đánh dấu tất cả
   phần gánh liên quan của thành viên này là đã trả."; ineligible — "Thành viên này vừa có khoản được nhận
   vừa có khoản phải trả trong đợt, nên không thể tự động đồng bộ — hãy đánh dấu từng phiếu/phần gánh
   riêng."

3. **OQ3 — Option (a): new `partial` `Badge` tone + a dedicated `SettlementStatusBadge` component.**
   `ui-designer` added the `partial` tone to `Badge`/`Badge.module.css` and built
   `SettlementStatusBadge` to own the icon+tone+text mapping for all 3 states in one place (`Unsettled` →
   `warning` + `ClockIcon`, `PartiallySettled` → `partial` + new `HalfCheckIcon`, `Settled` → `settled` +
   `CheckIcon`). *Reason:* rejected (b)/(c) (reusing `info`/`warning`) because neither carries an
   established "in-progress payment" connotation and (c) specifically weakens color-independent legibility
   per the a11y baseline — a dedicated tone + distinct icon silhouette per state reads correctly even
   without color vision. One component, not ad hoc `Badge` calls at each use site, closes part of the
   OQ-K "unify the settled notions" risk at the component level.

4. **OQ4 — Money-metaphor lives in the existing "Còn nợ" column, via a new `SettlementMeter` component.**
   `ui-designer` built `SettlementMeter` (fraction text + two-segment fill bar, `--fs-color-partial` fill,
   `role="progressbar"`) rather than a new column or a tooltip/expandable detail. *Reason:* no
   `COLUMN_COUNT`/responsive-stacking change on an already-6-column table; the fraction is inline exactly
   where the reader is already looking for "how much does this member still owe." The component's own doc
   comment mandates `netOwed` always be server-verbatim (composed as `clearedAmount + outstanding`, the
   one sanctioned display-only arithmetic — never a client-side sum of individually-settled bills), closing
   the OQ-L legibility trap explicitly. A one-time column-header `HelpHint` (not per-row) explains the
   net-model behavior, avoiding a repeated explanation on every partially-settled row.

5. **OQ5 — Cascade-aware toast copy for the event-member toggle when eligible; unconditional updated copy
   for the always-attempted share/expense-level toggle.** Draft Vietnamese copy locked (see Requirements
   M1-R4/M2-R1); exact final strings (including en-US mirrors) remain implementation-time i18n work, not a
   blocker on this doc. *Reason:* the original "keep everything unchanged" default recommendation was
   rejected at the checkpoint — communicating the cascade/sync side effect where it's real (eligible
   member-toggle; any event-linked share/expense-toggle) was judged more useful than staying silent, while
   the ineligible-member case correctly keeps the plain toast since nothing extra happened.

6. **OQ6 — Option (a): straight rename, not parallel-but-distinct copy.** `events:balance.statusOwing`
   changes from "Còn nợ"/"Owing" to "Chưa trả"/"Unsettled" in both locales. *Reason:* the same member state
   would otherwise read two different words on the same screen (or across the M1→M2 transition) —
   `statusOwing` and the incoming `SettlementStatusBadge`'s `Unsettled` label describe the identical
   condition, so parallel terminology would be a needless, confusing drift rather than a deliberate
   distinction. *Scoped explicitly, not blanket:* `events:balance.outstanding` (column header) and
   `summary`'s "Còn nợ {{amount}}" phrase label an amount, not a status adjective, and are unchanged;
   `share:public.statusOwing` (a different feature surface, the public settlement-report page) is a
   related finding, deliberately out of this doc's scope, carried to Future Improvements instead. The one
   test-fixture literal-text assertion this breaks (`memberSettled.test.tsx` line ~177) is listed in the
   Implementation Plan's test step.

## Progress Log

### 2026-08-25

- Read the finalized BA doc `planning/ba/event-expense-settlement-sync-business-analysis.md` in full
  (all four user stories, Business-Rule Impact, the OQ-L creditor-gate amendment and its algebraic
  characterization, Cross-Functional Workstreams → Web brief, Tier & Data Implications, Risks &
  Sequencing, all 10 Decision Log entries, the Handoff Summary's Milestone 1/2 split).
- Read the finalized API doc `FairShareMonApi/planning/event-expense-settlement-sync.md` in full (all 5
  implementation-level Open Questions and their Decision Log resolutions — OQ1 reversal mechanics, OQ2
  `ClearedAmount` as sole source of truth, OQ3 plain response shape, OQ4 `IsEligibleForAutoCascade`
  additive exposure, OQ5 naming/copy sign-off), the full per-milestone Implementation Plan, and the
  Impact Analysis (confirming the exact additive DTO growth: `MemberBalanceRow` gains `ClearedAmount`,
  `SettlementStatus`, `IsEligibleForAutoCascade`; `EventBalanceResponse` gains
  `PartiallySettledMemberCount`; all three settled-toggle routes keep their existing plain-success-message
  response shape).
- Read `FairShareMonWeb/CLAUDE.md` (locked stack/conventions) and
  `FairShareMonWeb/planning/settled-per-member.md` in full (the shipped Layer A/B web feature this one
  extends — all 12 locked OQ decisions, the exact shipped file list, and its own "don't start until the
  backend ships" gating precedent, which this doc mirrors).
- Read the live code grounding every touch point: `features/events/api/types.ts`
  (`MemberBalanceRow`/`EventBalanceResponse`, confirmed no overlay-growth fields yet beyond the shipped
  Layer B set), `features/expenses/hooks/useExpenses.ts` (`useSetSettled`/`useSetShareSettled`, confirmed
  the exact "expenses caches only" invalidation + the now-false doc-comment assumption), `features/events/
  hooks/useEvents.ts` (`useSetMemberSettled`, confirmed the exact "no expensesKeys reach" gap),
  `features/events/components/EventBalanceTable.tsx` (`StatusCell`, confirmed the exact `balance >= 0` →
  muted "—", no-control-at-all branch the task brief described), `features/expenses/components/
  {SettledToggle,ShareSettledToggle}.tsx` and `features/events/components/MemberSettledToggle.tsx`
  (confirmed neither toggle carries/threads an `eventUuid` today), `features/expenses/api/types.ts`
  (confirmed `ExpenseResponse`/`ExpenseSummaryResponse` already carry `eventUuid`, so every toggle call
  site already has it in scope), `features/expenses/components/{SharesSection.tsx,ExpensesTable.tsx}` and
  `features/expenses/pages/ExpenseDetailPage.tsx` (confirmed every `SettledToggle`/`ShareSettledToggle`
  call site has `expense.eventUuid` in scope already), `components/ui/Badge/Badge.tsx` (confirmed the
  current tone set has no "in-progress" tone), and grepped `Program.cs` for a `JsonStringEnumConverter`
  registration (found none) against `Models/Auth/UserResponse.cs` (`Tier`/`Role` typed `string`, not raw
  enums) to characterize OQ1's wire-format ambiguity precisely rather than guessing.
- Skimmed the six named existing test files (`features/events/memberSettled.test.tsx`,
  `features/events/eventBalanceTable.test.tsx`, `features/expenses/settledToggle.test.tsx`,
  `features/expenses/shareSettled.test.tsx`, `features/expenses/expenseSettledReconcile.test.tsx`,
  `features/events/settledQrFilter.test.ts`) and the relevant `src/test/msw/handlers.ts` sections
  (`computeBalance`, the three settled PUT handlers) to plan concrete, additive test extensions rather
  than a vague "add tests" instruction.
- Drafted the Objective, Background, per-milestone Requirements, six Open Questions (wire format of the
  new enum; the creditor-row affordance visual design; the `PartiallySettled` badge tone; the
  partial-amount money-metaphor layout; toast copy for the now-side-effecting toggles; the "Còn nợ" vs.
  locked "chưa trả" terminology reconciliation) — deliberately not resolving any of them, since each is
  either a genuine design-ownership boundary (`ui-designer`) or a cross-team contract ambiguity
  (wire format) rather than something a reasonable engineer would silently default. Wrote the two-milestone
  Implementation Plan naming every concrete file/component/hook/type change, the Impact Analysis, and this
  Progress Log entry. Did not write any code — planning only, per the task's explicit scope.

### 2026-08-25 (checkpoint — all six Open Questions resolved)

- Received the resolved answer set for all six Open Questions: OQ1 (wire format) from `feature-planner`,
  citing the API doc's own OQ-WF Decision Log entry; OQ2-OQ4 (creditor-row affordance, badge tone,
  money-metaphor) from `ui-designer`'s design, confirmed by the user; OQ5 (toast copy) with draft
  Vietnamese strings provided, final i18n locking left to implementation time; OQ6 (statusOwing rename)
  resolved directly.
- Confirmed every design-system artifact `ui-designer` referenced already exists in the tree by reading
  each file directly (not taking the resolution's word for it): `src/components/ui/HelpHint/HelpHint.tsx`
  + `.module.css` (new, dependency-free, `:focus-within`-driven, no Radix Tooltip); `src/components/ui/
  Badge/Badge.tsx` (confirmed the `partial` tone is in the `BadgeTone` union) + `Badge.module.css`
  (confirmed the `.partial` CSS rule referencing `--fs-color-partial-{surface,border,text}`);
  `src/features/expenses/components/SettlementStatusBadge.tsx` (confirmed its `SettlementTriState` union
  and exact tone/icon mapping per state); `src/features/expenses/components/SettlementMeter.tsx` +
  `.module.css` (confirmed its `clearedAmount`/`netOwed`/`format`/`accessibleLabel` prop shape and its
  doc-comment's explicit "never client-recompute `netOwed`" mandate); `src/features/expenses/components/
  icons.tsx` (confirmed `HalfCheckIcon` exists, distinct silhouette from `ClockIcon`/`CheckIcon`).
  `features/events/api/types.ts` was re-checked and confirmed **not yet** carrying
  `isEligibleForAutoCascade`/`clearedAmount`/`settlementStatus` — the design primitives are built, but the
  wiring (Implementation Plan) is genuinely still future work, consistent with this doc's planning-only
  scope.
- Traced OQ6's "Còn nợ" rename precisely rather than doing a blanket find-and-replace: grepped every
  `Còn nợ`/`statusOwing` occurrence across `src/`. Found and scoped **in**: `events:balance.statusOwing`
  (the key named by the resolution) and its one live test-fixture assertion,
  `src/features/events/memberSettled.test.tsx` (`expect(within(an).getAllByText("Còn nợ"))`, ~line 177).
  Found and scoped **out**, with reasoning recorded in Decision Log entry 6: `events:balance.outstanding`
  and `summary` (label an amount, not a status adjective — read the actual JSON to confirm both keys'
  exact current strings before deciding) and `share:public.statusOwing`
  (`src/features/share/components/PublicBalanceTable.tsx`'s public settlement-report page — a different
  feature surface, out of this doc's file list, carried to Future Improvements instead of silently
  dropped).
- Rewrote the Objective's implicit framing, Background, Requirements (M1-R2/R4, M2-R1/R4/R5),
  Open Questions (all six converted to `~~OQ-X~~ → Answered` inline annotations, mirroring the two
  upstream docs' convention), added six Decision Log entries with full rationale, and updated the
  Implementation Plan (new Steps M1.3's four-way `StatusCell` branch, M1.4's hint/toast i18n keys, a new
  M1.5 for the cascade-aware toast, M2.1's now-final types, M2.3's concrete component wiring) and Impact
  Analysis to reflect the locked design instead of open options. Did not write any product code — this
  doc's scope stays planning-only; implementation remains a separate, explicitly future step gated on
  each milestone's backend shipping (Assumptions, unchanged).

### 2026-08-25 (Milestone 1 implemented — Steps M1.1-M1.5)

- Confirmed the API's Milestone 1 has shipped: `MemberBalanceRow.IsEligibleForAutoCascade` (bool) is a real
  field in `FairShareMonApi/Models/Stats/MemberBalanceRow.cs`, read directly before starting.
- **M1.1** — added `isEligibleForAutoCascade: boolean` to `MemberBalanceRow`
  (`features/events/api/types.ts`) with the doc comment verbatim from the plan.
- **M1.2** — GitNexus's indexed graph does not contain this frontend TS symbol (`query`/`impact` both
  returned "not found" for `useSetMemberSettled`, not a staleness warning) — an intermittent gap the task
  brief pre-authorized falling back from. Did a manual grep-based caller check instead: `useSetMemberSettled`
  is called only from `MemberSettledToggle.tsx`; blast radius matches the doc's expectation exactly (low
  risk). Added the `expensesKeys.all` invalidation to `useSetMemberSettled`'s `onSuccess`
  (`features/events/hooks/useEvents.ts`) and rewrote the function's doc comment to state the cascade
  instead of the old "does not reach the expenses caches" claim.
- **M1.3** — `EventBalanceTable.tsx`'s `StatusCell` rewritten into the four explicit branches (owing /
  eligible-creditor+`HelpHint` / ineligible-creditor+`HelpHint` / net-zero unchanged), reusing
  `MemberSettledToggle` and the existing `Badge` unmodified, importing `HelpHint` from `@/components/ui`.
  Also refreshed the component's top-of-file doc comment, which still asserted the toggle only ever
  appears for `balance < 0` rows.
- **M1.4** — added `creditorEligibleHint`, `creditorIneligibleHint`, `settledToastOnCascade`,
  `settledToastOffCascade` to both `i18n/locales/{vi-VN,en-US}/events.json` under `balance` (vi-VN copy
  verbatim from the plan; natural en-US mirrors authored).
- **M1.5** — `MemberSettledToggle` gained the `isEligibleForAutoCascade: boolean` prop (threaded from
  `StatusCell`'s `row.isEligibleForAutoCascade`) and branches its success toast between the cascade-aware
  copy and the existing plain copy. Manual grep confirmed `MemberSettledToggle` is rendered only from
  `EventBalanceTable.tsx`'s `StatusCell` (plus its own test file) — matches the doc's expected blast radius.
- **Mechanical fixture fixes** (explicitly sanctioned by the task brief, not new test authorship): three
  existing test files construct a `MemberBalanceRow`/`Partial<MemberBalanceRow>` object typed against the
  now-larger interface and needed the new required field added —
  `features/events/eventBalanceTable.test.tsx` (`ROWS` fixture), `features/share/publicBalanceTable.test.tsx`
  (`mkRow` helper default), `features/share/publicSharePage.test.tsx` (`PAYLOAD.rows`). No assertions were
  changed; the `share` feature's own product code (`PublicBalanceTable.tsx`) doesn't read the new field, so
  it's untouched (this doc's Assumptions already scope `share` out). The MSW mock's `computeBalance`
  (`src/test/msw/handlers.ts`) is intentionally left untouched — that's Step M1.6, `web-test-engineer`'s job.
- **Verification**: `pnpm lint` clean (only pre-existing, unrelated `only-export-components` warnings),
  `tsc -b` clean, `pnpm build` succeeds, `pnpm test` green (114 files / 949 tests passing, no regressions).
  Ran the app for real: started `pnpm dev` with `VITE_ENABLE_MOCKS=true`, logged in as the seeded `admin`
  user, opened the pre-seeded "Chuyến Đà Lạt" event (a real mixed creditor/debtor balance: one net creditor,
  two net debtors). Temporarily patched the MSW `computeBalance` helper in-place to compute a plausible
  `isEligibleForAutoCascade` (`balance !== 0`) purely to exercise the branches visually — reverted via
  `git checkout` immediately after (confirmed no diff remains) so Step M1.6's MSW work is untouched. Screenshots
  confirmed: the eligible-creditor row renders the badge+toggle+`HelpHint` exactly like an owing row plus
  the eligibility hint; flipping the flag to ineligible hides the toggle and shows the muted "—" +
  ineligible `HelpHint` instead; hovering the eligible-creditor's hint renders the exact locked Vietnamese
  copy; clicking the eligible creditor's toggle produced the exact cascade-aware toast ("Đã đánh dấu An
  Nguyễn đã trả — các phần gánh liên quan của họ trong đợt cũng đã được tự động đánh dấu đã trả."). The
  owing-row branch and the net-zero branch were unchanged and confirmed via the existing/updated test suite.
- **No Open Questions added** — nothing encountered required a stop-and-ask; the plan's code snippets and
  locked copy were followed as written.

### 2026-08-25 (Step M1.6 — tests added, `web-test-engineer`)

- Read Step M1.6's exact test list, the implemented M1.1-M1.5 code (`features/events/api/types.ts`,
  `hooks/useEvents.ts`'s `useSetMemberSettled`, `EventBalanceTable.tsx`'s four-way `StatusCell`,
  `MemberSettledToggle.tsx`'s `isEligibleForAutoCascade` prop/toast branch), and both locales'
  `creditorEligibleHint`/`creditorIneligibleHint`/`settledToastOnCascade`/`settledToastOffCascade` copy
  before writing anything, per the task brief.
- **`src/features/events/memberSettled.test.tsx`** — extended:
  - Added `isEligibleForAutoCascade: boolean` to the pre-existing `BASE` fixture (all three rows kept
    `false`, preserving every pre-existing assertion — the plain non-cascade toast, the owner's row still
    showing no toggle, now via the ineligible-creditor branch instead of the old blanket muted-dash branch).
  - `MemberSettledToggle_MarkSettled_AlsoInvalidatesExpensesCache` — the concrete M1.2 regression: mounts a
    live `useExpensesQuery({})` subscriber alongside `EventBalanceTable` (mirrors the
    `useEvents.test.tsx` counters+Probe pattern) and counts `GET /v1/expenses` requests; asserts a second
    GET fires after the member-settle mutation succeeds (before the fix, `expensesKeys.all` was never
    reached, so no second GET would have fired here).
  - A new, isolated `CREDITOR_ROWS`/`installCreditorStore()` fixture (separate `ev-creditor` event/uuid, so
    it never perturbs BASE's hardcoded switch-count/footer-count assertions) backing four new tests:
    `MemberSettledToggle_CreditorRow_RendersAffordanceWhenEligible`,
    `MemberSettledToggle_CreditorRow_IneligibleGrossMixed_HidesToggleShowsHint`,
    `MemberSettledToggle_CreditorRow_NetZero_UnchangedMutedDash` (scoped to the status `cell` specifically,
    since the "Còn nợ" outstanding column also renders a muted "—" for any `balance >= 0` row — an
    unscoped assertion would be ambiguous/misleading), and `MemberSettledToggle_EligibleCascade_
    ToastCommunicatesCascade` (both an eligible creditor AND an eligible debtor get the cascade-aware
    toast — cascade communication is gated on eligibility, not polarity).
  - `MemberSettledToggle_IneligibleOwingRow_KeepsPlainToast` — explicit companion regression (An Nguyễn,
    `isEligibleForAutoCascade: false`) confirming the plain toast is unchanged; overlaps in spirit with the
    pre-existing `MemberSettledToggle_Click_PutsToPerMemberRouteThenToasts` assertion but named directly
    per the M1.6 checklist for traceability.
- **`src/features/events/eventBalanceTable.test.tsx`** — extended:
  `EventBalanceTable_EligibleCreditorRow_RendersSameStatusCellShapeAsDebtorRow` — the ROWS fixture's "An
  Nguyễn" (already `isEligibleForAutoCascade: true` from the M1.1-M1.5 mechanical fixture fix) is asserted
  to render the same Badge + `MemberSettledToggle` shape "Cũ" (a debtor row) gets, plus the M1-R2 eligibility
  `HelpHint` — the regression that the new eligible-creditor branch doesn't silently break owing-row
  rendering. (Discovered mid-write: `getByText("Còn nợ")` is ambiguous — the Badge text AND the switch's own
  `labelOff` both render that string — switched to `getAllByText(...).length >= 1`, matching the existing
  color-independent-status assertion pattern already used elsewhere in `memberSettled.test.tsx`.)
- **`src/test/msw/handlers.ts`** — extended:
  - `computeBalance` now derives `isEligibleForAutoCascade` per row via a `billableDebtors` set (built
    alongside the existing `advanced`/`owed` accumulation): any member holding a "billable" share (not the
    expense's payer, `amount > 0` — the same definition the whole-expense settled handler's own OQ3a
    cascade already uses) on any expense in the event is NOT auto-cascade-eligible unless they are a net
    debtor. Mirrors the API's four-way rule (net debtor → always true; net creditor with no billable
    debtor-share elsewhere → true; net creditor with one → false; net-zero → false) exactly.
  - The per-member settled PUT handler (`PUT /events/:uuid/members/:memberUuid/settled`) now cascades
    `Share.isSettled` (+ `settledAt`) across every billable share the member holds on any expense within
    that event, forward (settle) and reversal (un-settle) alike — needed for realistic end-to-end fixtures
    per the task brief, and keeps the browser-mock (`VITE_ENABLE_MOCKS=true`) demo realistic without the
    manual in-place patch the M1.1-M1.5 implementation note describes having reverted.
  - Confirmed no other test file depends on the global `computeBalance`/member-settled-PUT handlers for
    balance assertions — `eventBalanceTable.test.tsx`, `memberSettled.test.tsx`,
    `eventDetailPage.test.tsx`, `useEvents.test.tsx`, and `share/eventDetailShare.test.tsx` all install
    their own local `server.use(...)` overrides for these routes, so the handlers.ts change is additive/
    inert for the existing suite (verified by running the full suite — no regressions).
- **Verification**: `pnpm lint` clean (only the same pre-existing, unrelated `only-export-components`
  warnings noted in the M1.1-M1.5 entry), `tsc -b` clean, `pnpm test` green — 114 files / **956 tests**
  passing (949 pre-existing + 7 new: 6 in `memberSettled.test.tsx`, 1 in `eventBalanceTable.test.tsx`), no
  regressions.
- **No product-code bugs found** — every M1.1-M1.5 branch behaved exactly per the plan; no fix was routed
  back to `web-implementer`.
- **Coverage gaps / deferred, explicitly out of this step's scope**: `settledToastOffCascade` (the
  un-settle mirror of the cascade toast) and the ineligible-creditor's un-toggle path aren't separately
  exercised here (Direction 1's settle/un-settle symmetry is already covered for the plain-toast case by
  the pre-existing suite; a dedicated un-settle-cascade toast test would be a reasonable low-cost addition
  but wasn't in Step M1.6's named test list). Milestone 2 (`clearedAmount`/`settlementStatus`/
  `SettlementMeter`/`partiallySettledMemberCount`) is untouched, per the task brief — that's Step M2.5,
  gated on the API's Milestone 2 shipping.

## Final Outcome

**Milestone 1 (Steps M1.1-M1.6) implemented and tested 2026-08-25.** Types, cache invalidation, the
creditor-row affordance, i18n, cascade-aware toast copy, and Step M1.6's test coverage are all in per the
locked plan; `pnpm lint`/`tsc -b`/`pnpm build`/`pnpm test` are all green (956 tests, no regressions) and the
feature was exercised end-to-end in a running app (screenshots + a live toggle-and-toast check, M1.1-M1.5
entry). Milestone 2 (Steps M2.1-M2.5) remains unstarted, gated on the API's Milestone 2 (`ClearedAmount`
migration + overlay math) shipping — per the Assumptions section, unchanged.

Prior planning-only outcome (superseded by the above once implementation started): **Planning complete —
zero Open Questions remain.** This doc is now the final planning deliverable for this feature cycle: both
milestones' Requirements, Implementation Plan, and Impact Analysis are locked against a fully resolved
contract (the two upstream docs) and a fully resolved design (this doc's own six OQs, with every referenced
primitive already built and confirmed in the tree). The next step is handing this doc to `web-implementer`
once the relevant backend milestone lands.

## Future Improvements

- Carried forward from the upstream docs (not superseded here): extending auto-cascade to the excluded
  mixed-role-creditor case; a read-only "suggested cleared amount" signal for non-eligible members;
  unifying the display of all "settled" axes (whole-expense, per-share, per-member-event net, and this
  sync layer) in one coherent UI concept (already flagged twice upstream — BA doc OQ-K, shipped feature's
  own Future Improvements); a drift indicator for OPEN-event Layer B; extending audit coverage to
  settlement actions if payment-timing disputes ever become contentious; automated debt reminders driven
  off the partial-clearance figures.
- **Optimistic updates for the three settled toggles** — still deferred (per the shipped feature's own
  Future Improvements, OQ6b) — would make the now-larger blast-radius cascades feel instantaneous, but
  needs an app-wide optimistic-update pattern this codebase has deliberately not adopted yet.
- **`share:public.statusOwing` terminology parity** — discovered while tracing OQ6's rename: the public,
  unauthenticated settlement-report page (`src/features/share/components/PublicBalanceTable.tsx`,
  `share:public.statusOwing`) carries the identical pre-rename "Còn nợ" wording for the identical "owing,
  not yet settled" state this doc renames to "Chưa trả" on the authenticated `EventBalanceTable`. Left
  untouched here — the `share` feature isn't in this feature's file list and touching it would be scope
  creep beyond the task's explicit ask — but a future pass should decide whether the public page's copy
  should follow the same rename for full terminology consistency across both surfaces.
- If OQ2 resolves to a disabled-toggle-plus-explanation treatment for gross-mixed creditors, consider
  surfacing the *specific* debtor-share elsewhere in the event that made them ineligible (a genuine
  discoverability nicety, out of scope for this feature's first cut).
