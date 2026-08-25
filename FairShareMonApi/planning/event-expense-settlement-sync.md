# Event ↔ Expense Settlement Sync (Two-Way Automatic Cascade) + QR Remaining-Amount Fix

> Downstream of the finalized BA doc `planning/ba/event-expense-settlement-sync-business-analysis.md`
> (BA #1 + BA #2, all 10 Decision Log entries locked 2026-08-25) and the shipped
> `FairShareMonApi/planning/settled-per-member.md` (Layer A per-share settled + Layer B
> per-member-per-event net clearance + `outstanding` overlay, shipped 2026-07-21). This doc plans the
> **API implementation** only, per the BA doc's Handoff Summary, split into the same two milestones the
> BA doc recommends and the user accepted.

## Objective

Implement, on top of the shipped Layer A/Layer B settlement surface:

- **Milestone 1 — Direction 1 (event settle → cascade to expenses).** Marking a member's event-level
  net clearance (Layer B) `Settled = true` automatically cascades to **all** of that member's shares
  across every expense in the event (Layer A), **gated** by an eligibility rule that differs by role: a
  net **debtor** is always eligible; a net **creditor** is eligible only if **gross-pure** (holds no
  debtor-share anywhere else in the event). Fires identically on OPEN and CLOSED events, Free-tier, not
  audited, with symmetric capped/idempotent reversal on un-settle.
- **Milestone 2 — Direction 2 (expense/share settle → partial credit to event balance) + Story C (QR).**
  Marking a whole expense **or** an individual share settled automatically, partially, credits every
  **eligible debtor** member's event-level balance by the settled share's amount — capped at what that
  member actually owes, never negative — via **one shared code path** triggered by both the
  whole-expense toggle and any per-share toggle. Requires a new `EventMemberSettlement.ClearedAmount`
  column and **one shared, canonical balance/eligibility helper** consumed by both the read path
  (`StatsRepository`) and every new write path (`EventMemberSettlementRepository`, `ExpenseRepository`,
  `ShareRepository`) — the BA doc's own explicitly flagged, single-biggest risk (gross/net duplication
  drift) is addressed structurally by this one helper, not by convention alone. The event/expense QR
  then automatically bills the true remaining amount (`WalletQrService` needs **zero** code changes).

None of this changes the raw `advanced`/`owed`/`balance` figures (still sum-to-zero, §3.7/M7 OQ2) — every
new mechanism operates on the derived overlay only (D2, preserved).

## Background

Confirmed against the live code (2026-08-25):

- **Layer A (per-share settled).** `Share.IsSettled`/`SettledAt` (`Database/Entities/Share.cs`). Write
  paths: `ShareRepository.SetSettledAsync` (one share) and `ExpenseRepository.SetSettledAsync`
  (whole-expense, cascades to billable shares). Both call the shared static
  `Repositories/SettlementReconciler.cs`: `IsBillable(share, expense) = Amount > 0 && MemberId !=
  PayerMemberId`; `ReconcileExpense` recomputes `Expense.IsSettled` from billable shares;
  `CascadeToShares` pushes a whole-expense toggle down. Neither path is guarded against a closed event
  (§4.4 sole exception) nor audited (OQ10/OQ11 in the shipped doc).
- **Layer B (per-member-per-event net clearance).** `EventMemberSettlement` (`Database/Entities/
  EventMemberSettlement.cs` + `Partials/EventMemberSettlement.cs`) — composite PK `(event_id,
  member_id)`, `IsSettled`/`SettledAt`, cascade-deleted with the event, member FK restricts. Write path
  `EventMemberSettlementRepository.SetMemberSettledAsync` (`Repositories/
  EventMemberSettlementRepository.cs:51-96`): resource-owns the event, resolves the member as an owned
  **participant** (payer of, or share-holder in, one of the event's expenses; else `MemberNotFound`
  3000), upserts the flag. No closed-event guard, no audit.
- **Balance (§3.7, M7).** `StatsRepository.GetEventBalanceAsync` (`Repositories/StatsRepository.cs:56-114`):
  `advanced` = `SUM(share.Amount)` grouped by `share.Expense.PayerMemberId`; `owed` = the **same**
  share-set's `SUM(share.Amount)` grouped by `share.MemberId` — so `Σ balance == 0` by construction. This
  same method additively loads the Layer B flags (`EventMemberSettlement`) keyed by `member_id`, purely
  for display — it does **not** feed back into advanced/owed. `StatsService.GetEventBalanceAsync`
  computes the overlay: `Outstanding = (Balance < 0 && !IsSettled) ? -Balance : 0`.
  `MemberBalanceRow`/`EventBalanceResponse` carry the overlay fields (`Outstanding`, `IsSettled`,
  `SettledAt`, `TotalOutstanding`, `OwingMemberCount`, `SettledMemberCount`).
- **QR (§3.10).** `WalletQrService.GenerateEventQrAsync`/`GenerateEventMemberQrsAsync`/
  `GenerateEventMemberQrsForShareAsync` all reuse `IStatsService.GetEventBalanceAsync` and bill via the
  private `CollectEventBillables` helper: `balance.Rows.Where(row => row.Outstanding > 0m)`. The expense
  QR (`CollectExpenseBillables`) bills unsettled **gross** shares directly (`!share.IsSettled &&
  share.Amount > 0 && share.Member.Uuid != expense.Payer.Uuid`) — untouched by this feature (Layer A was
  already exact for its own purpose).
- **Transactions/DI.** `BaseRepository.ExecuteTransactionAsync`/`ExecuteQueryAsync`/`Query<T>` (`Repositories/
  Abstractions/BaseRepository.cs`). `AppDbContext` is registered via `AddDbContextPool<AppDbContext>`
  (`Program.cs:144`) — **Scoped** lifetime (pooling only affects instantiation/reset, not lifetime), so
  every repository resolved within one request/DI-scope shares the **same** `AppDbContext` instance.
  This is the mechanism that lets the new shared helper (below) be called from `ExpenseRepository`/
  `ShareRepository`/`EventMemberSettlementRepository`'s own transactions without violating the
  repo-convention ("repositories don't call other repositories") — it takes `AppDbContext` directly and
  runs its own query, exactly mirroring how `SettlementReconciler` is a pure static helper today.
- **Error codes.** `Constants/ErrorCodes.cs`: 15xxx reserved by `settled-per-member.md` (unclaimed),
  16xxx claimed by `event-share-link.md`. **17xxx is the next free block.**
- **Message keys.** `Success.MemberSettledUpdated` / `Success.ShareSettledUpdated` /
  `Success.ExpenseSettledUpdated` already exist and are reused (no new user-facing strings are
  anticipated — the response envelope stays a plain success message, see Open Questions).

## Requirements

From the BA doc's finalized Decision Log (entries 1-10, all binding, not reopened here) and Scope section:

- **Milestone 1:**
  - Direction 1 fires automatically (no confirmation step, OQ-F) whenever the owner sets a member's
    event-level `Settled = true`, **gated**: net debtor → always eligible; net creditor → eligible only
    if gross-pure (no debtor-share anywhere else in the event, OQ-L amendment/Decision Log 5); every
    other member → no cascade, existing manual toggling remains (OQ-A, Decision Log 1).
  - When it fires, it cascades to **all** of the eligible member's shares in the event, not owing-only
    (OQ-B, Decision Log 2) — the payer's own share(s) are already settled-by-definition no-ops (OQ6a,
    inherited, unchanged).
  - Symmetric, capped/idempotent reversal on un-settle (OQ-C, Decision Log 9).
  - Fires identically on OPEN and CLOSED events (OQ-H, Decision Log 7); Free-tier, no new gate (OQ-I,
    Decision Log 8); not audited (OQ-G, Decision Log 10).
  - Soft-deleted participants remain targetable (§4.7, inherited).
  - Never touches another member's `Share`/`EventMemberSettlement` row (OQ-J, confirmed no conflict).
