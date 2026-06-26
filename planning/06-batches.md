# Phase 6 — Expense Batches & Lifecycle

## Objective
Implement expense batches (`The-ideal.md` §2.4, §4 Batches, §5.1): CRUD, close, the `expense_time`-in-range rule, attaching existing vouchers, and the `CLOSED` write-lock — which is injected back into phase-5 voucher/record write paths (minor phase 6.A).

## Background
A batch groups vouchers over a date window with status `OPEN`/`CLOSED`. A `CLOSED` batch is read/export-only: no voucher/record writes and no new attachments.

## Requirements
- Batch owned per user; 404-not-403.
- Attaching/creating a voucher in a batch requires the batch `OPEN` and `expense_time` within `[start_date, end_date]`.
- `CLOSED` blocks all writes to vouchers in that batch (update/delete voucher, all record ops, attach).

## Dependencies
Phases 1, 2, 5.

---

## Stage 6.1 — Schema & entity
1. Append `expense_batches` DDL (`id`, `uuid`, `user_id`, `name`, `status` enum default `OPEN`, `start_date`, `end_date` nullable, timestamps).
2. `ExpenseBatch` entity + `ConfigureModel`.

**Acceptance:** DDL appended; entity maps; build green.

---

## Stage 6.2 — Repository & service (CRUD)
1. `BatchRepository`; `Services/Api/Batches/BatchService.cs`: `GetList(paging)`, `Create`, `Update(name/dates)`.

**Acceptance:** scoped CRUD + pagination work.

---

## Stage 6.3 — Close & lifecycle helpers
1. `Close(id)` → set `status = CLOSED` (idempotent / reject if already closed — decide).
2. Helper `EnsureBatchWritable(batchId, userId)` → loads batch, throws `400` if `CLOSED`; reused by voucher/record paths.
3. Helper `EnsureExpenseTimeInRange(batch, expenseTime)` → `400` if outside window.

**Acceptance:** close sets status; helpers enforce rules.

---

## Stage 6.4 — Attach existing voucher
1. `POST /api/batches/:id/vouchers` — set `voucher.batch_id` after `EnsureBatchWritable` + range check; scoped by user; audit `UPDATE` on the voucher.

**Acceptance:** attaching to a CLOSED batch → 400; out-of-range → 400.

---

## Stage 6.5 — Controller & DTOs
1. `BatchesController`: `GET`, `POST`, `PUT /:id`, `POST /:id/close`, `POST /:id/vouchers`. (`GET /:id/export` lands in phase 7.)
2. Requests/DTOs: `CreateBatchRequest`, `UpdateBatchRequest`, `AttachVoucherRequest`, `BatchDto`.

**Acceptance:** endpoints work via Swagger.

---

## Minor phase 6.A — Inject lifecycle guard into Phase 5 (change to VoucherService)
> **Changes phase-5 services** now that batches/lifecycle exist.
1. In `VoucherService.Create`/`Update`/`Delete` and all record sub-endpoints: if the voucher belongs to a batch, call `EnsureBatchWritable` (and `EnsureExpenseTimeInRange` on create/update with `expense_time` or batch change).
2. Update `05-vouchers-records-audit.md` progress log noting the guard wiring.

**Acceptance:** any write to a voucher in a CLOSED batch → 400; phase-5 tests still green + new guard tests.

---

## Stage 6.6 — Tests
1. Close batch; create/update/delete voucher in CLOSED batch→400; record ops in CLOSED batch→400; attach out-of-range→400; in-range OPEN→ok.

---

## Impact Analysis
- **APIs:** `/api/batches/*`; behavioral change to `/api/vouchers/*` writes.
- **Database:** `expense_batches`.
- **Services:** `BatchService`, `BatchRepository`; change to `VoucherService`.

## Open questions / Assumptions
- Re-open a CLOSED batch? (Assume **no** for now.)
- Detaching a voucher / moving between batches? (Assume out of scope.)
- `end_date` null → open-ended window (range check only enforces `start_date` lower bound). Confirm.

## Progress log
- (pending)

## Final outcome
- (to be completed)
