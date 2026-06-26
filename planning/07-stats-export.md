# Phase 7 — Statistics & Export

## Objective
Implement reporting (`The-ideal.md` §4 Statistics, §5.2–5.4): debt-balance summary, category breakdown for charts, and voucher/batch export. Final integration phase.

## Background
The SQL is specified in `The-ideal.md` §5.2 (export voucher), §5.3 (export batch + balance), §5.4 (category stats). Output is JSON; CSV/Excel optional.

## Requirements
- All queries scoped by `user_id`.
- Balance model: per member `balance = paid − owed` (paid = Σ amounts of vouchers where member is payer; owed = Σ member's own records).
- Stats respect time-range / `batch_id` filters; include soft-deleted members where they appear in history.

## Dependencies
Phases 1–6.

---

## Stage 7.1 — Stats summary & balance
1. `Services/Api/Stats/StatsService.cs` (interface + impl): `GetSummary(from, to)` → total spend in range + per-member balance (use the §5.3 balance SQL via raw SQL or LINQ).
2. `Controllers/Common/StatsController.cs` `GET /api/stats/summary?from=&to=`.
3. DTO: `MemberBalanceDto` (member, paid, owed, balance), `SummaryDto`.

**Acceptance:** balance matches the §5.3 query for seeded data; positive/negative signs correct.

---

## Stage 7.2 — Category breakdown
1. `GetByCategory(from, to, batchId)` implementing §5.4 SQL → category_name, color, voucher_count, total_amount.
2. `GET /api/stats/by-category`. DTO `CategoryStatDto`.

**Acceptance:** totals per category match seeded data; ordered by total desc.

---

## Stage 7.3 — Export voucher
1. `Services/Api/Export/ExportService.cs`: `ExportVoucher(id)` implementing §5.2 (per-member total + concatenated notes), scoped by user.
2. `GET /api/vouchers/:id/export` → JSON (default).

**Acceptance:** output matches §5.2 query.

---

## Stage 7.4 — Export batch
1. `ExportBatch(id)` implementing §5.3 (per-voucher member totals + `name: note` concatenation) + the balance block.
2. `GET /api/batches/:id/export` → JSON.

**Acceptance:** output matches §5.3 queries for a multi-voucher batch.

---

## Minor phase 7.A — Optional CSV/Excel output
> Only if requested. **Changes export endpoints.**
1. Add `?format=csv|xlsx`; stream a generated file (CSV first; Excel via a lightweight writer).

---

## Stage 7.5 — Tests & end-to-end
1. Balance sign tests; category totals; export shapes.
2. End-to-end: register → seed → members/categories/tags → batch → vouchers/records → close → summary/by-category/export/audit. Assert numbers reconcile.

**Acceptance:** full happy-path scenario green.

---

## Impact Analysis
- **APIs:** `/api/stats/summary`, `/api/stats/by-category`, `/api/vouchers/:id/export`, `/api/batches/:id/export`.
- **Database:** read-only (no schema change unless reporting indexes added).
- **Services:** `StatsService`, `ExportService`.

## Open questions / Assumptions
- Default export format JSON (assumed); CSV/Excel deferred to minor phase 7.A.
- Summary without a date range = all-time (assumed).
- Whether `stats/summary` is batch-scopable too (assume time-range only here; batch balance via batch export). Confirm.

## Progress log
- (pending)

## Final outcome
- (to be completed)
