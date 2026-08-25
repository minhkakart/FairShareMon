# Business Analysis — Two-Way Settlement Sync Between Event Balance and Expense/Share Settled

> BA #1 (Product/Domain) deliverable. Defines WHAT and WHY only — no endpoints, schema, or component
> design. BA #2 (`ba-solution-analyst`) appends feasibility/cross-functional analysis below this doc.

## Title

Two-way synchronization between an event member's debt-balance settlement and the settled status of
their individual expenses/shares inside that event, with the event/expense QR kept in sync with the
remaining unsettled amount.

## Problem Statement

Today the owner (chủ sổ) tracks "who has paid" at **two independent levels** that were deliberately kept
separate when they were built (see `FairShareMonApi/planning/settled-per-member.md`, shipped
2026-07-21):

- **Layer A — per-share/per-expense settled** (`shares.is_settled`, and the whole-expense
  `expenses.is_settled` that cascades to it): a **gross**, per-bill fact — "has member X paid *their
  portion of this one bill*".
- **Layer B — per-member-per-event net clearance** (`event_member_settlements.is_settled`): a **net**,
  per-event fact — "has member X cleared their overall net debt (`advanced − owed`) for this whole
  event" — this is the figure the event QR bills.

These two layers are **stored independently today by explicit design** (Layer B is *not* derived from
Layer A — see Business-Rule Impact below) and the owner must toggle each one separately. The raw idea
brought to BA is: when the owner asserts one of these two facts, the other should update **automatically**
instead of requiring a second manual action, and the generated QR should always reflect whatever amount is
**still actually outstanding** after any such sync — specifically:

1. Marking a member's **event-level balance Settled** should automatically mark **all of that member's
   expenses/shares in that event** as Settled too (event → expense direction).
2. Marking an **individual expense Settled** should automatically, and possibly only **partially**, clear
   that member's event-level balance (expense → event direction).
3. The event QR amount should always reflect whatever balance remains **unsettled** after either sync.

**Who benefits:** the **chủ sổ** (book owner) — the only real actor who performs any of these markings —
by not having to double-toggle two related settled flags for what is, in the real world, one payment
event. Indirectly, **thành viên** (members, who have no account) benefit from a QR that never asks them
to pay an amount they have already partly covered via an expense-level settlement.

**Why it matters now:** the QR-generation flow (§3.10) already bills based on the Layer B "outstanding"
overlay; if that overlay can go stale relative to what has actually been marked paid at the expense level,
the owner risks sending a QR for an amount that is wrong (too high, because a partial payment recorded at
the expense level was never reflected at the event level) — this is explicitly called out as a known,
accepted limitation ("Drift-aware Layer B") in the Future Improvements of the shipped settled-per-member
feature. This idea is, in effect, a request to close that drift gap — but the concrete mechanism proposed
(bidirectional, automatic cascade) is **more aggressive** than what was previously deferred, and it directly
contests decisions that were locked at the time. See Business-Rule Impact.

**Important scoping note:** part of what the raw idea describes ("QR reflects unsettled balance") is
**already shipped** — the event QR already bills only members with `outstanding > 0` (Layer B), and both
the expense and event QRs already exclude settled shares/cleared members. What is genuinely **new** here
is the **automatic bidirectional cascade** between Layer A and Layer B, and — for the expense→event
direction specifically — a **partial** clearance concept that does not exist in the shipped model at all
(Layer B today is boolean: settled or not, never "some of it").

## Personas / Stakeholders

- **Chủ sổ (book owner)** — primary actor. Performs every settled toggle (expense-level, per-share, and
  event-per-member) today, and would be the one relieved of double-toggling by this feature.
- **Thành viên (member)** — no account, never performs an action; is the *subject* of the balance and the
  *recipient* of the QR. Benefits from an accurate remaining-amount QR; is harmed by an inaccurate one
  (over/under-billed).
- **Admin** — no direct stake in this feature; standard privacy/ownership invariants still apply to any
  admin-facing views of another user's data (out of scope here — admin tooling is a separate area).

## Goals & Success Criteria

**Goals:**
1. The owner should not have to manually keep the expense-level settled flags and the event-level net
   clearance flag consistent with each other for the same real-world act of payment.
2. Whichever settled/clearance state the owner asserts, the derived state at the *other* level should be
   accurate and never overstate what a member still owes.
3. Any QR generated after such a sync must request exactly the remaining unpaid amount — never the
   original full amount once part of it has been recorded as paid via either layer.
4. None of the above may perturb the underlying, invariant debt-balance figures (`advanced`/`owed`/
   `balance`) that must sum to zero per event (§3.7, locked — see Business-Rule Impact).

**Success criteria (illustrative; final numeric acceptance thresholds are for BA #2/QA to formalize):**
- After marking a member's event balance Settled, 100% of that member's *owing* shares in that event's
  expenses read as settled (scope of "owing" is an Open Question below).
- After marking an expense (or one of its shares) Settled, the affected member's(s') event-level
  outstanding figure decreases by exactly the settled amount, never below zero, never exceeding what they
  actually owe.
- Regenerating an event or expense QR after any such sync bills exactly the remaining unsettled amount,
  and omits a member entirely once fully cleared (consistent with today's `NoOutstandingDebtForQr`
  behavior when everyone is cleared).
- The raw `advanced`/`owed`/`balance` numbers are byte-for-byte unchanged by any settlement-sync action
  (regression guard, mirrors the existing D2 invariant test).

## Terminology

**Existing terms reused (from `The-ideal.md` §2/§3.5/§3.7/§3.10 and the shipped `settled-per-member.md`):**
- **expense (phiếu)**, **share (phần gánh)**, **event (đợt)**, **payer (người trả)**, **balance (cân bằng
  nợ)** = `advanced − owed` per member per event, **closed/open event (đợt đã chốt/đang mở)**.
- **settled (đã trả)** — existing whole-expense flag (§3.5), payment metadata, does not change amounts,
  the one write allowed on a closed event.
- **Layer A (per-share settled)** and **Layer B (per-member-per-event net clearance)** — the two axes
  shipped by `settled-per-member.md`; this doc reuses these exact names/concepts rather than reinventing
  them, since the requested feature operates *between* them.
- **outstanding (còn nợ ròng)** — the existing derived-per-member figure surfaced alongside the event
  balance, driven today purely by Layer B (see Business-Rule Impact — this is precisely the figure this
  idea wants to also be driven by, or drive, Layer A).

**New candidate terms this idea would introduce — none of these are decided; naming is fixed by
convention (`The-ideal.md` §5) and is not BA #1's call. Flagged as Open Questions:**
- A **partial clearance / partial settlement** state or amount at the event-member level (today Layer B
  is a plain boolean — settled or not; there is no "some of it" concept in the shipped model). Candidate
  English names: `ClearedAmount`, `SettledAmount`, `AmountPaid`. Candidate Vietnamese: "số tiền đã tất
  toán", "đã trả một phần". See Open Question OQ-E.
- A name for the **cascade/sync mechanism itself** for use in documentation and UI copy (e.g. "đồng bộ
  trạng thái đã trả" / "settlement sync"). See Open Question OQ-E.
- Possibly a **tri-state** status for Layer B (Unsettled / PartiallySettled / Settled) replacing or
  augmenting today's boolean. See Open Question OQ-E.

## User Stories & Acceptance Criteria

> Each story is written against the **existing** Layer A/Layer B surface, since that is what this idea
> extends. Every AC that depends on an unresolved design choice is marked **[see OQ-x]** — these are not
> filled in with an invented default per the Human Confirmation Policy.

### Story A — Event-level settle cascades down to expense-level (Direction 1)

> Là chủ sổ, tôi muốn khi tôi đánh dấu một thành viên đã tất toán cân bằng nợ của họ trong một đợt, thì
> các phiếu/phần gánh mà thành viên đó còn nợ trong đợt cũng tự động được đánh dấu đã trả, để tôi không
> phải lặp lại thao tác thủ công cho từng phiếu.

> **Updated 2026-08-25 (round 2) — FULLY RESOLVED, no remaining open items.** Per user decisions on OQ-A,
> OQ-B, the OQ-L creditor-gate amendment, OQ-C (reversal), and OQ-H (closed-event classification) — see
> Decision Log entries 1, 2, 5, 7, 9. Auto-cascade is real, automatic, and unconditional-on-trigger; it is
> **gated to single-sided members**, with the eligibility gate now **asymmetric by role**: a net *debtor*
> needs only the plain single-sidedness test, while a net *creditor* additionally needs **gross purity**
> (no debtor-share anywhere else in the event). Once it fires, it covers **all** of the member's shares (not
> only owing ones), it fires identically on OPEN and CLOSED events, and it reverses symmetrically
> (capped/idempotent) on un-settle.

- **Given** event E (open or closed) and member M who participates in E, **and** M is **eligible**:
  - M is a **net debtor** per the existing `advanced − owed` balance (single-sidedness test only)
    **[Decision: OQ-A, option (a)]**, **or**
  - M is a **net creditor** per `advanced − owed` **and** holds **no debtor-share anywhere else in the
    event** (true gross purity for creditors) **[Decision: OQ-L amendment to OQ-A/OQ-B, accepted]**,
  - — never a member whose gross/net facts could diverge ambiguously outside those two eligible shapes,
- **When** the owner sets M's event-level settled flag to true (today's
  `PUT .../events/{E}/members/{M}/settled`),
- **Then** **all** of M's shares across every expense belonging to E — not only M's owing shares — have
  their Layer A `is_settled` set true (and `settled_at` stamped) **[Decision: OQ-B — literal "all their
  shares in the event", not owing-only]**, and each affected expense's whole-expense flag is reconciled per
  the existing "all billable shares settled" predicate;
- **And** M's raw `advanced`/`owed`/`balance` are unchanged;
- **And** this succeeds identically whether E is OPEN or CLOSED — the cascade is treated as within the
  existing §4.4 "settled is the sole write exception on a closed event" **[Decision: OQ-H, resolved —
  within the existing exception]**;
- **And** a soft-deleted M can still be targeted, exactly as today (history-preserving, §4.7);
- **And** shares belonging to expenses **outside** event E (loose expenses, or M's shares in a *different*
  event) are **not** touched;
- **And** shares where M is the **payer** (already settled-by-definition today) are unaffected/no-op — this
  also means marking "all shares" per OQ-B is a no-op for M's own payer-shares, since those were already
  settled-by-definition (this is also why debtors need no extra gross-purity check: their payer-shares, if
  any, can never be over-counted);
- **And**, **if M is not eligible per the gate above** (a non-single-sided member of either role, or a net
  creditor who holds a debtor-share elsewhere in E), **no auto-cascade occurs at all** — the owner falls
  back to today's existing manual per-share and per-member toggling **[Decision: OQ-A fallback clause,
  amended by OQ-L]**;
- **And**, symmetrically, when the owner later sets M's event-level settled flag back to **false**, the
  shares that were cascaded from this action are un-settled in the same transaction — but the reversal is
  floored so it never drives M below what M's CURRENT balance implies is still owed, protecting
  money-exactness if the balance changed between settle and unsettle **[Decision: OQ-C, option (c),
  symmetric capped/idempotent reversal]**;
- **And** none of the above writes are audited — the cascade inherits the existing no-audit exclusion for
  settled toggles unchanged **[Decision: OQ-G, resolved — no audit]**.

**Resolved 2026-08-25 (Decision Log — entries 1, 2, 5, 7, 9; nothing left open for this story):**
- Cascade scope is **all** of M's shares in the event, not owing-only (OQ-B).
- The "unconditional vs. requires negative balance" half of OQ-B is superseded by the single-sidedness
  gate above: the cascade is not conditioned on the balance's sign as such, but on M being single-sided
  — with the creditor branch additionally requiring gross purity per the OQ-L amendment.
- The residual boundary case flagged under OQ-A (a member single-sided by net balance who still holds a
  payer-share on one expense and a debtor-share on another) is resolved: excluded from auto-cascade when M
  is a net creditor with a debtor-share elsewhere; not a concern when M is a net debtor (OQ-L, Decision Log
  entry 5).
- Reversal is symmetric and capped/idempotent (OQ-C, Decision Log entry 9).
- The cascade never touches *other* members' shares in the same expenses (OQ-J — confirmed by BA #2's
  design-verification trace, no fresh decision needed; `EventMemberSettlement`'s composite PK and
  `Share.MemberId` scoping make cross-member writes structurally impossible).
- The cascade is within the existing closed-event exception (OQ-H, Decision Log entry 7).
- The cascade is not audited (OQ-G, Decision Log entry 10).

### Story B — Expense-level settle cascades up, partially, to event-level (Direction 2)

> Là chủ sổ, tôi muốn khi tôi đánh dấu một phiếu (hoặc một phần gánh) là đã trả, thì phần cân bằng nợ
> tương ứng của (các) thành viên liên quan trong đợt được ghi nhận là đã tất toán một phần, để tổng số
> tiền còn phải thu hiển thị đúng ngay cả khi thành viên trả dần qua nhiều phiếu.

> **Updated 2026-08-25 (round 2) — FULLY RESOLVED, no remaining open items.** Per user decisions on OQ-A,
> OQ-D (both the multi-member and trigger-action halves), OQ-L (creditor-gate amendment — self-protecting
> for Direction 2, see below), OQ-C (reversal), and OQ-H (closed-event classification) — see Decision Log
> entries 1, 3, 5, 6, 7, 9.