- **Milestone 2:**
  - Both the whole-expense settled toggle **and** any individual per-share toggle fire Direction 2, via
    one shared code path (OQ-D residual, Decision Log 6).
  - For every debtor member on the now-settled share/expense who is single-sided (net debtor), credit
    their event-level `ClearedAmount` by exactly the share's amount — capped at their net owed amount,
    never negative (Decision Log 3, 5's self-protection note, Business-Rule Impact item 2).
  - A net creditor/mixed-balance member is never credited (self-protecting via the existing `Outstanding`
    floor-at-zero — no extra gate needed for Direction 2, Decision Log 5).
  - Reaching full net owed via cumulative `ClearedAmount` auto-transitions the member to fully `Settled`
    (OQ-E representation).
  - Symmetric, capped/idempotent reversal: un-settling a contributing share claws back its credit,
    floored so it never drives `ClearedAmount` below 0 or above the CURRENT net owed amount (OQ-C,
    Decision Log 9).
  - New `EventMemberSettlement.ClearedAmount` (`decimal(18,2) NOT NULL DEFAULT 0`, DB CHECK `>= 0`), one
    EF migration (OQ-E representation, Decision Log's "also noted" entry).
  - `StatsService.GetEventBalanceAsync` overlay math changes to derive `Outstanding` from
    `max(0, NetOwed - ClearedAmount)`; `WalletQrService` needs **zero** changes (Story C).
  - The **same** Free-tier / not-audited / closed-event-allowed decisions apply (not re-litigated).
- **Cross-cutting (both milestones):**
  - Raw `advanced`/`owed`/`balance` byte-for-byte unchanged and still sum to zero (D2/M7 OQ2, hard
    invariant — regression-tested every time).
  - One canonical balance/eligibility helper — `StatsRepository` (read) and every write path must
    consume it; no second, independently-maintained gross/net computation anywhere (BA doc's
    single-biggest-risk finding).
  - All writes remain single `ExecuteTransactionAsync` blocks (§4.5 atomicity) — a cascade/credit step
    failing must not leave Layer A and Layer B inconsistent.

## Open Questions

> The BA doc resolved all ten of its own Open Questions before handoff — **none of those are reopened
> here.** The items below are implementation-level ambiguities/preference calls surfaced while turning
> the BA doc's decisions into concrete endpoints/DTOs/write-path behavior; the BA doc did not specify
> them precisely enough to plan against with full confidence, and each has more than one defensible
> answer.
>
> **Updated 2026-08-25 (checkpoint) — ZERO Open Questions remain.** All five items below (OQ1-OQ5) were
> put to the user and answered at the same checkpoint; each is annotated inline in the same
> `~~OQ-X~~ → Answered` style the BA doc used, with the original question text preserved for the record.
> See the Decision Log below for the binding answer + rationale on each. This doc is now ready to hand to
> `web-feature-planner` and `ui-designer`.

**OQ1 — Direction 1 reversal: recompute live, or gate the reversal on current eligibility?**
> ~~OQ1~~ → **Answered 2026-08-25: option (a) — unconditional, recomputed live.** On `Settled = false`,
> Direction 1's share reversal recomputes "all of the member's shares in the event" against **current**
> data (the same query as the forward cascade) and un-settles them all, regardless of whether the
> member's eligibility classification has changed since the original settle. Matches the literal
> "symmetric" framing most directly, and keeps Direction 1's reversal mechanics consistent with how
> Direction 2's own reversal already works (also recomputed off current data, no provenance tracking).
> See Decision Log entry 1.

When the owner sets a member's event-level flag back to `false`, Story A requires the shares Direction 1
cascaded to be un-settled "in the same transaction," floored/capped so it "never drives M below what M's
CURRENT balance implies is still owed" (OQ-C). For Direction 1's **boolean** per-share flags (no
`ClearedAmount` to floor against in Milestone 1), this "floor" doesn't have an obvious literal reading.
Two options:
- **(a) [recommended] Unconditional, recomputed live.** On `Settled = false`, recompute "all of the
  member's shares in the event" against **current** data (same query as the forward cascade) and set
  them all `IsSettled = false`/`SettledAt = null`, then reconcile affected expenses — regardless of
  whether the member is still eligible today. Matches the literal "symmetric" framing most directly;
  Direction 2's own floor (Milestone 2) works the same way (off current data, no provenance tracking), so
  this stays consistent across both directions.
- **(b) Eligibility-gated at reversal time.** Recompute the member's CURRENT eligibility; only perform
  the share reversal if they are still eligible today. If their gross/net facts changed since the settle
  (e.g., a new expense made a former gross-pure creditor gross-mixed), skip the share reversal entirely —
  only the Layer B flag itself flips off, leaving the previously-cascaded shares settled.
- Trade-off: (a) is simpler and more literally symmetric but can un-settle a share purely because the
  member's aggregate classification changed, not because the owner touched that share; (b) is more
  conservative but means "un-settle" sometimes silently does less than the owner might expect.

**OQ2 — Should Milestone 2 also make `EventMemberSettlementRepository`'s existing write path snapshot
`ClearedAmount`, unifying it as the single source of truth with the already-shipped `IsSettled` flag?**
> ~~OQ2~~ → **Answered 2026-08-25: option (a) — unify `ClearedAmount` as the sole source of truth.** The
> already-shipped manual event-level settle path (`EventMemberSettlementRepository.SetMemberSettledAsync`)
> must also snapshot `ClearedAmount = NetOwed` (on `true`) / `0` (on `false`); `IsSettled` stays consistent
> with `ClearedAmount ≥ NetOwed`, written by whichever path last touched it. Explicit, accepted
> consequence: a later per-share reversal (Direction 2) can partially claw back credit even from a member
> who was fully settled via a manual Direction-1 assertion, since both directions now share one number —
> this is a known, deliberate trade-off, not an oversight. See Decision Log entry 2.

Once Milestone 2 changes `Outstanding` to derive from `ClearedAmount` (not `IsSettled` directly), the
**already-shipped** Direction-1/manual Layer-B write path (`EventMemberSettlementRepository.
SetMemberSettledAsync`) must be touched again or the two fields can silently disagree. Two designs:
- **(a) [recommended] Unify: `ClearedAmount` is the sole source of truth.** Every manual Layer B toggle
  (Direction 1, or a plain manual mark with no cascade) also snapshots `ClearedAmount = NetOwed` (on
  `true`) or `0` (on `false`); `IsSettled` is then always kept consistent with `ClearedAmount ≥ NetOwed`,
  written by whichever path last touched it. Avoids the exact "two independently-maintained facts that
  can drift" risk the BA doc calls the single biggest implementation risk. **Accepted, explicit
  consequence:** a later per-share reversal (Direction 2) can partially claw back credit even from a
  member who was fully settled via a manual Direction-1 assertion, since both directions now share one
  number.
- **(b) Keep two independent signals, OR'd.** `Outstanding = IsSettled ? 0 : max(0, NetOwed -
  ClearedAmount)`. A manual full-settle (Direction 1) never touches `ClearedAmount` and is therefore
  immune to any later per-share reversal; only Direction 2's own accumulated credits are ever reversible.
  Simpler (no change needed to the Milestone-1-shipped repository), but re-introduces exactly the kind of
  "two things that can independently claim a member is settled" duplication the BA doc's risk section
  warns against, just scoped to one boolean-OR rather than a full derivation.

**OQ3 — Toggle-endpoint response shape (the BA doc's own explicit, undecided ask, Cross-Functional API
item 6).**
> ~~OQ3~~ → **Answered 2026-08-25: option (a) — keep the existing plain `ApiResult` success-message
> shape, unchanged, for all three settled-toggle routes.** No cascade/credit-count fields are added to any
> of the three response DTOs. Consistent with every other settled toggle and write endpoint in this
> codebase; the BA doc's own Web workstream plan already assumes the client refetches/invalidates the
> relevant caches rather than reading side-effect counts off the response. See Decision Log entry 3.

Today all three settled toggles (`PUT .../expenses/{uuid}/settled`, `.../shares/{shareUuid}/settled`,
`.../events/{eventUuid}/members/{memberUuid}/settled`) return only a plain `ApiResult` success message
(`Success.*Updated`). Once a single toggle can silently move many other rows (Direction 1: N shares
across M expenses; Direction 2: N debtor members' event balances), should the response say anything about
that blast radius?
- **(a) [recommended] Keep the existing plain success-message shape**, unchanged for all three routes.
  Consistent with every other settled toggle and every other write endpoint in this codebase (no
  precedent anywhere for a toggle reporting side-effect counts); the BA doc's own Web workstream plan
  (item 2) already assumes the client refetches/invalidates the relevant caches rather than reading
  counts off the response.
- **(b) Extend the response** with cascade/credit counts (e.g. `sharesSettledCount` +
  `expensesAffectedCount` for Direction 1; `membersCreditedCount` + per-member `clearedAmount` deltas for
  Direction 2), giving the owner immediate feedback without a second read. Unprecedented response shape
  in this codebase for a toggle endpoint; larger DTO surface for a first version of this feature.

**OQ4 — Should the balance overlay expose the Direction-1 eligibility fact itself, so the web UI can
correctly render the OQ-L-amended creditor-row affordance?**
> ~~OQ4~~ → **Answered 2026-08-25: option (a) — expose the Direction-1 eligibility fact additively.** A
> new `bool IsEligibleForAutoCascade` field is added to `MemberBalanceRow`, derived from the same
> canonical `EventSettlementClassifier` helper Direction 1 itself gates on. Avoids web/design having to
> reimplement the gross-purity classification client-side, which would duplicate the exact logic this
> whole feature exists to keep in one place. See Decision Log entry 4.

The BA doc's own Web item 3 / Design item 2 flag that a creditor row's real eligibility is now
gross-purity-gated, not a plain "is this a creditor" check, and that the UI "needs to either surface this
distinction... or always show the toggle but make the fallback-to-manual outcome legible." Web/Design
cannot make that call without knowing, per row, whether the gross-purity check passed.
- **(a) [recommended] Expose it additively on `MemberBalanceRow`** — e.g. a `bool
  IsEligibleForAutoCascade` field (derived from the same canonical helper Direction 1 itself gates on).
  Avoids web/design having to reimplement the gross-purity classification client-side, which would
  duplicate the exact logic this whole feature is trying to keep in one place.
- **(b) Keep it a purely server-internal gate**, with no client-visible signal. Web/Design would have to
  either show the toggle unconditionally for every `Balance > 0` row (with no visible distinction until
  the owner tries it and nothing happens) or infer eligibility indirectly some other way.

**OQ5 — Field/enum naming for the new partial-clearance concept (non-blocking per OQ-E, listed for
sign-off, not a design ambiguity).**
> ~~OQ5~~ → **Answered 2026-08-25: accepted the proposed naming as-is.** `EventMemberSettlement.
> ClearedAmount` (English field, stored, matches the BA doc's own recommended name verbatim); a new
> **service-computed** (not stored) `enum EventSettlementStatus { Unsettled, PartiallySettled, Settled }`
> exposed as `MemberBalanceRow.SettlementStatus`; Vietnamese copy "số tiền đã tất toán" (cleared amount) /
> "chưa trả" (unsettled) / "đã trả một phần" (partially settled) / "đã trả" (fully settled). No further
> naming round needed. See Decision Log entry 5.

Per the BA doc's OQ-E, the **representation** (a `ClearedAmount` column + a service-derived status, no
new stored enum) is already accepted; only the literal names/copy remain open, and BA #1's own framing is
that this needs explicit owner sign-off before use in a shipped contract (`The-ideal.md` §5 fixed-terms
convention). Proposed for confirmation, not silently finalized:
- `EventMemberSettlement.ClearedAmount` (English) — matches the BA doc's own recommended name verbatim.
- A new **service-computed** (not stored) tri-state, e.g. `enum EventSettlementStatus { Unsettled,
  PartiallySettled, Settled }`, exposed as `MemberBalanceRow.SettlementStatus`, computed in
  `StatsService` alongside `Outstanding` from `ClearedAmount`/`NetOwed`/`IsSettled` — never stored.
- Vietnamese copy for Swagger/XML docs: "số tiền đã tất toán" (cleared amount), "đã trả một phần" /
  "chưa trả" / "đã trả" (partially/un/fully settled) — matching the candidate terms the BA doc itself
  already floated (§ Terminology) without finalizing them.

## Assumptions

- **"Mixed" (the fourth, ineligible classification bucket) = `Balance == 0` exactly.** The BA doc's
  Decision Log entry 1 defines "single-sided" as "purely a net debtor **or** purely a net creditor... per
  the existing `advanced − owed` balance" and entry 5's own algebra shows this is a statement about the
  **sign** of `Balance`, not gross purity (a single-sided-by-net-balance member can still hold both
  gross roles). The only value of `Balance` not covered by "purely a net debtor" (`< 0`) or "purely a net
  creditor" (`> 0`) is exactly `0` — a member whose gross advances and gross owed happen to net to zero.
  This is the literal, unambiguous residual of the locked wording, not a fresh design choice, so it is
  recorded here as an assumption rather than a new Open Question.
- `EventMemberSettlement.ClearedAmount` is always clamped to `[0, NetOwed]` at every write (both
  directions) — this is the direct, unambiguous reading of Business-Rule Impact item 2's "non-negative...
  never allowed to exceed what that member actually owes."
- `SettledAt` mirrors `IsSettled`'s transitions for `EventMemberSettlement` exactly as it already does for
  `Share`/`Expense`: stamped `AppDateTime.Now` when the stored `IsSettled` flips to `true`, cleared to
  `null` when it flips to `false` — including when Direction 2's cumulative credit crosses the
  full/partial boundary in either direction.
- Direction 2 never applies to a **loose** expense (`Expense.EventId == null`) — there is no
  `EventMemberSettlement` row to credit without an event, consistent with the already-locked
  `settled-per-member.md` OQ14a ("loose expenses get Layer A only, no Layer B rollup"). This is not a
  fresh decision; it falls directly out of Layer B's existing, unchanged scope.
- `AppDbContext` is Scoped (confirmed: `AddDbContextPool<AppDbContext>` in `Program.cs`, pooling does not
  change the DI lifetime) — every repository resolved in one request shares the same instance, which is
  what makes the new shared helper (a plain static class taking `AppDbContext`) safe to call from
  multiple repositories' own transactions without violating the "repositories don't call other
  repositories" convention.
- No new tier gate, no new audit, no new error codes are introduced by either milestone (OQ-I/OQ-G/API
  item 7, all locked) — 17xxx is reserved with a comment only, exactly as 15xxx/16xxx were for their
  respective features, with no codes defined.
- No new user-facing message keys are needed — the response envelope for all three settled toggles stays
  a plain success message reusing the existing `Success.MemberSettledUpdated`/`ShareSettledUpdated`/
  `ExpenseSettledUpdated` keys (confirmed, OQ3 resolved to option (a) — no response-shape change at all,
  so no NEW message key is needed).

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. Milestone 1 is fully shippable and testable
> before Milestone 2 starts (per the BA doc's sequencing recommendation) — Milestone 2's steps assume
> Milestone 1 is already merged. Steps that modify shipped files are marked **[MOD]**.

### Milestone 1 — Direction 1 (event settle → cascade to expenses)

**Step M1.1 — Shared classification helper (new, foundational for both milestones).**

- **NEW** `Repositories/EventSettlementClassifier.cs`:
  - `public enum MemberSettlementEligibility { NetZero, NetDebtor, NetCreditorGrossPure,
    NetCreditorGrossMixed }` — the four-way classification the BA doc's API item 2 calls for.
  - `public sealed record MemberSettlementFacts(ulong MemberId, decimal Advanced, decimal Owed, bool
    HasDebtorShareElsewhereInEvent, MemberSettlementEligibility Eligibility)` with computed
    `Balance => Advanced - Owed`, `NetOwed => Balance < 0m ? -Balance : 0m`, and
    `IsEligibleForDirection1Cascade => Eligibility is NetDebtor or NetCreditorGrossPure`.
  - A **pure, DB-free** classification function (unit-testable in isolation):
    `public static MemberSettlementEligibility Classify(decimal advanced, decimal owed, bool
    hasDebtorShareElsewhere)` — the `switch` on `advanced - owed` described in Requirements.
  - `public static async Task<IReadOnlyDictionary<ulong, MemberSettlementFacts>> ClassifyAsync(
    AppDbContext dbContext, ulong eventId, IReadOnlyCollection<ulong>? restrictToMemberIds,
    CancellationToken cancellationToken)` — runs the **exact same** `GroupBy`/`Sum` shape
    `StatsRepository.GetEventBalanceAsync` runs today (advanced grouped by
    `share.Expense.PayerMemberId`, owed grouped by `share.MemberId`, over `Query<Share>().Where(share =>
    share.Expense.EventId == eventId)`), plus one more query for
    `HasDebtorShareElsewhereInEvent` (`shares.Where(SettlementReconciler.IsBillable-equivalent).Select(s
    => s.MemberId).Distinct()`), then calls the pure `Classify` helper per member. `restrictToMemberIds`
    lets a caller (Direction 1's single-member check; Direction 2's per-expense debtor set) avoid
    building facts for the whole event when only a few members are relevant, without changing the
    underlying aggregate query (still computed over the full event share-set, so `Σ balance == 0` is
    preserved).
- **[MOD]** `Repositories/StatsRepository.cs` `GetEventBalanceAsync` — replace the inline
  `advancedByPayer`/`owedByMember` `GroupBy` blocks with one call to
  `EventSettlementClassifier.ClassifyAsync(dbContext, eventId, restrictToMemberIds: null, ct)`; keep the
  existing member-display-info join and the existing Layer B (`IsSettled`/`SettledAt`) load unchanged.
  This is the concrete step that makes `StatsRepository` a **consumer** of the same canonical helper the
  new write paths use, closing the BA doc's single-biggest-risk gap. **Byte-for-byte regression
  requirement:** every existing `StatsRepositoryTests`/`StatsServiceTests` assertion must still pass
  unchanged after this refactor — it is a pure extraction, not a behavior change.

**Step M1.2 — Direction 1 cascade + reversal.**

- **[MOD]** `Repositories/EventMemberSettlementRepository.cs` `SetMemberSettledAsync` — inside the
  existing transaction, after resolving + upserting the `(event_id, member_id)` flag (unchanged
  behavior: the flag write **always** succeeds for any participant, eligible or not):
  - Call `EventSettlementClassifier.ClassifyAsync(db, evt.Id, [member.Id], cancellationToken)` to get
    `facts` for this one member (needed for the eligibility gate; NOT needed for `ClearedAmount` in this
    milestone — see Milestone 2 Step M2.4 for that follow-on).
  - **On `isSettled == true`:** if `facts.IsEligibleForDirection1Cascade`, load every `Expense` in the
    event where the member holds a share, tracked with its shares:
    `Query<Expense>(tracking: true).Where(e => e.EventId == evt.Id && e.Shares.Any(s => s.MemberId ==
    member.Id)).Include(e => e.Shares)`. For each such expense, set the member's own share(s)
    `IsSettled = true`, `SettledAt = now` (the unique `(expense_id, member_id)` index means exactly one
    share per expense), then call `SettlementReconciler.ReconcileExpense(expense)`. If not eligible: no
    share writes at all — silent fallback to manual toggling (OQ-A/OQ-L).
  - **On `isSettled == false`:** per **OQ1**'s confirmed resolution (option (a), unconditional):
    recompute `facts` against current data, then — **regardless of current eligibility** — load the same
    expense/share set and set `IsSettled = false`, `SettledAt = null` on the member's shares, then
    reconcile each affected expense. The reversal is never gated on whether the member is still eligible
    today; it always un-settles the full "all of the member's shares in the event" set recomputed live.
  - No `EventWriteGuard` call (unchanged — the existing §4.4 exception). No audit (unchanged).
- Update the XML doc comment on `IEventMemberSettlementRepository.SetMemberSettledAsync` and the
  Swagger summary on `EventsController.SetMemberSettledAsync` (Vietnamese) to document the new
  auto-cascade side effect and its eligibility gate.

**Step M1.3 — Response shape (confirmed OQ3, option (a)).**

- **No DTO changes** — `EventsController.SetMemberSettledAsync` keeps returning
  `ApiResult.SuccessMessage(localizer[MessageKeys.Success.MemberSettledUpdated].Value)` unchanged. The
  same applies to the other two settled-toggle routes (`PUT .../expenses/{uuid}/settled`,
  `.../shares/{shareUuid}/settled`) — no cascade/credit-count fields are added to any of the three
  responses.

**Step M1.4 — Eligibility exposure (confirmed OQ4, option (a)).**

- Add `bool IsEligibleForAutoCascade` to `Repositories/Stats/StatsAggregates.cs`'s
  `MemberBalanceAggregate` and `Models/Stats/MemberBalanceRow.cs`, populated from
  `EventSettlementClassifier`'s facts inside `StatsRepository.GetEventBalanceAsync` (already computing
  facts for every member per Step M1.1) and mapped through `Mappings/StatsProfile.cs`.

**Step M1.5 — Tests (test-engineer; definitive list for this milestone).**

Unit (no DB):
- `EventSettlementClassifierTests` — the pure `Classify(advanced, owed, hasDebtorShareElsewhere)`
  function: `advanced==owed` → `NetZero`; `advanced<owed` → `NetDebtor` regardless of
  `hasDebtorShareElsewhere`; `advanced>owed` + no debtor-share-elsewhere → `NetCreditorGrossPure`;
  `advanced>owed` + has debtor-share-elsewhere → `NetCreditorGrossMixed`. `IsEligibleForDirection1Cascade`
  true only for `NetDebtor`/`NetCreditorGrossPure`.

Integration (real MariaDB, `[SkippableFact]`, extends the existing `StatsRepositoryTests`/
`EventMemberSettlementRepositoryTests` fixtures):
- `StatsRepository.GetEventBalanceAsync` post-refactor: existing §3.7 Bình/Cường scenario and every
  currently-passing assertion unchanged (byte-for-byte regression on the `EventSettlementClassifier`
  extraction).
- New `EventSettlementCascadeRepositoryTests`:
  - Net debtor (single-sided) → settling the event flag cascades **all** their shares in the event to
    settled (including their own payer-own shares as harmless no-ops), reconciles every affected
    expense's whole-flag; balance figures byte-for-byte unchanged.
  - Net creditor, gross-pure (no debtor-share elsewhere) → same cascade fires.
  - Net creditor, gross-mixed (the exact OQ-L worked example: expense X they pay with a payer-own share,
    expense Y where they hold a genuine debtor-share) → event flag flips true, but **no share is
    touched** — the debtor share on Y stays exactly as it was.
  - `NetZero` member (advanced == owed) → event flag flips true, no cascade.
  - Cascade fires identically on a CLOSED event (OQ-H).
  - No audit row is written by the cascade.
  - Cross-member isolation: cascading M's shares never touches another member N's share amount/flag in
    the same expense (OQ-J regression).
  - Soft-deleted participant still cascade-eligible (§4.7).
  - Reversal (OQ1, confirmed option (a)): un-settling the event flag always un-settles the same "all
    shares" set recomputed live, regardless of whether the member's eligibility classification changed
    since the original settle (e.g., a gross-pure creditor who later picked up a debtor-share elsewhere
    still has their originally-cascaded shares reversed).

Endpoint (`WebApplicationFactory`, `[SkippableFact]`):
- `PUT /events/{eventUuid}/members/{memberUuid}/settled` for an eligible debtor → a subsequent
  `GET /expenses/{uuid}` for each of their expenses in the event shows `IsSettled: true` on their share.
- Same route for a gross-mixed creditor → their shares are unaffected; only the balance overlay's
  `IsSettled` for that member changes.
- Un-settle round-trips per OQ1's confirmed option (a): unconditional, recomputed live.

### Milestone 2 — Direction 2 (expense/share settle → partial credit) + Story C (QR)

**Step M2.1 — Entity + migration.**

- **[MOD]** `Database/Entities/EventMemberSettlement.cs` — add
  `public decimal ClearedAmount { get; set; }` with an XML doc explaining it is the cumulative amount
  credited via Direction 2, capped at the member's net owed amount, and that it drives `IsSettled`
  alongside any manual Direction-1 assertion — the sole source of truth per OQ2's confirmed option (a).
- **[MOD]** `Database/Entities/Partials/EventMemberSettlement.cs` — map `cleared_amount` as
  `decimal(18,2)`, `HasDefaultValue(0m)`; add the CHECK constraint via the table builder lambda (mirrors
  `Share`'s `ck_shares_amount_non_negative` pattern):
  `entity.ToTable("event_member_settlements", table => table.HasCheckConstraint(
  "ck_event_member_settlements_cleared_amount_non_negative", "cleared_amount >= 0"));`
- **Migration:** `dotnet ef migrations add AddEventMemberSettlementClearedAmount --project
  .\FairShareMonApi\FairShareMonApi.csproj`. One ALTER (`ADD COLUMN cleared_amount DECIMAL(18,2) NOT NULL
  DEFAULT 0` + the CHECK constraint). **No data backfill** — default `0` is correct for every existing
  row (no fabricated credit history, mirrors the shipped feature's own OQ4a philosophy for Layer B).

**Step M2.2 — Shared credit-step helper (the "one shared code path" OQ-D residual calls for).**

- **NEW** `Repositories/EventSettlementCreditApplier.cs` (static, mirrors `SettlementReconciler`'s
  shape):
  - `public static async Task ApplyAsync(AppDbContext db, ulong eventId,
    IReadOnlyList<(ulong MemberId, decimal Delta)> deltas, DateTime now, CancellationToken
    cancellationToken)` — `Delta` is `+share.Amount` on settle, `-share.Amount` on unsettle.
  - Batches: calls `EventSettlementClassifier.ClassifyAsync(db, eventId,
    deltas.Select(d => d.MemberId).Distinct().ToList(), cancellationToken)` once for every affected
    member, and loads/creates their `EventMemberSettlement` rows in one query.
  - Per member: `newCleared = Math.Clamp(existing.ClearedAmount + delta, 0m, facts.NetOwed)` (a creditor
    or `NetZero` member has `NetOwed == 0`, so `Math.Clamp(x, 0, 0) == 0` — self-protecting, no separate
    eligibility branch needed, per Decision Log 5's own finding); `fullySettled = facts.NetOwed > 0m &&
    newCleared >= facts.NetOwed`; if `fullySettled != existing.IsSettled`, also update `IsSettled` +
    `SettledAt` (stamped/cleared per the Assumptions section). No audit.

**Step M2.3 — Wire the credit step into both trigger points.**

- **[MOD]** `Repositories/ShareRepository.cs` `SetSettledAsync` — after loading the expense + share:
  capture `wasSettled = share.IsSettled` **before** mutating; set the flag; call
  `SettlementReconciler.ReconcileExpense`; then, if `expense.EventId is { } eventId &&
  SettlementReconciler.IsBillable(share, expense) && wasSettled != isSettled`, call
  `EventSettlementCreditApplier.ApplyAsync(db, eventId, [(share.MemberId, isSettled ? share.Amount :
  -share.Amount)], now, cancellationToken)`.
- **[MOD]** `Repositories/ExpenseRepository.cs` `SetSettledAsync` — before calling
  `SettlementReconciler.CascadeToShares`, snapshot each billable share's prior `IsSettled`; after the
  cascade, build the `deltas` list from every billable share whose flag actually flipped (`wasSettled !=
  isSettled`), amount signed per direction; if `expense.EventId is { } eventId && deltas.Count > 0`, call
  the same `EventSettlementCreditApplier.ApplyAsync`. This is the literal "one shared code path" (OQ-D
  residual, Decision Log 6) — both write paths call the identical static method.
- Idempotency is structural: a share whose flag does **not** change contributes no delta, so re-settling
  an already-settled share (or re-unsettling an already-unsettled one) never double-credits/double-claws.
- Neither path touches `EventWriteGuard` or audit (unchanged, inherited exceptions).

**Step M2.4 — Reconcile Direction 1's write path (confirmed OQ2, option (a)).**

- **[MOD]** `Repositories/EventMemberSettlementRepository.cs` `SetMemberSettledAsync` — after the
  existing upsert of `IsSettled`, also set `settlement.ClearedAmount = isSettled ? facts.NetOwed : 0m`
  (using the `facts` already computed in Step M1.2 for the eligibility gate) — **unconditionally**, not
  gated by eligibility, since the underlying Layer B assertion itself has never been eligibility-gated
  (only the share cascade is). This is the concrete step that unifies `ClearedAmount` as the sole source
  of truth: every manual Layer B toggle now snapshots it, so `IsSettled` and `ClearedAmount` can never
  silently drift apart. Accepted consequence (per OQ2's rationale): a later per-share Direction-2
  reversal can partially claw back credit even from a member who was fully settled via this manual
  Direction-1 path.

**Step M2.5 — Overlay math + DTOs.**

- **[MOD]** `Repositories/Stats/StatsAggregates.cs` — `MemberBalanceAggregate` gains
  `decimal ClearedAmount`.
- **[MOD]** `Repositories/StatsRepository.cs` `GetEventBalanceAsync` — the existing
  `Query<EventMemberSettlement>()` load (already fetching `IsSettled`/`SettledAt`) additionally selects
  `ClearedAmount`; default `0m` for a participant with no settlement row (unchanged pattern).
- **[MOD]** `Models/Stats/MemberBalanceRow.cs` — add `decimal ClearedAmount` and (per OQ5's confirmed
  naming, **retyped per OQ-WF**) `string SettlementStatus` — backed by a new small **internal, service-only**
  enum (`Models/Stats/EventSettlementStatus.cs`: `Unsettled, PartiallySettled, Settled`), **service-computed**,
  not stored (OQ-E representation, OQ5 naming), and **never the enum type itself on the DTO** (OQ-WF: no
  `JsonStringEnumConverter` is registered in `Program.cs`, so an `EventSettlementStatus`-typed property
  would serialize as a raw integer `0|1|2`; `string` matches the established `UserResponse.Tier`/`Role`
  convention of typing status/category fields as `string` at the DTO level — see Decision Log entry 6).
- **[MOD]** `Services/Api/Stats/StatsService.cs` `GetEventBalanceAsync` — change the per-row overlay
  computation from `Outstanding = (Balance < 0 && !IsSettled) ? -Balance : 0` to:
  `var netOwed = row.Balance < 0m ? -row.Balance : 0m; row.Outstanding = Math.Max(0m, netOwed -
  row.ClearedAmount); var status = netOwed <= 0m ? EventSettlementStatus.Unsettled /* n/a, not owing */ :
  row.Outstanding <= 0m ? EventSettlementStatus.Settled : row.ClearedAmount > 0m ?
  EventSettlementStatus.PartiallySettled : EventSettlementStatus.Unsettled; row.SettlementStatus =
  status.ToString();` — final boundary semantics, given OQ2/OQ5's confirmed answers (`ClearedAmount` is
  the sole source of truth `IsSettled` is derived from) and OQ-WF's confirmed string wire type. Also add
  `int PartiallySettledMemberCount` to `EventBalanceResponse`
  (`count(row.SettlementStatus == EventSettlementStatus.PartiallySettled.ToString())`) — a direct,
  low-risk continuation of the existing `OwingMemberCount`/`SettledMemberCount` rollup idiom.
- **[MOD]** `Mappings/StatsProfile.cs` — map `ClearedAmount` through; `Outstanding`/`SettlementStatus`
  stay `Ignore()`'d (computed once in the service, unchanged pattern).
- **No changes** to `Services/Api/Wallet/WalletQrService.cs` — confirmed additive-only; the existing
  `row.Outstanding > 0m` filter in `CollectEventBillables` already flows the new partial math through
  correctly (Story C).

**Step M2.6 — Tests (test-engineer; definitive list for this milestone).**

Unit (no DB):
- `EventSettlementClassifierTests` — extend with `NetOwed`/clamp-adjacent pure helpers if factored out
  (e.g. a pure `Clamp(existing, delta, netOwed)` extracted from `EventSettlementCreditApplier` for direct
  unit coverage).
- `StatsServiceTests` — `Outstanding = max(0, netOwed - clearedAmount)`; `SettlementStatus` transitions
  (`Unsettled → PartiallySettled → Settled` as `ClearedAmount` rises to `NetOwed`, and back on reversal);
  `advanced`/`owed`/`balance` still byte-for-byte unchanged (D2 regression, extended).
- `WalletQrServiceTests` — a fake balance row with `ClearedAmount` between `0` and `NetOwed` bills exactly
  `Outstanding`; reaching full clearance drops the member from the composite exactly as today.

Integration (real MariaDB, `[SkippableFact]`), new `EventSettlementCreditRepositoryTests`:
- Whole-expense settle credits **every** eligible debtor member on the expense simultaneously, each
  capped at their own net owed amount; a creditor/mixed member on the same expense gets zero credit
  (self-protecting, Decision Log 5's own corollary).
- A lone per-share settle (no whole-expense toggle) credits identically to the whole-expense path for an
  equivalent single-share scenario — the "one shared code path" cross-trigger consistency check (OQ-D
  residual regression).
- Idempotency: re-settling an already-settled share does not double-credit; re-unsettling an
  already-unsettled share does not double-claw.
- Cumulative credit across multiple expenses reaching exactly `NetOwed` auto-transitions `IsSettled` to
  `true`, `SettledAt` stamped; a further debtor-share settle for the same (now fully credited) member
  still flips that share's own Layer A flag true (unconditional, per OQ6a/OQ-D) but contributes zero
  further credit — the explicit OQ-L "corollary" fixture the BA doc calls out.
- Reversal: un-settling a contributing share claws back exactly its amount, floored at `0`, and re-capped
  at the member's CURRENT net owed if the event's shares changed since the credit was applied (the
  open-event drift fixture the BA doc explicitly asks for).
- Direction 2 never applies to a loose expense (`EventId == null`) — no `EventMemberSettlement` row is
  created/touched.
- Money-exactness regression: raw `advanced`/`owed`/`balance` byte-for-byte unchanged by any Direction-2
  write.
- No audit row is written by the credit step.
- Migration regression: an existing `event_member_settlements` row (from before this migration) reads
  `ClearedAmount == 0` and computes the same `Outstanding` as it did under the old boolean-only formula
  for a member with `IsSettled == false`.
- Per OQ2's confirmed option (a): a manual Direction-1 full-settle followed by an unrelated per-share
  Direction-2 reversal partially claws back the member's cleared amount as designed (explicit regression
  for the accepted cross-direction consequence).

Endpoint (`WebApplicationFactory`, `[SkippableFact]`):
- `GET /events/{uuid}/balance` after a partial per-share settle reflects the correct `ClearedAmount`/
  `Outstanding`/`SettlementStatus`.
- Event QR (closed event): after a partial credit, the generated QR bills exactly the remaining amount
  for the affected member (extends the existing `SettledPerMemberEndpointTests` QR case); reaching full
  clearance drops the member from the QR exactly as today; all-cleared still returns
  `NoOutstandingDebtForQr` (12003).
- `PUT /expenses/{uuid}/settled` and `PUT /expenses/{uuid}/shares/{shareUuid}/settled` on an equivalent
  single-share scenario produce the identical resulting event balance (cross-trigger consistency,
  end-to-end).

### Wrap-up (both milestones)

- Update `Constants/ErrorCodes.cs` with a `// 17xxx - ...` reservation comment (no codes defined),
  mirroring the 15xxx/16xxx precedent, once Milestone 1 lands.
