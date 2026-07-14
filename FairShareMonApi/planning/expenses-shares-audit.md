# Expenses + Shares + Audit (Milestone 5: Expenses + Shares + Audit)

Atomic **expense** (phiếu chi tiêu) + **share** (phần gánh) CRUD with share sub-routes, the **settled**
(đã trả) flag, list filters, the §4.2 cross-user link-integrity + §4.8 not-selectable-when-deleted
enforcement, and an immutable **audit log** (nhật ký thay đổi) over expenses and shares. This is the
first milestone that consumes the M4 category/tag tables (closing the M4 OQ10 deferral of the
expense↔category FK and expense↔tag join table) and the M3 member table.

## Objective

Implement `The-ideal.md` §3.5 (Phiếu chi tiêu & phần gánh) and §3.8 (Nhật ký thay đổi) on top of the
shipped Auth + Members + Categories + Tags skeleton:

- **Create an expense atomically with its shares** — all-or-nothing (§4.5); never a half-created
  expense missing its shares. Fields: name, description, expense_time, payer, category, tags, share
  list. Defaults on create (§3.5): no payer → owner-representative member; no category → default
  category; the owner-rep member is always present in the share list (0đ if not entered — §5 lock).
- **Update expense general info** (name, description, expense_time, payer, category, tag set) —
  separate from editing shares.
- **Share sub-routes:** add / edit / delete an individual share (amount, note, change member),
  distinct from editing the expense's general info.
- **Delete expense** — cascades to all its shares, atomically.
- **Settled flag (đã trả):** mark an expense settled — payment metadata, not an expenditure figure;
  doesn't change amounts. In M5 all expenses are loose (events are M6); design the flag so M6's
  "sole write allowed on a closed-event expense" (§3.6/§4.4) fits cleanly.
- **List + filters:** list expenses filtered by time range, category, tag, settled/unsettled status
  (§3.5). The event filter is an M6 seam.
- **Money safety (§4.3):** non-negative amounts, no float rounding, a DB CHECK constraint.
- **§4.2 link integrity + §4.8 not-selectable-when-deleted:** payer_member, each share's member, the
  category, and every tag must belong to the **same user** as the expense and be **active**
  (not soft-deleted) at create/update time; old expenses keep and display deleted links.
- **Audit log (§3.8):** every create/update/delete of an expense or share writes an immutable record
  (who, when, action, before + after). No-op edits produce no log. The log survives deletion of the
  source expense/share, shares the fate of its operation (fail → no log), and is viewable per expense,
  time-ordered.

This milestone owns the **expenses**, **shares**, **expense_tags** (join), and **audit_logs** tables.
It reuses the M3 `MemberRepository`, M4 `CategoryRepository`/`TagRepository`, and the established
resource-owned / transaction / soft-delete conventions. Events (§3.6), debt balance (§3.7), stats
(§3.9), export (§3.5 CSV), and QR (§3.10) are later milestones — read only to respect the boundary.

## Background

- **M3 (`planning/members.md`)** shipped `members` (owner-rep flag, soft-delete) + `MemberRepository`
  (resource-owned, `ListByUserAsync`, `GetByUuidAsync`). The owner-rep member is the create-time payer
  default and is always present in the share list.
- **M4 (`planning/categories-and-tags.md`)** shipped `categories` (default-category invariant,
  color/icon, soft-delete) + `tags` (name-only, soft-delete, reactivation) with `CategoryRepository`/
  `TagRepository`. **OQ10 explicitly deferred the expense↔category FK, the expense↔tag join table, and
  the §4.2 cross-user link validation to THIS milestone.** M5 builds them.