- **Given** expense X belongs to event E and has owing shares for one or more members M1..Mn,
- **When** the owner marks **either** the whole expense X settled (which today cascades to all its billable
  shares per the existing reconciliation rule) **or** an individual per-share toggle on one of X's shares
  **[Decision: OQ-D residual, option (c) — both triggers fire Direction 2, via one shared code path]**,
- **Then**, for **every** debtor member Mi who has a (now-settled) share on X **[Decision: OQ-D — "all their
  debtor members on that expense", not just one]**, **provided Mi is single-sided in E [Decision: OQ-A's
  scope applies here too — the OQ-L creditor-gate amendment does not add a further condition for Direction 2:
  a net creditor's `Outstanding` is already floored at zero regardless of any debtor-share they hold, so
  there is nothing to over-credit; Direction 2 is self-protecting for the OQ-L case]**, the event-level
  outstanding figure for Mi is reduced by exactly the amount of Mi's now-settled share in X — never below
  zero, never exceeding what Mi actually owes in E;
- **And**, for any Mi on X who is **not** single-sided in E, no auto partial-credit occurs for Mi — Layer B
  for Mi must still be toggled manually as today (the fallback from Decision OQ-A applies per-member, not
  per-expense: other single-sided Mi's on the same expense still get their auto partial-credit);
- **And** if Mi's cumulative cleared amount in E reaches their full net owed amount, Mi's event-level state
  transitions automatically to fully Settled;
- **And** if less than full, Mi is in a **partially-settled** state — represented as a service-derived status
  over a new stored `ClearedAmount` column, not a separate stored tri-state enum [OQ-E representation
  accepted; final naming deferred to feature-planner/ui-designer];
- **And** the raw `advanced`/`owed`/`balance` are unchanged (only the overlay/clearance state moves);
- **And** un-settling a previously-settled share reverses the corresponding partial credit (claws back the
  `ClearedAmount` contribution), and can move Mi back from Settled to PartiallySettled/Unsettled — but this
  reversal is floored so it never drives Mi's cleared amount below what Mi's CURRENT balance implies is
  still owed **[Decision: OQ-C, option (c), symmetric capped/idempotent reversal]**;
- **And** this all succeeds identically whether E is OPEN or CLOSED — within the same existing §4.4
  exception as Direction 1 **[Decision: OQ-H, resolved]**;
- **And** none of the above writes are audited **[Decision: OQ-G, resolved — no audit]**.

**Resolved 2026-08-25 (Decision Log — entries 1, 3, 5, 6, 7, 9; nothing left open for this story):**
- Multi-member expense: partial credit applies to **every** debtor share-member on the expense
  simultaneously, not only one (OQ-D).
- Trigger scope: both the whole-expense toggle and any individual per-share toggle fire Direction 2, via one
  shared "credit this member" code path (OQ-D residual, Decision Log entry 6).
- The gross-vs-net conflict that `OQ1`/`OQ8` (locked, `settled-per-member.md`) originally raised is
  **narrowly reopened**, not fully reopened: auto partial-credit only fires for a debtor member who is
  single-sided in E (OQ-A). A member who is genuinely mixed (both advances and owes such that gross ≠ net
  in an ambiguous way) gets no auto-cascade at all — manual Layer B toggling remains the fallback, so the
  conflict `OQ1`/`OQ8` warned about cannot occur through this feature for that member.
- The OQ-L residual (gross/net divergence within a "single-sided" member) does not require any change to
  Direction 2's eligibility logic: it is self-protecting via the existing `Outstanding` floor-at-zero for
  creditors (see Decision Log entry 5 and the OQ-L characterization above). The OQ-L amendment only
  tightens Direction 1's gate.
- Reversal is symmetric and capped/idempotent (OQ-C, Decision Log entry 9).
- The cascade is within the existing closed-event exception (OQ-H, Decision Log entry 7).
- The cascade is not audited (OQ-G, Decision Log entry 10).

### Story C — QR reflects the true remaining unsettled amount

> Là chủ sổ, tôi muốn khi tôi tạo lại mã QR của đợt/phiếu sau khi một số phần đã được đánh dấu đã trả từng
> phần, mã QR chỉ yêu cầu đúng số tiền còn thiếu của mỗi thành viên.

- **Given** member M owes 500.000đ net in event E, and 200.000đ of that has already been cleared via
  Story B's partial mechanism,
- **When** the owner (re)generates the event QR,
- **Then** M is billed exactly 300.000đ, not 500.000đ and not 0đ;
- **And** if M's cleared amount reaches or exceeds what they owe, M is dropped from the QR entirely —
  consistent with today's `NoOutstandingDebtForQr` behavior when the billed set becomes empty;
- **And** all amounts involved remain `decimal`, non-negative (§4.3) at every step of the computation.

> Note: for the **boolean** Layer B case (fully settled / not), this story is **already fully shipped**
> today — the event QR already bills only `outstanding > 0` members, and the expense QR already excludes
> settled/zero/payer shares. Story C is only "new work" to the extent that Story B introduces a **partial**
> amount that today's boolean overlay cannot represent.

> **Updated 2026-08-25 (round 2) — FULLY RESOLVED.** Story B's auto partial-credit is gated to single-sided
> members only (Decision OQ-A; net creditors additionally gated by gross purity per the OQ-L amendment,
> Decision Log entry 5 — though as noted under Story B this gate does not change Story C's math, since a
> creditor's `Outstanding` is already 0 regardless). A non-eligible M never accumulates an automatic partial
> clearance — the owner still clears Layer B for M manually as today, and the QR continues to bill exactly
> whatever Layer B currently reflects for M, unchanged from the shipped behavior. Reversal (OQ-C, Decision
> Log entry 9) flows through the same `ClearedAmount`/`Outstanding` computation, so a QR regenerated after an
> un-settle also reflects the correct (capped) remaining amount with no separate logic needed.

### Story D — Closed-event scope of the sync

> Là chủ sổ, tôi muốn cơ chế đồng bộ này vẫn hoạt động trên đợt đã chốt, giống hệt việc đánh dấu đã trả
> hiện tại, vì lúc đối chiếu ai đã chuyển khoản luôn diễn ra SAU khi chốt đợt.

- **Given** event E is CLOSED,
- **When** the owner performs any settled toggle that triggers either sync direction (including a
  reversal/un-settle toggle per OQ-C),
- **Then** the cascade succeeds exactly as it would on an open event — the cascade **is** classified as the
  same "settled = payment metadata" exception that §3.5/§4.4 already carve out **[Decision: OQ-H, resolved
  — within the existing exception, consistent with the precedent `settled-per-member.md OQ5a` already set,
  and requiring no new technical guard-bypass since it reuses the exact `EventWriteGuard` bypass the
  existing settled toggles already use]**.

> **Updated 2026-08-25 (round 2) — FULLY RESOLVED, no remaining open items.** OQ-F (fully automatic, no
> confirmation step) and OQ-H (closed-event classification) are both resolved — see Decision Log entries 4
> and 7. A same-click, no-confirmation, multi-row write on a CLOSED event is accepted as within the existing
> §4.4 "sole exception," with the atomicity requirement (Business-Rule Impact item 4) as the safety net. The
> blast radius is bounded by "every expense the member has a share in" (typically ~2-10 per BA #2's
> feasibility note), not bounded by the API to a fixed count, but this was surfaced to and accepted by the
> user alongside the OQ-H decision.

## Business-Rule Impact

Cross-checked against `The-ideal.md` §4 and the **locked decisions already recorded** in
`FairShareMonApi/planning/settled-per-member.md` (its Decision Log, "NOT to be reopened" per that doc's
own framing). This idea touches more locked ground than a typical net-new feature — flagged prominently.

1. **Absolute privacy (§4.1).** Unaffected in principle — every new write stays resource-owned, misses
   are 404. No new cross-user surface is implied by this idea.

2. **Money exactness (§4.3).** The raw idea's "partial settle" concept requires tracking a *cleared
   amount* per member per event, which must be `decimal`, non-negative, and never allowed to exceed what
   that member actually owes (over-clearing must be rejected or capped — a design choice, see OQ-E). No
   floating point; the existing DB CHECK/decimal conventions extend unchanged. **Resolved 2026-08-25 (round
   2, see Decision Log entry 9):** the same capping principle also governs **reversal** — un-settling
   (Direction 1 or Direction 2) is floored so it never drives a member's cleared amount below what their
   CURRENT balance implies is still owed, protecting this invariant against the open-event drift edge case
   (OQ-C, option (c)).

3. **Closed-event immutability (§4.4) — DIRECT TENSION.** §3.5/§4.4 state settled is "the **sole**
   exception" allowed on a closed event, framed as a *single flag flip* with no side effects on the frozen
   ledger. This idea's cascades are **not** single flag flips — one action can flip many shares/expenses
   (Direction 1) or update several members' clearance state at once (Direction 2, multi-member expense).
   Whether a **multi-row, cross-entity automatic cascade** still qualifies as "payment metadata, not
   expenditure data" — and therefore is still allowed to fire on a closed event — needs **explicit
   confirmation**, not an assumption that the existing exception simply stretches to cover it. The prior
   settled-per-member feature already extended the exception once (from whole-expense to per-share and
   per-member-event, OQ5a); this idea would extend it a second time, to an automatic *derivation between*
   the two axes. Recommend treating it as the same class of exception (consistent with precedent) but this
   is a decision, not a given — see OQ-H. **Resolved 2026-08-25 (round 2, see Decision Log entry 7):** the
   user confirmed the cascade **is** treated as within the existing exception, consistent with the
   `settled-per-member.md OQ5a` precedent and BA #2's finding that no new technical guard-bypass is needed.

4. **Atomicity (§4.5).** A cascade that touches N shares (Direction 1) or N members' clearance rows
   (Direction 2, multi-member expense) must be all-or-nothing — a partial cascade failure must not leave
   Layer A and Layer B inconsistent with each other, which is the exact problem this feature exists to
   prevent. This is a hard downstream implementation requirement.

5. **Soft-delete / history preservation (§4.7).** The existing Layer A/B feature already supports
   soft-deleted participants (OQ9a/OQ14a — a deleted member remains markable/visible in the overlay). This
   idea's cascades must preserve that: a cascade targeting or triggered by a soft-deleted member's
   share/participation must not fail or silently skip.

6. **Tier limits block creation only (§4.9, §3.11).** Settled toggles are Free-tier (§3.11 basic list);
   QR generation is Premium-gated (`13003`) independently. This idea creates no new *resource*, only a
   derived-state side effect, so tier limits (which govern creation counts) are not directly implicated —
   but whether the **sync behavior itself** should be Free or bundled into the Premium "mở rộng" group
   (since it directly feeds the Premium QR's billed amount) is an open call — see OQ-I. **Resolved 2026-08-25
   (round 2, see Decision Log entry 8):** the user confirmed the sync behavior is **Free-tier**, consistent
   with settled being Free today — no new "same response, tier-dependent side effect" gating shape is
   introduced.

7. **Audit log immutability (§3.8, and the locked OQ10 "no audit for settled toggles").** Today, no
   settled toggle (whole-expense, per-share, or per-member-event) is audited — a deliberate exclusion,
   because settled is payment metadata, not "số liệu chi tiêu" (expenditure data) that disputes are fought
   over. This idea's automatic cascades are **bigger-blast-radius, indirect** writes (one click changes
   many rows) than the single-row toggles the no-audit decision was made for. Whether that changes the
   audit calculus needs explicit confirmation rather than silent inheritance of the old exclusion — see
   OQ-G. (Note: if audit stays excluded, the "audit log is immutable" invariant is trivially preserved
   since no audit entries exist to begin with; the open question is whether it *should* start existing.)
   **Resolved 2026-08-25 (round 2, see Decision Log entry 10):** the user confirmed **no audit** — the
   cascade inherits the existing `OQ10` exclusion unchanged. The audit invariant remains trivially preserved
   since no audit entries exist for this feature; the schema work BA #2 characterized (new
   `AuditEntityType` variant, `ExpenseUuid` nullability) is not needed.

8. **Balance purity (§3.7, `M7 OQ2`/`D2` — LOCKED, explicitly "not reopened").** The debt balance
   (`advanced − owed`, sum-to-zero per event) must remain **pure** and untouched by any settled state. This
   idea does not ask to change that formula, and this doc treats that as a hard constraint carried
   forward unchanged: "partially settle the balance" in the raw idea's wording must be understood as
   **partially clearing the derived outstanding overlay**, never as mutating the raw `balance` figure
   itself. This must be stated explicitly downstream so nobody reads "partially settle the balance" as
   license to touch the `advanced`/`owed`/`balance` numbers.

9. **DIRECT CONFLICT with two explicitly-locked cross-functional decisions — the central issue of this
   whole idea:**
   - **`OQ1` (locked, option a):** Layer B is **STORED**, deliberately **NOT derived** from Layer A,
     *because* deriving "member cleared ⇔ all their shares settled" over-counts whenever a member both
     advances and owes in the same event (gross ≠ net) — the exact scenario in Story A/B above.
   - **`OQ8` (locked, option a):** the overlay's `outstanding` is driven by **Layer B (net) only**; Layer A
     (gross per-share settled) was explicitly decided to **not** reduce the event overlay's outstanding,
     for the same gross/net reason.
   - This idea's Direction 1 (event settle → cascade down to shares) and, especially, Direction 2
     (expense settle → partially clear the event balance) are **precisely** the derivations that `OQ1`/
     `OQ8` examined and rejected. Implementing this idea as literally described therefore **reopens
     architecture decisions that were locked at a user checkpoint**, which is squarely the kind of
     high-impact, preference-dependent call the Human Confirmation Policy reserves for explicit
     confirmation — not something BA #1 can resolve by picking an option. This is captured as the
     **blocking** Open Question OQ-A below.
   - **Resolved 2026-08-25 (see Decision Log):** the user chose **option (a), narrowly scoped**. `OQ1`/
     `OQ8` are reopened, but **only** for a member who is single-sided in the event (purely a net debtor or
     purely a net creditor per `advanced − owed`) — never for a member whose gross Layer A facts and net
     Layer B facts could conflict ambiguously. For every member who does not cleanly fit that single-sided
     shape, `OQ1`/`OQ8` stay locked exactly as shipped: Layer B remains stored/asserted manually, Layer A
     gross never auto-derives it. A residual boundary case — a member who is single-sided by net balance
     but still has a payer-share on one expense and a debtor-share on another within the same event — was
     explicitly flagged by the user as needing precise characterization; captured as new **Open Question
     OQ-L** for BA #2.
   - **Resolved 2026-08-25 (round 2, see Decision Log entry 5):** the OQ-L residual is now fully closed. The
     user accepted BA #2's amendment: a net **creditor** is eligible for Direction-1 auto-cascade only if
     they hold **no debtor-share anywhere else in the event** (true gross purity); a net **debtor** needs no
     such extra check, since a debtor's own payer-shares are already settled-by-definition no-ops and can
     never be over-counted. This closes the last gap in `OQ1`/`OQ8`'s narrow reopening — every member's
     eligibility for auto-cascade is now fully and unambiguously specified.

## Scope

> **Updated 2026-08-25 (round 2) — ALL Open Questions are now resolved; this Scope is FINAL.** All ten
> Decision Log entries are reflected below. Nothing in this section is contingent on a future user decision
> any longer; the two items explicitly marked "deferred to downstream agent" (OQ-E naming/copy, OQ-K UI
> specifics) are BA #2/downstream territory, not user-blocking.

**In scope (decided 2026-08-25 — see Decision Log):**
- **Auto-cascade, both directions, fully automatic (no confirmation step)** [Decision: OQ-F], but
  **narrowly scoped**: it only fires for an **eligible** member —
  - a **net debtor** who is single-sided in the event (purely owes, never advances, per `advanced − owed`)
    [Decision: OQ-A], or
  - a **net creditor** who is single-sided **and** holds no debtor-share anywhere else in the event (true
    gross purity for creditors) [Decision: OQ-A + the OQ-L amendment, Decision Log entry 5].
  For any member who does not cleanly fit one of those two eligible shapes, no auto-cascade fires; the
  existing manual two-layer toggling remains the fallback.
- **Direction 1** (event settle → expense level): when fired, cascades to **all** of the eligible member's
  shares in the event, not just their owing shares [Decision: OQ-B].
- **Direction 2** (expense settle → event level): when fired, applies partial credit to **every** debtor
  member on the settled expense who is single-sided in the event, simultaneously — not just one [Decision:
  OQ-D multi-member sub-question]. Triggered by **either** the whole-expense `settled` toggle **or** any
  individual per-share toggle, via one shared "credit this member" code path [Decision: OQ-D residual,
  option (c), Decision Log entry 6].
- **Symmetric, capped/idempotent reversal** on both directions: un-settling at the event level un-settles
  the shares Direction 1 cascaded; un-settling a contributing share claws back its Direction 2 partial
  credit — both floored so they never drive a member below what their CURRENT balance implies is still owed
  [Decision: OQ-C, option (c), Decision Log entry 9].
- **Fires identically on OPEN and CLOSED events**, treated as within the existing §3.5/§4.4 "settled is the
  sole write exception" [Decision: OQ-H, Decision Log entry 7].
- **Free-tier**, consistent with settled being Free today; no new tier-dependent-side-effect gating shape is
  introduced [Decision: OQ-I, Decision Log entry 8].
- **Not audited** — inherits the existing `OQ10` no-audit exclusion for settled toggles unchanged [Decision:
  OQ-G, Decision Log entry 10].
- The event/expense QR reflecting whatever the resulting remaining-unsettled amount is after any sync
  (including after a reversal).
- The new `EventMemberSettlement.ClearedAmount` column (`decimal(18,2) NOT NULL DEFAULT 0`, CHECK `>= 0`)
  plus a service-derived partial status, replacing no existing stored field — accepted as the OQ-E
  representation (final naming/copy deferred to feature-planner/ui-designer's normal process).
- Preserving every invariant listed in Business-Rule Impact (privacy, money exactness including capped
  reversal, atomicity, soft-delete history, tier-limits-block-creation-only, balance purity) without silent
  violation.
- `OQ1`/`OQ8` from `settled-per-member.md` are **narrowly reopened** per the OQ-A decision above (not fully
  reopened, and not left untouched), with the eligibility boundary now fully specified by the OQ-L amendment
  — this is the final, complete resolution of what was previously an open call in this doc's Business-Rule
  Impact §9.
- **Milestone sequencing (recommended by BA #2, adopted):** Milestone 1 ships **Direction 1 alone**;
  Milestone 2 ships **Direction 2 + Story C (QR)**. See the Risks & Sequencing section and the Handoff
  Summary at the end of this doc.

**Out of scope / explicitly deferred (Future Improvements):**
- Auto-cascade for a **non-eligible** member — a non-single-sided member of either role, or a net creditor
  who holds a debtor-share elsewhere in the event — explicitly excluded by the OQ-A decision and its OQ-L
  amendment; stays manual-only unless a future iteration revisits it.
- Changing the raw balance formula (`advanced − owed`) or its sum-to-zero invariant — remains untouched
  regardless of how this idea is resolved.
- Retroactively backfilling historical data with any new partial-clearance concept, unless separately
  requested.
- UI/UX design for how the two (or three, counting whole-expense) "settled" concepts are presented
  together to avoid user confusion — that is `ui-designer`'s downstream call, though the risk is flagged
  here since the shipped feature's own Future Improvements already noted it ("Unify the three settled
  notions in the UI"). See OQ-K (deferred to `ui-designer`, not user-blocking).
- Final field naming and Vietnamese/English copy for the new partial-settlement concept — deferred to
  `feature-planner`/`ui-designer`'s normal planning-doc process per `The-ideal.md` §5 (OQ-E, not
  user-blocking; the underlying representation itself is already decided, see above).
- Audit-trail coverage of settlement actions — stays excluded per the OQ-G decision (Decision Log entry 10).
- Automated debt reminders (§6 "Nhắc nợ") — unrelated future item, not touched by this idea.
- Multi-currency or cross-event netting — not requested, not implied by this idea.
- A read-only "suggested cleared amount" signal for non-eligible members (original OQ-A option (c)) — not
  adopted now; kept in Future Improvements below as a possible later iteration once real usage data exists.

## Open Questions

> **Updated 2026-08-25 (round 2) — ZERO Open Questions remain that genuinely need a user decision.** The
> four top-level/blocking questions (OQ-A, OQ-B, OQ-D multi-member, OQ-F) were answered in round 1. The six
> questions that were still genuinely open after round 1 — the **OQ-L creditor-gate amendment**, **OQ-D's
> residual trigger-action question**, **OQ-H**, **OQ-I**, **OQ-C**, and **OQ-G** — have now all been answered
> by the user; see Decision Log entries 5-10 for the binding answers and rationale. Every item below is
> annotated inline in the same `~~OQ-X~~ → Answered` style already used for OQ-A/B/D/F, with the original
> question text preserved for the record.
>
> **Final disposition of every Open Question raised in this doc:**
> - **Fully resolved by explicit user decision (Decision Log 1-10):** OQ-A, OQ-B, OQ-C, OQ-D (both halves),
>   OQ-F, OQ-G, OQ-H, OQ-I, OQ-L.
>   confirmed via BA #2's own investigation, no fresh user decision required, per the doc's original framing:
>   OQ-J (blast-radius scoping — confirmed, no conflict found).
> - **Explicitly deferred to a downstream agent's normal planning process (not user-blocking, was always
>   BA #2/downstream territory):** OQ-E's **final field naming and Vietnamese/English copy** only (the
>   underlying representation itself — `ClearedAmount` + service-derived status — is accepted as a
>   data-modeling recommendation, see the note after Decision Log entry 10), and OQ-K (UI unification
>   specifics — `ui-designer`'s downstream call).

**OQ-A [BLOCKING].**
> ~~OQ-A~~ → **Answered 2026-08-25 (option a, narrowly scoped); amended 2026-08-25 (round 2) per OQ-L:**
> `OQ1`/`OQ8` are reopened, but **only** for a member who is single-sided in the event — purely a net debtor
> or purely a net creditor per the existing `advanced − owed` balance, never a mix that would require
> ambiguously deriving one layer from the other. For a member who does not cleanly fit that shape, no
> auto-cascade fires at all; the existing manual two-layer toggling remains the fallback. The user
> additionally flagged a residual boundary case — a member single-sided by net balance who still has a
> payer-share on one expense and a debtor-share on another within the same event, where gross Layer A and net
> Layer B could still diverge — as needing precise characterization; captured as **Open Question OQ-L**, which
> BA #2 characterized and the user has now resolved (Decision Log entry 5): a **net creditor** is eligible for
> Direction-1 auto-cascade only if they hold **no debtor-share anywhere else in the event** (true gross
> purity); a **net debtor** needs no such extra check (already safe via `OQ6a`). See OQ-L below and Decision
> Log entry 5 for full rationale.

This idea's core mechanism directly reopens two decisions locked at the 2026-07-21
checkpoint in `settled-per-member.md`: `OQ1` (Layer B is stored, not derived from Layer A, because
deriving over-counts when a member both advances and owes) and `OQ8` (outstanding is driven by Layer B
net only; Layer A gross does not reduce it). Options for how to proceed:
- **(a)** Accept the previously-identified gross/net limitation for the *common case* (a member who is
  purely an ower, never a payer, in a given event) and define the cascade precisely for that case, while
  explicitly documenting reduced/undefined behavior for the mixed payer+ower case (i.e., re-open OQ1/OQ8
  but scope the reversal narrowly).
- **(b)** Do not derive/cascade automatically at all; instead offer the owner a single **manual**
  "mark both" quick action (still two stored facts, but one click) — avoids reopening OQ1/OQ8's rejected
  derivation, but does not fully match the raw idea's "then X automatically becomes Y" wording.
- **(c)** Introduce a new, separate **read-only "suggested cleared amount"** signal computed purely from
  Layer A (sum of a member's settled owing shares in the event), displayed alongside — but never
  overriding — the existing independent Layer B boolean; the owner still explicitly confirms Layer B
  themselves. This keeps OQ1/OQ8 intact (nothing is auto-derived into the stored net fact) while still
  giving the owner better visibility.
Each has materially different scope for BA #2 and downstream teams — this must be decided before
solution-level feasibility work proceeds.

**OQ-B.**
> ~~OQ-B~~ → **Answered 2026-08-25 (all shares):** cascade scope for Direction 1 is **all** of the
> member's shares in the event, not owing-only — matching the literal ask. The "unconditional vs. requires
> negative balance" half is superseded by OQ-A's single-sidedness gate: the trigger isn't the balance's
> sign, it's whether the member is single-sided (which covers a pure net debtor or a pure net creditor
> alike).

For Direction 1 (event settle → cascade down), does "all expenses of that member in the event"
mean only the member's *owing* (debtor, non-payer, amount > 0) shares, or literally every expense they
appear in any role? And should the cascade require the member's net balance to actually be negative
(they owe something), or apply unconditionally like today's Layer B toggle (which permits marking any
participant regardless of balance sign)?

**OQ-C.**
> ~~OQ-C~~ → **Answered 2026-08-25 (round 2), option (c) — symmetric, capped/idempotent reversal:**
> un-marking a member's event-level settled flag un-settles the shares that were cascaded from it (Direction
> 1 reversal); un-settling a contributing share claws back its corresponding partial credit at the event
> level (Direction 2 reversal) — but any reversal is floored/capped so it never drives the member below what
> their CURRENT balance implies is still owed, protecting money-exactness against the open-event drift edge
> case. This is BA #2's own recommended option, now accepted (see Decision Log entry 9).

