# Phase 5 — Vouchers, Records & Audit Logging

## Objective
The core domain: expense vouchers and their member records, with payer/category/tag handling, transactional integrity, cross-user validation, and append-only audit logging (`The-ideal.md` §2.5–2.6, §2.10, §4 Vouchers, §5.5).

## Background
A voucher has one payer (default = owner member), one category (default = user's `is_default`), optional tags, and ≥1 record (creator auto-record at `amount = 0`). Every create/update/delete on a voucher or record writes an `audit_logs` row in the same transaction.

## Requirements
- Create voucher + records (+ tag links + audit) atomically.
- `amount >= 0` (DB CHECK + app guard).
- Cross-user: `payer_member_id`, record `member_id`, `category_id`, `tag_id` all share the voucher's `user_id`.
- Audit is append-only; `entity_id` has no hard FK (survives deletion); UPDATE logs only real changes.
- Tag set on `PUT /vouchers/:id` is **replace-all**.

## Dependencies
Phases 1–4 (members, categories, tags must exist).

---

## Stage 5.1 — Schema & entities
1. Append DDL:
   - `expense_vouchers` (`id`, `uuid`, `user_id`, `batch_id` nullable, `payer_member_id`, `category_id` NOT NULL, `name`, `description`, `expense_time`, timestamps; FKs + indexes).
   - `voucher_records` (`id`, `uuid`, `user_id`, `voucher_id`, `member_id`, `amount` DECIMAL **CHECK >= 0**, `note`, timestamps).
   - `voucher_tags` (`voucher_id`, `tag_id`, `user_id`, `created_at`, PK `(voucher_id, tag_id)`, cascade on voucher/tag delete).
   - `audit_logs` (`id`, `uuid`, `user_id`, `entity_type` enum, `entity_id`, `action` enum, `old_values` JSON, `new_values` JSON, `created_at`; index `(user_id, entity_type, entity_id, created_at)`).
2. Entities + `ConfigureModel` for all four; register them.

**Acceptance:** DDL appended; entities map; build green.

---

## Stage 5.2 — Audit infrastructure
1. `Services/Audit/IAuditLogger.cs` (+ impl): `Log(entityType, entityId, action, oldSnapshot, newSnapshot, userId)` — serializes business fields to JSON, inserts an `audit_logs` row **within the caller's transaction** (no commit of its own).
2. Snapshot helpers: voucher → `{ name, description, payer_member_id, category_id, expense_time, tag_ids }`; record → `{ member_id, amount, note }`.
3. Decide mechanism: explicit calls in service write paths (simpler, chosen) **or** an EF `SaveChanges` interceptor scoped to the two entity types. Document the choice.

**Acceptance:** a CREATE produces one audit row with `old=null`, populated `new`.

---

## Stage 5.3 — Create voucher
1. `Services/Api/Vouchers/VoucherService.cs` (interface + impl), `Create(request)` inside `ExecuteTransactionAsync`:
   - Resolve payer (default owner member); resolve category (default `is_default`); validate all FKs same-user (else `NoCommit()` + 400/404).
   - If batch given: validate ownership + `OPEN` + `expense_time` in range (full guard added in phase 6).
   - Insert voucher; insert records from payload; **auto-insert creator record (`is_owner`) `amount = 0`** if absent.
   - Insert `voucher_tags` for `tag_ids` (validate same-user).
   - Audit `CREATE` (voucher) [+ optionally per-record CREATE].
2. `Models/Requests/CreateVoucherRequest.cs` (records[], category_id?, tag_ids?, payer_member_id?).

**Acceptance:** voucher persists with records + tags + one CREATE audit row; bad FK rolls everything back.

---

## Stage 5.4 — Update voucher (info + tags)
1. `Update(id, request)`: load tracked scoped-by-user (404 if not owned); capture old snapshot; update `name/description/payer_member_id/expense_time/category_id`; **replace** tag set from `tag_ids`; audit `UPDATE` only if changed.
2. Batch-lifecycle guard added in phase 6 (minor phase 6.A).

**Acceptance:** fields + tag set updated; audit UPDATE has correct old/new; no-op change writes no audit.

---

## Stage 5.5 — Delete voucher
1. `Delete(id)`: scoped load (404); within transaction delete records + `voucher_tags` + voucher; audit `DELETE` (old snapshot, new=null).

**Acceptance:** cascade delete verified; one DELETE audit row remains.

---

## Stage 5.6 — Record sub-endpoints
1. `POST /vouchers/:id/records` — add record (validate member same-user, amount≥0); audit `VOUCHER_RECORD/CREATE`.
2. `PUT /vouchers/:id/records/:recordId` — update `amount/note/member_id`; audit `UPDATE`.
3. `DELETE /vouchers/:id/records/:recordId` — delete; audit `DELETE`. Guard: keep ≥0 records (decide if last record removable).

**Acceptance:** each op persists + writes its audit row; ownership enforced.

---

## Stage 5.7 — Audit history endpoint
1. `GET /api/vouchers/:id/audit` — read `audit_logs` for the voucher and its records (`entity_id` in voucher id + record ids), ordered by `created_at`, paginated. Scoped by user.

**Acceptance:** returns chronological history including deleted records.

---

## Stage 5.8 — List & filters
1. `GET /api/vouchers` — paginated; filters `batch_id`, time range, `category_id`, `tag_id` (join `voucher_tags`). `ProjectTo<VoucherDto>` including category + tags.

**Acceptance:** each filter narrows correctly; pagination + total returned.

---

## Stage 5.9 — Controller & DTOs
1. `Controllers/Common/VouchersController.cs` — all endpoints above, Swagger annotations.
2. DTOs: `VoucherDto`, `VoucherRecordDto`, `AuditLogDto`.

---

## Stage 5.10 — Tests
1. Create-atomicity (bad FK → full rollback); creator auto-record; tag replace-all; amount<0 rejected; audit CREATE/UPDATE/DELETE rows; no-op update writes no audit; history includes deleted record; cross-user FK → 404/400.

---

## Impact Analysis
- **APIs:** `/api/vouchers/*` (+ records, audit).
- **Database:** `expense_vouchers`, `voucher_records`, `voucher_tags`, `audit_logs`.
- **Services:** `VoucherService`, `AuditLogger`, repos.

## Open questions / Assumptions
- Per-record audit on voucher create: log voucher only, or also each record? (Assume **voucher-level CREATE** captures records in snapshot; record-level audit only for the record sub-endpoints — confirm.)
- Can the last remaining record be deleted? (Assume the creator `amount=0` record stays; confirm.)
- Tag input: ids only, or accept names and upsert? (Assume **ids only**; tag creation is a separate endpoint.)

## Progress log
- (pending)

## Final outcome
- (to be completed)