- Update this doc's Progress Log + Final Outcome after each milestone ships; record the migration name
  and confirm it was reviewed/applied.
- OQ1-OQ5 answers are recorded in the Decision Log below; implementation on the affected steps may
  proceed against the final design captured there.

## Impact Analysis

**APIs:**
- **No new routes in either milestone.** `PUT api/v1/events/{eventUuid}/members/{memberUuid}/settled`,
  `PUT api/v1/expenses/{expenseUuid}/settled`, `PUT api/v1/expenses/{expenseUuid}/shares/{shareUuid}/settled`
  all keep their existing routes/verbs; their **side effects** grow (Direction 1/2 cascades), but their
  response shape stays exactly the plain `ApiResult` success message on all three (OQ3, confirmed
  option (a) — no response DTO changes).
- **Changed (additive, confirmed OQ4/OQ5):** `GET api/v1/events/{uuid}/balance` response gains
  `MemberBalanceRow.ClearedAmount`/`SettlementStatus`/`IsEligibleForAutoCascade`,
  `EventBalanceResponse.PartiallySettledMemberCount`. **Wire format (OQ-WF, confirmed):**
  `SettlementStatus` is a **`string`** DTO field (`"Unsettled" | "PartiallySettled" | "Settled"`), not the
  raw `EventSettlementStatus` enum — see Decision Log entry 6. Serialized under the default MVC camelCase
  policy (`Program.cs` registers no `PropertyNamingPolicy` override), so the wire key is
  `settlementStatus`.