- Conventions confirmed by reading the live code (identical across M2–M4):
  - Entities: partial POCO `Database/Entities/<Name>.cs` + `Database/Entities/Partials/<Name>.cs`
    (ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt = AppDateTime.Now`; static
    `ConfigureModel(ModelBuilder)` invoked from `AppDbContext.OnModelCreating`). `IEntity` = `ulong
    Id`, `string Uuid` (unique, max 64), `CreatedAt`, `UpdatedAt` (`ValueGeneratedOnAddOrUpdate` +
    `current_timestamp(6) ON UPDATE current_timestamp(6)`). Snake_case columns; UTC timestamps
    (`AppDateTime.Now`). FK `HasOne(...).WithMany().HasForeignKey(...)`.
  - Soft delete: `IEntityDeletable { bool IsDeleted }`; `BaseRepository.Query<T>(tracking,
    includeDeleted)` auto-excludes `IsDeleted` rows unless `includeDeleted: true`.
  - Repositories: interface + sealed impl in one file, `[ScopedService(typeof(IX))]`, extend
    `BaseRepository`; reads via `ExecuteQueryAsync`, writes via **one** `ExecuteTransactionAsync` with
    `TransactionContext.NoCommit()` on business failure. `ExecuteTransactionAsync` **cannot nest**
    (records own transaction; recorded since M2) — so a multi-table atomic write must live in a single
    transaction block, and sub-operations that also need atomicity must stage rows onto the shared
    `AppDbContext` inside that block (as the M3/M4 bootstrap steps do), never call another repository's
    own-transaction method. Resolve `user_id` from the caller's UUID (`ResolveUserIdAsync` pattern).
  - The `NameWriteResult<T>` / `NameWriteStatus` pattern (M4) is the model for returning a typed
    write outcome from a transactional repo method; M5 introduces an analogous typed result for
    expense/share writes (link-invalid variants) rather than throwing across the transaction boundary.
  - Controllers derive from `AppController` (LOCKED); routes `api/v{version:apiVersion}/[controller]`;
    `[ResponseWrapped]` auto-wraps into `ApiResult<T>`; `AuthenticatedUser.Id` = current user's UUID.
    Vietnamese `[SwaggerOperation]`/`[SwaggerResponse]`.
  - Errors: `ErrorCodes` — 1xxx infra, 2xxx auth, 3xxx members, 4xxx categories, 5xxx tags;
    `ErrorException(code, message)` → HTTP via `GetDefaultHttpStatus`. Vietnamese messages.
  - Validation: FluentValidation, auto-registered by `AddValidatorsFromAssembly`; services call
    `ValidateAndThrowAsync` (→ `ValidationException` → 400 with `error.fields` camelCase).
  - No CHECK constraint exists in the codebase yet — M5 introduces the first, via
    `entity.ToTable(t => t.HasCheckConstraint(name, sql))` in the migration.
- The dev DB holds no real product data beyond disposable smoke rows.

## Requirements

From `The-ideal.md` §2 (concepts), §3.5, §3.8, §5, §4.1/§4.2/§4.3/§4.5/§4.7/§4.8, and the conventions:

**Expenses (§2, §3.5):**
- An expense has: name, description, expense_time (thời điểm chi), payer (a member), category, tags
  (0..n), and a list of shares. **`The-ideal.md` §2 fixes the concept: "Tổng tiền của phiếu = tổng các
  phần gánh"** — the expense total is definitionally the sum of its shares (not a separately entered,
  reconciled figure). See OQ1 for derive-vs-cache.
- Create is **atomic with shares** (§4.5). Create defaults: payer → owner-rep member; category →
  default category; owner-rep share always present (0đ if absent — §5 lock).
- Update general info (name/description/expense_time/payer/category/tags) separately from shares.
- Delete an expense cascades to all its shares, atomically (§4.5).
- Settled flag (đã trả) — payment metadata, doesn't alter amounts (§3.5).
- List filtered by time range, category, tag, settled state (§3.5); event filter deferred to M6.

**Shares (§2, §3.5):**
- A share is one member's borne amount in one expense, with an optional note. Add / edit (amount,
  note, **change member**) / delete individual shares.

**Cross-cutting:**
- **Absolute privacy / resource-owned (§4.1):** every query scoped `WHERE uuid = :uuid AND user_id =
  :current_user_id`; ownership miss → **404, never 403**.
- **Link integrity within a ledger (§4.2):** payer, share members, category, and tags must belong to
  the same user as the expense.
- **Not-selectable-when-deleted (§4.8):** a soft-deleted member/category/tag cannot be chosen for a
  **new/edited** expense or share; but existing links to now-deleted resources stay intact and display
  full info (§4.7).
- **Money accuracy (§4.3):** amount ≥ 0 (DB CHECK), no float.
- **Atomicity (§4.5):** create/delete expense + shares all-or-nothing; audit write shares the same
  transaction (§3.8 "thao tác thất bại thì không có log").

**Audit (§3.8):**
- Scope: expenses and shares — every create/update/delete. Record who/when/action/before+after.
  No-op edit → no log. Immutable (no edit/delete). Survives source deletion. View per expense,
  time-ordered.

## Open Questions

> **All 20 answered by the user at the 2026-07-14 checkpoint — every recommended option (a) was
> accepted** (OQ9 and OQ10 were presented together and both accepted). The struck questions below
> carry the binding answers inline; the full options/trade-offs are preserved for the record and
> mirrored in the Decision Log. No open questions remain — implementation can start. The
> Implementation Plan, entity/schema section, error-code table, endpoint table, and test list below are
> synced to these answers. Decisions already locked in spec §5 (total = sum of shares; keep owner 0đ
> share in every expense; domain terms) were NOT reopened.

**OQ1 — Expense total: derived vs stored/cached.**
> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** the expense total is **derived** — `SUM(shares.amount)`
> computed on read; there is **no `total`/`amount` column** on `expenses`.
Spec §2 fixes *total = sum(shares)* as a concept — there is no separately entered "total" to reconcile
(so "must shares sum to the total?" is moot; the total *is* the sum). Open: whether the sum is
computed on read or cached in a column.
- **(a) [recommended] Derive on read** — no `amount`/`total` column on `expenses`; compute
  `shares.Sum(amount)` in the query/projection. Trade-off: a `SUM` per expense on list/get (cheap;
  indexed by `expense_id`), but a single source of truth with zero drift risk (§4.3) and no
  recompute-on-every-share-mutation code.
- **(b) Cached `total_amount` column** recomputed inside every expense/share write transaction.
  Trade-off: faster list/stats reads (helps M7), but introduces a value that can drift from the shares
  and must be re-summed on every share add/edit/delete — an accuracy hazard the spec warns against.

**OQ2 — Money representation + CHECK constraint (VND has no minor unit).**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** money = **`decimal(18,2)`** with a DB **CHECK
> `amount >= 0`** — the codebase's **first** CHECK constraint (noted in the migration + Impact).
- **(a) [recommended] `decimal(18,2)`** for `shares.amount`, with a DB CHECK `amount >= 0`.
  Trade-off: stores a `.00` fraction VND never uses, but matches CLAUDE.md's DECIMAL-first money rule,
  maps cleanly through EF/Pomelo, and accommodates the stated future **multi-currency** feature (§6)
  without a type migration.
- **(b) Integer smallest-unit — `bigint` whole VND.** Trade-off: the most literal fit for a
  no-minor-unit currency and makes fractional rounding *impossible*, but a future fractional currency
  (§6) would need a type/scale migration, and it diverges from the DECIMAL convention.
- **(c) `decimal(18,0)`** (whole-number decimal). Trade-off: middle ground — decimal type, no stored
  fraction — but still needs a scale migration for multi-currency and is an unusual scale choice.

**OQ3 — Expense & share deletion model: hard vs soft.**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** expenses and shares are **hard-deleted** (neither
> implements `IEntityDeletable`); deleting an expense cascades a physical delete of its shares +
> `expense_tags`; the immutable audit log retains the before-state.
§3.8 says the audit log "vẫn tồn tại kể cả khi phiếu/phần gánh gốc đã bị xóa" (survives after the
expense/share is deleted), and §4.7's soft-delete list names only member/category/tag — implying
expenses/shares are physically removed and the audit log *is* their history.
- **(a) [recommended] Hard delete** — deleting an expense physically removes it and cascades a
  physical delete of its shares (and `expense_tags`); the immutable audit log preserves the
  before-state. Deleting a share physically removes it; audit preserves it. Matches §3.8 wording and
  keeps deleted expenses out of debt balance (M7) and stats — deleting an expense means "it never
  happened", unlike deleting a member ("stop using, keep history"). Trade-off: recovery is only via
  the audit snapshot (already listed as a §6 future "restore from snapshot").
- **(b) Soft delete** expenses/shares too (`is_deleted`). Trade-off: uniform with members/categories
  and trivially satisfies "survives deletion", but contradicts the §4.7 scope, and every list/stats/
  balance query must then exclude deleted expenses — and a "deleted" expense lingering in the table is
  semantically odd for a mistaken entry.

**OQ4 — Owner-rep share always-present enforcement (§5 lock) + zero amount.**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** on create, **auto-inject a 0đ owner-representative
> share** when the submitted list omits it; **block deleting** the owner-rep's share (`7002`); editing
> its amount/note (incl. to non-zero) is allowed and its member cannot be changed away. (§5 lock;
> 0đ valid per §4.3.)
§5 locks "Giữ phần gánh 0 đồng của chủ sổ trong mọi phiếu" and §4.3 allows amount ≥ 0 (so 0đ is
valid). Open: the mechanism.
- **(a) [recommended]** On **create**, if the submitted share list omits the owner-rep member,
  auto-inject a 0đ owner-rep share; and **block deleting the owner-rep's share** on any expense
  (error `7002 OwnerRepresentativeShareNotDeletable`), mirroring "owner-rep member not deletable"
  (M3 OQ4). Editing the owner-rep share's amount/note (including to non-zero) is allowed; the
  owner-rep share's member cannot be changed away. Trade-off: one guard + one auto-inject, but exactly
  realizes the §5 lock.
- **(b)** Auto-inject on create but allow deleting the owner-rep share afterward. Trade-off: simpler,
  but violates "keep the owner in every expense's split screen".
- **(c)** Require the client to always send the owner-rep share (reject create if missing). Trade-off:
  strictest, but pushes an invariant the server can satisfy itself onto every client.

**OQ5 — Duplicate share members on one expense.**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** **forbid duplicates** — one share per member per
> expense (`7003`), backed by a **unique index on `(expense_id, member_id)`** plus the in-transaction
> check.
- **(a) [recommended] Forbid** — at most one share per member per expense (app-level check; error
  `7003 DuplicateShareMember`). Trade-off: needs an in-transaction uniqueness check on create/add/
  edit-change-member, but matches the "danh sách phần gánh" = one row per member split-screen model and
  makes the owner-rep-always-present rule unambiguous. (MariaDB can also back this with a unique index
  on `(expense_id, member_id)` — see note.)
- **(b) Allow duplicates** — a member may hold several share lines; debt balance sums them (§3.7 "phải
  gánh = tổng các phần gánh"). Trade-off: more flexible for odd cases, but complicates the split screen
  and lets "change member" silently create a second line for someone.

**OQ6 — Expense↔tag join table shape.**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** **lightweight composite-PK `expense_tags`** join
> (no id/uuid/soft-delete), FK `expense_id` cascade + FK `tag_id` restrict; a soft-deleted tag stays
> linked on existing expenses (§4.7) but is not selectable for new/edited expenses (§4.8).
- **(a) [recommended] Lightweight join** `expense_tags` with a **composite PK (expense_id, tag_id)**,
  FK `expense_id` → `expenses.id` (**cascade delete**, so tags detach when an expense is hard-deleted)
  and FK `tag_id` → `tags.id` (restrict — tags are soft-deleted). No `id`/`uuid`/soft-delete on the
  join; a soft-deleted tag stays linked (its row remains) and still displays on old expenses (§4.7).
  Trade-off: deviates from the "every entity has id+uuid" convention, but a pure link table needs
  neither and this is the standard EF `HasMany().WithMany().UsingEntity<ExpenseTag>` shape.
- **(b) Full `IEntity` join** (id + uuid + timestamps). Trade-off: convention-uniform, but adds a
  surrogate key + uuid a link table never addresses individually.

**OQ7 — Expense↔category / expense↔payer FK requiredness + on-delete.**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** `category_id` and `payer_member_id` both
> **required**, `OnDelete(Restrict)` — inert in practice since categories/members are soft-deleted.
- **(a) [recommended]** `category_id` **required** (a default category always exists, so an expense can
  always be assigned one) FK → `categories.id`; `payer_member_id` **required** FK → `members.id`;
  both **`OnDelete(Restrict/NoAction)`**. Because categories/members are **soft-deleted** (never hard-
  deleted), the on-delete behavior is inert in practice — kept for referential integrity. Trade-off:
  none significant; consistent with treating category/payer as always-present.
- **(b)** Nullable `category_id` (fall back to default at read time). Trade-off: needs read-time
  fallback logic and contradicts "phiếu vì thế luôn có danh mục hợp lệ" (§4.6).

**OQ8 — Event seam (M6).**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** **defer the entire event relationship to M6** — NO
> `event_id` column on `expenses` now (mirrors M4 OQ10); leave a clean seam. The list endpoint gains
> no event filter until M6.
- **(a) [recommended] Defer entirely to M6** — no `event_id` column on `expenses` in M5; the
  expense↔event FK, the "expense_time within event range" validation (§3.6), the closed-event write
  block (§4.4), and the list-by-event filter are all added by M6 together. Mirrors how M4 (OQ10)
  deferred linking until the consumer existed. Trade-off: the M5 list endpoint has no event filter yet
  (added in M6); avoids shipping an FK-less orphan column with no integrity.
- **(b) Add a nullable `event_id` column now (no FK, no events table).** Trade-off: the list filter
  could be wired now, but the column has no referential integrity until M6 and pre-empts M6's join-
  design decisions.

**OQ9 — Audit storage model.**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** **one `audit_logs` table** for both expenses and
> shares — `entity_type`, `entity_uuid`, `expense_uuid` stored as **plain values with no FK** (survive
> hard-deletes), `action`, actor (current user), `before_json`, `after_json`, `created_at`; **no-op
> edits (snapshots equal) produce no row.**
- **(a) [recommended] One `audit_logs` table + full JSON before/after snapshots.** Columns:
  `entity_type` (enum: `Expense` | `Share`), `entity_uuid` (plain value, **no FK**), `expense_uuid`
  (plain value, **no FK** — set on both expense and share rows so history groups per expense even
  after the expense is hard-deleted), `action` (enum: `Create` | `Update` | `Delete`),
  `actor_user_id`, `before_data` (JSON, null on Create), `after_data` (JSON, null on Delete),
  `created_at` (when). No-op detection: serialize before/after and skip the write when equal.
  Trade-off: snapshots are larger than diffs, but simplest, matches §3.8 "giá trị trước và sau"
  literally, and per-field diffing is explicitly a §6 future item.
- **(b) Per-field diff records** (one row per changed field). Trade-off: compact and query-friendly,
  but more code now and beyond the spec's stated scope.
- **(c) Separate `expense_audit_logs` + `share_audit_logs` tables.** Trade-off: no `entity_type`
  column, but two tables + two read paths for one time-ordered per-expense view.

**OQ10 — Audit granularity on expense create/delete + snapshot content.**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a, together with OQ9):** **per-entity rows** — creating
> an expense with N shares writes 1 Expense/Create + N Share/Create rows (delete similarly); snapshots
> store **denormalized names** (payer/category/member/tag names) alongside uuids so history reads
> correctly after those are renamed/deleted.
- **(a) [recommended] Per-entity rows.** Creating an expense with N shares writes 1 `Expense`/`Create`
  row + N `Share`/`Create` rows (all in the one transaction); deleting writes 1 `Expense`/`Delete` +
  N `Share`/`Delete`. Snapshots store **denormalized display names** alongside uuids (payer name,
  category name, member name, tag names) so the history stays readable after those are renamed/deleted.
  Trade-off: more rows per create, but every share carries its own trail from birth and the per-share
  edit history is uniform.
- **(b) Single embedded snapshot** — one `Expense`/`Create` row whose JSON embeds the full share list;
  later per-share edits write their own `Share` rows. Trade-off: fewer rows, but create and per-share
  history are represented inconsistently.

**OQ11 — Does marking settled produce an audit entry?**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** **no** audit entry for a settled toggle — it is
> payment metadata, not expenditure số liệu (§3.5); `settled_at` records the last toggle.
- **(a) [recommended] No.** Settled is explicitly "metadata thanh toán, không phải số liệu chi tiêu"
  (§3.5); the audit exists to resolve disputes about số liệu ("sao phần tôi thành 200k"). Trade-off:
  the settled history isn't in the audit log (a `settled_at` timestamp records the last toggle).
- **(b) Yes** — treat any expense update, including settled, as an audited change (§3.8 "mọi lần
  sửa"). Trade-off: literal to §3.8's wording, but pollutes the dispute-oriented log with
  non-expenditure toggles.

**OQ12 — Settled representation + endpoint shape.**
> ~~**OQ12**~~ → **Answered 2026-07-14 (option a):** `is_settled` (bool) + nullable `settled_at`;
> endpoint **`PUT api/v1/expenses/{uuid}/settled`** body `{ isSettled }`; exposed as a **dedicated
> service method** so M6's closed-event settled-exception fits cleanly.
- **(a) [recommended]** `is_settled` (bool, default false) + nullable `settled_at` (datetime, set when
  toggled true, cleared when toggled false); endpoint `PUT api/v1/expenses/{uuid}/settled` with body
  `{ isSettled: bool }` (idempotent explicit set). Trade-off: one column more than strictly needed, but
  records *when* it was settled cheaply and the explicit-set body is unambiguous. The service exposes
  this as a **dedicated method** so M6 can allow it while blocking all other writes on closed-event
  expenses (§4.4).
- **(b)** `is_settled` bool only; `PATCH .../settled` with body, or two routes
  (`.../settle` + `.../unsettle`). Trade-off: minimal, but loses the "when" and (two-route form) adds
  routes.

**OQ13 — List filters, sort, pagination, list vs detail shape.**
> ~~**OQ13**~~ → **Answered 2026-07-14 (option a):** filters `from`/`to` (inclusive datetime) +
> `categoryUuid` + `tagUuid` + `settled`, **AND-combined**; sort **`expense_time` DESC**; **no
> pagination** this milestone; **list returns a summary DTO, `GET /{uuid}` returns the full DTO** (with
> shares + tags + derived total).
- **(a) [recommended]** Filters `from`, `to` (datetime, **inclusive** `[from, to]`), `categoryUuid`,
  `tagUuid`, `settled` (bool) — combined with **AND**; default sort **`expense_time` DESC**, then
  `created_at` DESC. **No pagination this milestone** (return the full owned list, mirroring
  members/categories; tier limits in M10 cap volume). List returns a **summary** DTO
  (`ExpenseSummaryResponse`: uuid, name, expense_time, total, category, payer, isSettled, tag names,
  share count); `GET /{uuid}` returns the **full** `ExpenseResponse` (incl. shares + tags). Trade-off:
  a large ledger returns many rows, but consistent with existing list endpoints.
- **(b)** Add offset/limit (or cursor) pagination now. Trade-off: scales better, but introduces a
  pagination envelope none of the shipped list endpoints use yet.

**OQ14 — `expense_time` bounds in M5.**
> ~~**OQ14**~~ → **Answered 2026-07-14 (option a):** **no `expense_time` bounds** in M5 — the
> within-event-range rule (§3.6) is added by M6.
- **(a) [recommended] No bounds** — accept any valid datetime; the "within event range" rule (§3.6) is
  added by M6 (all M5 expenses are loose). Trade-off: a typo'd far-future/past date is accepted until
  M6, but M5 has no event to validate against.
- **(b) Reject future-dated expenses** (expense_time > now). Trade-off: catches typos, but the spec
  never forbids future dates and legitimately-scheduled entries would be blocked.

**OQ15 — Error-code block allocation.**
> ~~**OQ15**~~ → **Answered 2026-07-14 (option a):** **6xxx Expenses / 7xxx Shares / 8xxx Audit
> (reserved)** with the concrete codes below (incl. `7002` owner-rep-share-not-deletable, `7003`
> duplicate-member, and the §4.2 link-invalid `6001/6002/6003/7001`); extend
> `ErrorException.GetDefaultHttpStatus`.
- **(a) [recommended]** Expenses = **6xxx**, Shares = **7xxx**, Audit = **8xxx** (reserved; no codes
  needed now — the history read reuses `ExpenseNotFound`/empty-list semantics). Concrete codes:
  `6000 ExpenseNotFound` (404), `6001 ExpensePayerInvalid` (400), `6002 ExpenseCategoryInvalid` (400),
  `6003 ExpenseTagInvalid` (400); `7000 ShareNotFound` (404), `7001 ShareMemberInvalid` (400),
  `7002 OwnerRepresentativeShareNotDeletable` (400), `7003 DuplicateShareMember` (400).
  Extend `ErrorException.GetDefaultHttpStatus`. Trade-off: none; continues the one-block-per-feature
  pattern (2xxx…5xxx).
- **(b)** One shared block, or fold the three link-invalid codes into a single
  `ExpenseLinkInvalid`. Trade-off: denser, but clients lose the payer/category/tag distinction.

**OQ16 — Field lengths + description optionality.**
> ~~**OQ16**~~ → **Answered 2026-07-14 (option a):** `name` required max **200**; `description`
> optional max **1000**; share `note` optional max **500**.
- **(a) [recommended]** `name` required, max **200**; `description` **optional/nullable**, max
  **1000**; share `note` optional/nullable, max **500**. Trade-off: expense names run longer than the
  100-char member/category names, so 200; the others are generous defaults.
- **(b)** Match the members/categories `name` max (100) and pick different description/note caps.
  Trade-off: cross-entity consistency vs room for descriptive expense names.

**OQ17 — History endpoint behavior for a deleted/unknown expense.**
> ~~**OQ17**~~ → **Answered 2026-07-14 (option a):** the history read scopes audit by **user +
> expense_uuid**, time-ordered, and returns an **empty list** when none — a deleted-but-owned expense
> still returns its rows; a foreign/unknown uuid returns empty (leaks nothing).
Because an expense can be hard-deleted (OQ3=a) yet still have audit history (§3.8), the history read is
scoped by `actor_user_id` + `expense_uuid` on `audit_logs`, **not** by the (possibly gone) expense row.
- **(a) [recommended]** `GET /expenses/{uuid}/history` returns the audit rows for that
  user+expense_uuid, time-ordered; **empty list** when there are none. A deleted-but-owned expense
  still has its rows (so its history is viewable per §3.8); an unknown/foreign uuid has none → empty.
  Trade-off: a foreign/unknown uuid returns an empty list rather than 404 — acceptable
  (resource-owned: it leaks nothing), and required so a deleted-but-owned expense's history is
  viewable.
- **(b)** 404 when the expense row doesn't currently exist. Trade-off: breaks §3.8 ("view history even
  after the expense is deleted").

**OQ18 — Update-general-info tag-set semantics.**
> ~~**OQ18**~~ → **Answered 2026-07-14 (option a):** **full replace** — `PUT /expenses/{uuid}` carries
> the complete `tagUuids`; the service diffs it against the current set and adds/removes join rows in
> one transaction.
- **(a) [recommended] Full replace** — `PUT /expenses/{uuid}` carries the complete `tagUuids` list; the
  service diffs it against the current set and adds/removes join rows to match, in one transaction.
  Trade-off: the client sends the whole set each time (natural for a multi-select), simplest server
  logic.
- **(b) Delta** — separate add/remove tag operations (or add/remove arrays). Trade-off: finer-grained,
  but adds routes/shape the "edit the expense's tag set" UX doesn't need.

**OQ19 — Endpoint surface confirmation.**
> ~~**OQ19**~~ → **Answered 2026-07-14 (option a):** the Step-8 route table confirmed — nested share
> routes `POST/PUT/DELETE /expenses/{uuid}/shares[/{shareUuid}]`, `PUT /{uuid}/settled`,
> `GET /{uuid}/history`, all under `api/v1/expenses`, guarded, resource-owned.
- **(a) [recommended]** The full route table in Step 9 below: expense list (filters) / get / create /
  update-general-info / delete; `PUT /{uuid}/settled`; share sub-routes
  `POST|PUT|DELETE /expenses/{uuid}/shares[/{shareUuid}]`; `GET /expenses/{uuid}/history`. All under
  `api/v1/expenses`, all guarded, resource-owned. Trade-off: none; confirms shapes before coding.
- **(b)** Any change (e.g. a top-level `api/v1/shares/{uuid}` instead of nested; a combined
  update-with-shares endpoint). Trade-off: flat share routes lose the owning-expense scoping in the
  path; a combined update blurs the §3.5 "edit general info separate from editing shares" split.

**OQ20 — Audit build/stage placement (transaction boundary).**
> ~~**OQ20**~~ → **Answered 2026-07-14 (option a):** audit is built by a pure **`AuditLogFactory`** and
> staged via `db.AuditLogs.Add(...)` **inside the repository's `ExecuteTransactionAsync`** — the audit
> shares the mutation's fate, no nested transaction; the transaction stays in the repository.
- **(a) [recommended]** A pure `AuditLogFactory` (no DB, no transaction) builds `AuditLog` rows from
  before/after entity states; `ExpenseRepository`/`ShareRepository` call it **inside** their existing
  `ExecuteTransactionAsync` and `db.AuditLogs.Add(...)`, so the audit shares the mutation's transaction
  (§3.8 "fail → no log"). Reads via `AuditLogRepository.ListByExpenseAsync`. The transaction stays in
  the repository (existing pattern). Trade-off: the repo references the factory, but keeps the
  established "transaction in the repository" architecture and guarantees atomicity without nesting.
- **(b)** Move the transaction to the service layer so the service can wrap repo calls + audit writes.
  Trade-off: lets the service orchestrate, but breaks the codebase-wide "transaction in the repository"
  convention and spreads DbContext usage into services.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 20 Open Questions — these
> are now decisions, not vetoable assumptions. Each is derived from spec/prior decisions and the
> answered OQs.

- All expense/share/audit endpoints are **guarded** (valid access token required); no anonymous
  operation.
- `users` are not soft-deletable; members/categories/tags are soft-deleted and never hard-deleted, so
  FK on-delete from expenses/shares to them is inert in practice (kept for integrity).
- The audit write lives **inside the same `ExecuteTransactionAsync`** as the mutation it records
  (mandated by §3.8 + CLAUDE.md transactions rule) — this is treated as settled, not an OQ; only its
  *placement* (OQ20) and *content/model* (OQ9/OQ10) are open.
- The **actor** on every audit row is the current authenticated user (only the owner touches their own
  data); `actor_user_id` is stored anyway for the future shared-event feature (§6).
- `AuditLog` is immutable — never updated or deleted by any code path; it implements `IEntity` for the
  `uuid` + `created_at` conventions, with `updated_at` present but inert (never written after insert).
- Tier limits on expenses/shares (§3.11: "K phiếu/tháng") are **out of scope** (M10); M5 imposes no
  count limit.
- Export (§3.5 CSV), events (§3.6), debt balance (§3.7), stats (§3.9), and QR (§3.10) are later
  milestones; M5 leaves seams (no event_id per OQ8; total derivable for stats) but builds none of them.
- Snapshots are serialized with `System.Text.Json`; the JSON columns are `longtext`/`json`.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repos use DiDecoration
> `[ScopedService]`. All user-facing strings Vietnamese. Concrete names reflect the **confirmed** OQ
> answers (all option (a), accepted at the 2026-07-14 checkpoint) — no further re-sync needed.

### Step 1 — Entities

1. `Database/Entities/Expense.cs` (POCO, `partial`, `IEntity`; **not** `IEntityDeletable` — hard
   delete per OQ3): `ulong Id`, `string Uuid`, `ulong UserId` (FK → `users.id`), `required string
   Name`, `string? Description`, `DateTime ExpenseTime`, `ulong PayerMemberId` (FK → `members.id`),
   `ulong CategoryId` (FK → `categories.id`), `bool IsSettled`, `DateTime? SettledAt`, `DateTime
   CreatedAt`, `DateTime UpdatedAt`; navs `User User`, `Member PayerMember`, `Category Category`,
   `ICollection<Share> Shares`, `ICollection<Tag> Tags` (through `ExpenseTag`).
2. `Database/Entities/Partials/Expense.cs`: ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt =
   AppDateTime.Now`; `NameMaxLength = 200`, `DescriptionMaxLength = 1000` (OQ16); static
   `ConfigureModel`: table `expenses`; `uuid` (max 64, unique index); `user_id`, `payer_member_id`,
   `category_id`, `expense_time`, `is_settled` (all indexed as noted below); `is_settled` default
   false; `updated_at` computed default. FKs `HasOne(User)/PayerMember/Category .WithMany()
   .OnDelete(Restrict)` for payer/category, cascade for `User` (mirrors siblings). Indexes:
   `IX_expenses_user_id`, and a composite `(user_id, expense_time)` to serve the default sort + range
   filter.
3. `Database/Entities/Share.cs` (POCO, `partial`, `IEntity`; hard delete): `ulong Id`, `string Uuid`,
   `ulong ExpenseId` (FK → `expenses.id`, **cascade**), `ulong MemberId` (FK → `members.id`,
   restrict), `decimal Amount`, `string? Note`, `DateTime CreatedAt`, `DateTime UpdatedAt`; navs
   `Expense Expense`, `Member Member`.
4. `Database/Entities/Partials/Share.cs`: ctor; `NoteMaxLength = 500`; static `ConfigureModel`: table
   `shares`; `uuid` unique; `expense_id` indexed; **unique index `(expense_id, member_id)`** (OQ5, one
   share per member); `amount` `decimal(18,2)` (OQ2) with **CHECK** `entity.ToTable(t =>
   t.HasCheckConstraint("ck_shares_amount_non_negative", "amount >= 0"))`; `note` max 500; FKs:
   `Expense` cascade delete, `Member` restrict; `updated_at` computed default.
5. `Database/Entities/ExpenseTag.cs` + `Partials/ExpenseTag.cs` (OQ6=a, lightweight join, **not**
   `IEntity`): `ulong ExpenseId`, `ulong TagId`; navs `Expense`, `Tag`. `ConfigureModel`: table
   `expense_tags`; composite PK `(expense_id, tag_id)`; FK `expense_id` → `expenses.id` **cascade**,
   FK `tag_id` → `tags.id` restrict; index on `tag_id` (serves the tag filter).
6. `Database/Entities/AuditLog.cs` + `Partials/AuditLog.cs` (`IEntity`, immutable): `ulong Id`,
   `string Uuid`, `ulong ActorUserId` (FK → `users.id`), `AuditEntityType EntityType`,
   `string EntityUuid`, `string ExpenseUuid`, `AuditAction Action`, `string? BeforeData`,
   `string? AfterData`, `DateTime CreatedAt`, `DateTime UpdatedAt` (inert). Enums `AuditEntityType
   { Expense, Share }`, `AuditAction { Create, Update, Delete }` (stored as int or string — decide with
   OQ9; recommend int). `ConfigureModel`: table `audit_logs`; `uuid` unique; index
   `(actor_user_id, expense_uuid, created_at)` to serve the per-expense time-ordered read; `before_data`
   / `after_data` as `longtext`; **no FK** on `entity_uuid`/`expense_uuid` (survive deletion). FK on
   `actor_user_id` cascade to `users`.
7. `Database/AppDbContext.cs`: add `DbSet<Expense> Expenses`, `DbSet<Share> Shares`,
   `DbSet<ExpenseTag> ExpenseTags`, `DbSet<AuditLog> AuditLogs`; invoke each `ConfigureModel` in
   `OnModelCreating`. `AppDbContext.partial.cs` untouched.

### Step 2 — EF migration

- `dotnet ef migrations add AddExpensesSharesAndAudit --project .\FairShareMonApi\FairShareMonApi.csproj`
  (offline via the pinned design-time factory). **Migration name: `AddExpensesSharesAndAudit`.**
- Review: four tables (`expenses`, `shares`, `expense_tags`, `audit_logs`), utf8mb4/unicode_ci; unique
  `uuid` indexes; the composite indexes above; FK cascade/restrict as specified; the **CHECK
  constraint** `ck_shares_amount_non_negative`; `decimal(18,2)` scale; bool/`updated_at` defaults;
  `longtext` JSON columns. Keep the model snapshot in sync. Apply to the dev DB during the Test step
  per the orchestration protocol.

### Step 3 — Error codes + messages

Append to `Constants/ErrorCodes.cs` (never renumber). **6xxx Expenses, 7xxx Shares, 8xxx Audit
(reserved)** (OQ15):

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `6000` | `ExpenseNotFound` | 404 | "Không tìm thấy phiếu chi tiêu." |
| `6001` | `ExpensePayerInvalid` | 400 | "Người trả không hợp lệ hoặc đã bị xóa." |
| `6002` | `ExpenseCategoryInvalid` | 400 | "Danh mục không hợp lệ hoặc đã bị xóa." |
| `6003` | `ExpenseTagInvalid` | 400 | "Nhãn không hợp lệ hoặc đã bị xóa." |
| `7000` | `ShareNotFound` | 404 | "Không tìm thấy phần gánh." |
| `7001` | `ShareMemberInvalid` | 400 | "Thành viên của phần gánh không hợp lệ hoặc đã bị xóa." |
| `7002` | `OwnerRepresentativeShareNotDeletable` | 400 | "Không thể xóa phần gánh của thành viên đại diện chủ sổ." |
| `7003` | `DuplicateShareMember` | 400 | "Mỗi thành viên chỉ có một phần gánh trong một phiếu." |

- Extend `ErrorException.GetDefaultHttpStatus`: `6000`→404, `6001/6002/6003`→400, `7000`→404,
  `7001/7002/7003`→400.
- `6000`/`7000` used for every resource-owned miss (never 403), preferred over generic `NotFound`.
- Success messages (via the endpoint contract): create "Thêm phiếu chi tiêu thành công."; update
  "Cập nhật phiếu chi tiêu thành công."; delete "Đã xóa phiếu chi tiêu."; settled "Đã cập nhật trạng
  thái đã trả."; share add "Thêm phần gánh thành công."; share update "Cập nhật phần gánh thành
  công."; share delete "Đã xóa phần gánh."

### Step 4 — Audit infrastructure

- `Services/Audit/AuditLogFactory.cs` (OQ20=a) — pure builder (`[ScopedService]` or static helper):
  `BuildExpenseAudit(AuditAction, Expense before?, Expense after?, ...)` and
  `BuildShareAudit(AuditAction, Share before?, Share after?, expenseUuid, ...)`; serializes
  denormalized snapshots (uuid + display name for payer/category/member/tags — OQ10) with
  `System.Text.Json`; performs **no-op detection** (returns null when a `before`/`after` pair
  serializes equal — OQ9). Called by the expense/share repositories inside their transactions and
  staged via `db.AuditLogs.Add(...)`.
- `Repositories/AuditLogRepository.cs` — `IAuditLogRepository` + sealed impl (`[ScopedService]`):
  `ListByExpenseAsync(string userUuid, string expenseUuid, ct)` → rows where `ActorUser.Uuid ==
  userUuid && ExpenseUuid == expenseUuid`, ordered `created_at` ASC (OQ17). Read-only; the write path
  is the factory staged inside the mutation transactions.

### Step 5 — Repositories

`Repositories/ExpenseRepository.cs` — `IExpenseRepository : IBaseRepository, IQueryRepository<Expense>`
+ sealed impl (`[ScopedService]`, extends `BaseRepository`, ctor also injects the `AuditLogFactory`):
- `Query(tracking, includeDeleted)` (includeDeleted inert — expenses aren't `IEntityDeletable`).
- `ListByUserAsync(userUuid, ExpenseFilter filter, ct)` — resource-owned; AND-combine `from`/`to`/
  `categoryUuid`/`tagUuid`/`settled`; sort `expense_time` DESC (OQ13); project the summary shape
  (total = `Shares.Sum(Amount)`).
- `GetByUuidAsync(userUuid, expenseUuid, ct)` — resource-owned; `Include` shares (+ members),
  category, payer, and tags for the full response. Null on miss.
- `CreateAsync(userUuid, CreateExpenseData data, ct)` — **one transaction**: resolve `user_id`; resolve
  defaults (payer→owner-rep, category→default) when omitted; **link-validate** payer/category/each
  tag/each share member are owned + **active** (soft-deleted → the matching `6001/6002/6003/7001`
  signal via a typed `ExpenseWriteResult`); enforce owner-rep-share-present (auto-inject 0đ — OQ4) and
  no-duplicate-members (OQ5); insert `expenses` + `shares` + `expense_tags`; stage
  `Expense`/`Create` + per-share `Share`/`Create` audit rows via the factory. All-or-nothing (§4.5).
- `UpdateGeneralInfoAsync(userUuid, expenseUuid, UpdateExpenseData data, ct)` — resource-owned tracked
  load; link-validate payer/category/tags; diff + apply the tag set (full replace, OQ18); capture
  before/after and stage an `Expense`/`Update` audit (skipped if no-op — OQ9). Miss → NotFound signal.
- `DeleteAsync(userUuid, expenseUuid, ct)` — resource-owned load incl. shares; stage `Expense`/`Delete`
  + per-share `Share`/`Delete` audits **before** removing; hard-delete (cascade removes shares +
  expense_tags). Miss → false.
- `SetSettledAsync(userUuid, expenseUuid, bool isSettled, ct)` — resource-owned; set `is_settled` +
  `settled_at`; **no audit** (OQ11). Exposed as a distinct method for the M6 closed-event exception.
  Miss → false.

`Repositories/ShareRepository.cs` — `IShareRepository` + sealed impl (injects `AuditLogFactory`):
- `AddAsync(userUuid, expenseUuid, ShareData data, ct)` — resource-own the expense; link-validate the
  member (owned + active → else `7001`); no-duplicate (OQ5 → `7003`); insert; stage `Share`/`Create`.
- `UpdateAsync(userUuid, expenseUuid, shareUuid, ShareData data, ct)` — resource-own via the expense;
  link-validate a changed member; owner-rep-share member-change guard; capture before/after; stage
  `Share`/`Update` (skip no-op). Miss → NotFound.
- `DeleteAsync(userUuid, expenseUuid, shareUuid, ct)` — resource-own; block deleting the owner-rep's
  share (`7002`, OQ4); stage `Share`/`Delete`; hard-delete. Miss → NotFound.

> All writes are single `ExecuteTransactionAsync` blocks with `NoCommit()` on any validation/business
> failure, so a rejected write leaves no audit row (§3.8). Typed results (`ExpenseWriteResult` /
> reuse of a `NameWriteResult`-style enum) carry the link-invalid variants back to the service.

**Create-path & §4.2/§4.8 link-validation flow (the definitive order inside `ExpenseRepository.CreateAsync`'s one transaction):**

1. **Resolve owner** — `user_id` from `userUuid`; unknown → `NoCommit()` + `NotFound` (`6000`).
2. **Resolve defaults (OQ4/§3.5)** — if `PayerMemberUuid` is null, resolve the owner-rep member's id;
   if `CategoryUuid` is null, resolve the default category's id.
3. **Link-validate every reference belongs to the same user AND is active (not soft-deleted)** — this
   is the §4.2 same-owner + §4.8 not-selectable-when-deleted enforcement, all scoped by `user_id`:
   - **payer member** — owned + not `IsDeleted`; else `NoCommit()` + `ExpensePayerInvalid` (`6001`).
   - **category** — owned + not `IsDeleted`; else `ExpenseCategoryInvalid` (`6002`).
   - **each tag in `TagUuids`** — owned + not `IsDeleted`; any miss → `ExpenseTagInvalid` (`6003`).
   - **each share's member** — owned + not `IsDeleted`; any miss → `ShareMemberInvalid` (`7001`).
   > A default resolved in step 2 is inherently owned + active (owner-rep member / default category
   > always exist active), so defaults never trip this step.
4. **Owner-rep share auto-inject (OQ4/§5)** — if no submitted share references the owner-rep member,
   add a 0đ owner-rep share to the set.
5. **No-duplicate-members (OQ5)** — reject a set with two shares for the same member →
   `DuplicateShareMember` (`7003`) (the unique `(expense_id, member_id)` index is the DB backstop).
6. **Insert** the `expenses` row, the `shares` rows, and the `expense_tags` rows.
7. **Stage audit (OQ9/OQ10/OQ20)** — via `AuditLogFactory`, `db.AuditLogs.Add(...)` one `Expense`/
   `Create` row + one `Share`/`Create` row per share, snapshots carrying denormalized payer/category/
   member/tag names.
8. Commit — the whole expense + shares + expense_tags + audit is one atomic unit (§4.5); any failure in
   steps 1–5 `NoCommit()`s and nothing is written (no partial expense, no audit).

`UpdateGeneralInfoAsync` and the share `AddAsync`/`UpdateAsync` (incl. change-member) repeat step 3's
link-validation for whatever reference they touch, and stage an `Update` audit only when the before/
after snapshot actually differs (no-op → no row, OQ9). `DeleteAsync` stages the `Delete` audit rows
**before** the cascade removes the data.

### Step 6 — Services + mappings

`Services/Api/Expenses/ExpensesService.cs` — `IExpensesService` + sealed impl (`[ScopedService]`,
primary ctor injecting `IExpenseRepository`, `IAuditLogRepository`, `IMapper`, and the request
validators):
- `ListAsync`, `GetAsync` (miss → `ExpenseNotFound`), `CreateAsync`, `UpdateAsync`, `DeleteAsync`,
  `SetSettledAsync`, and `GetHistoryAsync(userUuid, expenseUuid, ct)` → maps `AuditLogRepository`
  rows (empty list if none — OQ17). Each maps the repo's typed write result to the right
  `ErrorException` (`6001/6002/6003`, etc.).

`Services/Api/Shares/SharesService.cs` — `ISharesService` + sealed impl: `AddAsync`, `UpdateAsync`,
`DeleteAsync` — mapping typed results to `7000/7001/7002/7003`.

`Mappings/ExpenseProfile.cs` (`Expense`→`ExpenseResponse`/`ExpenseSummaryResponse`, with denormalized
payer/category/tag/share member info incl. their `isDeleted`), `Mappings/ShareProfile.cs`,
`Mappings/AuditLogProfile.cs` (`AuditLog`→`AuditLogResponse`; deserialize the JSON into a generic
object/`JsonElement` for the response).

### Step 7 — DTOs + validators

- `Models/Expenses/`: `CreateExpenseRequest { string Name, string? Description, DateTime ExpenseTime,
  string? PayerMemberUuid, string? CategoryUuid, IReadOnlyList<string>? TagUuids,
  IReadOnlyList<CreateShareInput>? Shares }`; `CreateShareInput { string MemberUuid, decimal Amount,
  string? Note }`; `UpdateExpenseRequest { string Name, string? Description, DateTime ExpenseTime,
  string? PayerMemberUuid, string? CategoryUuid, IReadOnlyList<string>? TagUuids }` (no shares);
  `SetSettledRequest { bool IsSettled }`; `ExpenseSummaryResponse`, `ExpenseResponse` (full: shares,
  tags, category, payer, total, isSettled, settledAt), `ExpenseFilter` (from/to/categoryUuid/tagUuid/
  settled), `AuditLogResponse { string Uuid, string EntityType, string EntityUuid, string Action,
  object? Before, object? After, DateTime CreatedAt }`.
- `Models/Shares/`: `CreateShareRequest { string MemberUuid, decimal Amount, string? Note }`,
  `UpdateShareRequest { string MemberUuid, decimal Amount, string? Note }`, `ShareResponse`.
- `Validators/Expenses/`: `CreateExpenseRequestValidator` — `Name` required + max 200; `Description`
  max 1000; `ExpenseTime` required (non-default); each `Shares[i].Amount >= 0` ("Số tiền không được
  âm."); `Shares[i].Note` max 500. `UpdateExpenseRequestValidator` — same minus shares. `SetSettled`
  needs no validator (bool).
- `Validators/Shares/`: `CreateShareRequestValidator` / `UpdateShareRequestValidator` — `MemberUuid`
  required; `Amount >= 0`; `Note` max 500. Field keys camelCase (`name`, `amount`, `memberUuid`, …).
- Vietnamese messages throughout; auto-registered by the existing `AddValidatorsFromAssembly`.

### Step 8 — Controller

`Controllers/ExpensesController.cs` (derives from `AppController`, LOCKED). All actions guarded,
Vietnamese Swagger annotations, `userUuid = AuthenticatedUser.Id`.

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/expenses` | `[FromQuery] from,to,categoryUuid,tagUuid,settled` → `ApiResult<IReadOnlyList<ExpenseSummaryResponse>>` | filters AND; sort expense_time DESC (OQ13) |
| `GET api/v1/expenses/{uuid}` | route → `ApiResult<ExpenseResponse>` | resource-owned; miss → 6000 |
| `POST api/v1/expenses` | `CreateExpenseRequest` → `ApiResult<ExpenseResponse>` | atomic w/ shares; defaults; link-validate → 6001/6002/6003/7001; dup → 7003 |
| `PUT api/v1/expenses/{uuid}` | `UpdateExpenseRequest` → `ApiResult<ExpenseResponse>` | general info + tag set (full replace); miss → 6000 |
| `DELETE api/v1/expenses/{uuid}` | route → `ApiResult` message | hard delete + cascade shares; miss → 6000 |
| `PUT api/v1/expenses/{uuid}/settled` | `SetSettledRequest` → `ApiResult` message | settled toggle; no audit (OQ11); miss → 6000 |
| `POST api/v1/expenses/{uuid}/shares` | `CreateShareRequest` → `ApiResult<ShareResponse>` | link-validate → 7001; dup → 7003 |
| `PUT api/v1/expenses/{uuid}/shares/{shareUuid}` | `UpdateShareRequest` → `ApiResult<ShareResponse>` | change member allowed; owner-rep member-change guard; miss → 7000 |
| `DELETE api/v1/expenses/{uuid}/shares/{shareUuid}` | route → `ApiResult` message | owner-rep share → 7002; miss → 7000 |
| `GET api/v1/expenses/{uuid}/history` | route → `ApiResult<IReadOnlyList<AuditLogResponse>>` | scoped by user+expense_uuid; time-ordered; empty if none (OQ17) |

### Step 9 — Tests (owned by the test-engineer; definitive list)

Reuse the Members/Categories harness: `[Collection("AuthIntegration")]`; DB tests use `AuthDbTestBase`
(own connections) / `AuthApiTestBase` (app DI, real HTTP) with a unique lowercase username prefix per
class and dispose-time cleanup (deleting the prefix's users cascades to their members/categories/tags
→ expenses → shares/expense_tags/audit_logs via FK). All DB-dependent tests `[SkippableFact]`.

**Unit (no DB):**
- `CreateExpenseRequestValidator` / `UpdateExpenseRequestValidator` — name required/max-200,
  description max-1000, expense_time required, per-share amount ≥ 0, note max-500; exact Vietnamese
  messages; camelCase `error.fields` keys.
- `CreateShareRequestValidator` / `UpdateShareRequestValidator` — memberUuid required, amount ≥ 0,
  note max-500.
- `ExpensesService` (fake `IExpenseRepository`/`IAuditLogRepository`) — create maps repo write results
  to 6001/6002/6003/7001/7003; get/update/delete/settled miss → 6000; history maps rows / empty list;
  no-op update produces no error.
- `SharesService` (fake `IShareRepository`) — add dup → 7003, invalid member → 7001; delete owner-rep
  share → 7002; miss → 7000.
- `AuditLogFactory` — Create builds before=null; Delete builds after=null; **no-op Update returns
  null** (no log); snapshots contain denormalized names + uuids.

**Integration (real MariaDB — `ExpenseRepositoryTests`, `ShareRepositoryTests`, `AuditLogRepositoryTests`):**
- Create is **atomic** — a forced failure (e.g. an invalid share member mid-list) rolls back the
  expense, all shares, expense_tags, **and** the audit rows (0 rows of each); success persists all.
- Defaults — omitted payer → owner-rep; omitted category → default; owner-rep share auto-injected at
  0đ when absent (OQ4).
- **Resource-owned** — another user's expense/share invisible to get/list/update/delete/settled/share
  ops (null/false, never the row); `6000/7000` semantics.
- **§4.2/§4.8 link integrity** — a soft-deleted or foreign payer/category/tag/share-member is rejected
  on create/update/add/change-member (6001/6002/6003/7001); an expense created earlier keeps and
  displays a link whose member/category/tag was **later** soft-deleted (history intact).
- **Money** — `amount >= 0` CHECK rejects a negative insert at the DB; 0đ accepted; total = sum(shares)
  (OQ1 derive).
- Owner-rep share **not deletable** (7002); duplicate member rejected (7003, OQ5).
- **Delete** hard-removes expense + cascades shares + expense_tags; audit rows for the deleted expense
  **remain** and are readable via history (§3.8).
- **Audit** — create writes 1 Expense/Create + N Share/Create; update writes 1 Expense/Update with
  before/after; a **no-op** update writes nothing; settled toggle writes **nothing** (OQ11); audit rows
  are ordered by created_at; `entity_uuid`/`expense_uuid` stored as plain values (survive expense
  deletion).
- Filters — from/to inclusive, category, tag, settled, AND-combined; sort expense_time DESC.

**Endpoint (WebApplicationFactory — `ExpensesEndpointTests`, `ExpenseSharesEndpointTests`,
`ExpenseHistoryEndpointTests`):**
- Register → create an expense with 3 shares → `GET /{uuid}` shows total, category, payer, tags, 3
  shares; owner-rep present at 0đ if omitted.
- Create with omitted payer/category → defaults applied; with a deleted category → 400 (6002).
- Update general info + tag set (full replace) persists; settled toggle flips `isSettled` and doesn't
  change amounts; delete → subsequent `GET` → 404 (6000) but `GET /history` still returns the create +
  delete audit entries.
- Share add/edit (change member)/delete work; owner-rep share delete → 400 (7002); duplicate member →
  400 (7003); invalid member → 400 (7001).
- **Resource-owned:** another user's expense/share UUID → 404 (6000/7000) on every route (never 403);
  another user's `/history` → empty list (leaks nothing).
- Invalid payloads → 400 with `error.fields` (Vietnamese, camelCase); negative amount rejected;
  anonymous → 401 wrapped.

### Step 10 — Wrap-up

- `dotnet build` clean; `dotnet test` green (DB tests skip only when MariaDB unreachable). Live smoke:
  register → create expense (defaults + owner-rep 0đ share) → get/list/filter → update general info +
  tags → share add/edit/delete + owner-rep guard → settled toggle → delete → history survives →
  resource-owned 404s. `dotnet ef database update` per protocol. Update this doc's Progress Log +
  Final Outcome; note in `agent-dev-team.md` that M5 closed the M4 OQ10 linking deferral and built the
  audit log, and record the M6 event seam (OQ8) it left open.

## Impact Analysis

- **APIs:** ten new endpoints under `api/v1/expenses` (list, get, create, update, delete, settled,
  share add/edit/delete, history). No existing endpoint changes shape.
- **Database:** new migration `AddExpensesSharesAndAudit` — four tables (`expenses`, `shares`,
  `expense_tags`, `audit_logs`), the first **CHECK constraint** (`amount >= 0`), FK cascade
  (share/expense_tag → expense) + restrict (payer/category/member), composite indexes for the sort +
  filters, JSON snapshot columns. No data migration.
- **Infrastructure:** no new hosted service, no new packages (System.Text.Json is in-box), no Redis
  involvement.
- **Services:** new `ExpensesService`, `SharesService`, `ExpenseRepository`, `ShareRepository`,
  `AuditLogRepository`, `AuditLogFactory`, `ExpenseProfile`/`ShareProfile`/`AuditLogProfile`, and the
  expense/share validators. `AppController`, `ApiResult`, middleware, and the M3/M4 repositories are
  reused unchanged.
- **UI:** none (API only).
- **Documentation:** this doc; `ErrorCodes` XML docs (6xxx/7xxx/8xxx); a note in `agent-dev-team.md`.

## Decision Log

### Decision
**User checkpoint 2026-07-14 — all 20 Open Questions resolved; every recommended option (a) accepted**
(OQ9 and OQ10 presented together, both accepted).

1. **Expense total (OQ1a):** derived `SUM(shares.amount)` on read; **no total column** on `expenses`.
   *Reason:* single source of truth, zero drift (§4.3); the sum is cheap (indexed by `expense_id`).
2. **Money (OQ2a):** `decimal(18,2)` + DB **CHECK `amount >= 0`** (the codebase's first CHECK).
   *Reason:* matches CLAUDE.md's DECIMAL money rule, maps cleanly through EF/Pomelo, multi-currency
   ready (§6); the CHECK enforces §4.3 at the DB, not just in app code.
3. **Deletion model (OQ3a):** expenses/shares are **hard-deleted** (not `IEntityDeletable`); expense
   delete cascades shares + `expense_tags`; the audit log retains the before-state. *Reason:* matches
   §3.8 ("audit survives deletion") and the §4.7 soft-delete list (member/category/tag only); a deleted
   expense correctly leaves debt balance/stats.
4. **Owner-rep share (OQ4a):** auto-inject a 0đ owner-rep share on create when omitted; block deleting
   it (`7002`); amount/note editable, member not changeable away. *Reason:* realizes the §5 lock "keep
   the owner 0đ share in every expense"; mirrors owner-rep-member-not-deletable (M3).
5. **Duplicate members (OQ5a):** forbid — one share per member per expense (`7003`) + unique index
   `(expense_id, member_id)`. *Reason:* matches the one-row-per-member split screen and keeps the
   owner-rep-present rule unambiguous; DB index backs the app check.
6. **Join table (OQ6a):** lightweight composite-PK `expense_tags` (no id/uuid/soft-delete), FK
   `expense_id` cascade + `tag_id` restrict. *Reason:* a pure link table needs no surrogate key; a
   soft-deleted tag stays linked on old expenses (§4.7) but isn't selectable for new ones (§4.8).
7. **Category/payer FK (OQ7a):** both required, `OnDelete(Restrict)`. *Reason:* a default category and
   owner-rep member always exist, so an expense always has a valid category/payer (§4.6); Restrict is
   inert since those are soft-deleted.
8. **Event seam (OQ8a):** defer the entire event relationship to M6 — no `event_id` column now.
   *Reason:* mirrors M4 OQ10; avoids an FK-less orphan column and pre-empting M6's join design.
9. **Audit storage (OQ9a):** one `audit_logs` table; `entity_type`/`entity_uuid`/`expense_uuid` as
   plain no-FK values; `action`, actor, `before_json`/`after_json`, `created_at`; no-op edits (equal
   snapshots) write no row. *Reason:* simplest, matches §3.8 "giá trị trước và sau" literally; no-FK
   references survive hard-deletes; per-field diffs are a §6 future item.
10. **Audit granularity (OQ10a):** per-entity rows (1 Expense/Create + N Share/Create; delete
    similarly) with denormalized names in the JSON snapshot. *Reason:* every share carries its own
    trail from birth; denormalized names keep history readable after renames/deletes.
11. **Settled audit (OQ11a):** marking settled writes **no** audit entry. *Reason:* settled is payment
    metadata, not expenditure số liệu (§3.5); the audit is for số liệu disputes.
12. **Settled representation/endpoint (OQ12a):** `is_settled` + nullable `settled_at`; `PUT
    /{uuid}/settled` body `{isSettled}`; dedicated service method. *Reason:* records the "when" cheaply;
    the dedicated method is the exact seam for M6's closed-event settled-exception (§4.4).
13. **List filters/sort/pagination/shape (OQ13a):** `from`/`to` (inclusive datetime) + category + tag +
    settled, AND-combined; sort `expense_time` DESC; no pagination this milestone; list = summary DTO,
    get = full DTO. *Reason:* consistent with the shipped list endpoints; tier limits (M10) cap volume.
14. **`expense_time` bounds (OQ14a):** none in M5. *Reason:* the within-event-range rule (§3.6) is M6;
    the spec never forbids future/past dates for loose expenses.
15. **Error blocks (OQ15a):** 6xxx Expenses / 7xxx Shares / 8xxx Audit (reserved), with the concrete
    codes in Step 3; extend `GetDefaultHttpStatus`. *Reason:* one 1000-block per feature area,
    consistent with 2xxx–5xxx.
16. **Field lengths (OQ16a):** name max 200; description optional max 1000; share note optional max 500.
    *Reason:* expense names run longer than 100-char member/category names; generous defaults elsewhere.
17. **History for deleted/unknown expense (OQ17a):** scope audit by user + expense_uuid, time-ordered,
    empty list when none. *Reason:* required by §3.8 (view history after deletion); resource-owned (a
    foreign/unknown uuid leaks nothing).
18. **Tag-set update (OQ18a):** full replace — client sends the complete `tagUuids`, service diffs.
    *Reason:* natural for a multi-select; simplest server logic.
19. **Endpoint surface (OQ19a):** the Step-8 route table confirmed (nested share routes, `/settled`,
    `/history`). *Reason:* nested routes preserve owning-expense scoping; keeps §3.5's "edit general
    info separate from editing shares" split.
20. **Audit placement (OQ20a):** pure `AuditLogFactory` staged via `db.AuditLogs.Add(...)` inside the
    repository's `ExecuteTransactionAsync`. *Reason:* audit shares the mutation's fate without a nested
    transaction; keeps the codebase-wide "transaction in the repository" convention.

### Reason
User answers at the Milestone-5 planning checkpoint (2026-07-14), brought by the orchestrator per the
Clarification-First protocol; recorded verbatim so the api-implementer needs no other source.

### Alternatives Considered
The full option sets (b)/(c) with trade-offs, as presented to the user, are preserved in the struck
Open Questions above.

### Decision (inherited — NOT reopened)
Spec §5 locks (total = sum of shares; keep owner 0đ share in every expense; domain terms
expense/share/event/wallet/settled/Premium-Free); resource-owned 404 scoping (§4.1); soft-delete via
`IEntityDeletable`/`is_deleted` for members/categories/tags with inviolable history (§4.7/§4.8);
default-category-always-one (§4.6); UTC timestamps; entity/repo/controller conventions; EF-migration-
only schema with the pinned design-time factory; writes through `ExecuteTransactionAsync` +
`NoCommit()` (non-nesting); the M4 OQ10 deferral that assigns the expense↔category FK, expense↔tag
join, and §4.2 link validation to this milestone; the M3 `MemberRepository` and M4 `CategoryRepository`
/`TagRepository` reused unchanged.

## Progress Log

### 2026-07-14

- Feature-planner: required reading completed — `The-ideal.md` §2, §3.5, §3.8, §5, and cross-cutting
  §4.1/§4.2/§4.3/§4.5/§4.7/§4.8 (plus §3.6/§3.7 read only for the M6/M7 boundary); `CLAUDE.md`;
  `.claude/rules/rule.md` (template); `planning/members.md` + `planning/categories-and-tags.md`
  (exemplars — structure, harness, seam, resource-owned discipline, M4 OQ10 deferral to M5);
  `planning/agent-dev-team.md` (M5 line + protocol); `planning/hosted-service-di-registration.md`; and
  the live code: `Member`/`Category`/`Tag` entities + partials, `MemberRepository`/`CategoryRepository`
  (incl. `NameWriteResult`), `CategoriesService`, `CategoriesController`, `AppDbContext`,
  `BaseRepository`, `TransactionContext`, `DatabaseExtensions` (`ExecuteTransactionAsync` — non-nesting,
  `NoCommit`), `ErrorCodes`, `ErrorException`, `AppController`.
- Drafted this plan: `expenses`/`shares`/`expense_tags`/`audit_logs` entities + the
  `AddExpensesSharesAndAudit` migration (first CHECK constraint), ten `api/v1/expenses` endpoints
  (list w/ filters, get, create atomic-with-shares, update-general-info, delete, settled, three share
  sub-routes, history), `ExpensesService`/`SharesService`/three repositories/`AuditLogFactory`/
  validators/DTOs/profiles, the 6xxx/7xxx/8xxx error blocks, and the full test list.
- **20 Open Questions raised** (expense total derive-vs-cache; money type + CHECK; hard-vs-soft delete;
  owner-rep-share enforcement; duplicate share members; join-table shape; category/payer FK; event
  seam; audit storage model; audit granularity + snapshot content; settled-audit; settled
  representation + endpoint; list filters/sort/pagination; expense_time bounds; error-code blocks;
  field lengths; history-for-deleted-expense; tag-set replace-vs-delta; endpoint surface; audit
  build/stage placement) — awaiting user answers at the checkpoint before implementation starts.

### 2026-07-14 (checkpoint — all Open Questions answered, plan unblocked)

- **User answered all 20 Open Questions; every recommended option (a) accepted** (OQ9 and OQ10
  presented together, both accepted). See the consolidated Decision Log entry: OQ1 derived total (no
  column); OQ2 `decimal(18,2)` + first-in-codebase CHECK `amount >= 0`; OQ3 hard-delete expenses/shares
  (audit retains before-state); OQ4 auto-inject 0đ owner-rep share + block deleting it (`7002`); OQ5
  forbid duplicate members (`7003`, unique `(expense_id, member_id)`); OQ6 lightweight composite-PK
  `expense_tags`; OQ7 required category/payer FK `Restrict`; OQ8 defer event_id to M6; OQ9 one
  `audit_logs` table, plain no-FK refs, no-op → no row; OQ10 per-entity rows + denormalized names; OQ11
  no audit for settled; OQ12 `is_settled` + `settled_at`, `PUT /{uuid}/settled`, dedicated method; OQ13
  from/to/category/tag/settled AND-filters, `expense_time` DESC, no pagination, summary vs full DTO;
  OQ14 no `expense_time` bounds; OQ15 6xxx/7xxx/8xxx blocks; OQ16 name 200 / description 1000 / note
  500; OQ17 history scoped by user+expense_uuid, empty if none; OQ18 full tag-set replace; OQ19 Step-8
  endpoint surface confirmed; OQ20 pure `AuditLogFactory` staged inside the repo transaction.
- Plan synchronized with the answers: Open Questions struck + annotated; Assumptions promoted to
  confirmed; the explicit create-path & §4.2/§4.8 link-validation flow added to Step 5; entity/schema,
  error-code table, endpoint table, and test list already match the recommendations; Decision Log
  recorded (20 numbered points + Reason + Alternatives-Considered + inherited-decisions block). **No
  open questions remain — implementation can start.**

### 2026-07-14 (implementation — api-implementer)

- **Entities (Step 1):** added `Database/Entities/Expense.cs`+`Partials/Expense.cs` (IEntity, hard-delete,
  name≤200/description≤1000, `payer_member_id`/`category_id` required FK `Restrict`, `user_id` cascade,
  composite index `(user_id, expense_time)`, computed `updated_at`; **no total column**);
  `Share.cs`+`Partials/Share.cs` (IEntity, `expense_id` cascade / `member_id` restrict, `decimal(18,2)`
  amount with the **first-in-codebase CHECK** `ck_shares_amount_non_negative`, unique `(expense_id, member_id)`);
  `ExpenseTag.cs`+`Partials/ExpenseTag.cs` (lightweight composite-PK join, expense cascade / tag restrict,
  index on `tag_id`); `AuditLog.cs`+`Partials/AuditLog.cs` (IEntity, `entity_type`/`action` int enums,
  `entity_uuid`/`expense_uuid` plain no-FK values, `before_data`/`after_data` longtext, actor FK cascade,
  index `(actor_user_id, expense_uuid, created_at)`). Wired all four into `AppDbContext` (DbSets +
  `ConfigureModel` calls).
- **Migration (Step 2):** authored offline `20260714060908_AddExpensesSharesAndAudit` via the design-time
  factory; reviewed (4 tables, utf8mb4/unicode_ci, CHECK `amount >= 0`, unique `(expense_id, member_id)`,
  `expense_tags` composite PK + cascade/restrict, decimal(18,2), computed `updated_at`, longtext JSON,
  audit no-FK ref columns) and **applied to the dev DB** (`database update`). Snapshot in sync.
- **Errors (Step 3):** appended 6000-6003 / 7000-7003 to `ErrorCodes` (8xxx reserved, no codes) with
  Vietnamese messages; extended `ErrorException.GetDefaultHttpStatus`.
- **Audit infra (Step 4):** `Services/Audit/AuditSnapshots.cs` (denormalized `ExpenseAuditSnapshot`/
  `ShareAuditSnapshot` with `From(...)` builders, tags ordered by uuid for stable no-op detection) +
  `Services/Audit/AuditLogFactory.cs` (pure `IAuditLogFactory`, `System.Text.Json`, returns null for
  no-op Update); `Repositories/AuditLogRepository.cs` (`ListByExpenseAsync` scoped by user+expense_uuid,
  ordered created_at then id).
- **Repositories (Step 5):** `Repositories/ExpenseWriteResult.cs` (typed `ExpenseWriteStatus` +
  `ExpenseWriteResult<T>` + `CreateExpenseData`/`UpdateExpenseData`/`ShareData` records);
  `ExpenseRepository.cs` (list w/ AND filters + expense_time DESC, full get, create following the exact
  §4.2/§4.8 8-step flow, update-general-info w/ full tag replace, cascade delete, settled — all staging
  audit inside one `ExecuteTransactionAsync`, `NoCommit()` on any failure); `ShareRepository.cs`
  (add/update/delete, owner-rep protection, duplicate guard).
- **Services + mappings (Step 6):** `Services/Api/Expenses/ExpensesService.cs`,
  `Services/Api/Shares/SharesService.cs` (map typed results to the 6xxx/7xxx ErrorExceptions);
  `Mappings/ExpenseProfile.cs` (derived total, denormalized payer/category/tags reusing the Member/
  Category/Tag profiles), `ShareProfile.cs`, `AuditLogProfile.cs` (JSON → `JsonElement`).
- **DTOs + validators (Step 7):** `Models/Expenses/*` (create/update/settled requests, filter, summary +
  full responses, audit response), `Models/Shares/*`; `Validators/Expenses/*` + `Validators/Shares/*`
  (Vietnamese, camelCase field keys, per-share amount ≥ 0 / note ≤ 500).
- **Controller (Step 8):** `Controllers/ExpensesController.cs` — all 10 routes, guarded, Vietnamese
  Swagger, `userUuid = AuthenticatedUser.Id`.
- **Build/tests:** `dotnet build` clean (only the pre-existing pinned-AutoMapper NU1903 advisory);
  `dotnet test` 337/337 green (nothing broken; new tests are the test-engineer's step).
- **Live smoke (Step 10):** 43/43 assertions passed against the running API — defaults + owner-rep 0đ
  auto-inject; atomic all-or-nothing on a bad share member (no partial expense); list filters
  (category/settled/range); update general info + full tag replace; share add/edit/dup(7003)/owner-rep
  member-change(7002)/owner-rep delete(7002)/normal delete; settled toggle (no audit row); §4.2/§4.8
  rejection of cross-user payer(6001)/soft-deleted tag(6003)/soft-deleted category(6002); resource-owned
  404 (6000) + empty foreign history; §4.7 deleted-member still displayed; delete cascade + history
  survival with Create/Update/Delete before/after + per-share rows. Smoke data cleaned (users deleted →
  cascade cleared expenses/shares/expense_tags/audit_logs; verified 0 remaining).
- **In-latitude design choices (no requirement changed):**
  1. `ExpenseTag` is a first-class configured entity with an explicit `ICollection<ExpenseTag> ExpenseTags`
     navigation on `Expense` (rather than an EF skip-navigation `Tags`), because EF Core cannot both
     fully self-configure the join in its own `ConfigureModel` (Step 1.5/Step 5) and expose it as a skip
     navigation; the resulting `expense_tags` schema (composite PK, cascade/restrict FKs, tag_id index)
     is exactly as specified, and reads project tags via `ExpenseTags.Select(link => link.Tag)`.
  2. The owner-rep-share **member-change-away** guard reuses error code `7002`
     (`OwnerRepresentativeShareNotDeletable`) — the only owner-rep-share protection code the doc defines —
     with a context-appropriate Vietnamese message ("Không thể đổi thành viên..."); the delete path keeps
     the doc's delete message. The doc mandated the guard + code 7002 for the owner-rep share but did not
     give a distinct message for the change-away case.
- **No Open Questions added; no deviations from the decisions.**

### 2026-07-14 (tests — test-engineer)

- **Added 150 automated tests for M5** (suite total 337 → 487). Full run: **485 passed / 2 failed / 0
  skipped** (both failures are DB-reachable, not skips), deterministic across two consecutive full runs
  (identical results). `dotnet build` clean. The 2 failures are **real production bugs** the tests
  exposed (kept failing per protocol; production code untouched — see below).
- **Per-class breakdown (all mirror the M3/M4 harness: `[Collection("AuthIntegration")]`, `IClassFixture`
  wiring, `[SkippableFact]` + `SkipIfNoDb()` for DB paths, unique lowercase username prefix per class
  with dispose-time cascade cleanup):**
  - *Unit (no DB) — 72:* `ExpenseValidatorsTests` (29 — create/update expense + create/update share
    validators: name required/trim/≤200, description ≤1000, expense_time required, per-share amount ≥ 0,
    note ≤ 500, pinned Vietnamese messages); `AuditLogFactoryTests` (9 — Create null-before / Delete
    null-after / real-change logs both / **no-op Update → null** incl. reordered-same-tags / denormalized
    names + expense_uuid grouping); `ExpensesServiceTests` (21 — fakes; create maps 6001/6002/6003/7001/
    7003/6000, validation short-circuits repo, get/update/delete/settled miss → 6000, no-op update no
    error, history maps rows / empty); `SharesServiceTests` (13 — fakes; add/update/delete map 7001/7003/
    7002/7000/6000, validation short-circuits repo).
  - *Integration (real MariaDB) — 50:* `ExpenseRepositoryTests` (28 — atomic create + all-or-nothing
    rollback of a bad-share-member mid-list (0 expense/share/tag/audit), defaults (owner-rep payer /
    default category / auto-injected 0đ owner-rep share), §4.2/§4.8 rejection of foreign/soft-deleted
    payer(6001)/category(6002)/tag(6003)/share-member(7001), §4.7 later-deleted member still displayed,
    resource-owned null/NotFound on get/list/update/delete/settled, tag-set full replace, delete hard
    cascade + surviving audit (6 rows), settled toggle no-audit, **amount ≥ 0 CHECK rejects a negative
    insert at the DB**, 0đ accepted, derived total = sum(shares), duplicate-member(7003), audit
    granularity (1 Expense/Create + N Share/Create), filters from/to/category/tag/settled AND-combined +
    expense_time DESC); `ShareRepositoryTests` (17 — add/update/delete, link-validate 7001/7003, change-
    member allowed, owner-rep member-change 7002, owner-rep delete 7002, owner-rep amount edit allowed,
    resource-owned via the owning expense, per-mutation audit staging); `AuditLogRepositoryTests` (5 —
    per-expense time-ordered read, empty for foreign/unknown uuid, **survives the expense hard-delete**).
  - *Endpoint (WebApplicationFactory, real HTTP) — 28:* `ExpensesEndpointTests` (13 — create-with-3-shares
    full DTO (total/category/payer/tags/shares), defaults + owner-rep 0đ auto-inject, deleted category
    → 400/6002, summary list + settled filter, update general info + tag replace, settled toggle (amounts
    unchanged), delete → 404/6000, §4.7 deleted member still displays, resource-owned 404/6000 on
    get/put/delete/settled (never 403), validation `error.fields` camelCase + negative amount,
    anonymous → 401); `ExpenseSharesEndpointTests` (10 — add/edit(change-member)/delete, owner-rep delete
    7002, owner-rep member-change 7002, duplicate 7003, invalid member 7001, unknown share 404/7000,
    cross-user share routes 404 (6000 add / 7000 update+delete), negative amount 400); `ExpenseHistoryEndpointTests`
    (5 — Create/Update/Delete entries with before/after snapshots, empty for foreign/unknown uuid,
    history survives the delete).
- **Test-infra added (test project only):** `Infrastructure/ExpenseDbTestBase.cs` (ledger/member/
  category/tag seed helpers + repository factories wired with the real pure `AuditLogFactory`) and
  `Infrastructure/ExpenseApiTestBase.cs` (authorized-client + HTTP helpers). **Cleanup fix (mandatory
  for M5):** expenses' RESTRICT FKs to categories/members block the base class's user-cascade delete, so
  both bases override `DisposeAsync` to delete the prefix's **expenses first** (cascading shares +
  expense_tags) and sweep **audit_logs** by actor, *then* call the base user-cascade. Note: `audit_logs`
  entity_uuid/expense_uuid carry no FK, but its `actor_user_id` FK *does* cascade on user delete — the
  explicit sweep is defensive belt-and-suspenders. **Verified 0 leftover rows** (expenses/shares/
  expense_tags/audit_logs/test-prefix users) after a clean full run.
- **Production bugs found (2 — reported, NOT fixed; failing tests retained as evidence):** the audit
  **no-op detection (OQ9 "no-op update → NO rows") misfires on a genuine no-op** because
  `AuditLogFactory` compares raw serialized snapshots and the snapshots are not canonicalized:
  1. `ExpenseAuditSnapshot.ExpenseTime` — DB round-trip yields `DateTimeKind.Unspecified` (serializes
     `"2026-07-14T12:00:00"`) while the request value is `DateTimeKind.Utc` (serializes
     `"…T12:00:00Z"`); the trailing `Z` makes the before/after JSON differ → a spurious Expense/Update
     row on a no-op. (`ExpenseRepositoryTests.UpdateGeneralInfoAsync_NoChange_WritesNoAuditRow`).
  2. `ShareAuditSnapshot.Amount` — DB DECIMAL(18,2) round-trip yields scale-2 (`"40000.00"`) while the
     request value has scale 0 (`"40000"`) → a spurious Share/Update row on a no-op.
     (`ShareRepositoryTests.UpdateAsync_NoChange_WritesNoAuditRow`). Suggested fix (implementer): before
     serializing snapshots, canonicalize `ExpenseTime` to UTC (`DateTime.SpecifyKind`/`ToUniversalTime`)
     and normalize `Amount` scale (e.g. `decimal.Round(amount, 2)` / a fixed-scale format) so
     semantically-equal values serialize identically.
- **Production code untouched** — all work confined to `FairShareMonApi.Tests/`.

### 2026-07-14 (fix — audit no-op detection canonicalization, api-implementer)

- **Bug (found by the M5 test suite, 2 failing tests, both violating OQ9 "a no-op update writes NO
  audit rows"):** `AuditLogFactory` compares raw serialized before/after snapshots, but two fields
  weren't canonicalized so a semantically-identical no-op serialized differently and produced a
  spurious `Update` row: (1) `ExpenseTime` — the DB round-trip returns `DateTimeKind.Unspecified`
  (`"…T12:00:00"`) while the request value is `Kind.Utc` (`"…T12:00:00Z"`); (2) `Share.Amount` —
  `DECIMAL(18,2)` returns scale 2 (`40000.00`) while a scale-0 request value serializes as `40000`.
  False positives only (extra rows on genuine no-ops; no data loss). Failing tests:
  `ExpenseRepositoryTests.UpdateGeneralInfoAsync_NoChange_WritesNoAuditRow`,
  `ShareRepositoryTests.UpdateAsync_NoChange_WritesNoAuditRow`.
- **Fix (production code only, tests untouched):** added `AuditSnapshotCanonicalizer` in
  `Services/Audit/AuditSnapshots.cs` and applied it at the single snapshot-build point (the `From`
  builders, covering every no-op comparison): `ExpenseTime` → `DateTime.SpecifyKind(value, Utc)`
  (labels the UTC clock value without shifting - consistent with `AppDateTime.Now` being UTC, so the
  stored `Unspecified` and incoming `Utc` values canonicalize identically); `Share.Amount` →
  `decimal.Round(value, 2) + 0.00m` (forces the column's fixed scale 2, so `40000` and `40000.00`
  serialize identically). Audited the other snapshot fields — no other `DateTime`/`decimal` fields, so
  these two normalizations are complete. Verified the serialization behavior empirically before
  applying. Only file changed: `Services/Audit/AuditSnapshots.cs`.
- **Result:** `dotnet build` clean; `dotnet test` from repo root **487/487, 0 failed, 0 skipped**; the
  two named tests confirmed green; no regressions.

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

- **Code-reviewer verdict: APPROVE, 0 blocking findings** (after the Test-step fix-loop that corrected
  the 2 audit no-op canonicalization bugs). All 20 accepted decisions and the 2 in-latitude choices
  verified against the code; no silent deviations, no Open Questions reopened.
- **Verified checks:**
  - **Atomicity (§4.5):** every mutation is one `ExecuteTransactionAsync`; `NoCommit()`/abort on every
    early return; all validation runs before any insert; the audit is staged inside the same
    transaction (fail → no log, §3.8).
  - **Resource-owned scoping (§4.1):** present on every path — expense CRUD, share sub-routes (scoped
    through the owning expense), history, and settled — an ownership miss returns 6000/7000 (404), never
    403.
  - **§4.2/§4.8 link integrity:** enforced on create + update + share routes via user-scoped
    `!IsDeleted` queries (payer 6001 / category 6002 / tag 6003 / share member 7001); soft-deleted or
    foreign references rejected for new/edited data while existing links stay displayed (§4.7).
  - **Money (§4.3):** `decimal(18,2)` + DB CHECK `ck_shares_amount_non_negative`; no float; total is
    derived (no stored column).
  - **Owner-rep share:** 0đ auto-inject on create; not-deletable and not-change-away (7002);
    duplicate-member rejected (7003) with the unique `(expense_id, member_id)` index backstop.
  - **Audit:** immutable, no-FK entity/expense uuid refs, survives hard-deletes; per-entity rows;
    no-op suppressed; settled toggle produces no row; history scoped by user + expense_uuid.
  - **Canonicalization fix confirmed sound and complete** — only one `DateTime` (ExpenseTime →
    `SpecifyKind(Utc)`) and one `decimal` (Amount → `decimal.Round(v,2)`) snapshot field exist, both
    normalized.
  - **In-latitude choices valid:** `ExpenseTag` as a first-class configured join entity; `7002` reused
    with two distinct Vietnamese messages (delete vs change-away).
  - Migration + model snapshot in sync; `AppController`, `Program.cs`, and `AppDbContext.partial.cs`
    untouched.
- **2 informational notes (recorded, not defects):** (I1) `PUT /expenses/{uuid}` full-replace resets an
  omitted payer/category back to owner-rep/default — document for API clients (see Future
  Improvements); (I2) the summary list `Include`s full `Shares` to compute the total in-memory — a
  DB-side `SUM`/`COUNT` projection is a Future Improvement.
- **1 nit:** a redundant `When(Shares is not null)` guard in a validator (harmless).
- **Final green state:** `dotnet test` = **487 passed / 0 failed / 0 skipped**, deterministic across
  runs, DB swept clean. Milestone 5 complete.

## Final Outcome

Milestone 5 (Expenses + Shares + Audit) is **implemented, fully tested, and code-reviewed (APPROVE, 0
blocking)** per the approved doc and all 20 accepted decisions. Delivered: four tables (`expenses`,
`shares`, `expense_tags`, `audit_logs`) via migration `AddExpensesSharesAndAudit` (authored + applied
to dev; the **codebase's first CHECK constraint**, `ck_shares_amount_non_negative` on `shares.amount`);
the 6xxx/7xxx error blocks (8xxx reserved); `ExpenseRepository`/`ShareRepository`/`AuditLogRepository`;
the pure `AuditLogFactory` + snapshots (with `AuditSnapshotCanonicalizer`); `ExpensesService`/
`SharesService`; the expense/share/audit profiles, DTOs, and validators; and `ExpensesController` with
all ten `api/v1/expenses` routes. Atomic create-with-shares + per-entity immutable snapshot audit in
one transaction (no-op edits suppressed, settled toggle unaudited); hard-delete cascade with surviving
audit; derived total (no stored column); resource-owned 404 scoping on every path incl. share
sub-routes/history/settled; §4.2/§4.8 link integrity; owner-rep 0đ auto-inject + not-deletable/
not-change-away protection; duplicate-member (7003) and money CHECK guards; owner-rep + default-category
create-time defaults. The Test step surfaced 2 audit no-op canonicalization bugs (ExpenseTime Kind
mismatch; Amount decimal-scale mismatch — both false-positive-only, violating OQ9), fixed at the
snapshot-build point (`SpecifyKind(Utc)` + `decimal.Round(v,2)`). `dotnet build` clean; `dotnet test`
= **487 passed / 0 failed / 0 skipped**, deterministic across runs, DB swept clean; 43/43 live smoke
assertions passed. This **closes the M4 OQ10 deferral** (expense↔category FK, expense↔tag join, §4.2
validation) and leaves the **M6 event seam** (no `event_id`; a dedicated `SetSettledAsync` for the
closed-event settled exception). No open questions remain; no unrecorded deviations.

## Future Improvements

- **Audit expansion (§6):** widen scope to members/events/categories; per-field diffs; restore an
  entity from its snapshot.
- **Per-member settled (§6):** track each member's payment on an expense, finer than the expense-level
  flag.
- Pagination / cursor for very large ledgers (OQ13-b) once list volume warrants it.
- DB-level enforcement of one-share-per-member (unique `(expense_id, member_id)` index) and the
  owner-rep-share invariant, reducing reliance on app-level checks.
- Cached expense total (OQ1-b) if M7 stats profiling shows the derived `SUM` is a hotspot.
- **Summary-list total via a DB-side `SUM`/`COUNT` projection** instead of `Include`-ing full `Shares`
  and summing in memory (code-review note I2) — a read-perf refinement for large ledgers.
- **Document for API clients (code-review note I1):** `PUT /expenses/{uuid}` is a full replace of the
  general info, so omitting `payerMemberUuid`/`categoryUuid` resets them to the owner-rep/default (not
  "leave unchanged") — call out in the API docs, or consider PATCH-style partial-update semantics.
- Remove the redundant `When(Shares is not null)` validator guard (code-review nit; harmless).
- Multi-currency (§6): a per-expense currency + rate-at-creation, which is why OQ2 weighs `decimal`
  against integer VND.