Is the cascade **symmetric** on reversal? I.e.,
does un-marking a member's event-level settled flag also un-settle the shares that were cascaded from it?
Does un-settling one contributing share partially reverse (not fully clear) the event-level
partially-settled state? Not addressed by the 2026-08-25 decisions; BA #2 should narrow the options and
bring back a recommendation.

> **BA #2 feasibility note (2026-08-25):** both directions are technically symmetric to implement — the
> same transaction that sets `IsSettled=true`/cascades forward can, on `false`, run the inverse (unset the
> cascaded shares / subtract the reversed share's amount from `ClearedAmount`, floored at 0). No schema or
> architecture blocker either way. The options actually differ in **data fidelity**, not feasibility:
> - **(a) Fully symmetric.** Un-settling M's event balance un-settles every share Direction 1 had cascaded
>   (and vice versa for Direction 2's per-share contribution). Simple mental model, but loses information:
>   if the owner later toggles a single share back on manually, there's no record of "this was originally
>   cascaded vs. manually marked," so a reversal can undo shares the owner never touched by hand.
> - **(b) One-way (no reversal cascade).** Un-marking the event flag leaves the previously-cascaded shares
>   settled; un-settling a share does not claw back credit already granted at the event level (it would need
>   a floor at the CURRENT owed amount, not simply subtract, to avoid going negative against a
>   since-changed balance). Matches how today's shipped Layer A/Layer B already behave as fully independent
>   flags once set — no new "reversal" semantics to reason about — at the cost of the two layers being able
>   to drift apart again after an unsettle, which is the exact drift this whole feature exists to close.
> - **(c) Symmetric but capped/idempotent.** Like (a), but reversal never goes below what the member's
>   CURRENT (possibly-changed) balance implies is still owed — protects the money-exactness invariant if the
>   balance itself changed between the settle and the unsettle (an OPEN-event edge case already flagged as
>   an accepted limitation in the shipped feature, OQ9a "drift-aware Layer B").
> Recommend **(c)** to the user: it matches the literal "symmetric" framing of OQ-C while explicitly
> protecting against the open-event drift case the shipped feature already documented as a known limitation
> rather than silently inheriting it into a new reversal path.
>
> **User decision (2026-08-25, round 2): option (c) accepted as recommended.** See Decision Log entry 9.

**OQ-D.**
> ~~OQ-D (multi-member sub-question)~~ → **Answered 2026-08-25 (every debtor member):** for a multi-member
> expense marked settled, partial credit applies to **every** debtor share-member on that expense
> simultaneously (subject to each being single-sided per OQ-A) — not just one. **The trigger-action
> sub-question below remains open and unanswered.**

For Direction 2 (expense settle → cascade up, partially), which action is the trigger: the
whole-expense settled toggle, any individual per-share settled toggle, or both? For a multi-member
expense marked settled at the whole-expense level, does the partial credit apply to **every** debtor
share-member in that expense at once, or only to one (and if only one, which)?

**OQ-D (residual).**
> ~~OQ-D (residual)~~ → **Answered 2026-08-25 (round 2), option (c) — both triggers, one shared code path:**
> the whole-expense `settled` toggle AND any individual per-share toggle both fire Direction 2, implemented
> as one shared "credit this member for this now-settled share" helper invoked from both
> `ShareRepository.SetSettledAsync` and `ExpenseRepository.SetSettledAsync`'s existing per-billable-share
> cascade loop — per BA #2's own feasibility finding that this requires no extra engineering effort over
> either alone (see Decision Log entry 6).

Which action triggers Direction 2: the
whole-expense `settled` toggle only, any individual per-share toggle, or both independently? This was not
addressed by the decisions received on 2026-08-25. BA #2 should narrow the options and bring back a
recommendation rather than assume.

> **BA #2 feasibility note (2026-08-25):** confirmed by reading `ExpenseRepository.SetSettledAsync`
> (`Repositories/ExpenseRepository.cs:332-352`) and `ShareRepository.SetSettledAsync`
> (`Repositories/ShareRepository.cs:165-194`) — both already exist as separate write paths today, and both
> already call the shared `SettlementReconciler`, so hanging Direction 2 off either or both is mechanically
> possible with no structural blocker. The real trade-off is double-counting risk, not feasibility:
> - **(a) Whole-expense toggle only.** Simplest: one trigger point, matches Story B's literal UC wording
>   ("đánh dấu một phiếu... là đã trả"). Risk: a per-share toggle (still a separate, still-supported action)
>   would NOT feed Direction 2 at all, which could surprise an owner who marks shares individually rather
>   than the whole bill.
> - **(b) Per-share toggle only.** More granular credit (each share settle immediately credits its member),
>   but the existing whole-expense toggle already **cascades to shares** (OQ3a, shipped) — meaning marking
>   the whole expense settled would ALSO flip every share's `IsSettled=true`, and if only the per-share
>   toggle fires Direction 2, this cascade path would need to independently re-trigger it per affected share
>   inside `ExpenseRepository.SetSettledAsync`'s own cascade loop anyway — so "per-share only" doesn't
>   actually avoid touching `ExpenseRepository`.
> - **(c) Both, sharing one code path.** Given (b)'s finding, the pragmatic implementation is a single
>   internal "credit this member for this now-settled share" step invoked from BOTH
>   `ShareRepository.SetSettledAsync` (one share) and `ExpenseRepository.SetSettledAsync`'s existing
>   per-billable-share cascade loop (`SettlementReconciler.CascadeToShares`) — so whichever action the user
>   picks, the implementation shape is the same shared helper either way; the only real product question is
>   whether a lone per-share toggle (without touching the whole expense) should ALSO fire Direction 2.
> Recommend **(c)** — both trigger it, backed by one shared credit-step helper — since it requires no extra
> engineering effort beyond (a) or (b) individually (the whole-expense path already iterates every billable
> share) and best matches the literal "phiếu (hoặc một phần gánh)" wording in Story B's own user story.
>
> **User decision (2026-08-25, round 2): option (c) accepted as recommended.** See Decision Log entry 6.

**OQ-E — representation accepted 2026-08-25 (round 2); final naming/copy explicitly deferred, not
blocking.** The underlying representation recommended below (`ClearedAmount` column + service-derived
status, no tri-state enum) is accepted as a data-modeling decision (see the note after Decision Log entry
10) — this did not require a fresh preference-call sign-off round. **Final field naming and the exact
Vietnamese/English copy remain `feature-planner`'s/`ui-designer`'s normal job to draft in their own planning
docs**, per this repo's established fixed-terminology process; this is explicitly noted so it is not mistaken
for a still-blocking item. Naming/
representation of the new "partial settlement" concept — none of this is BA #1's call per the domain's
fixed-terminology convention (`The-ideal.md` §5). Does Layer B become a numeric "cleared amount" (with a
derived Unsettled/Partially/Fully status), or a separate tri-state enum, or something else? What are the
Vietnamese/English terms? BA #2 can investigate/recommend the representation; final naming still needs
explicit owner sign-off per convention.

> **BA #2 recommendation (2026-08-25):** grounded in the live `EventMemberSettlement` entity
> (`Database/Entities/EventMemberSettlement.cs`) and how `StatsService.GetEventBalanceAsync` already
> computes `Outstanding` as a SERVICE-derived field rather than a stored one (per the shipped feature's
> Step 6) — recommend the **same pattern**, not a new stored enum:
> - Add one new stored column, `EventMemberSettlement.ClearedAmount` (`decimal(18,2)`, `NOT NULL DEFAULT 0`,
>   DB CHECK `>= 0`, mirrors the existing `shares.amount` convention) — the cumulative amount credited via
>   Direction 2.
> - Keep the existing stored `IsSettled` (bool) as-is, meaning "fully cleared" — recomputed whenever
>   `ClearedAmount` changes (`IsSettled = ClearedAmount >= NetOwed`, capped) so nothing downstream that
>   already reads the boolean (the QR filter, the existing overlay contract) needs to change shape.
> - Do **NOT** add a separate stored tri-state enum. A "PartiallySettled" status is fully derivable
>   (`0 < ClearedAmount < NetOwed`) and should be computed in `StatsService`, exactly like `Outstanding` is
>   today — one more service-computed field alongside it, not a second source of truth to keep in sync.
> - This is additive to `MemberBalanceRow` (`ClearedAmount` + the derived status), consistent with the
>   shipped feature's own additive-DTO idiom (OQ15a) — no breaking change to the existing contract.
> Final field/status naming (English/Vietnamese copy) still needs explicit owner sign-off per `The-ideal.md`
> §5, per BA #1's framing — this is a representation recommendation only, not a naming decision.

**OQ-F.**
> ~~OQ-F~~ → **Answered 2026-08-25 (fully automatic):** no confirmation step; toggling either side of
> either direction cascades immediately, matching the raw idea's literal wording. The narrow single-sided
> scope (OQ-A) and the atomicity requirement (Business-Rule Impact item 4) are relied on as the safety net
> in place of a confirmation step.

Should either cascade direction be fully **automatic** (an invisible side effect of the
existing settled toggles, as the raw idea's wording implies), or should it require an explicit,
separate confirming action (e.g., a distinct "also sync related settlement" step)? An automatic
multi-row side effect from a single click is a materially bigger blast radius than today's isolated
single-row toggle, which is itself a Human-Confirmation-Policy-relevant trade-off (potentially high-impact
action) that should be surfaced rather than assumed.

**OQ-G.**
> ~~OQ-G~~ → **Answered 2026-08-25 (round 2): no audit.** The cascade inherits the existing no-audit
> exclusion unchanged, consistent with the locked `OQ10` decision that settled is payment metadata, not
> expenditure data. No new `AuditEntityType` variant or schema change is needed for this feature (see
> Decision Log entry 10).

Should the automatic cascade be
audited, given it can silently change many rows from one user action — a materially larger footprint than
the single-row toggles the existing "no audit for settled" decision (`OQ10`, locked) was made for? Or does
it inherit the existing exclusion unchanged? Sharpened, not resolved, by OQ-F's "fully automatic" answer.
BA #2 should assess feasibility of either answer and bring back a recommendation.

> **BA #2 feasibility note (2026-08-25):** confirmed via `Database/Entities/AuditLog.cs` — auditing this
> feature is **not** a simple flip of the existing no-audit exclusion; it requires its own schema work.
> `AuditEntityType` (line 6-10) has exactly two variants today, `Expense` and `Share`; there is no variant
> for a per-member-per-event settlement. More importantly, `AuditLog.ExpenseUuid` is a `required`
> (non-nullable) column used to group history per expense (line 47-48) — a Direction-1 cascade is
> event-scoped and can span **N unrelated expenses** in one action, so it has no single `ExpenseUuid` to
> assign. Auditing this properly would need: a new `AuditEntityType.EventMemberSettlement` (or similar)
> variant, either a nullable `ExpenseUuid` or a new `EventUuid` column, and an explicit cardinality decision
> (one audit row summarizing the whole cascade, vs. one row per affected share/member — the latter matches
> the existing one-row-per-entity-change idiom but could produce a burst of dozens of rows from one click).
> This is a small but real migration + design item, not a policy-only toggle — bringing this back as
> feasibility input for the user's OQ-G decision rather than resolving it: **if audit is wanted, budget it
> as its own schema step**, not a follow-on config change.

**OQ-H.**
> ~~OQ-H~~ → **Answered 2026-08-25 (round 2): within the existing closed-event exception.** The cascade is
> allowed to fire on CLOSED events, consistent with the precedent already set by the shipped
> settled-per-member feature extending this exception once before, and consistent with BA #2's finding that
> no new technical guard-bypass is needed (it reuses the exact bypass the existing settled toggles already
> use). See Decision Log entry 7.

Does this multi-row, cross-entity cascade still
qualify as the §3.5/§4.4 "settled is the sole write exception on a closed event", or does its broader scope
— now confirmed fully automatic with no confirmation step (OQ-F) — cross into territory the closed-event
immutability rule is meant to protect? (Recommend treating it as within the exception, consistent with the
precedent already set by `settled-per-member.md OQ5a`, but this must be confirmed rather than assumed.)

> **BA #2 feasibility note (2026-08-25):** confirmed by reading `EventWriteGuard.cs` (the guard checks
> `Expense.Event is { IsClosed: true }` and is invoked by every M5 write path EXCEPT the settled toggles)
> and by tracing the actual write shape Direction 1 requires (see Feasibility & Affected Surface below): the
> cascade is mechanically a bypass of the SAME guard the existing settled toggles already bypass — no new
> guard-bypass code path is needed, it falls out naturally from reusing the existing `SetSettledAsync`-family
> transactions. So from a **pure code-mechanics** standpoint, "does it still qualify" is a non-question — it
> literally reuses the exact same bypass. The substance of OQ-H is therefore purely the **policy** question
> BA #1 already framed (a single click now moves potentially dozens of rows across multiple expenses on a
> ledger that's supposed to be frozen) — BA #2 has nothing further to add technically; recommend the user
> decide on the policy framing already presented, with the concrete number attached: on a closed event, a
> single Direction-1 cascade's blast radius is bounded by "every expense the member has a share in," which
> for a typical event (the domain's own examples run ~2-10 expenses) is small in practice, but is NOT
> bounded by the API to any fixed count today.

**OQ-I.**
> ~~OQ-I~~ → **Answered 2026-08-25 (round 2): Free-tier.** This sync behavior is Free-tier, consistent with
> settled being Free today. No new "same response, tier-dependent side effect" gating shape is introduced by
> this feature. See Decision Log entry 8.

Is this sync behavior
itself Free-tier (consistent with settled being Free, §3.11) or should it be considered part of the
Premium "mở rộng" (wallet/QR) group, since its main payoff is a more accurate Premium-gated QR?

> **BA #2 feasibility note (2026-08-25):** technically gateable either way — `ITierService.EnsurePremiumFeature`
> (see `WalletQrService`/`BankAccountsService` for the existing call-site pattern) is a simple guard that
> could wrap the new cascade logic. But there's a real shape wrinkle worth surfacing alongside the business
> call: Direction 1/2 do **not** get new routes — per OQ-F's decision, the cascade is an automatic side
> effect of the SAME existing Free-tier endpoints (`PUT .../settled` on expenses/shares/event-members) that
> already work today. Every existing Premium gate in this codebase blocks the **entire** action with a 403
> (`PremiumFeatureRequired` 13003) — e.g. a Free user calling the QR endpoint gets nothing. Gating "the sync"
> to Premium would instead mean the SAME already-Free PUT call **still succeeds** (the flag still flips) but
> silently does or doesn't cascade depending on the caller's tier — a materially different, less precedented
> gating shape than anything else in the codebase (nothing here does "same 200 response, different side
> effects, by tier" today). This doesn't block feasibility, but the user should decide with this shape
> difference in view, not just the abstract Free-vs-Premium question.

**OQ-J — resolved by BA #2's own investigation; no user decision was ever needed.** Confirm the cascade is strictly scoped
to the triggering member's own shares/clearance and never touches another member's data as a side effect
(e.g., marking payer X settled must not alter what other members owe X, since that is tracked via *their*
shares, not X's). Already directionally decided that the cascade must be scoped to the triggering member's
own data; BA #2 to confirm this holds through solution design rather than assume it, and only raise it back
to the user if design work surfaces a real conflict.

> **BA #2 confirmation (2026-08-25) — holds, no conflict found.** Traced against the live entity model:
> `EventMemberSettlement` is keyed by composite PK `(event_id, member_id)` (`Database/Entities/
> EventMemberSettlement.cs`), so an upsert for member M can only ever touch M's own row by construction —
> there is no bulk/shared row to accidentally cross-write. `Share.MemberId` scopes each share to exactly one
> member, so Direction 1's cascade (`Share.MemberId == M.Id`) cannot touch another member's `Share.IsSettled`.
> The one shared, non-member-scoped piece of state Direction 1 DOES touch is `Expense.IsSettled` (the
> whole-expense rollup flag) via `SettlementReconciler.ReconcileExpense` — but this is a **recompute over
> all billable shares of that expense**, i.e. the exact same shared-rollup recompute the existing per-share
> toggle already performs today (OQ3a, shipped); it changes a shared DERIVED flag, never another member's
> underlying `Share`/`EventMemberSettlement` row. Direction 2 similarly writes to `EventMemberSettlement`
> rows keyed one-per-debtor-member, never the triggering payer's own row unless the payer happens to also be
> a debtor elsewhere (a distinct, legitimate write to *their own* row). **No cross-member leakage found; no
> fresh user decision needed.** Recommend code-reviewer specifically re-verify this invariant against the
> actual diff once implemented (i.e. that no cascade write path is ever keyed by anything other than the
> triggering member's own id), since it's easy to accidentally widen a `Where` clause during implementation.

**OQ-K — explicitly deferred to `ui-designer`'s downstream process; not user-blocking.** Should this BA also flag a requirement
for the UI to visually unify/distinguish the (now potentially four) related "settled" concepts —
whole-expense, per-share gross, per-member-event net, and this new sync/partial layer — or is that entirely
`ui-designer`'s downstream call? (Recommend leaving the concrete design to `ui-designer`, but the confusion
risk is real and already flagged as a Future Improvement in the shipped feature.)

> **BA #2 note (2026-08-25):** confirmed as ui-designer territory, not reopening. Two concrete, code-grounded
> inputs to hand ui-designer directly (see Cross-Functional Workstreams / Design below for the full brief):
> (1) `EventBalanceTable.tsx`'s `StatusCell` today renders the settle toggle ONLY for `row.balance < 0`
> (owing members) — creditors get a muted "—" with no control; OQ-A's decision explicitly makes creditors
> eligible for Direction 1, now **gross-purity-gated** per the accepted OQ-L amendment (Decision Log entry
> 5 — a creditor with a debtor-share elsewhere in the event is NOT eligible), so a design decision is needed
> on whether/how to expose that distinction, not just a plain creditor-vs-debtor toggle. (2) the OQ-L
> corollary above (a member's per-share badges can look "more paid" than their event-level partial-clearance
> total) is a legibility risk the unified design must account for, not just a binary-vs-tri-state color
> question.

**OQ-L [NEW, 2026-08-25].**
> ~~OQ-L~~ → **Answered 2026-08-25 (round 2): BA #2's recommended amendment accepted.** A net creditor is
> eligible for Direction-1 auto-cascade only if they hold **no debtor-share anywhere else in the event**
> (true gross purity for creditors). Net debtors keep the exact scope already decided (OQ-A/OQ-B) — no extra
> check for them, since their payer-shares are already settled-by-definition no-ops. Recorded as **accepted**,
> not merely proposed. See Decision Log entry 5 and the OQ-A annotation above.

Raised as a residual of the OQ-A
decision: a member can be **single-sided by net balance** (purely a net debtor or purely a net creditor for
the event) and still have, within that same event, a **payer-share on one expense and a debtor-share on
another** — meaning their gross Layer A facts and net Layer B fact could still diverge even though they
pass the "single-sided" gate as currently worded. The user's decision did not characterize whether such a
member should be treated as eligible for auto-cascade or excluded from it. BA #2 should characterize
precisely when/whether this case exists in the data model and how it should be treated, and bring it back
to the user only if the characterization reveals a genuine ambiguity requiring a fresh decision.

> **BA #2 characterization (2026-08-25) — the case is real, occurs in the shipped data model, and DOES
> surface a genuine safety gap in Direction 1 for one specific sub-case. Recommend bringing this back to the
> user (not silently resolving it) because it affects the scope of an already-locked decision (OQ-A).**
>
> Traced directly against `StatsRepository.GetEventBalanceAsync` (`Repositories/StatsRepository.cs:56-114`)
> — the actual, shipped M7 balance computation this feature's "single-sided" gate is defined against:
> `advanced` is `SUM(share.Amount)` grouped by `share.Expense.PayerMemberId`; `owed` is the SAME share-set's
> `SUM(share.Amount)` grouped by `share.MemberId`. Algebraically, for any member M:
> `balance(M) = advanced(M) − owed(M) = Σ(other members' shares on expenses M paid for) − Σ(M's debtor
> shares on expenses paid by others)` — **the payer-own-share terms cancel out of the formula entirely**,
> because a payer's own share appears in both `advanced` (as part of the expense they paid) and `owed` (as
> part of their own membership in that share-set).
>
> **This means "single-sided by net balance" is fully compatible with holding both roles in gross terms.**
> Concrete example, one event E, two expenses:
> - Expense X: M pays 100đ total; shares = M's own 50đ (payer-own) + N's 50đ (N's debtor share, not M's).
> - Expense Y: N pays 200đ total; shares = M's 30đ (**M's debtor share** — M ≠ payer of Y) + N's own 170đ.
> - `advanced(M) = 100` (all of X, since M is X's payer). `owed(M) = 50 (X, payer-own) + 30 (Y, debtor) = 80`.
> - `balance(M) = 100 − 80 = +20` → **M is single-sided: a pure net CREDITOR.** Yet M holds a payer-share on
>   X **and** a genuine debtor-share on Y in the same event — exactly the OQ-L scenario.
>
> **Consequence for Direction 1 (Story A, OQ-B's "cascade ALL shares"):** if the owner marks M's event
> balance Settled (M is single-sided, so the OQ-A gate passes), the literal OQ-B decision cascades **every**
> one of M's shares to `IsSettled=true` — including M's 30đ debtor share on Y. But M being a net creditor
> means the GROUP owes M money (M is being paid back), which has nothing to do with whether M has actually
> transferred the 30đ M separately owes N on Y. The cascade would assert a real, unrelated gross debt (M → N,
> 30đ) as paid, purely as a side effect of M's aggregate credit position being cleared — **this is precisely
> the over-counting failure mode `OQ1`/`OQ8` were locked to prevent**, and it survives the OQ-A gate because
> that gate only inspects the SIGN of the net aggregate, not each share's gross role.
>
> **The risk is asymmetric — it does NOT occur for a single-sided net DEBTOR.** By the same algebra, a net
> debtor's OWN payer-shares (if any) are already "settled-by-definition" no-ops in every derivation
> (`SettlementReconciler.IsBillable`/OQ6a, shipped) — cascading `IsSettled=true` onto a payer-own share is
> defined as harmless regardless of gross/net divergence. So a mixed-role net DEBTOR is safe under the
> current decisions; only a mixed-role net CREDITOR is not.
>
> **Direction 2 is self-protecting for this case, confirmed by reading `StatsService`'s outstanding formula
> (`Outstanding = (Balance < 0 && !IsSettled) ? -Balance : 0`):** a net creditor's `Outstanding` is already
> floored at 0 regardless of any debtor-share they hold, so Direction 2's partial-credit mechanism has
> nothing to over-credit for a creditor — no correctness gap there.
>
> **Corollary worth flagging alongside this:** for a mixed-role net DEBTOR (safe case above), Direction 2's
> credit is capped at the member's net owed amount (`outstanding`). Because the payer-own-share terms cancel
> out of the balance formula, it is possible for such a member to have MORE gross debtor-share krona than
> their net owed amount (offset by money they're separately owed as a payer elsewhere) — meaning once their
> cumulative credit hits the cap, settling a FURTHER individual debtor-share still flips that share's own
> `IsSettled=true` (Layer A, unconditional) but contributes zero additional event-level credit. A member
> could show 3 green "đã trả" per-share badges yet their event-level partial-clearance total reads less than
> the naive sum of those 3 bills — correct under the net model, but a real UI-legibility risk to flag for
> ui-designer (feeds OQ-K) and a required test fixture for test-engineer (feeds the Risks & Sequencing
> section below).
>
> **Recommendation brought back to the user:** tighten the Direction-1 single-sidedness gate specifically
> for the **net-creditor** case — a member should be eligible for Direction-1 auto-cascade as a creditor
> only if they ALSO hold no debtor-share anywhere else in the event (true gross purity for creditors); net
> debtors need no such extra check (already safe via OQ6a). This is a narrow amendment to OQ-A/OQ-B, not a
> reopening of them — debtors keep the exact literal scope already decided; only the creditor branch gets a
> stricter gate. Presenting as an amendment for explicit confirmation rather than assuming it, since it
> changes the boundary of an already-locked decision.
>
> **User decision (2026-08-25, round 2): amendment accepted as recommended.** See Decision Log entry 5.

## Assumptions

- The **owner (chủ sổ)** remains the only actor who triggers any settled/sync action; members never act
  directly (they have no account), consistent with the whole domain model.
- The raw debt-balance formula (`advanced − owed`, sum-to-zero per event) is a **hard invariant** that
  this idea must not change under any resolution of the Open Questions above (per Business-Rule Impact
  item 8/9).
- "Partially settle the balance" in the raw idea is understood as partially clearing the **derived
  outstanding overlay**, not the raw `balance` figure — this reading is assumed correct but should be
  explicitly reconfirmed given how central it is to Story B.
- This idea builds on top of the already-shipped Layer A (per-share settled) and Layer B
  (per-member-per-event net clearance) rather than introducing a third, unrelated tracking mechanism —
  though OQ-A's resolution could change how much of that shipped surface is reused vs. extended.
- No new domain concept from this idea should be named unilaterally by any downstream agent; naming stays
  subject to the same owner sign-off convention that fixed "expense/share/event/settled" on 2026-07-10.

## Feasibility & Affected Surface

> BA #2 (`ba-solution-analyst`) deliverable, 2026-08-25. GitNexus MCP tools (`gitnexus_query`/
> `gitnexus_context`/`gitnexus_impact`) were **not available in this session** (not registered — same
> environment gap already logged in `settled-per-member.md`'s 2026-07-21 implementation entry). Per the
> repo's fallback policy, proceeded with direct reading of `FairShareMonApi/FairShareMonApi/` and
> `FairShareMonWeb/src/` plus manual upstream-caller tracing in place of automated impact analysis. No
> HIGH/CRITICAL blast-radius findings to report from that manual trace (see per-story detail and Risks &
> Sequencing) — every write path this feature touches has a small, single-controller-action caller set
> today; the real risk in this feature is internal correctness/atomicity, not breakage of unrelated callers.

**Story A — Direction 1 (event settle → cascade to shares). Verdict: buildable as-is, but is genuinely a
new multi-row write path, not a small extension.**
- Today `PUT /events/{eventUuid}/members/{memberUuid}/settled` → `EventMemberSettlementRepository.
  SetMemberSettledAsync` (`Repositories/EventMemberSettlementRepository.cs:51-96`) only upserts ONE
  `(event_id, member_id)` row. Direction 1 requires the SAME transaction to additionally: load every
  `Expense` in the event where M holds a `Share` (tracked, `.Include(Shares)` — needs **every** share of
  each such expense, not just M's, because reconciliation depends on all billable shares); set M's own
  share(s) `IsSettled=true`/`SettledAt`; then re-run the existing `SettlementReconciler.ReconcileExpense`
  (`Repositories/SettlementReconciler.cs:29-42`) per affected expense.
- This is a genuine **multi-row, cross-entity** write bounded by "every expense M has a share in" — not a
  single flag flip — which is exactly the characterization BA #1's Business-Rule Impact item 3 already
  flagged and directly informs OQ-H (see updated Open Questions above).
- Manual caller check: `EventMemberSettlementRepository.SetMemberSettledAsync` has exactly one caller
  (`EventsService.SetMemberSettledAsync` ← `EventsController.SetMemberSettledAsync`) — widening its
  transaction is contained; LOW risk to unrelated callers.

**Story B — Direction 2 (expense settle → partial-clear event balance). Verdict: buildable, but needs a new
schema column AND new cross-repository architecture, not just new logic in an existing method.**
- `ExpenseRepository.SetSettledAsync` (`Repositories/ExpenseRepository.cs:332-352`) needs, inside its own
  transaction, each debtor member's single-sidedness, net-owed amount, and cumulative-cleared-so-far — none
  of which `ExpenseRepository` can see today. The only existing balance computation is
  `StatsRepository.GetEventBalanceAsync` (`Repositories/StatsRepository.cs:56-114`), a **separate**
  repository invoked via `ExecuteQueryAsync` (read-only), not `ExecuteTransactionAsync` — and per this
  codebase's own established convention (explicitly called out in `settled-per-member.md`'s Deviation entry,
  "repos don't call other repos here"), `ExpenseRepository` cannot simply call `IStatsRepository`.
- **This is the single largest piece of new API architecture this feature needs:** a shared, pure/static
  balance-and-single-sidedness helper (same pattern as the existing `SettlementReconciler`), computable
  against an already-open transaction's `AppDbContext`, that BOTH `StatsRepository` (read path, unchanged
  behavior) and the new write paths (`EventMemberSettlementRepository` for Direction 1,
  `ExpenseRepository`/possibly `ShareRepository` for Direction 2) can call without violating the no-cross-
  repo-calls convention. Not reusing `IStatsRepository` directly is a deliberate architecture requirement,
  not an oversight — see Risks & Sequencing for the drift risk if this isn't done.
- Needs a new stored column (see Tier & Data Implications / OQ-E above) — this is NOT a zero-migration
  feature the way Story C looks in isolation.

**Story C — QR reflects remaining unsettled amount. Verdict: buildable, additive-only, near-free once B
ships.**
- `WalletQrService.CollectEventBillables` (`Services/Api/Wallet/WalletQrService.cs:233-240`) already bills
  `row.Outstanding > 0m` — it needs **zero code changes**; once `Outstanding` can represent a genuine partial
  amount (via B's new column), the QR path already flows it through correctly. Confirms the shipped D2
  design ("balance stays pure, overlay is derived") already anticipated exactly this extension. The only
  real work is in `StatsService.GetEventBalanceAsync`'s overlay math, which today derives `Outstanding` from
  a boolean and must instead derive it from `max(0, NetOwed − ClearedAmount)`.

**Story D — Closed-event scope. Verdict: mechanically trivial; the substance is entirely OQ-H's policy call.**
- Confirmed via `EventWriteGuard.cs` (`IsCurrentEventClosed`, invoked by every M5 write path except the
  settled toggles): every write path Direction 1/2 hang off of already bypasses this guard today. No new
  bypass code is needed — it falls out for free from reusing the existing `SetSettledAsync`-family
  transactions. There is nothing further to add technically; see OQ-H above for the sharpened policy framing.

## Cross-Functional Workstreams

> **Updated 2026-08-25 (round 2) — all six previously-open user decisions (OQ-C, OQ-D residual, OQ-G, OQ-H,
> OQ-I, and the OQ-L creditor-gate amendment) are now resolved; see Decision Log entries 5-10.** Scoped
> enough for the orchestrator to hand each agent a focused brief per the Milestone 1 / Milestone 2 split in
> the Handoff Summary at the end of this doc. The items below are confirmed still accurate against the final
> design, with the OQ-L amendment specifically reflected in API item 2 and Web/Design item 3/2
> (creditor-row eligibility is now **gross-purity-gated**, not a plain single-sided gate).

### API (`feature-planner`)

1. Extend `EventMemberSettlementRepository.SetMemberSettledAsync` (or add a sibling method on it) to
   perform Direction 1's cross-expense cascade in the SAME transaction: load the event's expenses where M
   has a share (tracked, `.Include(Shares)`), set M's own share(s) settled, reconcile each affected expense
   via the existing `SettlementReconciler.ReconcileExpense`.
2. Build the new shared balance/single-sidedness helper described in Feasibility above (mirrors
   `SettlementReconciler`'s static-helper pattern) exposing a classification with **four** outcomes, not
   three, per the accepted OQ-L amendment (Decision Log entry 5): pure debtor (eligible, no extra check) /
   pure creditor with no debtor-share elsewhere in the event (eligible) / pure creditor WITH a debtor-share
   elsewhere (**not eligible** — this is the new, amended branch) / mixed net balance (not eligible) — plus
   net owed amount and (for Direction 2) the cumulative-credit cap. **Both** `EventMemberSettlementRepository`
   and `ExpenseRepository` (+ `ShareRepository`, since OQ-D residual decided both triggers fire Direction 2)
   must consume this ONE helper — call this out as a hard requirement in the plan, not a suggestion (see
   Risks & Sequencing). Direction 1 must call the full four-way classification; Direction 2 only needs the
   debtor/creditor/mixed split (the OQ-L amendment does not add a further condition for Direction 2, since a
   creditor's `Outstanding` is already floored at zero — see Story B).
3. Direction 2 write path in `ExpenseRepository.SetSettledAsync` **and** `ShareRepository.SetSettledAsync`
   (both triggers, per Decision Log entry 6): after `SettlementReconciler.CascadeToShares`, iterate every
   debtor member on the expense, classify via the new helper, upsert `EventMemberSettlement.ClearedAmount`
   (capped) — same transaction, same all-or-nothing guarantee. Also implement the symmetric reversal path
   (Decision Log entry 9) in the same shared helper: on `IsSettled=false`, subtract the reversed amount,
   floored at the member's current owed amount.
4. Schema/migration: new `event_member_settlements.cleared_amount` (`decimal(18,2) NOT NULL DEFAULT 0`,
   CHECK `>= 0`, mirrors `shares.amount`'s convention) — one EF migration, naming to follow the
   `AddPerMemberSettlement` precedent. **OQ-G resolved to "no audit"** (Decision Log entry 10) — the
   `AuditEntityType`/`AuditLog.ExpenseUuid`-nullability schema work characterized under OQ-G above is **not**
   needed; do not build it.
5. `StatsService.GetEventBalanceAsync` overlay math: `Outstanding` derivation changes from
   `(Balance < 0 && !IsSettled) ? -Balance : 0` to `max(0, NetOwed − ClearedAmount)`; `MemberBalanceRow`
   gains `ClearedAmount` + a service-derived partial status (per the OQ-E recommendation above).
   `WalletQrService` needs NO change — note this explicitly in the plan so it isn't accidentally touched.
6. Decide and document the toggle-endpoints' response shape: today `PUT .../settled` (all three variants)
   returns only an `ApiResult` success message. No existing precedent in this codebase for a toggle endpoint
   reporting cascade side-effect counts — feature-planner should explicitly decide whether the owner needs
   immediate feedback ("5 shares settled across 3 expenses") or a plain refetch is sufficient, since this
   shapes what web-feature-planner can build against.
7. No new error codes anticipated for the core cascade (resource-owned misses reuse the existing 404
   family); 15xxx stays reserved-unclaimed per `ErrorCodes.cs`'s existing comment — confirmed, since OQ-G
   resolved to "no audit" and OQ-I resolved to "Free-tier," neither introduces a new failure mode.
8. Test fixtures to specify for test-engineer: the shipped D2/M7-OQ2 "balance byte-for-byte unchanged"
   regression pattern extended to every new cascade path, PLUS the two OQ-L-derived fixtures traced by hand
   above (a single-sided creditor with a debtor-share elsewhere in the event; a single-sided debtor with a
   payer-share elsewhere whose cumulative Direction-2 credit hits the cap before all debtor shares are
   settled) — these should become the canonical regression cases, not left implicit.

### Web (`web-feature-planner`)

1. Extend `MemberBalanceRow` (`features/events/api/types.ts`) with `clearedAmount` + whatever derived status
   field the API ships, mirroring the OQ-E representation (accepted; final field names come from
   feature-planner's contract).
2. **Concrete, already-broken-by-this-feature invalidation logic to fix:** `useSetSettled`/
   `useSetShareSettled` (`features/expenses/hooks/useExpenses.ts:98-125`) today invalidate ONLY the expenses
   cache, with an explicit code comment asserting "the event overlay `outstanding` is Layer-B (net) driven,
   so a per-share (gross) flip does not change the balance overlay" — **that becomes false once Direction 2
   ships.** Both hooks need to additionally invalidate `eventsKeys.balance(eventUuid)`, which requires
   plumbing the owning event's uuid through mutation call sites that don't carry it today. Symmetrically,
   `useSetMemberSettled` (`features/events/hooks/useEvents.ts:101-116`) invalidates only the events caches
   today; once Direction 1 ships it must also invalidate `expensesKeys.all`/relevant `expensesKeys.detail`.
3. `EventBalanceTable.tsx`'s `StatusCell` (~line 149-178) renders the settle toggle ONLY when
   `row.balance < 0`; a creditor row shows a muted "—" with **no control at all** today. Since OQ-A's
   decision makes "purely a net creditor" eligible for Direction 1, web-feature-planner must plan whether/how
   creditor rows get a settle affordance — **and, per the OQ-L amendment (Decision Log entry 5), the
   eligibility this affordance represents is now gross-purity-gated, not a plain single-sided gate**: a
   creditor row is only truly eligible for the Direction-1 auto-cascade if that member holds no debtor-share
   elsewhere in the event. The UI needs to either (a) surface this distinction to the owner (e.g. only enable
   the toggle for gross-pure creditors, with an explanation for why an otherwise-single-sided creditor's
   toggle is disabled/hidden), or (b) always show the toggle but make the fallback-to-manual outcome legible
   when the gross-purity check fails. This is a real product-UX decision to resolve with ui-designer, not
   something to silently paper over — compounded by the pre-existing finding that marking a creditor settled
   has **zero visible effect** on `outstanding` today (always 0 for `balance >= 0`).
4. New partial-state display: today's binary `<Badge tone={isSettled ? "settled" : "warning"}>` needs a
   three-state treatment (unsettled / partially settled / fully settled) — see Design workstream.
5. Copy/toast review for `SettledToggle`/`ShareSettledToggle`: these remain no-confirmation, Free-tier
   controls (OQ-F) that will now silently move OTHER members' event balances too — flag for ui-designer
   whether the success toast should communicate the cascade side effect.
6. Known existing test surface to hand to web-test-engineer as a starting point (found via `grep -rl
   settled`, not yet verified against the new behavior): `features/events/memberSettled.test.tsx`,
   `features/events/eventBalanceTable.test.tsx`, `features/expenses/settledToggle.test.tsx`,
   `features/expenses/shareSettled.test.tsx`, `features/expenses/expenseSettledReconcile.test.tsx`,
   `features/events/settledQrFilter.test.ts`.

### Design (`ui-designer`)

1. Per OQ-K above: design a coherent visual language across `SettledSwitch`/`SettledToggle`/
   `ShareSettledToggle`/`MemberSettledToggle`/`Badge` that can show a 3-state (unsettled / partially settled
   / fully settled) status, replacing today's binary `tone={isSettled ? "settled" : "warning"}` in
   `EventBalanceTable.tsx`.
2. Decide the creditor-row settle affordance (show it with an explanation of what it does / don't show it at
   all) — a genuine new design call surfaced by OQ-A's scope, not previously needed since today's UI never
   renders a control for `balance >= 0` rows. **Per the OQ-L amendment (Decision Log entry 5), this is not a
   plain "is this row a creditor" question** — the toggle's real eligibility is gross-purity-gated: a
   creditor with a debtor-share elsewhere in the event does NOT get the auto-cascade even though the row
   reads as a net creditor. Design must decide how (or whether) to communicate that distinction at the
   individual-row level rather than treating all creditor rows as uniformly eligible.
3. A money-metaphor for "partially settled" (e.g. a fraction/progress affordance next to `Money` in
   `EventBalanceTable`'s outstanding column — "300.000đ / 500.000đ"), consistent with the `--fs-viz-*`
   dataviz tokens per `FairShareMonWeb/CLAUDE.md`.
4. Account for the OQ-L corollary in the visual design: a member can show fully-settled per-share badges on
   individual bills while their event-level partial-clearance total reads less than the naive sum of those
   bills (correct under the net model, but a legibility risk if not designed for explicitly).
5. Toast/confirmation copy pass per Web workstream item 5.

## Tier & Data Implications

> **Updated 2026-08-25 (round 2) — OQ-G and OQ-I are resolved; both contingencies below collapse to a single
> final answer each.**

- **Schema (required):** one EF migration adding `event_member_settlements.cleared_amount`
  (`decimal(18,2) NOT NULL DEFAULT 0`, DB CHECK `>= 0`) per the OQ-E representation accepted above — same
  category of change as the shipped `AddPerMemberSettlement` migration. Story C, in isolation, looks like a
  zero-migration story; it is not, because it depends on Direction 2's column (Milestone 2).
- **Audit schema: NOT needed.** OQ-G resolved to "no audit" (Decision Log entry 10) — the `AuditEntityType`
  new-variant / `AuditLog.ExpenseUuid`-nullability schema work characterized under OQ-G above is explicitly
  **not** part of this feature.
- **Tier gating: Free-tier, no gating code needed.** OQ-I resolved to "Free-tier" (Decision Log entry 8) —
  the sync is a side effect of the same already-Free `PUT .../settled` endpoints; no `ITierService
  .EnsurePremiumFeature` guard is added, and the "same 200, different side effects, by tier" shape flagged as
  a risk under OQ-I is explicitly **avoided**, not adopted.
- **No new tier-limit COUNT is implicated** — confirmed BA #1's Business-Rule Impact item 6 is correct: this
  feature creates no new countable resource, so §4.9 (tier limits block creation only) needs no changes.
- **Audit-log scope:** stays excluded (inherits `OQ10`, locked), final per the OQ-G decision above — no
  further contingency.

## Risks & Sequencing

- **Risk (assessed manually — no GitNexus this session): balance-logic duplication drift.** If the new
  shared single-sidedness/net-owed helper described in the Cross-Functional Workstreams / API section above
  is NOT strictly the ONE place this logic lives, a second, divergent reimplementation inside
  `ExpenseRepository`/`EventMemberSettlementRepository` (vs. the canonical logic in `StatsRepository`) could
  silently drift over time and reintroduce the exact "gross ≠ net" bug class `OQ1`/`OQ8` were locked to
  prevent. This is the single biggest implementation risk in this feature — feature-planner should treat "one
  canonical helper, every write path reuses it" as a hard requirement, not a nice-to-have.
- **Risk: Direction 1's write size is unbounded by the API today.** A member with shares across many
  expenses in one event triggers a proportionally larger single transaction (N expenses reconciled at once).
  No pathological complexity found, but worth a defensive note for feature-planner given this runs
  synchronously in a request/response cycle. This was surfaced explicitly alongside the OQ-H decision
  (Decision Log entry 7, with the concrete "~2-10 expenses typical, not API-bounded" figure) and accepted by
  the user as-is — no further mitigation (e.g. a hard cap) was requested, but feature-planner should still
  treat it as a defensive-coding note.
- **Risk, web side (concrete, code-confirmed, not speculative):** see Cross-Functional Workstreams / Web
  items 2-3 above — the two mutation hooks whose invalidation logic is currently correct but will become
  incorrect the moment Direction 2 (and separately Direction 1) ship, and the creditor-row UI gap (now
  additionally shaped by the OQ-L gross-purity amendment — see Web item 3 and Design item 2).
- **No HIGH/CRITICAL GitNexus risk flags to report** — the MCP impact-analysis tools were unavailable this
  session (see the note at the top of Feasibility & Affected Surface); the manual caller trace found small,
  contained caller sets for every touched write path, so blast radius on unrelated callers is low. The real
  risk in this feature is internal correctness/atomicity (addressed above), not breakage elsewhere.
- **Sequencing — FINAL, all inputs resolved 2026-08-25 (round 2):**
  1. OQ-E's representation is locked (accepted as a data-modeling decision, see the note after Decision Log
     entry 10) — Direction 2 and Story C's migration can both proceed against it.
  2. All six previously-open user decisions are now closed (OQ-C, OQ-D residual, OQ-G, OQ-H, OQ-I, the OQ-L
     creditor-gate amendment — Decision Log entries 5-10). The API contract web-feature-planner and
     ui-designer plan against is fully specified: `ShareRepository.SetSettledAsync` DOES need Direction-2
     logic (OQ-D residual, both triggers); the cascade fires on closed events (OQ-H); is Free-tier (OQ-I); is
     not audited (OQ-G); reverses symmetrically and capped (OQ-C); and Direction 1's eligibility gate is the
     amended four-way classification (OQ-L).
  3. **Milestone split — adopted as recommended:** **Milestone 1 ships Direction 1 (Story A) alone** — it
     reuses the existing single-endpoint transaction shape, needs no new column, and is the lower-risk half
     (only needs the single-sidedness + OQ-L gross-purity classification, not the net-owed-amount cap
     tracking Direction 2 needs). **Milestone 2 ships Direction 2 (Story B) + Story C (QR)** together, since
     Story C is additive-only once Direction 2's `ClearedAmount` column and `Outstanding` derivation exist.
     Full scope breakdown for both milestones is in the Handoff Summary at the end of this doc.
  4. Web cannot finalize hook/invalidation and DTO changes until feature-planner locks the exact
     response-shape questions above (API item 6) — standard API-before-Web dependency, called out explicitly
     here since this feature's cascades touch caches on both sides of today's expense/event boundary in ways
     the existing hooks don't anticipate. This applies per-milestone: Web's Milestone 1 work only needs
     Direction 1's response shape; Milestone 2 response-shape questions can be locked later.

## Decision Log

> **Resolved at the 2026-08-25 user checkpoint** — the blocking Open Question (OQ-A) and the three other
> top-level Open Questions the user was asked to close (OQ-B, OQ-D's multi-member sub-question, OQ-F). Full
> options/trade-offs remain inline under each matching OQ above; this log records the binding answer and
> the reason. **Do not reopen these four without a new explicit user decision.**

1. **OQ-A — Option (a), narrowly scoped: auto-cascade gated to single-sided members only.** Both cascade
   directions are automatic, but apply **only** to a member whose role in the event is single-sided — the
   member is purely a net debtor or purely a net creditor for that event per the existing
   `advanced − owed` balance, never a mix that would require ambiguously deriving one layer from the other.
   For a member who does not cleanly fit that shape, no auto-cascade fires; the existing manual two-layer
   toggling remains the fallback. *Reason:* this reopens `OQ1`/`OQ8` (locked in `settled-per-member.md`)
   only for exactly the case where gross (Layer A) and net (Layer B) cannot diverge in a way that matters —
   preserving the original gross/net conflation concern for every member it was meant to protect, while
   still delivering the automatic cascade the raw idea asked for in the common case. *Residual flagged by
   the user:* a member who is single-sided by net balance could still have a payer-share on one expense and
   a debtor-share on another within the same event, where gross Layer A and net Layer B could still diverge
   — this specific boundary was not characterized and is carried forward as new **Open Question OQ-L** for
   BA #2 to characterize precisely, brought back to the user only if a genuine ambiguity surfaces.

2. **OQ-B — Direction 1 cascades ALL of the member's shares in the event, not just owing shares.** When a
   member's event-level balance is marked Settled, every one of that member's shares across every expense
   in the event becomes settled (subject to the OQ-A gate above). *Reason:* matches the literal ask ("all
   of that member's expenses/shares... become settled too") rather than narrowing to owing-only; the
   "unconditional vs. requires negative balance" half of the original question is superseded by the OQ-A
   single-sidedness gate rather than answered separately (payer-shares are already settled-by-definition,
   so including them is a no-op).

3. **OQ-D (multi-member sub-question) — every debtor member on the settled expense, not just one.** When an
   expense is marked settled, every debtor member who has a share on that expense gets their event-level
   balance partially cleared by their share amount, simultaneously — subject to the OQ-A gate (a debtor
   member on the expense who is not single-sided in the event does not get auto partial-credit; other
   single-sided debtor members on the same expense still do). *Reason:* matches the literal ask ("(các)
   thành viên liên quan" / "the affected member's(s')") rather than picking one arbitrary member to credit.
   *Not resolved by this decision:* which action triggers Direction 2 (whole-expense toggle only, per-share
   toggle only, or both) — restated as a still-open residual under OQ-D above.

4. **OQ-F — Fully automatic, no confirmation step.** Toggling either side of either direction cascades
   immediately; there is no separate "also sync" confirming action. *Reason:* matches the raw idea's literal
   wording ("then X automatically becomes Y"); the narrow single-sided scope (Decision 1) and the mandatory
   atomicity requirement (Business-Rule Impact item 4) are relied upon as the safety net in place of a
   confirmation step, given the Human-Confirmation-Policy trade-off this raises (flagged, not silently
   dropped, in OQ-F above and in the closed-event/audit questions OQ-H/OQ-G that remain open as a result).

> **Resolved at the 2026-08-25 (round 2) user checkpoint** — the six remaining genuinely-open questions
> (the OQ-L creditor-gate amendment, OQ-D's residual trigger-action question, OQ-H, OQ-I, OQ-C, OQ-G). With
> these, **zero Open Questions remain that genuinely need a user decision** — every item below is either
> fully resolved or explicitly deferred to a downstream agent's normal planning process (OQ-E naming/copy,
> OQ-K UI unification specifics). **Do not reopen entries 5-10 without a new explicit user decision.**

5. **OQ-L amendment — Accepted: tighten the Direction-1 gate for net creditors to require gross purity.** A
   net creditor is eligible for Direction-1 auto-cascade **only if they hold no debtor-share anywhere else
   in the event** (true gross purity for creditors). Net debtors keep the exact scope already decided under
   OQ-A/OQ-B — **no additional check for debtors**, since a net debtor's own payer-shares are already
   settled-by-definition no-ops (`SettlementReconciler.IsBillable`/OQ6a, shipped) and can never be
   over-counted by the cascade. *Reason:* accepts BA #2's own OQ-L finding, as recommended, rather than
   merely noting it — "single-sided by net balance" does not imply gross-role purity, because the
   payer-own-share terms cancel out of the `advanced − owed` formula: a pure net creditor can still hold a
   genuine, unrelated debtor-share on a different expense in the same event. Cascading "all shares" (OQ-B)
   onto such a member would incorrectly auto-settle that unrelated debt, reintroducing the exact
   over-counting failure `OQ1`/`OQ8` were locked to prevent. This is a narrow amendment to the *boundary* of
   OQ-A/OQ-B — the literal scope already decided for debtors is unchanged; only the creditor branch gets the
   stricter, gross-purity gate.

6. **OQ-D (residual, trigger action) — Both the whole-expense settled toggle and any individual per-share
   toggle fire Direction 2, via one shared code path.** *Reason:* per BA #2's own feasibility finding, this
   requires no extra engineering effort beyond implementing either trigger alone — the whole-expense toggle
   already iterates every billable share via `SettlementReconciler.CascadeToShares`, so the pragmatic
   implementation shape is a single internal "credit this member for this now-settled share" helper invoked
   from both `ShareRepository.SetSettledAsync` and `ExpenseRepository.SetSettledAsync`'s existing
   per-billable-share cascade loop. This is the option BA #2 recommended (option (c)), and it also best
   matches Story B's own literal wording ("một phiếu (hoặc một phần gánh)" / "an expense (or a share)").

7. **OQ-H — The cascade is within the existing §3.5/§4.4 closed-event exception; allowed to fire on CLOSED
   events, identically to how it fires on open ones.** *Reason:* consistent with the precedent already set
   when the shipped settled-per-member feature extended this same exception once before (from whole-expense
   to per-share and per-member-event, `OQ5a`), and consistent with BA #2's own finding that no new technical
   guard-bypass is needed for this — the cascade mechanically reuses the exact `EventWriteGuard` bypass the
   existing settled toggles already rely on; nothing new is added to that bypass surface.

8. **OQ-I — This sync behavior is Free-tier.** *Reason:* consistent with settled being Free-tier today
   (§3.11); avoids introducing a new "same 200 response, tier-dependent side effect" gating shape that would
   be unprecedented in this codebase (every existing Premium gate blocks the whole action with a 403, per
   BA #2's feasibility note under OQ-I) — no such gating shape is introduced by this feature.

9. **OQ-C — Option (c): symmetric, capped/idempotent reversal.** Un-marking a member's event-level settled
   flag un-settles the shares that were cascaded from it (Direction 1 reversal); un-settling a contributing
   share claws back its corresponding partial credit at the event level (Direction 2 reversal) — but any
   reversal is floored/capped so it never drives the member's cleared amount / settled state below what their
   CURRENT balance implies is still owed. *Reason:* this is BA #2's own recommended option — it matches the
   literal "symmetric" framing of the original idea while explicitly protecting the money-exactness invariant
   (Business-Rule Impact item 2) against the open-event drift edge case already documented as a known,
   accepted limitation in the shipped feature (`OQ9a`, "drift-aware Layer B"), rather than silently
   inheriting that drift into a new reversal path.

10. **OQ-G — No audit; the cascade inherits the existing no-audit exclusion unchanged.** *Reason:* consistent
    with the locked decision (`OQ10`, `settled-per-member.md`) that settled is payment metadata, not
    expenditure data ("số liệu chi tiêu") that disputes are fought over. No new `AuditEntityType` variant, no
    `AuditLog.ExpenseUuid`-nullability change, and no new `EventUuid` column are needed for this feature as a
    result — the small schema/design item BA #2 characterized under OQ-G is not needed.

> **Also noted (not a fresh decision requiring its own sign-off round):** OQ-E's underlying **representation**
> — the `EventMemberSettlement.ClearedAmount` column plus a service-derived partial status (no separate
> tri-state enum) — is accepted as BA #2's recommendation, treated as a **data-modeling** decision rather than
> a preference call. Final field naming and the exact Vietnamese/English copy remain `feature-planner`'s /
> `ui-designer`'s normal job to draft in their own planning docs, per this repo's established
> fixed-terminology sign-off process (`The-ideal.md` §5) — noted explicitly here so it is not mistaken for a
> still-blocking item.

## Handoff Summary (for the orchestrator — dispatching `feature-planner`, `web-feature-planner`, `ui-designer`)

> All ten Open Questions this doc raised are now resolved (Decision Log 1-10) or explicitly deferred to
> downstream planning (OQ-E naming/copy, OQ-K UI specifics). This doc is ready for implementation planning.
> Recommend dispatching in two milestones, API-before-Web-and-Design within each milestone (per Risks &
> Sequencing item 4).

**Milestone 1 — Direction 1 only (Story A + Story D's closed-event scope for Direction 1).**
- Auto-cascade, event-level settle → all of the eligible member's shares in the event, fully automatic, no
  confirmation step.
- Eligibility gate: a net debtor (single-sided) is always eligible; a net creditor is eligible **only** if
  gross-pure (no debtor-share elsewhere in the event) — the amended OQ-A/OQ-L gate. Non-eligible members get
  no cascade; manual toggling remains.
- Fires identically on OPEN and CLOSED events (OQ-H). Free-tier (OQ-I). Not audited (OQ-G).
- Symmetric, capped/idempotent reversal on un-settle (OQ-C) — un-marking the event flag un-settles the
  shares this cascade set.
- New shared classification helper (four-way: pure debtor / gross-pure creditor / non-gross-pure creditor /
  mixed) — build it now even though Milestone 1 only needs three of the four branches, since Milestone 2
  reuses the same helper for its debtor/creditor/mixed split.
- **No new schema/migration required for Milestone 1** — Direction 1 needs no `ClearedAmount` column.
- Web: creditor-row settle affordance decision (gross-purity-gated, not a plain single-sided toggle) +
  `useSetMemberSettled` invalidating `expensesKeys` too.
- Design: creditor-row affordance treatment; no partial-state badge work needed yet (that's Milestone 2).

**Milestone 2 — Direction 2 + Story C (QR).**
- Auto partial-credit, expense/share settle → every single-sided debtor member on that expense, triggered by
  **both** the whole-expense toggle and any individual per-share toggle via one shared code path (OQ-D
  residual). The OQ-L creditor-gate amendment does **not** add a condition here — Direction 2 is
  self-protecting via the existing `Outstanding` floor-at-zero for creditors.
- Same Free-tier / not-audited / closed-event-allowed / symmetric-capped-reversal decisions as Milestone 1
  (OQ-I, OQ-G, OQ-H, OQ-C) — no separate re-litigation needed, they were decided doc-wide, not per-direction.
- Requires the new `event_member_settlements.cleared_amount` column (OQ-E representation) and the
  cross-repository balance/single-sidedness helper `ExpenseRepository`/`ShareRepository` can call without
  violating the no-cross-repo-calls convention.
- `StatsService.GetEventBalanceAsync` overlay math changes to `max(0, NetOwed − ClearedAmount)`;
  `WalletQrService` needs zero code changes (Story C is additive-only once this ships).
- Web: `useSetSettled`/`useSetShareSettled` must additionally invalidate the event balance cache; new
  partial-state (3-state) display on `EventBalanceTable`/badges.
- Design: 3-state settled visual language, money-metaphor for partial clearance, and the OQ-L corollary
  legibility risk (per-share badges reading "more paid" than the event-level partial total for a mixed-role
  debtor).

**Cross-Functional Workstreams accuracy check — confirmed accurate, one item updated in place.** The API,
Web, and Design workstream lists in this doc (see the section above) still correctly describe the work
needed for the final design; nothing needs to be dropped or newly added as a *category* of work. The one
place the workstreams needed sharpening after these last six decisions was the **creditor-row UI affordance
item** (Web item 3, Design item 2): both have been edited in place above to reference the amended,
**gross-purity-gated** eligibility (a creditor is eligible only if they hold no debtor-share elsewhere in the
event) rather than the plain single-sided gate the workstreams originally described. All other workstream
items already anticipated the final shape (e.g. API item 2's helper was already scoped for a "mixed"
classification bucket; the OQ-D residual, OQ-C reversal, OQ-G/OQ-I contingencies have been resolved to their
final single answer in place, per the edits above) and needed no structural changes, only removal of
now-stale "if this OQ resolves the other way" hedging language.

## Progress Log

### 2026-08-25

- Read `FairShareMonApi/The-ideal.md` (full spec, all sections) as source of truth.
- Read `AGENTS.md`/`CLAUDE.md` (root) and `FairShareMonApi/CLAUDE.md` for fixed domain terms and
  invariants.
- Surveyed `planning/ba/` (empty — first BA doc in this location) and skimmed the
  `FairShareMonApi/planning/` and `FairShareMonWeb/planning/` indexes to avoid duplicating prior decisions.
- Read in full `FairShareMonApi/planning/settled-per-member.md` (the shipped Layer A/B + outstanding
  overlay feature, all 15 OQs answered/locked, Final Outcome + Future Improvements sections), and skimmed
  `wallet-and-qr.md`, `expense-qr-per-member.md`, `per-member-qr-sharing.md` for how the QR already
  consumes the settled/outstanding overlay.
- **Key finding:** the raw idea substantially overlaps with work already shipped (QR already reflects the
  Layer B outstanding overlay) and with two items explicitly named in that feature's own "Future
  Improvements" ("drift-aware Layer B", "partial per-member settlement") — but the specific mechanism
  requested (automatic bidirectional cascade between Layer A and Layer B) directly reopens two decisions
  that were explicitly locked at the 2026-07-21 checkpoint (`OQ1`: Layer B stored not derived; `OQ8`:
  outstanding driven by net only, not gross). Flagged this as the central, blocking Open Question (OQ-A)
  rather than resolving it unilaterally.
- Drafted this BRD: problem statement, personas, goals, terminology (existing + new candidate terms),
  four user stories with acceptance criteria covering both sync directions plus the QR and closed-event
  stories, business-rule impact against all nine numbered invariants in `The-ideal.md` §4, scope in/out,
  eleven Open Questions (one flagged blocking), and assumptions.
- **User resolved the blocking + top-level Open Questions** (OQ-A, OQ-B, OQ-D's multi-member sub-question,
  OQ-F). Recorded all four in a new **Decision Log** section (with rationale). Updated Stories A/B/C/D,
  Business-Rule Impact items 3/6/7/9, and Scope (in/out) to reflect the resolved narrow-single-sided-only
  auto-cascade design. Re-closed the Open Questions list: annotated OQ-A/OQ-B/OQ-F as fully answered and
  OQ-D as partially answered (multi-member resolved, trigger-action sub-question restated as still open);
  added new **OQ-L** (residual gross/net divergence within a "single-sided" member, flagged by the user
  during the OQ-A decision) for BA #2 to characterize; classified each remaining open item as either
  "genuinely still needs a user decision" (OQ-C, OQ-D trigger-action, OQ-G, OQ-H, OQ-I) or "BA #2/downstream
  agent territory to investigate and recommend" (OQ-E's representation half, OQ-J, OQ-K, OQ-L). Did not
  start any solution/feasibility work — that remains BA #2's job next.

### 2026-08-25 (BA #2 — solution/feasibility analysis)

- Read this doc in full plus `FairShareMonApi/planning/settled-per-member.md` (the shipped Layer A/B
  feature, all 777 lines) end-to-end, `FairShareMonApi/CLAUDE.md`, `FairShareMonWeb/CLAUDE.md`, and
  `FairShareMonApi/.claude/rules/rule.md` to ground the analysis in fixed conventions and the human
  confirmation policy.
- **GitNexus MCP tools were not available in this session** (`gitnexus_query`/`gitnexus_context`/
  `gitnexus_impact` did not resolve via tool search — same environment gap already logged in
  `settled-per-member.md`'s 2026-07-21 implementation entry, not a stale-index condition `npx gitnexus
  analyze` would fix). Per the repo's fallback policy, proceeded with direct `Read`/`Grep`/`Glob` over
  `FairShareMonApi/FairShareMonApi/` and `FairShareMonWeb/src/` plus manual upstream-caller tracing in place
  of automated impact analysis; no HIGH/CRITICAL findings to report from that manual trace (see Risks &
  Sequencing).
- Read the live code grounding every user story: `Share`/`Expense`/`EventMemberSettlement` entities,
  `StatsRepository.GetEventBalanceAsync` (the exact M7 balance formula), `SettlementReconciler`
  (billable-share predicate + reconcile/cascade, shared by `ShareRepository`/`ExpenseRepository`
  `SetSettledAsync`), `EventMemberSettlementRepository.SetMemberSettledAsync`, `EventWriteGuard`,
  `AuditLogFactory`/`AuditLog` entity, `WalletQrService` (both QR paths + the billing-selection helpers),
  `ErrorCodes.cs` (confirmed 15xxx still reserved/unclaimed), and on the web side
  `MemberSettledToggle`/`EventBalanceTable`/`SettledToggle`/`ShareSettledToggle`/`QrDialog` plus the
  `useEvents.ts`/`useExpenses.ts` mutation hooks and their cache-invalidation logic.
- **Key finding (OQ-L):** algebraically derived from `StatsRepository.GetEventBalanceAsync`'s actual formula
  that a member's payer-own-share terms cancel out of the net balance, meaning "single-sided by net balance"
  does NOT imply gross-role purity — a member can be a pure net creditor while still holding a genuine
  debtor-share on a different expense in the same event. Traced the concrete consequence: Direction 1's
  literal "cascade ALL shares" (OQ-B) would incorrectly auto-settle that unrelated debtor-share for a
  mixed-role net creditor, reintroducing the exact over-counting failure `OQ1`/`OQ8` were locked to prevent
  — but confirmed the risk is asymmetric (a mixed-role net DEBTOR is safe, because their payer-shares are
  already settled-by-definition no-ops per the shipped OQ6a) and that Direction 2 self-protects for creditors
  via the existing `Outstanding` floor-at-zero. Recommended a narrow amendment to the Direction-1 gate
  (creditors need gross purity too; debtors don't) and flagged a corollary UI-legibility risk for a mixed
  debtor whose cumulative credit caps below their per-share badge count. Brought this back as a genuine
  ambiguity per the doc's own instruction, rather than resolving it unilaterally.
- Produced **Feasibility & Affected Surface** (per-story verdicts with exact file/line grounding),
  **Cross-Functional Workstreams** (concrete API/Web/Design briefs), **Tier & Data Implications**, and
  **Risks & Sequencing** (including a recommended smaller first milestone: ship Direction 1 alone before
  Direction 2). Added BA #2 feasibility notes/recommendations inline to OQ-C, OQ-D (residual), OQ-E, OQ-G,
  OQ-H, OQ-I, OQ-J (confirmed, no conflict), OQ-K (two concrete design inputs), and OQ-L (full
  characterization + recommended amendment) in the existing Open Questions section — no new Open Questions
  section created, no BA #1 scope/decision silently reinterpreted.
- Did not write any code, migration, or DTO — this doc remains planning-only per the BA #2 charter; API,
  Web, and Design implementation planning is the next agents' job once the remaining Open Questions above
  are resolved by the user.

### 2026-08-25 (round 2 — user resolved the last six Open Questions)

- **User resolved all six remaining genuinely-open questions**: the OQ-L creditor-gate amendment (accepted),
  OQ-D's residual trigger-action question (both triggers, one shared code path), OQ-H (within the existing
  closed-event exception), OQ-I (Free-tier), OQ-C (symmetric, capped/idempotent reversal), and OQ-G (no
  audit). Also noted: OQ-E's representation recommendation is accepted as a data-modeling decision, not
  requiring a fresh preference-call sign-off round.
- Recorded all six in **Decision Log entries 5-10** (with rationale), plus an unnumbered "also noted" block
  for OQ-E's representation acceptance.
- Updated **Stories A/B/C/D** to reflect the fully-resolved design (Story A/B rewritten with the amended
  eligibility gate, symmetric reversal, closed-event and no-audit language folded into the Given/When/Then;
  Story C's reversal-flows-through-QR note; Story D's closed-event exception now stated as resolved, not
  open).
- Updated **Business-Rule Impact** items 2 (money exactness — reversal capping), 3 (closed-event immutability
  — resolved), 6 (tier limits — resolved to Free), 7 (audit — resolved to no-audit), and 9 (the OQ-L residual
  under the `OQ1`/`OQ8` conflict — resolved).
- Rewrote **Scope** in/out to be final and non-contingent, folding in every one of the six decisions plus a
  reference to the adopted Milestone 1/2 split.
- Rewrote **Open Questions** section: annotated OQ-C, OQ-D (residual), OQ-G, OQ-H, OQ-I, and OQ-L inline in
  the same `~~OQ-X~~ → Answered` style already used for OQ-A/B/D/F, preserving the original question text for
  the record; updated OQ-A's annotation to reference the OQ-L amendment; updated OQ-E's annotation to note
  the representation is accepted and only naming/copy is deferred (not user-blocking). Confirmed **zero**
  Open Questions remain that genuinely need a user decision.
- Updated **Cross-Functional Workstreams**: API items 2/3/4/7 (four-way classification helper including the
  OQ-L branch, both-triggers Direction 2, no audit schema, no new error codes); Web item 3 and Design item 2
  rewritten to describe the creditor-row affordance as **gross-purity-gated**, not a plain single-sided gate.
- Updated **Tier & Data Implications** to collapse the OQ-G/OQ-I contingencies to their single final answers
  (no audit schema; Free-tier, no gating code).
- Updated **Risks & Sequencing**: the unbounded-write-size risk noted as surfaced-and-accepted; the
  sequencing recommendation rewritten as final (all inputs resolved), with the Milestone 1/Milestone 2 split
  confirmed as adopted.
- Added a new **Handoff Summary** section (before Progress Log) for the orchestrator: finalized Milestone 1
  (Direction 1 alone) and Milestone 2 (Direction 2 + Story C) scope, and an explicit confirmation that the
  Cross-Functional Workstreams sections are accurate against the final design, with the creditor-row UI
  affordance item called out as the one place language needed updating (now gross-purity-gated).
- Did not write any code, migration, or DTO — this doc remains planning-only. It is now ready for
  `feature-planner`, `web-feature-planner`, and `ui-designer` to begin implementation planning per the
  Handoff Summary's milestone split.

## Future Improvements

- **Superseded 2026-08-25, further resolved 2026-08-25 (round 2):** OQ-A was resolved as option (a),
  narrowly scoped, not option (c) — this bullet's original premise no longer applies. The OQ-L residual this
  bullet anticipated has since been characterized and resolved (the creditor gross-purity amendment, Decision
  Log entry 5), so the "once OQ-L is characterized" condition below is satisfied. Keeping the underlying idea
  for the record as a genuine future direction, not a pending dependency: a later iteration could revisit
  whether auto-cascade should extend to the excluded mixed-role-creditor case too (or, conversely, whether a
  read-only "suggested cleared amount" signal per original option (c) is worth adding for non-eligible
  members as a lesser fallback, since they get no auto-cascade under the current decision) once real usage
  data shows how often that case actually occurs.
- Unify the display of all "settled" axes (whole-expense, per-share, per-member-event net, and this sync
  layer) in one coherent UI concept — already flagged as a Future Improvement in the shipped feature,
  reiterated here (OQ-K).
- Extend audit coverage to settlement actions (§6 "Audit mở rộng") if disputes over *payment* timing ever
  become as contentious as disputes over *expenditure* data — currently out of scope (OQ-G).
- Automated debt reminders (§6 "Nhắc nợ") could eventually consume the same partial-clearance figures this
  idea introduces, once they exist.