- **Behavior change (no shape change):** the three settled toggles now have a materially larger,
  automatic blast radius (documented above); the event/expense QR generation endpoints are unaffected in
  shape, only in the amount they compute (Story C, additive-only per the BA doc's own finding).

**Database:**
- **Milestone 1: no migration.** Purely additive repository logic reusing existing tables/columns.
- **Milestone 2 (REQUIRES MIGRATION — `AddEventMemberSettlementClearedAmount`):** ALTER
  `event_member_settlements` adding `cleared_amount` (`decimal(18,2) NOT NULL DEFAULT 0`, CHECK `>= 0`).
  No data backfill. Money model (`decimal(18,2)`, CHECK, non-negative) extended, not changed.

**Infrastructure:** none (no Redis/workers/new packages) in either milestone.

**Services:**
- **New (Milestone 1):** `Repositories/EventSettlementClassifier.cs`.
- **New (Milestone 2):** `Repositories/EventSettlementCreditApplier.cs`, `Models/Stats/
  EventSettlementStatus.cs`. `EventSettlementStatus` is an **internal, service-only** enum used for
  type-safe computation in `StatsService`; it is never the type of a DTO property (OQ-WF, confirmed) —
  `MemberBalanceRow.SettlementStatus` is `string`, assigned via `.ToString()` on the computed enum value.
- **Modified (Milestone 1):** `StatsRepository` (refactored to consume the classifier),
  `EventMemberSettlementRepository` (cascade + reversal), `StatsAggregates`/`MemberBalanceRow`/
  `StatsProfile` (OQ4, confirmed — `IsEligibleForAutoCascade` added). No new
  `Models/Events/SetMemberSettledResponse.cs` (OQ3, confirmed — response shape unchanged).
- **Modified (Milestone 2):** `EventMemberSettlement` entity + partial, `ExpenseRepository`,
  `ShareRepository`, `EventMemberSettlementRepository` (OQ2, confirmed — also snapshots `ClearedAmount`),
  `StatsAggregates`, `StatsRepository`, `MemberBalanceRow`, `EventBalanceResponse`, `StatsService`,
  `StatsProfile`. **`WalletQrService` unchanged — explicitly called out so it is not accidentally
  touched.**

**Documentation:** this planning doc; updated Vietnamese Swagger summaries on the three settled routes
(both milestones); no new message keys anticipated (existing `Success.*Updated` keys reused).

## Decision Log

> Inherited, locked, NOT reopened here (from the BA doc's own Decision Log, entries 1-10): the full
> eligibility gate (OQ-A/OQ-L), cascade scope (OQ-B), symmetric capped reversal (OQ-C), both triggers
> share one code path (OQ-D), fully automatic/no confirmation (OQ-F), no audit (OQ-G), allowed on closed
> events (OQ-H), Free-tier (OQ-I). See the BA doc for full rationale; this planning doc's Requirements
> section restates the binding shape only.

> Entries below record THIS doc's own implementation-level decisions, confirmed by the user at the
> 2026-08-25 checkpoint. **Zero Open Questions remain in this doc.** Do not reopen these five without a
> new explicit user decision.

1. **OQ1 — Option (a): unconditional, recompute live.** On un-settle, Direction 1's share reversal
   recomputes "all of the member's shares in the event" against **current** data (the same query as the
   forward cascade) and un-settles them all, regardless of whether the member's eligibility
   classification has changed since the original settle. *Reason:* matches the literal "symmetric"
   framing most directly, and keeps Direction 1's reversal mechanics consistent with how Direction 2's
   own reversal already works — both are recomputed off current data with no provenance tracking, so
   neither direction needs a special-cased "was this share/credit originally cascaded vs. manually
   touched" distinction. Trade-off accepted: a share can be un-settled purely because the member's
   aggregate classification changed, not because the owner touched that specific share.

2. **OQ2 — Option (a): unify `ClearedAmount` as the sole source of truth.** The already-shipped manual
   event-level settle path (`EventMemberSettlementRepository.SetMemberSettledAsync`) must also snapshot
   `ClearedAmount = NetOwed` (on `true`) / `0` (on `false`); `IsSettled` stays consistent with
   `ClearedAmount ≥ NetOwed`, written by whichever path last touched it. *Reason:* avoids the exact
   "two independently-maintained facts that can drift" risk the BA doc calls the single biggest
   implementation risk, now that `Outstanding` derives from `ClearedAmount` rather than `IsSettled`
   directly. *Explicit, accepted consequence:* a later per-share reversal (Direction 2) can partially
   claw back credit even from a member who was fully settled via a manual Direction-1 assertion, since
   both directions now share one number — this is deliberate, not an oversight, and is covered by a
   dedicated regression test (Step M2.6).

3. **OQ3 — Option (a): keep the existing plain `ApiResult` success-message shape.** Unchanged for all
   three settled-toggle routes (`PUT .../expenses/{uuid}/settled`, `.../shares/{shareUuid}/settled`,
   `.../events/{eventUuid}/members/{memberUuid}/settled`) — no cascade/credit-count fields added. *Reason:*
   consistent with every other settled toggle and write endpoint in this codebase (no precedent anywhere
   for a toggle reporting side-effect counts); the BA doc's own Web workstream plan already assumes the
   client refetches/invalidates the relevant caches rather than reading counts off the response.

4. **OQ4 — Option (a): expose the Direction-1 eligibility fact additively.** A new `bool
   IsEligibleForAutoCascade` field is added to `MemberBalanceRow`, derived from the same canonical
   `EventSettlementClassifier` helper Direction 1 itself gates on. *Reason:* avoids web/design having to
   reimplement the gross-purity classification client-side, which would duplicate the exact logic this
   whole feature is trying to keep in one place — the same rationale that motivated the shared
   `EventSettlementClassifier` helper in the first place.

5. **OQ5 — Accept the proposed naming as-is.** `EventMemberSettlement.ClearedAmount` (English, stored
   field); a new service-computed (not stored) `enum EventSettlementStatus { Unsettled,
   PartiallySettled, Settled }` exposed as `MemberBalanceRow.SettlementStatus`; Vietnamese copy "số tiền
   đã tất toán" (cleared amount) / "chưa trả" (unsettled) / "đã trả một phần" (partially settled) / "đã
   trả" (fully settled). *Reason:* matches the BA doc's own recommended representation and candidate
   terms verbatim (§ Terminology); satisfies `The-ideal.md` §5's fixed-terms sign-off convention with no
   further naming round needed before implementation.

6. **OQ-WF — `MemberBalanceRow.SettlementStatus` is typed `string` on the wire, not the raw
   `EventSettlementStatus` enum.** Raised by `web-feature-planner` while planning the frontend against
   this doc's finalized contract: the DTO section (OQ5, Step M2.5) named `SettlementStatus`'s C# type as
   the `EventSettlementStatus` enum but never stated its JSON wire format, which blocks pinning a precise
   TypeScript type. *Investigation:* confirmed via `grep` that no `JsonStringEnumConverter` is registered
   anywhere in `Program.cs` (`FairShareMonApi/Program.cs` only adds `UtcAwareDateTimeConverter`/
   `UtcAwareNullableDateTimeConverter`, no enum converter, no `PropertyNamingPolicy` override beyond MVC's
   default camelCase) — so System.Text.Json's default would serialize a raw `EventSettlementStatus`
   property as an **integer** (`0|1|2`). Checked the codebase for an existing precedent: no response DTO
   anywhere exposes a real C# enum type directly (`ExportFormat` is parsed from a query string internally
   and never appears on a response DTO; the other enums in the repo — `EventWriteStatus`,
   `ExpenseWriteStatus`, `SettlementWriteStatus`, `NameWriteStatus`, `AuditEntityType`/`AuditAction` — are
   all internal-only). The one existing status/category-like response field, `UserResponse.Tier`/`Role`,
   is typed **`string`** end-to-end — the entity (`Database/Entities/User.cs`) stores `Tier`/`Role` as
   `string` backed by string-constant classes (`Constants/UserTiers.cs`: `"FREE"`/`"PREMIUM"`,
   `Constants/UserRoles.cs`: `"USER"`/`"ADMIN"`), not a C# enum at all, and that string flows unchanged
   into `UserResponse`. *Decision:* keep `EventSettlementStatus` as an **internal, service-only** enum
   used purely for type-safe branching inside `StatsService` (as OQ5 already named it), but type
   `MemberBalanceRow.SettlementStatus` as **`string`**, assigned via `status.ToString()` at the point
   `StatsService` computes it (`"Unsettled"` / `"PartiallySettled"` / `"Settled"`) — matching the
   `UserResponse.Tier`/`Role` convention of never putting a raw enum on a response DTO. *Reason:* this is
   the only pattern with precedent in the codebase, avoids depending on a global JSON option
   (`JsonStringEnumConverter`) that isn't registered and that this doc has no mandate to add, and keeps
   the wire value human-readable without any client-side lookup table. *Wire type pinned for
   `web-feature-planner`:* `SettlementStatus: "Unsettled" | "PartiallySettled" | "Settled"` (camelCase key
   `settlementStatus`, per MVC's unmodified default naming policy) — **not** `0 | 1 | 2`. *Alternative
   considered and rejected:* adding `[JsonConverter(typeof(JsonStringEnumConverter))]` on the enum/property
   to keep the DTO enum-typed — rejected because it would be a one-off pattern with no precedent anywhere
   else in the codebase, whereas DTO-level `string` typing already is the established convention.

## Progress Log

### 2026-08-25

- Read the finalized BA doc `planning/ba/event-expense-settlement-sync-business-analysis.md` in full
  (all four user stories, Business-Rule Impact, Feasibility & Affected Surface, Cross-Functional
  Workstreams, Tier & Data Implications, Risks & Sequencing, all 10 Decision Log entries, the Handoff
  Summary) and the shipped `FairShareMonApi/planning/settled-per-member.md` in full (all 15 locked OQs,
  Implementation Plan, Final Outcome, Future Improvements) to avoid contradicting or silently redoing any
  locked decision.
- Read `FairShareMonApi/CLAUDE.md`, `.agents/rules/rules.md`/`.claude/rules/rule.md` for conventions and
  the Human Confirmation Policy.
- Read the live code grounding every touch point: `EventMemberSettlement` entity + partial,
  `EventMemberSettlementRepository`, `SettlementReconciler`, `StatsRepository.GetEventBalanceAsync`
  (the exact M7 balance formula this feature's eligibility gate is defined against), `ExpenseRepository`/
  `ShareRepository.SetSettledAsync`, `StatsService`/`StatsAggregates`/`MemberBalanceRow`/
  `EventBalanceResponse`/`StatsProfile`, `WalletQrService` (both QR billing-selection helpers),
  `EventsService`/`EventsController`, `BaseRepository`/`Program.cs` (confirmed `AppDbContext` is Scoped
  via `AddDbContextPool`, which underpins the shared-helper architecture), `ErrorCodes.cs` (confirmed
  17xxx is the next free block), `MessageKeys.cs` (confirmed the three existing `Success.*Updated` keys
  are reusable, no new keys needed).
- **Key design finding:** the BA doc's own literal wording for "single-sided" (Decision Log entry 1) and
  its own OQ-L algebra (Decision Log entry 5) together pin down a precise fourth classification bucket
  ("mixed" = `Balance == 0` exactly) that the BA doc names but never spells out numerically — resolved as
  an Assumption (not an Open Question) since it follows deterministically from already-locked wording,
  not from a fresh preference call.
- Designed the one canonical shared helper (`EventSettlementClassifier`) the BA doc's Risk section
  demands, split into a DB-querying half (reused by `StatsRepository` and every new write path) and a
  pure, unit-testable classification half — concretely closing the "gross/net duplication drift" risk the
  BA doc calls the single biggest implementation risk, by construction rather than by convention.
- Found and worked through a load-bearing consistency gap the BA doc's own task list did not name:
  once Milestone 2 changes `Outstanding`'s formula to depend on `ClearedAmount`, the **already-shipped**
  Milestone-1 manual Layer-B write path must also be updated or it will silently regress (a member marked
  fully settled via Direction 1 would read as still owing once the formula changes) — captured as OQ2 with
  a recommended resolution, not silently patched in.
- Split the BA doc's own explicitly-undecided items (toggle response shape — API item 6; UI eligibility
  exposure — Web item 3/Design item 2) into concrete, options-with-trade-offs Open Questions (OQ3, OQ4)
  scoped precisely to the DTO/contract-shape decisions feature-planner owns, plus the reversal-mechanics
  gap for Direction 1's boolean shares (OQ1, genuinely unaddressed by the BA doc's OQ-C, which only
  reasoned about the numeric `ClearedAmount` case) and the non-blocking naming items already flagged
  deferred by the BA doc (OQ5).
- Wrote the full Implementation Plan as two clearly separated, independently shippable/testable
  milestones (naming every concrete file, the one migration, and the full unit/integration/endpoint test
  list for test-engineer per milestone), the Impact Analysis, and this Progress Log entry. Did not write
  any code, migration, or DTO — planning only.

### 2026-08-25 (checkpoint round 2)

- User answered all five Open Questions raised in this doc (OQ1-OQ5), each accepting the recommended
  option (a): OQ1 unconditional/recompute-live reversal; OQ2 unify `ClearedAmount` as the sole source of
  truth (with the explicit, accepted cross-direction claw-back consequence for a manual Direction-1
  full-settle); OQ3 keep the plain `ApiResult` success-message shape on all three settled-toggle routes;
  OQ4 expose `IsEligibleForAutoCascade` additively on `MemberBalanceRow`; OQ5 accept the proposed
  `ClearedAmount`/`EventSettlementStatus` naming and Vietnamese copy as-is.
- Recorded all five as Decision Log entries 1-5 (rationale + accepted trade-offs for each), annotated
  each Open Question inline (`~~OQ-X~~ → Answered`, original question text preserved, same convention the
  BA doc used for its own OQ-A through OQ-L).
- Updated the Implementation Plan to drop every "pending OQx" / "default assumption" hedge and state the
  final design directly: Step M1.2's reversal branch, Step M1.3 (no DTO changes), Step M1.4
  (`IsEligibleForAutoCascade` ships), Step M2.1's entity XML doc, Step M2.4 (unconditional `ClearedAmount`
  snapshot on the manual Layer B path), Step M2.5 (final `SettlementStatus`/`Outstanding` formula), and
  the corresponding Step M1.5/M2.6 test-list bullets (single confirmed test shape instead of "write both,
  keep one").
- Updated the Impact Analysis section to state the final, confirmed DTO/response-shape impact instead of
  a range of possibilities gated by unresolved OQs.
- **Zero Open Questions remain in this doc.** Ready to hand to `web-feature-planner` (frontend planning)
  and `ui-designer` (visual design) against the finalized contract below.

### 2026-08-25 (OQ-WF — SettlementStatus wire format)

- `web-feature-planner` flagged a gap while planning the frontend: this doc typed
  `MemberBalanceRow.SettlementStatus` as the C# enum `EventSettlementStatus` but never stated its JSON
  wire format, and `web-feature-planner` had found via `grep` that no `JsonStringEnumConverter` is
  registered in `Program.cs` — meaning System.Text.Json's default would serialize it as a raw integer —
  while also finding codebase precedent (`UserResponse.Tier`/`Role`) for typing enum-like response fields
  as `string` instead.
- Resolved directly (technical consistency question, not a preference call): confirmed no
  `JsonStringEnumConverter` is registered anywhere in `Program.cs`; confirmed `UserResponse.Tier`/`Role`
  (and their backing entity + `Constants/UserTiers.cs`/`UserRoles.cs`) are `string` end-to-end with no
  enum type involved at all; confirmed no other response DTO in the codebase exposes a raw C# enum.
  Recorded as **Decision Log entry 6**: `MemberBalanceRow.SettlementStatus` is typed **`string`**
  (`"Unsettled" | "PartiallySettled" | "Settled"`), with `EventSettlementStatus` demoted to an
  internal/service-only enum used for computation inside `StatsService` and converted via `.ToString()`
  before assignment. Updated Step M2.5 (Implementation Plan) and the Impact Analysis DTO bullet to state
  the `string` type and the resulting overlay-math pseudocode explicitly.
- Wire type pinned for `web-feature-planner`'s TypeScript type: `SettlementStatus: "Unsettled" |
  "PartiallySettled" | "Settled"` (wire key `settlementStatus`, camelCase per MVC's unmodified default) —
  not `0 | 1 | 2`.

### 2026-08-25 (Milestone 1 implementation)

- Implemented Milestone 1 (Direction 1: event settle -> cascade to expenses) only, per the finalized
  Implementation Plan (Steps M1.1-M1.4). Milestone 2 (Direction 2, `ClearedAmount`, QR remaining-amount
  math) intentionally NOT started - out of scope for this pass, sequenced after M1 per the doc's own
  recommendation.
- **Step M1.1** - added `Repositories/EventSettlementClassifier.cs`: `MemberSettlementEligibility` enum
  (`NetZero`/`NetDebtor`/`NetCreditorGrossPure`/`NetCreditorGrossMixed`), `MemberSettlementFacts` record
  (`Balance`/`NetOwed`/`IsEligibleForDirection1Cascade` computed), the pure `Classify(advanced, owed,
  hasDebtorShareElsewhere)` function, and the DB-querying `ClassifyAsync(dbContext, eventId,
  restrictToMemberIds, ct)` running the same advanced/owed `GroupBy`/`Sum` shape
  `StatsRepository.GetEventBalanceAsync` ran inline before, plus the gross-purity
  (`SettlementReconciler.IsBillable`-equivalent) query. Refactored `Repositories/StatsRepository.cs`
  `GetEventBalanceAsync` to call this helper instead of its own inline `GroupBy` blocks - `StatsRepository`
  is now a **consumer** of the same canonical classification Direction 1's write path gates on, closing the
  BA doc's single-biggest-risk gap. Noted inline that the classifier call is not additionally scoped by
  `share.Expense.User.Uuid` (unlike the pre-refactor inline query) since the caller always resolves
  `eventId` through an already-resource-owned lookup first - matches the doc's own `ClassifyAsync` signature
  (no `userUuid` parameter), not a security regression.
- **Step M1.2** - modified `Repositories/EventMemberSettlementRepository.cs` `SetMemberSettledAsync`:
  after the existing upsert of the `(event_id, member_id)` flag (unchanged - always succeeds for any
  participant), classifies the member via `EventSettlementClassifier.ClassifyAsync` and, on
  `isSettled == true`, cascades to all of the member's shares across the event's expenses (via new private
  helpers `LoadMemberExpensesAsync`/`CascadeMemberShares`) ONLY if eligible, calling
  `SettlementReconciler.ReconcileExpense` per affected expense; on `isSettled == false`, unconditionally
  reverses the same "all shares in the event" set recomputed live (OQ1, option (a)) regardless of current
  eligibility. No `EventWriteGuard` call, no audit (both inherited, unchanged). Updated the interface XML
  doc and the Swagger summary on `EventsController.SetMemberSettledAsync` (Vietnamese) to document the
  auto-cascade + its eligibility gate and the unconditional reversal.
- **Step M1.3** - confirmed no DTO/response-shape change: `EventsController.SetMemberSettledAsync` (and
  the other two settled-toggle routes) keep returning the plain `ApiResult` success message, unchanged.
- **Step M1.4** - added `bool IsEligibleForAutoCascade` to `Repositories/Stats/StatsAggregates.cs`'s
  `MemberBalanceAggregate` (new required positional parameter, populated in `StatsRepository` from the
  classifier facts already computed in Step M1.1) and to `Models/Stats/MemberBalanceRow.cs` (Vietnamese XML
  doc). `Mappings/StatsProfile.cs` needed no change - AutoMapper already maps the identically-named
  property by convention. Also appended a short Vietnamese clause to the `GET
  /events/{uuid}/balance` Swagger description documenting the new field (not an explicit doc step, but a
  direct, low-risk documentation consequence of the wire-shape addition).
- **Wrap-up** - added the `// 17xxx - Event/expense settlement sync` reservation comment to
  `Constants/ErrorCodes.cs` (no codes defined), mirroring the 15xxx/16xxx precedent, per the doc's Wrap-up
  section.
- **Test-suite compile fix (mechanical, not new test authorship):** `FairShareMonApi.Tests/StatsServiceTests.cs`
  constructed `MemberBalanceAggregate` positionally in 8 call sites; the new required
  `IsEligibleForAutoCascade` parameter broke compilation. Added the 9th positional argument to each
  existing call site (no new tests, no new assertions) purely to restore buildability - writing the
  *dedicated* `EventSettlementClassifierTests`/`EventSettlementCascadeRepositoryTests` (Step M1.5) remains
  test-engineer's job.
- **Build/test result:** `dotnet build FairShareMonApi.sln` succeeds (0 errors; only the pre-existing
  `AutoMapper` NU1903 advisory warning and one pre-existing unrelated `CS8619` nullability warning in
  `ExpensesEndpointTests.cs`). `dotnet test FairShareMonApi.sln`: 801 passed, 0 failed, 571 skipped. All
  skips are the pre-existing `[SkippableFact]` integration tests (including `StatsRepositoryTests` and
  `EventMemberSettlementRepositoryTests`, which this change touches) - confirmed the skip reason is
  `MariaDB unreachable ... Access denied for user 'root'@'localhost'`, i.e. this sandbox's local MariaDB
  instance has different credentials than `appsettings.json`'s `ConnectionStrings:Default`. This is a
  pre-existing environment limitation (the connection string is unmodified by this change) and not
  something introduced by this implementation; it means the DB-backed regression coverage for the
  `StatsRepository` refactor (Step M1.1's "byte-for-byte regression requirement") and the new cascade
  behavior (Step M1.2) could not be exercised live in this session. No migration was needed for Milestone 1
  (purely additive repository logic over existing tables/columns), consistent with the Impact Analysis.

### 2026-08-25 (Milestone 1 test coverage - test-engineer)

- Read this doc's Step M1.5 test list, the Decision Log (esp. OQ1/OQ4/OQ-L), `FairShareMonApi/CLAUDE.md`,
  and the existing test infrastructure (`Infrastructure/DatabaseFixture.cs`, `IntegrationTestBase.cs`,
  `ExpenseDbTestBase.cs`, `AuthApiTestBase.cs`, `ExpenseApiTestBase.cs`) plus the sibling
  `EventMemberSettlementRepositoryTests.cs`/`StatsRepositoryTests.cs`/`StatsServiceTests.cs` to follow
  their exact seeding/assertion patterns before writing anything.
- **New unit tests (no DB):** `FairShareMonApi.Tests/EventSettlementClassifierTests.cs` - 13 test cases
  covering the pure `EventSettlementClassifier.Classify` switch (`NetZero`/`NetDebtor` regardless of
  gross-purity/`NetCreditorGrossPure`/`NetCreditorGrossMixed` per the OQ-L algebra) and
  `MemberSettlementFacts`'s computed `Balance`/`NetOwed`/`IsEligibleForDirection1Cascade`.
- **New integration tests (real MariaDB, `[SkippableFact]`):**
  `FairShareMonApi.Tests/EventSettlementCascadeRepositoryTests.cs` - 9 tests extending the shipped
  `EventMemberSettlementRepositoryTests` fixture: net-debtor full cascade + cross-member isolation +
  byte-for-byte balance invariant; gross-pure net-creditor cascade; the OQ-L gross-mixed-creditor
  regression (flag flips, debtor-share on the other expense stays exactly as it was); the `Balance == 0`
  NetZero bucket (no cascade); OQ1's unconditional live-recomputed reversal (a gross-pure creditor who
  later acquires a debtor-share elsewhere still has their originally-cascaded share reversed on unsettle,
  despite now being ineligible); closed-event parity; a soft-deleted cascade target; no audit row written;
  and cross-user isolation (two ledgers with an identical shape - settling one user's member never touches
  the other user's shares/settlement row).
- **New endpoint tests (`WebApplicationFactory`, `[SkippableFact]`):**
  `FairShareMonApi.Tests/EventSettlementCascadeEndpointTests.cs` - 3 tests: an eligible debtor's cascade
  is visible on a subsequent `GET /expenses/{uuid}`; a gross-mixed creditor's shares are unaffected while
  the balance overlay's `isSettled`/`isEligibleForAutoCascade` still reflect correctly; and the
  settle/un-settle round trip reverses live.
- **Extended existing unit test:** added `StatsServiceTests.GetEventBalanceAsync_MapsIsEligibleForAutoCascadeThroughFromAggregate`
  confirming the new `MemberBalanceAggregate.IsEligibleForAutoCascade` field flows through the real
  AutoMapper `StatsProfile` to `MemberBalanceRow` unchanged, for both a debtor (eligible) and a
  gross-mixed creditor (ineligible) fixture - closes the doc's own "confirm `IsEligibleForAutoCascade`
  populates correctly for known fixtures" ask (Step M1.5 item 9) at the mapping layer.
- Extra coverage beyond the doc's literal list (noted per protocol): `MemberSettlementFacts`
  Balance/NetOwed unit tests; an explicit cross-user isolation repository test (the doc's own list didn't
  spell this out as a separate bullet, only implied via OQ-J's cross-member framing).
- **Test run:** `dotnet build FairShareMonApi.sln` - 0 errors (only the pre-existing AutoMapper NU1903
  advisory + the pre-existing unrelated `ExpensesEndpointTests.cs` CS8619 nullability warning).
  `dotnet test FairShareMonApi.sln`: **1398 total, 815 passed, 0 failed, 583 skipped.** All 13 new unit
  test cases (`EventSettlementClassifierTests`) + the 1 new `StatsServiceTests` case passed (801 -> 815
  passed, exactly +14). All 12 new integration/endpoint tests (9 repository + 3 endpoint) skipped cleanly
  (571 -> 583 skipped, exactly +12) - **MariaDB is unreachable in this sandbox** (`SkipIfNoDb` reason:
  `Access denied for user 'root'@'localhost' (using password: YES)`), the same pre-existing environment
  credential mismatch `api-implementer` already flagged in the prior Progress Log entry; not caused by
  this change and not something test-engineer can fix (owning only the test project, not
  `appsettings.json`/environment credentials). No production code was modified.
- **Coverage gap / follow-up needed:** because MariaDB could not be reached, none of the new DB-backed
  cascade/reversal/isolation tests, nor the `StatsRepository` byte-for-byte regression re-run, were
  actually exercised against a live database in this session - only compiled and skip-verified. The
  orchestrator should re-run `dotnet test` (or set `FSM_TEST_CONNECTION` to a reachable MariaDB) before
  considering Milestone 1's DB-backed behavior fully verified.

### 2026-08-25 (orchestrator — live DB verification)

- Root-caused the "MariaDB unreachable" skip: `DatabaseFixture.ResolveConnectionString()` only reads
  `FSM_TEST_CONNECTION` or the plain `appsettings.json` fallback (`ConnectionStrings:Default =
  ...Password=fairsharemon@123`) - it never loads `appsettings.Development.local.json`, so the actual
  local dev credential (`Password=123456789`) was never in play. MariaDB itself was reachable and running
  the whole time (Windows service `MariaDb`, confirmed running); this was a test-harness config-resolution
  gap, not a real environment outage. Not fixed here (out of scope for this feature) - flagged as a
  candidate Future Improvement below.
- Re-ran with `FSM_TEST_CONNECTION` set to the correct local credential:
  - Filtered run (`EventSettlementCascade*`, `EventSettlementClassifier*`, `StatsRepositoryTests`,
    `StatsServiceTests`): **59/59 passed, 0 failed, 0 skipped** - every new cascade/reversal/isolation/OQ-L
    test from Step M1.5 executed live against real MariaDB and passed, including the OQ-L gross-mixed-
    creditor regression and the unconditional-reversal-after-eligibility-drift case.
  - Full suite: **1391 passed, 0 failed, 7 skipped, 1398 total** - the 7 remaining skips are unrelated
    Redis-cache-fallback tests (`EventShareLinkCacheTests`, `TokenWhitelistStoreTests`,
    `AdminEndpointTests`), not MariaDB-related.
- **Milestone 1's DB-backed behavior is now fully verified**, superseding the coverage-gap note above.

### 2026-08-25 (Milestone 2 implementation)

- Implemented Milestone 2 (Direction 2: expense/share settle -> partial credit to event balance + Story C
  QR), per the finalized Implementation Plan (Steps M2.1-M2.6's production-code steps M2.1-M2.5; M2.6 test
  authorship remains test-engineer's job next). Read Milestone 1's shipped code
  (`EventSettlementClassifier`, `EventMemberSettlementRepository.SetMemberSettledAsync`,
  `SettlementReconciler`, `StatsRepository`/`StatsService`/`StatsAggregates`/`MemberBalanceRow`/
  `StatsProfile`, `WalletQrService`) before starting, plus this doc's Decision Log (all 6 entries) for
  binding rationale.
- **Step M2.1** - added `EventMemberSettlement.ClearedAmount` (`decimal`) to
  `Database/Entities/EventMemberSettlement.cs` with an XML doc explaining it is the cumulative Direction-2
  credit, clamped to `[0, NetOwed]`, and the sole source of truth `IsSettled` derives from. Mapped
  `cleared_amount` as `decimal(18,2)` with `HasDefaultValue(0m)` and the
  `ck_event_member_settlements_cleared_amount_non_negative` CHECK constraint (mirroring `Share`'s
  `ck_shares_amount_non_negative` pattern) in `Database/Entities/Partials/EventMemberSettlement.cs`.
  Generated migration `AddEventMemberSettlementClearedAmount`
  (`FairShareMonApi/Migrations/20260825095114_AddEventMemberSettlementClearedAmount.cs`) via
  `dotnet ef migrations add AddEventMemberSettlementClearedAmount --project ./FairShareMonApi/FairShareMonApi.csproj`
  - reviewed: exactly one `AddColumn<decimal>` (`decimal(18,2)`, `NOT NULL DEFAULT 0`) + one
  `AddCheckConstraint`, no data backfill, model snapshot updated. Matches the plan byte-for-byte.
- **Step M2.2** - added `Repositories/EventSettlementCreditApplier.cs`: the ONE shared static
  `ApplyAsync(db, eventId, deltas, now, cancellationToken)` helper both write paths call. Batches
  `EventSettlementClassifier.ClassifyAsync` once per affected member set, loads/creates their
  `EventMemberSettlement` rows in one query, and per member computes
  `newCleared = Math.Clamp(existing.ClearedAmount + delta, 0m, facts.NetOwed)` (self-protecting for a
  creditor/`NetZero` member since their `NetOwed == 0`, no separate eligibility branch, per Decision Log
  entry 5's own finding) and flips `IsSettled`/`SettledAt` only when crossing the full/partial boundary. No
  audit.
- **Step M2.3** - wired the shared helper into both trigger points: `ShareRepository.SetSettledAsync`
  captures `wasSettled` before mutating and calls `ApplyAsync` with a single `(MemberId, ±Amount)` delta
  when the flag actually flips on a billable, event-scoped share; `ExpenseRepository.SetSettledAsync`
  snapshots every billable share's prior flag before `SettlementReconciler.CascadeToShares`, then builds the
  full delta list from shares that actually flipped and calls the identical `ApplyAsync` (the literal "one
  shared code path", OQ-D residual/Decision Log entry 6). Idempotency is structural in both (an unchanged
  flag contributes no delta). Neither path touches `EventWriteGuard` or audit (unchanged, inherited
  exceptions).
- **Step M2.4** - `EventMemberSettlementRepository.SetMemberSettledAsync` now also snapshots
  `settlement.ClearedAmount = isSettled ? memberFacts.NetOwed : 0m` unconditionally (using the `facts`
  already computed for the M1 eligibility gate), unifying `ClearedAmount` as the sole source of truth per
  OQ2/Decision Log entry 2. Updated the interface XML doc to note this.
- **Step M2.5** - `Repositories/Stats/StatsAggregates.cs`'s `MemberBalanceAggregate` gained
  `decimal ClearedAmount`; `StatsRepository.GetEventBalanceAsync`'s existing `EventMemberSettlement` load
  now also selects `ClearedAmount` (default `0m` for a participant with no row). Added
  `Models/Stats/EventSettlementStatus.cs` (internal, service-only enum: `Unsettled`/`PartiallySettled`/
  `Settled`). `Models/Stats/MemberBalanceRow.cs` gained `decimal ClearedAmount` and `string
  SettlementStatus` (Vietnamese XML docs) - confirmed `string`, never the raw enum, per OQ-WF/Decision Log
  entry 6. `StatsService.GetEventBalanceAsync`'s overlay now computes
  `netOwed`/`Outstanding = max(0, netOwed - ClearedAmount)`/`SettlementStatus` per the doc's exact
  pseudocode, and `EventBalanceResponse` gained `PartiallySettledMemberCount`. `Mappings/StatsProfile.cs`
  maps `ClearedAmount` through by convention and explicitly `Ignore()`s `SettlementStatus` alongside the
  already-ignored `Outstanding` (both computed once in `StatsService`). Confirmed **zero changes** to
  `Services/Api/Wallet/WalletQrService.cs` - its existing `row.Outstanding > 0m` filter already flows the
  new partial-clearance math through correctly (Story C, additive-only).
- Updated Vietnamese Swagger summaries on all three settled-toggle routes affected by the new side effect
  (`PUT .../expenses/{uuid}/settled`, `.../shares/{shareUuid}/settled` in `ExpensesController.cs`) and the
  `GET /events/{uuid}/balance` description (`EventsController.cs`) to document `clearedAmount`/
  `outstanding`/`settlementStatus`. No response DTO/route/verb changes (OQ3, unchanged).
- **Test-suite compile fix (mechanical, not new test authorship):** `FairShareMonApi.Tests/StatsServiceTests.cs`
  constructed `MemberBalanceAggregate` positionally in 5 call sites (10 rows); the new required
  `ClearedAmount` parameter broke compilation. Added the 10th positional argument to each row - for rows
  with `IsSettled == false` used `0m` (behaviorally identical to before); for the 2 rows with
  `IsSettled == true` (`SettledOwingMember...` and `Overlay_DoesNotPerturbBalanceAdvancedOrOwed`'s Cường
  fixtures) set `ClearedAmount = NetOwed` (`500_000m` and `300_000m` respectively) so the pre-existing
  `Outstanding == 0`/`SettledMemberCount == 1` assertions continue to hold under the new
  `Outstanding = max(0, netOwed - ClearedAmount)` formula - this is the minimal, mechanical value needed to
  keep each fixture internally consistent with what production code would actually persist for an
  already-fully-settled member (a hand-built fake aggregate has no other way to express "fully settled" once
  `ClearedAmount` becomes the source of truth); no assertions were added, removed, or otherwise altered.
- **Build result:** `dotnet build FairShareMonApi.sln` succeeds (0 errors; only the pre-existing AutoMapper
  NU1903 advisory + the pre-existing unrelated `ExpensesEndpointTests.cs` CS8619 nullability warning).
- **Migration applied:** `dotnet ef database update --project ./FairShareMonApi/FairShareMonApi.csproj`
  (with `ConnectionStrings__Default` pointed at this sandbox's real local MariaDB) applied
  `AddEventMemberSettlementClearedAmount` cleanly - one `ADD COLUMN cleared_amount` + one CHECK constraint,
  no errors.
- **Test result (live MariaDB, `FSM_TEST_CONNECTION` set to the sandbox's real credential):**
  `dotnet test FairShareMonApi.sln`: **1391 passed, 0 failed, 7 skipped, 1398 total** - identical
  pass/skip counts to Milestone 1's own live-verified baseline. The 7 skips are the same unrelated
  Redis-cache-fallback tests (`EventShareLinkCacheTests`, `TokenWhitelistStoreTests`, `AdminEndpointTests`),
  not MariaDB-related. Every pre-existing MariaDB-backed test (including `StatsRepositoryTests`,
  `EventMemberSettlementRepositoryTests`, and Milestone 1's `EventSettlementCascadeRepositoryTests`/
  `EventSettlementCascadeEndpointTests`) still passes unchanged against the migrated schema, confirming the
  new `cleared_amount` column/CHECK constraint and every M2.2-M2.5 write-path change introduced zero
  regressions. Step M2.6's own dedicated `EventSettlementCreditRepositoryTests`/extended
  `StatsServiceTests`/`WalletQrServiceTests`/endpoint tests were NOT written in this pass (test-engineer's
  job next, per the doc's own division of labor) - only the pre-existing suite's compile-fix and full live
  run are reported here.
- No Open Questions were added; nothing deviated from the doc's Step M2.1-M2.5 shape.

### 2026-08-25 (Milestone 2 test coverage - test-engineer)

- Read this doc's Step M2.6 test list in full, the Decision Log (esp. entries 2/5/6), `FairShareMonApi/CLAUDE.md`,
  and the existing test infrastructure (`Infrastructure/DatabaseFixture.cs`/`ExpenseDbTestBase.cs`/
  `ExpenseApiTestBase.cs`) plus the sibling `EventSettlementCascadeRepositoryTests.cs`/
  `EventSettlementCascadeEndpointTests.cs`/`StatsServiceTests.cs`/`WalletQrServiceTests.cs` to follow their
  exact seeding/assertion patterns. Read the actual M2 production code
  (`EventSettlementCreditApplier.cs`, `ShareRepository`/`ExpenseRepository`/`EventMemberSettlementRepository.
  SetSettledAsync`, `StatsService.GetEventBalanceAsync`, `EventSettlementStatus.cs`, `MemberBalanceRow.cs`)
  before writing anything.
- **Confirmed no pure `Clamp` helper was factored out** of `EventSettlementCreditApplier` - the clamp is
  inline `Math.Clamp(existing.ClearedAmount + delta, 0m, memberFacts.NetOwed)` inside `ApplyAsync`, not a
  separately-testable pure function. Per the doc's own Step M2.6 item 1 fallback instruction ("if not
  factored out, test the clamping behavior at the integration level instead and note why"), the clamp/cap/
  floor behavior is instead covered by the new integration tests below (whole-expense capping, the
  reversal-floor-at-0 test, and the reversal-drift-recap test) rather than by a new
  `EventSettlementClassifierTests` unit case. No production code was touched to add a pure helper - out of
  scope for test-engineer per the assignment.
- **New unit tests (no DB):**
  - `FairShareMonApi.Tests/StatsServiceTests.cs` - 6 new test methods (8 test cases incl. a 3-case
    `[Theory]`): `SettlementStatus` transitions `Unsettled -> PartiallySettled -> Settled` as `ClearedAmount`
    rises to `NetOwed`; a not-owing member stays `Unsettled` regardless of `ClearedAmount`; a claw-back drops
    `Settled` back to `PartiallySettled`; `Outstanding` never goes negative even if `ClearedAmount` were to
    exceed `NetOwed` (defensive, since the applier itself always clamps); `ClearedAmount` maps through from
    the aggregate without perturbing `Balance`/`Advanced`/`Owed` (D2 extended); `PartiallySettledMemberCount`
    counts only `PartiallySettled` rows.
  - `FairShareMonApi.Tests/WalletQrServiceTests.cs` - 2 new tests: a fake balance row with `ClearedAmount`
    strictly between `0` and `NetOwed` bills exactly `Outstanding` (not the raw balance); `ClearedAmount`
    reaching `NetOwed` (`Outstanding == 0`) drops the member from the composite exactly as a plain Layer-B
    settle already did. Added a `PartiallyClearedRow` helper mirroring `StatsService`'s exact overlay formula,
    proving `WalletQrService` needs zero code changes (Story C).
- **New integration tests (real MariaDB, `[SkippableFact]`):** `FairShareMonApi.Tests/
  EventSettlementCreditRepositoryTests.cs` - 13 tests covering the doc's full Step M2.6 integration list:
  whole-expense settle credits every eligible debtor simultaneously while a creditor on the same expense
  gets zero credit despite its own Layer A flag flipping; a lone per-share settle credits identically to an
  equivalent whole-expense settle (cross-trigger consistency); idempotency (re-settle does not double-credit,
  re-unsettle does not double-claw); the OQ-L cumulative "corollary" fixture (reaching `NetOwed` auto-settles
  with `SettledAt` stamped; a further non-billable `0đ` debtor-share settle for the same member still flips
  its own Layer A flag but contributes zero further credit, since `SettlementReconciler.IsBillable` gates it
  out of the credit applier entirely); reversal floored at 0; reversal re-capped at the member's CURRENT net
  owed after the event's shares changed since the credit was applied (the open-event drift fixture - proved
  the clamp can leave a member reading as fully settled again at their new, smaller debt, an accepted
  consequence of "recomputed against current data, no provenance tracking"); Direction 2 never creating or
  touching an `EventMemberSettlement` row for a loose expense (both the per-share and whole-expense paths);
  the D2/M7 OQ2 money-exactness invariant (`advanced`/`owed`/`balance` byte-for-byte unchanged); no audit row
  written by the credit step; the migration regression (a `ClearedAmount == 0` row computes the same
  `Outstanding` as the pre-M2 boolean-only formula); and the OQ2-confirmed cross-direction consequence (a
  manual Direction-1 full-settle via `SetMemberSettledAsync`, followed by an unrelated per-share Direction-2
  reversal via `ShareRepository`, partially claws back the member's `ClearedAmount` as designed).
- **New endpoint tests (`WebApplicationFactory`, `[SkippableFact]`):** `FairShareMonApi.Tests/
  EventSettlementCreditEndpointTests.cs` - 3 tests: `GET /events/{uuid}/balance` after a partial per-share
  settle reflects the correct `clearedAmount`/`outstanding`/`settlementStatus`/`partiallySettledMemberCount`;
  the closed-event QR (via the JSON `GET /events/{uuid}/qr/members` list, which shares the same
  `Outstanding`-driven billing as the binary composite QR) bills exactly the remaining amount after a partial
  credit, drops the member on full clearance, and the all-cleared case still returns `NoOutstandingDebtForQr`
  (12003) on both the member-list and composite QR routes; and the whole-expense vs. per-share settled
  toggles produce an identical resulting event balance for an equivalent single-share scenario (cross-trigger
  consistency, end-to-end).
- **Bug found and fixed during authoring (test-only, not production code):** the first draft of the
  whole-expense/creditor-gets-zero-credit test miscalculated the creditor fixture's `Advanced` figure
  (conflated "the payer's own share amount" with "the whole expense total", which is what `Advanced` actually
  sums to) - the test failed against production code on the first live run (`Expected: 0, Actual: 100000`).
  Diagnosed and corrected the test fixture's seeded amounts (no production code was ever suspected or
  touched); re-run passed.
- **Test run (live MariaDB, `FSM_TEST_CONNECTION` set to the sandbox's real credential):**
  - Filtered run (`EventSettlementCredit*`, `StatsServiceTests`, `WalletQrServiceTests`,
    `EventSettlementClassifierTests`, `EventSettlementCascade*`): **110/110 passed, 0 failed, 0 skipped** -
    every new M2.6 test executed live against real MariaDB and passed, alongside the full pre-existing M1
    regression suite it shares a fixture family with.
  - Full suite: **1417 passed, 0 failed, 7 skipped, 1424 total** (up from Milestone 1's verified baseline of
    1391 passed/1398 total - exactly +26 new passing tests, 0 regressions). The 7 skips are the same
    pre-existing, unrelated Redis-cache-fallback tests (`EventShareLinkCacheTests`, `TokenWhitelistStoreTests`,
    `AdminEndpointTests`), not MariaDB-related.
- **Coverage gaps:** none against the doc's Step M2.6 test list beyond the noted, doc-anticipated pure-`Clamp`
  fallback (item 1) - every other unit/integration/endpoint bullet has a corresponding new test, all executed
  live and passing. No production code was modified.

### 2026-08-25 (orchestrator — code-review fix)

- `code-reviewer` found one nit: `Models/Stats/EventSettlementStatus.cs`'s enum was declared `public`,
  contradicting its own doc comment and Decision Log entry 6's explicit "internal, service-only, never a
  DTO property type" framing. No live bug (`MemberBalanceRow.SettlementStatus` was already correctly
  `string`), but the accessibility modifier is a guardrail the doc asked for. Confirmed no other project
  (including `FairShareMonApi.Tests`) references the enum type directly, changed it to `internal enum`,
  rebuilt clean. Not re-run against the full live suite again (a pure accessibility-modifier change with
  no consumers outside the assembly cannot affect runtime behavior) - build success is sufficient
  verification here.

## Final Outcome

**Milestone 1 (Direction 1) is implemented, builds clean, and is fully verified: 1391/1398 tests passing
live against real MariaDB (7 unrelated Redis-cache skips), including every DB-backed cascade/reversal/
isolation/OQ-L test written for it.**

**Milestone 2 (Direction 2 + Story C) is implemented and builds clean: the `AddEventMemberSettlementClearedAmount`
migration was generated, reviewed, and applied to the sandbox's live MariaDB; the shared
`EventSettlementCreditApplier` helper is the sole credit/claw-back code path for both
`ShareRepository.SetSettledAsync` and `ExpenseRepository.SetSettledAsync`; `EventMemberSettlementRepository`'s
manual Layer B path now unifies `ClearedAmount` as the sole source of truth (OQ2); the
`Outstanding`/`SettlementStatus` overlay in `StatsService` derives from `ClearedAmount` per the doc's exact
formula; and `WalletQrService` required zero changes, confirmed. The full pre-existing test suite (1391/1398,
7 unrelated skips) passes live against the migrated database with no regressions.

**Step M2.6's own dedicated test coverage is now complete** (test-engineer, 2026-08-25): 26 new tests across
`EventSettlementCreditRepositoryTests.cs` (13, integration), `EventSettlementCreditEndpointTests.cs` (3,
endpoint), extended `StatsServiceTests.cs` (8 test cases) and `WalletQrServiceTests.cs` (2), covering every
bullet in the doc's Step M2.6 test list. Full suite: **1417 passed, 0 failed, 7 unrelated skips, 1424 total**,
verified live against real MariaDB. No production code was modified by test-engineer.**

### 2026-08-25 (orchestrator — DatabaseFixture credential-fallback fix)

- **Fixed** the `DatabaseFixture` nit below, at the user's explicit request. `ResolveConnectionString()`
  now also probes `appsettings.Development.local.json` (source path and the copy next to the test
  assembly), layered with higher precedence than the base `appsettings.json` but lower than
  `FSM_TEST_CONNECTION` - a developer's real local MariaDB credentials in the gitignored `.local.json`
  override are now honored automatically. Verified: `dotnet test FairShareMonApi.sln` with **no**
  `FSM_TEST_CONNECTION` set now runs live against real MariaDB - **1417 passed, 0 failed, 7 unrelated
  skips, 1424 total**, identical to the env-var-override baseline. `FSM_TEST_CONNECTION` still takes
  precedence for CI/other environments that don't have a `.local.json` file.

## Future Improvements

- ~~**Unrelated infra nit found during verification:**~~ **Fixed 2026-08-25** - `FairShareMonApi.Tests/
  Infrastructure/DatabaseFixture.cs`'s `ResolveConnectionString()` never loads `appsettings.Development.
  local.json`, only `FSM_TEST_CONNECTION` or the plain `appsettings.json` fallback - meaning a developer's
  actual local DB credentials in the `.local.json` override are silently ignored and every integration
  test skips with a misleading "MariaDB unreachable" reason instead of the real "wrong password in the
  fallback config." See the dated Progress Log entry above for the fix.
- Carried forward verbatim from the BA doc's own Future Improvements (not superseded by this plan):
  extending auto-cascade to the excluded mixed-role-creditor case, or a read-only "suggested cleared
  amount" signal for non-eligible members, once real usage data exists; unifying the display of all
  "settled" axes in the UI (ui-designer territory); extending audit coverage to settlement actions if
  payment-timing disputes ever become contentious; driving automated debt reminders off the
  partial-clearance figures this feature introduces.
- A hard API-level cap on Direction 1's blast radius (number of expenses cascaded in one request) was
  explicitly considered and explicitly NOT requested by the user when surfaced alongside OQ-H — noted
  here as a candidate defensive measure if a future real-world event turns out to have an unusually large
  expense count per member.
- OQ2's rejected alternative (option (b), keep `IsSettled`/`ClearedAmount` independent, OR'd) is not
  being pursued — OQ2 was confirmed as option (a) (unify `ClearedAmount` as the sole source of truth,
  Decision Log entry 2). Noted here only for the record, not as a live future item.
