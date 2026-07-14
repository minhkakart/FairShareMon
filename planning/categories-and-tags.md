# Categories + Tags (Milestone 4: Categories + Tags)

CRUD + soft-delete for expense **categories** (danh mục) and **tags** (nhãn), plus the
default-category invariant (always exactly one, not deletable, atomic reassignment), unique active
names per ledger, tag reactivation on name reuse, and the suggested-category seeding on registration
(via the shared bootstrap seam established in Milestone 3) with its idempotent backfill for
pre-existing users.

## Objective

Implement `The-ideal.md` §3.3 (Danh mục chi tiêu), §3.4 (Nhãn), and the registration seeding clause of
§3.1 ("tạo … bộ danh mục gợi ý … trong đó một danh mục được đặt làm mặc định"), on top of the shipped
Auth + Members skeleton:

- **Categories:** add / edit (name, color, icon) / soft-delete; unique active name per ledger; set a
  category as **default** (atomic swap that clears the previous default); the default category is
  **not deletable**; deleted categories are not selectable for new expenses but old expenses keep the
  link.
- **Tags:** add / rename / soft-delete; unique active name per ledger; **reactivate** a soft-deleted
  tag when a new tag reuses its name (relinking history) instead of creating a duplicate.
- **Suggested-category seeding:** on registration, seed the suggested set (Ăn uống, Đi lại, Khách
  sạn, Mua sắm, Khác) with exactly one default — atomically, via the **existing**
  `IRegistrationBootstrapStep` seam (`IUserRepository.CreateWithBootstrapAsync`); plus an idempotent
  **backfill** for users created before this milestone shipped.
- Honor the cross-cutting rules: resource-owned 404 scoping (§4.1), link integrity within a ledger
  (§4.2), default-category-always-one / not-deletable (§4.6), soft-delete preserves history inviolably
  (§4.7), deleted resources not selectable for new data (§4.8).

This milestone owns the **categories** and **tags** tables. It **reuses** the M3 registration-bootstrap
seam and mirrors the M3 idempotent-backfill pattern — it does **not** re-invent bootstrap. The
expense↔tag / expense↔category linking (many-to-many for tags, FK for categories) is an **Expenses
(M5)** concern; M4 owns the tables + CRUD only (see OQ10).

## Background

- **Milestone 3 (`planning/members.md`)** shipped the shared registration-bootstrap seam exactly for
  this milestone to extend:
  - `Services/Registration/IRegistrationBootstrapStep.cs` — a step runs INSIDE the user-creation
    transaction (after an intermediate `SaveChanges` assigns `user.Id`); implementations register with
    `[ScopedService(typeof(IRegistrationBootstrapStep), Multiple = true)]`.
  - `IUserRepository.CreateWithBootstrapAsync(User, Func<AppDbContext, User, CancellationToken, Task>, ct)`
    (`Repositories/UserRepository.cs`) inserts the user, flushes to assign `user.Id`, then runs the
    bootstrap in the SAME transaction; it preserves the in-transaction username re-check and the
    `DbUpdateException`/`MySqlErrorCode.DuplicateKeyEntry` race absorption.
  - `AuthService.RegisterAsync` (`Services/Api/Auth/AuthService.cs`) already injects
    `IEnumerable<IRegistrationBootstrapStep>` and runs every registered step via
    `RunRegistrationBootstrapAsync`. **M4 adds a second step (`SuggestedCategoriesBootstrapStep`);
    `AuthService` needs no change** — it already iterates all registered steps.
  - The owner-rep example step is `Services/Api/Members/OwnerRepresentativeBootstrapStep.cs`; the
    idempotent backfill pattern is `HostedServices/OwnerRepresentativeBackfillHostedService.cs`
    (own DI scope, logs + swallows startup failure, no-op when nothing missing), registered in
    `Program.cs` via `AddHostedService`.
- Conventions confirmed by reading the live code (identical to those recorded in `members.md`):
  - Entities: partial POCO `Database/Entities/<Name>.cs` + `Database/Entities/Partials/<Name>.cs`
    (ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt = AppDateTime.Now`; static
    `ConfigureModel(ModelBuilder)` invoked from `AppDbContext.OnModelCreating`). `IEntity` = `ulong
    Id`, `string Uuid` (unique, max 64), `CreatedAt`, `UpdatedAt` (`ValueGeneratedOnAddOrUpdate` +
    `current_timestamp(6) ON UPDATE current_timestamp(6)`). Snake_case columns; UTC timestamps.
  - Soft delete: `IEntityDeletable { bool IsDeleted }`; `BaseRepository.Query<T>(tracking,
    includeDeleted)` auto-excludes `IsDeleted` rows unless `includeDeleted: true`.
  - Repositories: interface + sealed impl in one file, `[ScopedService(typeof(IX))]`, extend
    `BaseRepository`; reads via `ExecuteQueryAsync`, writes via `ExecuteTransactionAsync` with
    `TransactionContext.NoCommit()` on business failure. Resolve `user_id` from the caller's UUID
    (mirror `MemberRepository.ResolveUserIdAsync`).
  - Controllers derive from `AppController` (LOCKED); routes `api/v{version:apiVersion}/[controller]`;
    `[ResponseWrapped]` auto-wraps into `ApiResult<T>`; `AuthenticatedUser.Id` = current user's UUID
    string. Vietnamese `[SwaggerOperation]`/`[SwaggerResponse]`.
  - Errors: `ErrorCodes` — 1xxx infra, 2xxx auth, 3xxx members (3000/3001). `ErrorException(code,
    message)` maps to HTTP via `GetDefaultHttpStatus`. Vietnamese for every user-facing message.
  - Validation: FluentValidation, auto-registered by `AddValidatorsFromAssembly(typeof(Program)...)`;
    services call `ValidateAndThrowAsync` (→ `ValidationException` → 400 with `error.fields` camelCase).
  - **MariaDB has no filtered/partial unique index** (established in `members.md`), so "unique among
    active rows" cannot be a plain DB unique index — it is enforced in application code and claimed as
    an error code (see OQ6 / Future Improvements).
- The dev DB currently holds no real product data beyond disposable smoke rows; the backfill will
  simply seed categories idempotently for any existing user.

## Requirements

From `The-ideal.md` §3.1 (seeding), §3.3, §3.4, §5, §4.1/§4.2/§4.6/§4.7/§4.8, and the conventions:

**Categories (§3.3, §4.6):**
- Add / edit (**name, color, icon**) / soft-delete a category. Categories carry màu/icon for charts
  (§2, §3.3) — unlike members.
- **Unique active name per ledger** — "Tên danh mục không trùng nhau trong một sổ (tính trên các danh
  mục đang hoạt động)": enforced among non-deleted categories only, app-level (no partial index).
- **Default-category invariant** — "Mỗi sổ luôn có đúng một danh mục mặc định": setting a new default
  clears the old flag **atomically**; the default category is **not deletable**; there is always
  exactly one immediately after registration.
- **Soft-delete** — deleted categories are not selectable for new expenses, but old expenses keep the
  link and still show in stats (§4.7/§4.8).

**Tags (§3.4, §5):**
- Add / rename / soft-delete a tag. Free-form optional classification.
- **Unique active name per ledger** — same rule as categories.
- **Soft-delete preserves history** (đồng bộ với danh mục): deleted tags leave new-data selection but
  old expenses keep and display them.
- **Reactivation on name reuse** — "Tạo nhãn mới trùng tên với một nhãn đã xóa → kích hoạt lại nhãn
  cũ": relink history, do not create a duplicate.

**Cross-cutting:**
- **Absolute privacy / resource-owned (§4.1):** every query scoped `WHERE uuid = :uuid AND user_id =
  :current_user_id`; an ownership miss returns **404, never 403**.
- **Link integrity within a ledger (§4.2):** a category/tag attached to an expense must belong to the
  same user — validated in **M5** when expenses reference them; M4 guarantees resource-owned scoping
  so M5 can rely on it.
- **Conventions:** entities per rules.md; schema via **EF migration only**; writes via
  `ExecuteTransactionAsync`; `Async` suffix + `CancellationToken`; Vietnamese messages; claim new
  error-code block(s) (OQ8).

## Open Questions

> **All 12 answered by the user at the 2026-07-14 checkpoint.** Every recommended option (a) was
> accepted **except OQ1, where the user chose option (b)** ("Ăn uống" as the default, with a specific
> seed set of colors + emoji icons). The struck questions below carry the binding answers inline; the
> full options/trade-offs are preserved for the record and mirrored in the Decision Log. No open
> questions remain — implementation can start. The Implementation Plan, error-code table, and test
> list below are synced to these answers.

**OQ1 — Suggested-category set: colors, icons, and which one is default.**
> ~~**OQ1**~~ → **Answered 2026-07-14 (option b):** default = **"Ăn uống"** (the everyday category,
> NOT "Khác"); seed all five spec names with emoji icons + chart colors. **Exact seed set** (name,
> icon, color hex): Ăn uống 🍜 `#F97316` **(DEFAULT)**; Đi lại 🚗 `#3B82F6`; Khách sạn 🏨 `#8B5CF6`;
> Mua sắm 🛍️ `#EC4899`; Khác ⋯ `#6B7280`.
The five **names** are fixed by spec §3.1 ("Ăn uống, Đi lại, Khách sạn, Mua sắm, Khác"). Undecided:
each category's color + icon, and which of the five is the default.
- **(a) [recommended]** Seed the five with sensible chart colors + icon keys and make **"Khác"**
  (Other) the default — the natural catch-all for uncategorized expenses (§3.5: "Không chọn danh mục →
  dùng danh mục mặc định"). Proposed values (client maps icon keys to glyphs): Ăn uống `#FF7043`
  `food`; Đi lại `#42A5F5` `transport`; Khách sạn `#AB47BC` `hotel`; Mua sắm `#26A69A` `shopping`;
  Khác `#78909C` `other` (default). Trade-off: colors/icons are a UI-preference call the user may want
  to set themselves.
- **(b)** Same list, but make **"Ăn uống"** the default (the most common expense). Trade-off: a
  meaningful category becomes the fallback bucket, which can silently mis-bucket "uncategorized"
  spend.
- **(c)** Seed the five with **no color/icon** (null) and let the user set them later; default = Khác.
  Trade-off: charts start colorless.

**OQ2 — Color & icon representation and validation.**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** `color` required hex `#RRGGBB` (regex
> `^#[0-9A-Fa-f]{6}$`, stored max length 7); `icon` optional free string key (max 50), client-mapped.
- **(a) [recommended]** `color` = required 7-char hex string `#RRGGBB` (validated by regex
  `^#[0-9A-Fa-f]{6}$`, stored max length 7); `icon` = optional free string key (max 50), client maps
  to a glyph, server does not enumerate icons. Trade-off: server can't guarantee the icon key is
  renderable.
- **(b)** `color` optional/nullable (fallback color in the client); `icon` optional. Trade-off:
  simpler input, but the "có màu … phục vụ biểu đồ" concept then leans entirely on the client.
- **(c)** `icon` as a server-side enum (fixed catalog). Trade-off: guarantees renderability but
  couples the API to an icon catalog the spec never defines and needs a migration to extend.

**OQ3 — Category bootstrap step + backfill mechanism.**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** new `SuggestedCategoriesBootstrapStep` on the
> shared M3 seam (`Multiple = true`), plus a **second dedicated** `SuggestedCategoriesBackfillHostedService`
> mirroring `OwnerRepresentativeBackfillHostedService` (own DI scope, idempotent, logs + swallows
> startup failure, no-op when nothing missing), registered in `Program.cs` via `AddHostedService`.
Two parts. Bootstrap: a **new** `SuggestedCategoriesBootstrapStep : IRegistrationBootstrapStep`
(`Multiple = true`) on the shared M3 seam is the clear, spec-aligned choice (no reasonable
alternative — `AuthService` already runs all steps) and is treated as settled unless the user objects.
The open part is the **backfill**:
- **(a) [recommended]** A **second dedicated** `SuggestedCategoriesBackfillHostedService`, mirroring
  `OwnerRepresentativeBackfillHostedService` exactly (own DI scope, idempotent, logs + swallows
  startup failure), registered alongside it in `Program.cs`. Trade-off: two boot-time queries instead
  of one, but each self-heals independently and stays a 1:1 mirror of the proven M3 pattern.
- **(b)** Extend/replace the existing hosted service into one combined
  `RegistrationBackfillHostedService` that runs both owner-rep and category backfills in one scope.
  Trade-off: one boot pass, fewer moving parts — but edits an already-shipped, reviewed M3 file and
  couples two independent invariants.

**OQ4 — Do categories reactivate on name reuse, like tags?**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a): YES** — categories reactivate on name reuse exactly
> like tags. A create whose normalized name matches a soft-deleted category revives that row instead
> of creating a duplicate.
Spec §3.4/§5 mandate reactivation **only for tags**. §3.3 is silent for categories. Because
uniqueness is active-only, creating a category whose name matches a *soft-deleted* category is not
blocked by the uniqueness rule — so the behavior must be chosen:
- **(a) [recommended]** Categories reactivate too (same as tags): reusing a soft-deleted category's
  name un-deletes the old row and relinks its history instead of creating a second same-named
  category. Trade-off: mild extension of a tag-only spec rule, but avoids two same-named categories
  (one deleted, one active) that would confuse name-grouped stats — consistent with the §3.3/§3.4
  "đồng bộ" framing for soft-delete/history.
- **(b)** Categories do **not** reactivate — a fresh active category is created (allowed, since
  uniqueness is active-only); the old deleted one stays deleted with its history. Strictly literal to
  the spec. Trade-off: can leave a deleted + an active category with the same name.
- **(c)** Categories neither reactivate nor allow the collision — reusing a soft-deleted category's
  name is rejected with `CategoryNameDuplicate`, and the user must explicitly restore the old one via
  a new restore endpoint. Trade-off: safest against dupes, but adds a restore concept the spec never
  mentions.

**OQ5 — Reactivation matching + field semantics (tags, and categories if OQ4=a).**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** name matching uses the DB's `utf8mb4_unicode_ci`
> collation (case- AND accent-insensitive). On reactivation: clear `is_deleted`; for categories,
> **overwrite** the revived row's color/icon with the new request values and **do NOT** change its
> default flag as a side effect; tags carry only a name so nothing else changes.
- **Name-match sensitivity:** the `categories`/`tags` name columns use `utf8mb4_unicode_ci`, which is
  **both case- AND accent-insensitive** — so an app-level `Name == request.Name` comparison runs in
  the DB and treats "Ăn uống" = "ăn uống" = "an uong" as the same name (for both uniqueness and
  reactivation matching).
  - **(a) [recommended]** Accept the column collation (case- and accent-insensitive) as the matching
    rule — simplest, consistent with how usernames rely on `utf8mb4_unicode_ci`, and arguably desirable
    (users won't create "Ăn uống" and "An uong" as separate categories). Trade-off: users cannot keep
    two names that differ only by accent/case.
  - **(b)** Case-insensitive but **accent-sensitive** matching (a dedicated accent-sensitive
    collation such as `utf8mb4_0900_as_ci`/`_bin` on those columns, or a normalized compare). Trade-off:
    "Cà phê" ≠ "Ca phe" — closer to literal text, but needs a per-column collation override and more
    careful queries.
- **Fields on reactivation:** when a create request reuses a soft-deleted name —
  - Tags carry only a name, so reactivation just clears `IsDeleted` (nothing else to set).
  - Categories (if OQ4=a) carry color/icon in the create request: **(a) [recommended]** overwrite the
    reactivated category's color/icon with the new request's values (treat create-with-same-name as
    "recreate"); **(b)** keep the old row's color/icon untouched (ignore the new request's values).
    The reactivated category stays **non-default** either way (default is only set via the dedicated
    endpoint, OQ7).

**OQ6 — Name normalization + max length.**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** trim leading/trailing whitespace before persisting
> and before the uniqueness/reactivation check; case/accent handling per OQ5; max length 100 chars
> (mirrors Members).
- **(a) [recommended]** Trim leading/trailing whitespace before persisting and before the uniqueness
  /reactivation check; case/accent handling per OQ5; max length **100** chars (mirrors members).
  Trade-off: none significant; matches the Members precedent.
- **(b)** No trim (store as typed) / different max length. Trade-off: " Ăn uống" and "Ăn uống" become
  distinct rows; inconsistent with Members.

**OQ7 — Default-category reassignment endpoint shape + atomic swap.**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** a dedicated `PUT api/v1/categories/{uuid}/default`
> performs the atomic swap (clear old default, set new) in one `ExecuteTransactionAsync`; the normal
> `PUT /categories/{uuid}` update edits name/color/icon only and CANNOT change `isDefault`.
- **(a) [recommended]** A dedicated `PUT api/v1/categories/{uuid}/default` (no body); the service
  clears the current default and sets the target in **one** `ExecuteTransactionAsync`. `PUT
  /categories/{uuid}` (update) edits name/color/icon only and **cannot** change `isDefault`. Only an
  active, owned category can be made default (soft-deleted → 404). Trade-off: one extra route, but the
  atomic-swap logic lives in exactly one place and update stays simple.
- **(b)** A boolean `isDefault` on the update body (`PUT /categories/{uuid}`); setting it true clears
  the old default in the same transaction; setting it false is rejected (can't unset without electing
  another). Trade-off: fewer routes, but overloads update with an invariant-bearing side effect and
  makes "set to false" an awkward no-op/error.

**OQ8 — Error-code block allocation.**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** Categories = 4xxx (`4000 CategoryNotFound` 404,
> `4001 CategoryNameDuplicate` 400, `4002 DefaultCategoryNotDeletable` 400); Tags = 5xxx
> (`5000 TagNotFound` 404, `5001 TagNameDuplicate` 400). Extend `ErrorException.GetDefaultHttpStatus`.
Members claimed 3xxx (3000/3001). Options:
- **(a) [recommended]** Categories = **4xxx** (`4000 CategoryNotFound`, `4001 CategoryNameDuplicate`,
  `4002 DefaultCategoryNotDeletable`); Tags = **5xxx** (`5000 TagNotFound`, `5001 TagNameDuplicate`).
  Trade-off: leaves 4002+/5001+ headroom; one feature per 1000-block, consistent with 2xxx/3xxx.
- **(b)** One shared 4xxx block for both (e.g. 4000–4002 categories, 4010–4011 tags). Trade-off:
  denser numbering, but couples two resources into one block and breaks the "one block per feature
  area" pattern.

**OQ9 — Do tags have any fields beyond name (color / icon / default)?**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** tags are **name-only** (no color, no icon, no
> default).
Spec §3.4 describes tags as free-form optional classification with no color/icon/default.
- **(a) [recommended]** Tags are **name-only** (no color, no icon, no default). Trade-off: none —
  matches the spec exactly; can be extended later without breaking changes.
- **(b)** Give tags an optional color for chart legends. Trade-off: adds columns/DTO fields the spec
  never asks for.

**OQ10 — Tag/Category ↔ Expense linking boundary (M4 vs M5).**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a):** M4 = `categories` + `tags` tables + CRUD ONLY.
> NO expense↔category FK, NO expense↔tag join table, NO §4.2 cross-user link validation — all deferred
> to M5 (Expenses). The `AddCategoriesAndTags` migration creates only the `categories` and `tags`
> tables.
- **(a) [recommended]** M4 delivers the `categories` and `tags` tables + CRUD **only**. The
  expense→category FK and the expense↔tag many-to-many join table are created by **M5 (Expenses)**,
  together with the §4.2 cross-user link validation and the §4.8 "can't select a deleted resource for
  new data" enforcement (which lives at expense-create time). Trade-off: M4 can't yet enforce §4.8 at
  the link site — but there is no link site until M5, so this is correct sequencing.
- **(b)** M4 also creates the join table / FK now. Trade-off: creates schema with no consumer and
  pre-empts M5 design decisions (e.g. join-table shape, ON DELETE behavior).

**OQ11 — Endpoint surface + default sort order.**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** the full endpoint surface below; categories
> sorted default-first then name A→Z; tags name A→Z; `includeDeleted=false` by default.
- **(a) [recommended]** Categories: `GET /categories?includeDeleted`, `GET /categories/{uuid}`, `POST
  /categories`, `PUT /categories/{uuid}`, `DELETE /categories/{uuid}`, `PUT
  /categories/{uuid}/default` — default sort **default-category first, then name A→Z**. Tags: `GET
  /tags?includeDeleted`, `GET /tags/{uuid}`, `POST /tags`, `PUT /tags/{uuid}`, `DELETE /tags/{uuid}` —
  default sort **name A→Z**. `includeDeleted=false` by default (mirrors Members). Trade-off: none
  significant.
- **(b)** Creation-order (`created_at`) sort instead of A→Z. Trade-off: selection lists aren't
  alphabetical; harder to scan.

**OQ12 — Guard when soft-deleting a category/tag referenced by future expenses.**
> ~~**OQ12**~~ → **Answered 2026-07-14 (option a):** no reference-guard on soft-delete now or later;
> the only delete guard is default-category-not-deletable (`4002`). Mirrors Members OQ9 and §4.7/§4.8.
- **(a) [recommended]** No guard now and none later — soft-delete only hides the resource from
  new-data selection; it never hides or blocks historical expenses (mirrors Members OQ9 and §4.7/§4.8).
  The default-category-not-deletable guard (§4.6) is the *only* delete guard on categories; tags have
  no delete guard. Trade-off: a category/tag still referenced by old expenses stays visible in that
  old data after deletion — which is exactly the intended history-preserving behavior.
- **(b)** A future warning/block when the category/tag is still referenced. Trade-off: contradicts the
  "delete = hide from new data only" model; flagged for completeness.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 12 Open Questions — these
> are now decisions, not vetoable assumptions. Each is derived from spec/prior decisions.

- Both `Category` and `Tag` are **soft-deletable** — each implements `IEntityDeletable` with an
  `is_deleted` column, reusing `BaseRepository.Query`'s built-in filter and the `includeDeleted`
  parameter (as Members did, `members.md` OQ3).
- All category/tag endpoints are **guarded** (valid access token required); there is no anonymous
  operation.
- `users` are not soft-deletable, so the `user_id` FK cascade is inert in practice; kept for
  referential integrity and consistency with `members`/`auth_tokens`.
- "Unique active name" and "exactly one default category" cannot be enforced by a plain MariaDB unique
  index (no filtered indexes); both are enforced in application code and kept idempotent by the
  bootstrap/backfill (see Future Improvements for a generated-column option).
- Tier limits on the number of categories/tags (§3.11) are **out of scope** (Milestone 10); M4 imposes
  no count limit.
- The suggested-category set is seeded once per user; the backfill only touches users that lack it —
  it never adds a second copy for a user who already has categories.
- The reactivated tag/category keeps its original `Uuid`, `CreatedAt`, and history links (that is the
  whole point of reactivation); only `IsDeleted` (and, per OQ5, color/icon for categories) change.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repos use DiDecoration
> `[ScopedService]`. All user-facing strings Vietnamese. Concrete names below reflect the
> **recommended** OQ answers and MUST be re-synced to the user's choices before coding.

### Step 1 — Entities

1. `Database/Entities/Category.cs` (POCO, `partial`, `IEntity` + `IEntityDeletable`): `ulong Id`,
   `string Uuid`, `ulong UserId`, `required string Name`, `required string Color`, `string? Icon`,
   `bool IsDefault`, `bool IsDeleted`, `DateTime CreatedAt`, `DateTime UpdatedAt`; nav `User User`.
2. `Database/Entities/Partials/Category.cs`: ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt =
   AppDateTime.Now`; static `ConfigureModel(ModelBuilder)`:
   - Table `categories`; `id` PK; `uuid` (max 64, unique index); `user_id` (indexed); `name` (max
     100); `color` (max 7); `icon` (max 50, nullable); `is_default` (bool default `false`);
     `is_deleted` (bool default `false`); `created_at`; `updated_at` (`ValueGeneratedOnAddOrUpdate` +
     `current_timestamp(6) ON UPDATE current_timestamp(6)`).
   - FK: `HasOne(User).WithMany().HasForeignKey(UserId).OnDelete(Cascade)` (mirrors `Member`).
   - Add a `SuggestedCategories` static descriptor (name/icon/color/isDefault tuples per OQ1) shared
     by the bootstrap step and the backfill, so the seeded set is defined once. **Exact seed set:**

     | Name | Icon | Color | Default |
     |---|---|---|---|
     | Ăn uống | 🍜 | `#F97316` | **yes** |
     | Đi lại | 🚗 | `#3B82F6` | no |
     | Khách sạn | 🏨 | `#8B5CF6` | no |
     | Mua sắm | 🛍️ | `#EC4899` | no |
     | Khác | ⋯ | `#6B7280` | no |
3. `Database/Entities/Tag.cs` (POCO, `partial`, `IEntity` + `IEntityDeletable`): `ulong Id`, `string
   Uuid`, `ulong UserId`, `required string Name`, `bool IsDeleted`, `DateTime CreatedAt`, `DateTime
   UpdatedAt`; nav `User User`.
4. `Database/Entities/Partials/Tag.cs`: ctor + static `ConfigureModel`: table `tags`; same column
   conventions (`uuid` unique, `user_id` indexed, `name` max 100, soft-delete flag, timestamps); FK
   cascade to `users`.
5. `Database/AppDbContext.cs`: add `DbSet<Category> Categories => Set<Category>();` and `DbSet<Tag>
   Tags => Set<Tag>();`; invoke `Category.ConfigureModel(modelBuilder)` and
   `Tag.ConfigureModel(modelBuilder)` in `OnModelCreating`. `AppDbContext.partial.cs` untouched
   (soft-delete filtering is generic).

### Step 2 — EF migration

- `dotnet ef migrations add AddCategoriesAndTags --project .\FairShareMonApi\FairShareMonApi.csproj`
  (offline via the pinned design-time factory). **Migration name: `AddCategoriesAndTags`.**
- Review: `categories` + `tags` tables, utf8mb4/unicode_ci, unique index on each `uuid`, index on each
  `user_id`, FK cascade, bool defaults, `updated_at` default. Keep the model snapshot in sync. Apply
  to the dev DB during the Test step per the orchestration protocol.

### Step 3 — Error codes + messages

Append to `Constants/ErrorCodes.cs` (never renumber). **4xxx block = Categories, 5xxx = Tags** (OQ8):

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `4000` | `CategoryNotFound` | 404 | "Không tìm thấy danh mục." |
| `4001` | `CategoryNameDuplicate` | 400 | "Tên danh mục đã tồn tại." |
| `4002` | `DefaultCategoryNotDeletable` | 400 | "Không thể xóa danh mục mặc định." |
| `5000` | `TagNotFound` | 404 | "Không tìm thấy nhãn." |
| `5001` | `TagNameDuplicate` | 400 | "Tên nhãn đã tồn tại." |

- Extend `ErrorException.GetDefaultHttpStatus`: `4000`→404, `4001`→400, `4002`→400, `5000`→404,
  `5001`→400.
- `4000`/`5000` used for every resource-owned miss (never 403), preferred over the generic `NotFound
  (1003)` so clients get a resource-specific signal.
- Success messages (via the endpoint contract): categories — "Thêm danh mục thành công." / "Cập nhật
  danh mục thành công." / "Đã xóa danh mục." / "Đã đặt danh mục mặc định."; tags — "Thêm nhãn thành
  công." / "Cập nhật nhãn thành công." / "Đã xóa nhãn."

### Step 4 — Repositories

`Repositories/CategoryRepository.cs` — `ICategoryRepository : IBaseRepository,
IQueryRepository<Category>` + sealed impl (`[ScopedService]`, extends `BaseRepository`):
- `Query(tracking = false, includeDeleted = false)` → `Query<Category>(...)`.
- `ListByUserAsync(userUuid, includeDeleted, ct)` — scoped; sort default-first then name A→Z (OQ11).
- `GetByUuidAsync(userUuid, categoryUuid, ct)` — resource-owned (`Uuid == categoryUuid && User.Uuid
  == userUuid`), includes deleted (callers decide).
- `FindActiveByNameAsync(userUuid, name, ct)` — for the uniqueness check (active only).
- `FindDeletedByNameAsync(userUuid, name, ct)` — for reactivation (OQ4=yes, soft-deleted only).
- `CreateAsync(userUuid, category, ct)` — resolves `user_id`; in-transaction uniqueness re-check;
  `NoCommit()` + signal on duplicate/unknown user.
- `UpdateAsync(userUuid, categoryUuid, name, color, icon, ct)` — tracked update scoped to user;
  in-transaction uniqueness re-check excluding self; `NoCommit()` on miss/duplicate.
- `ReactivateAsync(userUuid, categoryUuid, color, icon, ct)` — clears `IsDeleted`, overwrites
  color/icon (OQ5), leaves the default flag untouched.
- `SoftDeleteAsync(userUuid, categoryUuid, ct)` — scoped; the default guard is enforced in the service
  (so `4002` can be raised distinctly).
- `SetDefaultAsync(userUuid, categoryUuid, ct)` — **atomic swap** in one `ExecuteTransactionAsync`:
  load the target (active, owned) → `NoCommit()`/miss if absent or deleted; clear `IsDefault` on the
  current default; set it on the target.
- `HasAnyCategoryAsync(userUuid, ct)` / `GetUserUuidsWithoutDefaultCategoryAsync(ct)` — backfill
  support (mirror `MemberRepository`'s bootstrap helpers).

`Repositories/TagRepository.cs` — `ITagRepository` + sealed impl:
- `Query`, `ListByUserAsync` (name A→Z), `GetByUuidAsync` (resource-owned, incl. deleted),
  `FindActiveByNameAsync`, `FindDeletedByNameAsync`, `CreateAsync` (uniqueness re-check),
  `ReactivateAsync(userUuid, tagUuid, ct)` (clears `IsDeleted`), `RenameAsync` (uniqueness re-check
  excluding self), `SoftDeleteAsync`.

> All name comparisons run in the DB against `utf8mb4_unicode_ci` (OQ5-a) — case/accent-insensitive.

### Step 5 — Services + mappings

`Services/Api/Categories/CategoriesService.cs` — `ICategoriesService` + sealed impl
(`[ScopedService]`, primary ctor injecting `ICategoryRepository`, `IMapper`,
`IValidator<CreateCategoryRequest>`, `IValidator<UpdateCategoryRequest>`):
- `ListAsync` / `GetAsync` (miss → `CategoryNotFound`).
- `CreateAsync` — validate → trim name → **create-path decision tree** (run inside one transaction to
  avoid a check-then-act race):
  1. an **active** category with the (normalized, case/accent-insensitive) name exists →
     `CategoryNameDuplicate (4001)`;
  2. else a **soft-deleted** category with that name exists → **reactivate** it (clear `is_deleted`,
     overwrite color/icon per OQ5, leave the default flag untouched) and return it;
  3. else → **insert** a new category (never default).
- `UpdateAsync` — validate → uniqueness (excluding self) → update name/color/icon; miss →
  `CategoryNotFound`; duplicate → `CategoryNameDuplicate`.
- `SetDefaultAsync` — resource-owned atomic swap; miss/deleted target → `CategoryNotFound`.
- `DeleteAsync` — fetch resource-owned; miss → `CategoryNotFound`; if `IsDefault` →
  `DefaultCategoryNotDeletable (4002)`; else soft-delete.
- **Bootstrap consumer:** `SeedSuggestedCategories(dbContext, user)` helper (staging rows only, no
  commit) used by the bootstrap step.
- **Backfill:** `EnsureSuggestedCategoriesForAllAsync(ct)` — idempotent; for each user lacking an
  active default category, seed the suggested set (and if they somehow have categories but no default,
  elect one) — returns count created.

`Services/Api/Categories/SuggestedCategoriesBootstrapStep.cs` —
`[ScopedService(typeof(IRegistrationBootstrapStep), Multiple = true)]` `IRegistrationBootstrapStep`
that stages the suggested `Category` rows (one `IsDefault = true`) on the context, mirroring
`OwnerRepresentativeBootstrapStep`. No `AuthService` change (it already runs all steps).

`Services/Api/Tags/TagsService.cs` — `ITagsService` + sealed impl:
- `ListAsync` / `GetAsync` (miss → `TagNotFound`).
- `CreateAsync` — validate → trim → **identical create-path decision tree** (in one transaction):
  1. an **active** tag with the normalized name exists → `TagNameDuplicate (5001)`;
  2. else a **soft-deleted** tag with that name exists → **reactivate** it (clear `is_deleted`; nothing
     else to set — tags are name-only) and return it (§3.4/§5);
  3. else → **insert** a new tag.
- `RenameAsync` — validate → uniqueness excluding self; miss → `TagNotFound`; duplicate →
  `TagNameDuplicate`.
- `DeleteAsync` — soft-delete; miss → `TagNotFound`.

`Mappings/CategoryProfile.cs` — `CreateMap<Category, CategoryResponse>()`.
`Mappings/TagProfile.cs` — `CreateMap<Tag, TagResponse>()`.

### Step 6 — DTOs + validators

- `Models/Categories/`: `CreateCategoryRequest { string Name, string Color, string? Icon }`,
  `UpdateCategoryRequest { string Name, string Color, string? Icon }`, `CategoryResponse { string
  Uuid, string Name, string Color, string? Icon, bool IsDefault, bool IsDeleted, DateTime CreatedAt }`.
- `Models/Tags/`: `CreateTagRequest { string Name }`, `UpdateTagRequest { string Name }`,
  `TagResponse { string Uuid, string Name, bool IsDeleted, DateTime CreatedAt }`.
- `Validators/Categories/`: `CreateCategoryRequestValidator`, `UpdateCategoryRequestValidator` —
  `Name` required + max 100 ("Tên danh mục không được để trống." / "…không được vượt quá 100 ký
  tự."); `Color` required + hex regex `^#[0-9A-Fa-f]{6}$` ("Màu danh mục không hợp lệ."); `Icon`
  optional max 50 ("Icon danh mục không được vượt quá 50 ký tự.") — per OQ2.
- `Validators/Tags/`: `CreateTagRequestValidator`, `UpdateTagRequestValidator` — `Name` required + max
  100 ("Tên nhãn không được để trống." / "…không được vượt quá 100 ký tự.").
- Auto-registered by the existing `AddValidatorsFromAssembly`. Field keys camelCase (`name`, `color`,
  `icon`).

### Step 7 — Controllers

`Controllers/CategoriesController.cs` and `Controllers/TagsController.cs` (derive from `AppController`,
LOCKED). All actions guarded; Vietnamese `[SwaggerOperation]`/`[SwaggerResponse]`; `userUuid =
AuthenticatedUser.Id`.

Categories:

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/categories?includeDeleted=false` | query → `ApiResult<IReadOnlyList<CategoryResponse>>` | default-first, name A→Z |
| `GET api/v1/categories/{uuid}` | route → `ApiResult<CategoryResponse>` | resource-owned; miss → 4000 |
| `POST api/v1/categories` | `CreateCategoryRequest` → `ApiResult<CategoryResponse>` | dup → 4001; may reactivate (OQ4) |
| `PUT api/v1/categories/{uuid}` | `UpdateCategoryRequest` → `ApiResult<CategoryResponse>` | edit name/color/icon; dup → 4001; miss → 4000 |
| `PUT api/v1/categories/{uuid}/default` | route → `ApiResult` success message | atomic swap; miss/deleted → 4000 |
| `DELETE api/v1/categories/{uuid}` | route → `ApiResult` success message | default → 4002; miss → 4000 |

Tags:

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/tags?includeDeleted=false` | query → `ApiResult<IReadOnlyList<TagResponse>>` | name A→Z |
| `GET api/v1/tags/{uuid}` | route → `ApiResult<TagResponse>` | resource-owned; miss → 5000 |
| `POST api/v1/tags` | `CreateTagRequest` → `ApiResult<TagResponse>` | active dup → 5001; soft-deleted name → reactivate |
| `PUT api/v1/tags/{uuid}` | `UpdateTagRequest` → `ApiResult<TagResponse>` | rename; dup → 5001; miss → 5000 |
| `DELETE api/v1/tags/{uuid}` | route → `ApiResult` success message | soft-delete; miss → 5000 |

### Step 8 — Bootstrap wiring (OQ3) + backfill (OQ3)

1. Register `SuggestedCategoriesBootstrapStep` on the shared seam (`Multiple = true`). `AuthService`
   already iterates all `IRegistrationBootstrapStep`s — a registration now atomically creates the
   owner-rep member **and** the suggested categories (one default) in the same transaction; a rollback
   leaves none of them.
2. Add the backfill per OQ3: recommended `HostedServices/SuggestedCategoriesBackfillHostedService.cs`
   (own DI scope, idempotent, logs + swallows startup failure), registered in `Program.cs` via
   `AddHostedService` next to `OwnerRepresentativeBackfillHostedService` (service section only —
   pipeline order untouched).
3. Register's response is unchanged (still `UserResponse`); categories are a side effect.

### Step 9 — Tests (owned by the test-engineer; definitive list)

Reuse the Members harness pattern: `[Collection("AuthIntegration")]` so category/tag suites serialize
with auth (the backfill exercises a **global** `EnsureSuggestedCategoriesForAllAsync`); DB tests use
`AuthDbTestBase` (own connections) / `AuthApiTestBase` (app DI, real HTTP) with a unique lowercase
username prefix per class and dispose-time cleanup (deleting the prefix's users cascades to their
`categories`/`tags` rows via FK). Backfill tests assert **per-seeded-user**, never on the global
return count. All DB-dependent tests are `[SkippableFact]`.

**Unit (no DB):**
- `CreateCategoryRequestValidator` / `UpdateCategoryRequestValidator` — name required/trim/max-100;
  color required + valid/invalid hex; icon max-50/optional; exact Vietnamese messages; camelCase field
  keys (`name`/`color`/`icon`).
- `CreateTagRequestValidator` / `UpdateTagRequestValidator` — name required/max-100; messages; field
  key `name`.
- `CategoriesService` (fake `ICategoryRepository`) — create trims name; **create-path tree**:
  active-dup → 4001, soft-deleted-name → reactivate (overwrites color/icon, default flag untouched),
  else insert; update dup → 4001, miss → 4000; delete of default → 4002 **without** calling
  soft-delete; delete non-default soft-deletes; set-default miss/deleted → 4000; `includeDeleted`
  passthrough; backfill seeds the suggested set for a category-less user and is a no-op otherwise.
- `TagsService` (fake `ITagRepository`) — create trims; active-dup → 5001; soft-deleted-name →
  **reactivate**; rename dup → 5001, miss → 5000; delete miss → 5000; `includeDeleted` passthrough.

**Integration (real MariaDB — `CategoryRepositoryTests`, `TagRepositoryTests`):**
- Create sets uuid/UTC/user_id + flags; unknown user → signal; **resource-owned** — another user's
  category/tag invisible to get/list/update/delete/set-default (null/false, never the row).
- Soft-delete sets `is_deleted`, row still exists; default list excludes it, `includeDeleted:true`
  returns it (history preserved).
- **Unique active name** — a second active row with a colliding name is rejected; a colliding name
  against a *soft-deleted* row reactivates (both categories and tags, OQ4=yes) rather than
  duplicating; verify the case/accent-insensitivity per OQ5 (e.g. "Ăn uống" collides with "an uong").
- **Default invariant** — set-default clears exactly the previous default and sets the target
  atomically (never zero, never two); set-default on a soft-deleted/foreign category → miss; the
  default category cannot be soft-deleted at the repo layer used by the service.
- Sort order matches OQ11 (categories default-first then A→Z; tags A→Z).
- Backfill helpers (`GetUserUuidsWithoutDefaultCategoryAsync`) correctness, incl. treating a user with
  only soft-deleted categories as missing.

**Integration — bootstrap/backfill (`CategoryBootstrapTests`, app DI):**
- Register creates the suggested set with **exactly one** default, atomically alongside the owner-rep
  member; a throwing bootstrap rolls back user + member + categories together.
- Backfill gives a category-less user the suggested set (one default); running it again creates no
  duplicate; a user that already has a default is untouched.

**Endpoint (WebApplicationFactory — `CategoriesEndpointTests`, `TagsEndpointTests`):**
- Register → `GET /categories` already returns the five suggested categories (with the emoji icons +
  hex colors from OQ1) with exactly one `isDefault:true` — **"Ăn uống"**.
- Category create appears in the default list; edit persists name/color/icon; duplicate active name →
  400 code 4001; set-default flips the flag (old default now false); delete non-default → hidden from
  default list, present with `?includeDeleted=true`; delete default → 400 code 4002 and it remains.
- Tag create → appears; create reusing a soft-deleted tag's name → **reactivates** (same uuid,
  `isDeleted:false`), not a duplicate; duplicate active name → 400 code 5001; rename/delete work;
  soft-deleted tag hidden from default list, present with `?includeDeleted=true`.
- **Resource-owned:** another user's category/tag UUID → 404 (4000/5000) on get/put/delete/set-default
  (never 403).
- Invalid payloads → 400 with `error.fields` (Vietnamese, camelCase); anonymous → 401 wrapped.

### Step 10 — Wrap-up

- `dotnet build` clean; `dotnet test` green (DB tests skip only when MariaDB unreachable). Live smoke:
  register → new account has 5 categories (1 default) + reactivation/uniqueness/default-swap/delete
  guards + resource-owned 404s. `dotnet ef database update` per protocol. Update this doc's Progress
  Log + Final Outcome; note in `agent-dev-team.md` that M4 closed the M3 suggested-category obligation
  and reused the shared bootstrap seam.

## Impact Analysis

- **APIs:** eleven new endpoints — six under `api/v1/categories` (list, get, create, update, delete,
  set-default) and five under `api/v1/tags` (list, get, create, update, delete). No existing endpoint
  changes shape; register gains an atomic side effect (suggested categories).
- **Database:** new migration `AddCategoriesAndTags` — tables `categories` (color/icon/default/soft-
  delete, FK cascade to `users`, unique `uuid`, index `user_id`) and `tags` (soft-delete, FK cascade,
  unique `uuid`, index `user_id`). No data migration, but a one-time idempotent **backfill** seeds
  suggested categories for pre-existing users.
- **Infrastructure:** one new `IHostedService` (OQ3-a) registered in `Program.cs`; no new packages, no
  Redis involvement.
- **Services:** new `CategoriesService`, `TagsService`, `CategoryRepository`, `TagRepository`,
  `SuggestedCategoriesBootstrapStep`, `CategoryProfile`, `TagProfile`, four validators; a new bootstrap
  step registered on the existing seam (no `AuthService`/`UserRepository` change). `AppController`,
  `ApiResult`, middleware untouched.
- **UI:** none (API only).
- **Documentation:** this doc; `ErrorCodes` XML docs (4xxx/5xxx); a note in `agent-dev-team.md`.

## Decision Log

### Decision
**User checkpoint 2026-07-14 — all 12 Open Questions resolved; every recommended option (a) accepted,
except OQ1 where the user chose option (b) ("Ăn uống" as the default with a specific seed set).**

1. **Suggested-category set (OQ1b):** default = **"Ăn uống"** (NOT "Khác"); seed all five spec names
   with emoji icons + chart colors — Ăn uống 🍜 `#F97316` (default); Đi lại 🚗 `#3B82F6`; Khách sạn 🏨
   `#8B5CF6`; Mua sắm 🛍️ `#EC4899`; Khác ⋯ `#6B7280`. *Reason:* the everyday food category is the most
   useful fallback for uncategorized expenses; the concrete list makes the bootstrap deterministic.
2. **Color/icon representation (OQ2a):** `color` required hex `#RRGGBB` (regex `^#[0-9A-Fa-f]{6}$`, max
   length 7); `icon` optional free string key (max 50), client-mapped. *Reason:* charts need a color;
   the server should not enumerate an icon catalog the spec never defines.
3. **Bootstrap + backfill (OQ3a):** new `SuggestedCategoriesBootstrapStep` on the shared M3 seam
   (`Multiple = true`), plus a **second dedicated** `SuggestedCategoriesBackfillHostedService`
   mirroring `OwnerRepresentativeBackfillHostedService`. *Reason:* reuses the proven M3 seam without
   editing shipped files; each invariant self-heals independently.
4. **Category reactivation (OQ4a): YES** — categories reactivate on name reuse exactly like tags.
   *Reason:* avoids confusing duplicate (deleted + active) same-named categories and keeps §3.3/§3.4
   soft-delete behavior consistent.
5. **Reactivation matching + fields (OQ5a):** match via `utf8mb4_unicode_ci` (case- AND
   accent-insensitive); on reactivation clear `is_deleted`, overwrite the revived category's color/icon
   with the request values, and leave its default flag untouched; tags (name-only) just clear
   `is_deleted`. *Reason:* uses the column collation already in place; treating recreate-with-same-name
   as a refresh of the presentation fields is the least surprising.
6. **Name normalization + length (OQ6a):** trim; matching case/accent-insensitive per OQ5; max 100
   chars (mirrors Members). *Reason:* consistency with the Members precedent.
7. **Default reassignment endpoint (OQ7a):** dedicated `PUT /categories/{uuid}/default` does the atomic
   swap in one `ExecuteTransactionAsync`; the normal update cannot change `isDefault`. *Reason:* keeps
   the invariant-bearing swap in one place and keeps update simple.
8. **Error-code blocks (OQ8a):** Categories 4xxx (`4000`/`4001`/`4002`), Tags 5xxx (`5000`/`5001`);
   extend `ErrorException.GetDefaultHttpStatus`. *Reason:* one 1000-block per feature area, consistent
   with 2xxx/3xxx.
9. **Tag fields (OQ9a):** tags are name-only (no color/icon/default). *Reason:* matches §3.4 exactly.
10. **M4/M5 linking boundary (OQ10a):** M4 = `categories` + `tags` tables + CRUD only; expense↔category
    FK, expense↔tag join table, and §4.2 cross-user link validation all deferred to M5. *Reason:*
    there is no link site until Expenses exist; creating unused schema pre-empts M5 design.
11. **Endpoint surface + sort (OQ11a):** the full surface below; categories default-first then name
    A→Z, tags name A→Z; `includeDeleted=false` default. *Reason:* the default is the natural top of a
    category selection list.
12. **Soft-delete guard (OQ12a):** no reference-guard now or later; the only delete guard is
    default-category-not-deletable (`4002`). *Reason:* §4.7/§4.8 keep historical data inviolable and
    expect deleted resources to stay visible in old data (mirrors Members OQ9).

### Reason
User answers at the Milestone-4 planning checkpoint (2026-07-14), brought by the orchestrator per the
Clarification-First protocol; recorded so the implementer needs no other source.

### Alternatives Considered
The full option sets (b)/(c) with trade-offs, as presented to the user, are preserved in the struck
Open Questions above.

### Decision (inherited — NOT reopened)
Resource-owned 404 scoping; soft-delete via `IEntityDeletable`/`is_deleted`; UTC timestamps;
entity/repo/controller conventions; EF-migration-only schema; the M3 shared registration-bootstrap
seam (`IRegistrationBootstrapStep` + `IUserRepository.CreateWithBootstrapAsync`) that M4 extends with
the suggested-category step; "unique active name" and "exactly one default" enforced at the app level
(no MariaDB filtered index).

## Progress Log

### 2026-07-14

- Feature-planner: required reading completed — `The-ideal.md` §3.1 (seeding clause) / §3.3 / §3.4 /
  §5 / §4.1 / §4.2 / §4.6 / §4.7 / §4.8; `CLAUDE.md`; `.agents/rules/rules.md`; `.claude/rules/rule.md`
  (template); `planning/members.md` (exemplar — structure, harness, seam), `planning/agent-dev-team.md`
  (M4 line + protocol), `planning/user-authentication.md` (conventions); and the live code:
  `MembersService`, `OwnerRepresentativeBootstrapStep`, `IRegistrationBootstrapStep`,
  `MemberRepository`, `UserRepository` (`CreateWithBootstrapAsync`), `AuthService` (RegisterAsync),
  `Member` entity + partial, `AppDbContext`, `ErrorCodes`, `ErrorException`,
  `OwnerRepresentativeBackfillHostedService`, `MembersController`, member DTOs/validators/profile,
  `Program.cs` (hosted-service + validator registration), `AuthDbTestBase` (harness).
- Drafted this plan: `categories` + `tags` entities + `AddCategoriesAndTags` migration; eleven
  endpoints (six categories incl. `/default`, five tags); `CategoriesService` / `TagsService` /
  repositories / four validators / DTOs / profiles; the suggested-category bootstrap step on the shared
  M3 seam + an idempotent backfill hosted service; 4xxx (categories) + 5xxx (tags) error blocks; full
  test list.
- **12 Open Questions raised** (suggested-category colors/icons + default; color/icon representation;
  bootstrap step + backfill mechanism; category reactivation-on-name-reuse; reactivation match +
  field semantics; name normalization + length; default-reassignment endpoint shape; error-code block
  allocation; tags name-only; tag/category↔expense linking boundary; endpoint surface + sort;
  soft-delete guard) — awaiting user answers at the checkpoint before implementation starts.

### 2026-07-14 (checkpoint — all Open Questions answered, plan unblocked)

- **User answered all 12 Open Questions; every recommended option (a) accepted, except OQ1 = option
  (b)** (see the consolidated Decision Log entry): OQ1 default = **"Ăn uống"** with the fixed seed set
  (🍜 `#F97316` default; Đi lại 🚗 `#3B82F6`; Khách sạn 🏨 `#8B5CF6`; Mua sắm 🛍️ `#EC4899`; Khác ⋯
  `#6B7280`); OQ2 color required hex / icon optional string; OQ3 new bootstrap step + second dedicated
  backfill hosted service; OQ4 categories reactivate (yes); OQ5 `utf8mb4_unicode_ci` match, overwrite
  color/icon on reactivation, default flag untouched; OQ6 trim + max 100; OQ7 dedicated
  `/default` endpoint; OQ8 4xxx categories / 5xxx tags; OQ9 tags name-only; OQ10 tables + CRUD only,
  linking deferred to M5; OQ11 default-first then A→Z / tags A→Z; OQ12 no reference-guard.
- Plan synchronized with the answers: OQ1 seed table pinned in Step 1, create-path decision tree spelled
  out for both resources in Step 5 (active collision → NameDuplicate; soft-deleted match → reactivate;
  else insert), Step 4 category `ReactivateAsync` finalized, error-code table (4xxx/5xxx) confirmed,
  test list updated (Ăn uống default, reactivation for both resources, accent-insensitive collision),
  Assumptions moved to confirmed, Decision Log recorded. Open Questions struck and marked answered.
  **No open questions remain — implementation can start.**

### 2026-07-14 (implementation — Steps 1-8 built, migration applied, live smoke passed)

- **Implementer:** required reading completed — this doc; `CLAUDE.md`; `.agents/rules/rules.md`;
  `.claude/rules/rule.md`; and the live M3 reference code (`Member` entity + partial,
  `MemberRepository`, `MembersService`, `OwnerRepresentativeBootstrapStep`, `MemberProfile`, member
  DTOs/validators, `MembersController`, `OwnerRepresentativeBackfillHostedService`,
  `IRegistrationBootstrapStep`, `UserRepository.CreateWithBootstrapAsync`, `AuthService`,
  `ErrorCodes`, `ErrorException`, `AppDbContext`(+partial), `BaseRepository`/`IQueryRepository`,
  `AppController`, `Program.cs`, `AddMembers` migration).
- **Step 1 — entities:** `Database/Entities/Category.cs` (+ `Partials/Category.cs`) and
  `Database/Entities/Tag.cs` (+ `Partials/Tag.cs`), both `IEntity` + `IEntityDeletable`, ctor sets
  `Uuid.NewV7()` + `AppDateTime.Now`, snake_case columns, `updated_at` computed default, FK cascade to
  `users`. `Category` carries `color` (varchar 7) / nullable `icon` (varchar 50) / `is_default`. The
  fixed OQ1 seed set lives in `Category.SuggestedCategories` (+ `SuggestedCategory` record) with a
  shared `Category.BuildSuggestedSet(userId)` builder used by both the bootstrap step and the backfill.
  `AppDbContext` gains `Categories`/`Tags` DbSets + `ConfigureModel` calls;
  `AppDbContext.partial.cs` untouched.
- **Step 2 — migration:** `AddCategoriesAndTags` (`20260714033039_...`) authored offline via the
  design-time factory and reviewed (both tables, utf8mb4/unicode_ci, unique `uuid` index, `user_id`
  index, FK cascade, bool defaults, `updated_at` computed default, `color` varchar(7), nullable `icon`
  varchar(50)); snapshot in sync. **Applied to the dev DB** (`database update`) per the Decision-Log
  protocol so the Test step runs against live schema.
- **Step 3 — errors:** `ErrorCodes` 4xxx (`4000 CategoryNotFound`, `4001 CategoryNameDuplicate`,
  `4002 DefaultCategoryNotDeletable`) + 5xxx (`5000 TagNotFound`, `5001 TagNameDuplicate`);
  `ErrorException.GetDefaultHttpStatus` extended (4000/5000→404, 4001/4002/5001→400). Vietnamese
  messages per the Step-3 table.
- **Step 4 — repositories:** `CategoryRepository` + `TagRepository` (interface+sealed impl,
  `[ScopedService]`). To honor the cross-cutting "reactivation + uniqueness = one atomic
  check-then-act inside the write transaction", the create/update/rename decision trees run **inside
  a single `ExecuteTransactionAsync`** in the repository (not split across separate `Find*` calls),
  returning a shared `NameWriteResult<T>` / `NameWriteStatus` (`Repositories/NameWriteResult.cs`) so
  the service maps duplicate→NameDuplicate, unknown-scope→NotFound, else success. Category also has
  the atomic `SetDefaultAsync` swap, `SoftDeleteAsync`, backfill helpers
  (`HasAnyCategoryAsync`, `GetUserUuidsWithoutDefaultCategoryAsync`,
  `SeedSuggestedOrElectDefaultAsync`); the listed `FindActiveByNameAsync`/`FindDeletedByNameAsync`
  are also exposed. Name matching relies on the `utf8mb4_unicode_ci` column collation (case/accent-
  insensitive, OQ5). Categories sort default-first then name A→Z; tags name A→Z.
- **Step 5 — services + mappings:** `CategoriesService` (list/get/create/update/set-default/delete +
  `EnsureSuggestedCategoriesForAllAsync` backfill) and `TagsService` (list/get/create/rename/delete),
  both `[ScopedService]` primary-ctor with injected `IValidator`s; `CategoryProfile`/`TagProfile`.
  `SuggestedCategoriesBootstrapStep` (`Multiple = true`) stages the seed set on the shared M3 seam;
  `AuthService` unchanged (already iterates all steps).
- **Step 6 — DTOs + validators:** `Models/Categories/*` + `Models/Tags/*`; `Validators/Categories/*`
  (name req+max100, color req+hex `^#[0-9A-Fa-f]{6}$`, icon optional max50) and `Validators/Tags/*`
  (name req+max100), Vietnamese messages, auto-registered.
- **Step 7 — controllers:** `CategoriesController` (6 endpoints incl. `PUT /{uuid}/default`) +
  `TagsController` (5 endpoints), derive from `AppController`, guarded, Vietnamese Swagger.
- **Step 8 — wiring:** `SuggestedCategoriesBackfillHostedService` registered in `Program.cs`
  (service section only, next to the owner-rep backfill; pipeline order untouched).
- **Build:** `dotnet build` clean (only the pre-existing pinned-AutoMapper NU1903 warnings).
  `dotnet test` green — **202 passed, 0 failed, 0 skipped** (existing suite; MariaDB reachable).
- **Live smoke (all passed, data cleaned up afterwards):** register → new account seeded with the 5
  categories (emoji icons + hex colors), exactly one default = **Ăn uống**, sorted default-first then
  A→Z; category create/update/delete; set-default swap leaves exactly one default; delete-default →
  400 `4002`; active-name duplicate → 400 `4001`; **accent/case-insensitive** collision confirmed
  ("an uong" vs seeded "Ăn uống" → 4001); category reactivation on name reuse (delete then recreate
  reused the soft-deleted row's UUID, `isDeleted:false`, color/icon overwritten, name kept as the
  collation-equal existing value); tag create/rename/delete + reactivation (same UUID revived);
  resource-owned misses from a second user → 404 `4000`/`5000` (never 403) on get/put/delete/set-
  default; anonymous → 401.
- **In-latitude design choices (within the doc, noted for the record):** (1) the atomic create/update
  logic lives in one repository transaction returning `NameWriteResult<T>` rather than the service
  orchestrating separate `Find*`+`Create*`/`Reactivate*` calls — this is what satisfies the doc's
  explicit "one atomic check-then-act, no race" requirement (the granular `Find*` methods are still
  exposed). (2) The shared seed builder is `Category.BuildSuggestedSet` on the entity (single source
  of the OQ1 set) instead of a `CategoriesService.SeedSuggestedCategories(dbContext,user)` helper, so
  the bootstrap step and backfill can share it without a step→service dependency. (3) On category
  reactivation the stored name is left as-is (the collation treats request and stored names as equal)
  — the doc's OQ5 reactivation field list is is_deleted + color/icon only, so name is intentionally
  untouched. No requirements were invented and no Open Questions were reopened.

### 2026-07-14 (tests — Step 9 complete, full suite green, DB clean)

- **Test-engineer:** required reading completed — this doc (Step 9 test list is the checklist);
  `CLAUDE.md`; the M4 production code (entities `Category`/`Tag` + partials, `CategoryRepository` /
  `TagRepository` / `NameWriteResult`, `CategoriesService` / `TagsService` /
  `SuggestedCategoriesBootstrapStep`, `CategoryProfile` / `TagProfile`, DTOs, validators, controllers,
  `ErrorCodes`); and the M3 harness + suites mirrored exactly (`AuthDbTestBase`, `AuthApiTestBase`,
  `DatabaseFixture`, `[Collection("AuthIntegration")]`, `MembersValidatorsTests`, `MembersServiceTests`,
  `MemberRepositoryTests`, `MemberBootstrapTests`, `MembersEndpointTests`).
- **135 M4 tests added across 9 files** (all in `FairShareMonApi.Tests`, production code untouched):
  - **Unit — validators (37):** `CategoriesValidatorsTests.cs` — `CreateCategoryRequestValidatorTests`
    (22) + `UpdateCategoryRequestValidatorTests` (5): name required/whitespace/max-100, color
    required + `#RRGGBB` hex regex (valid/invalid theories), icon optional/max-50, pinned Vietnamese
    messages. `TagsValidatorsTests.cs` — `CreateTagRequestValidatorTests` (7) +
    `UpdateTagRequestValidatorTests` (3): name required/whitespace/max-100, messages.
  - **Unit — services (34, fake repos):** `CategoriesServiceTests.cs` (20) — create trims name;
    create-path tree (active-dup → 4001, soft-deleted-name → reactivate overwriting color/icon while
    leaving the default flag untouched, else insert); update dup → 4001 / miss → 4000; delete-of-default
    → 4002 WITHOUT calling soft-delete; delete non-default soft-deletes; set-default miss → 4000;
    unknown user → 4000; `includeDeleted` passthrough; backfill seeds per missing user / no-op otherwise.
    `TagsServiceTests.cs` (14) — create trims; active-dup → 5001; soft-deleted-name → reactivate (same
    row); rename dup → 5001 / miss → 5000; delete miss → 5000; unknown user → 5000; `includeDeleted`
    passthrough. Both fakes re-implement the atomic decision tree in memory so the service mapping is
    proven without a DB (mirrors the M3 `FakeMemberRepository` approach).
  - **Integration — repositories (37, real MariaDB, `[SkippableFact]`):** `CategoryRepositoryTests.cs`
    (23) — create defaults (uuid/UTC/user_id/flags), unknown-user → NotFound, resource-owned invisibility
    on get/list/update/delete/set-default, soft-delete-keeps-the-row + includeDeleted, unique-active-name
    duplicate + **accent/case-insensitive collision** ("Ăn uống" vs "an uong"), **reactivation**
    (same uuid, is_deleted→false, color/icon overwritten, default untouched, no duplicate), default
    invariant (atomic swap clears exactly the old + sets the new, count-default = 1; soft-deleted/foreign
    target → false), OQ11 default-first→A→Z sort, backfill helper
    (`GetUserUuidsWithoutDefaultCategoryAsync` treats a user with only a soft-deleted default as missing)
    and `SeedSuggestedOrElectDefaultAsync` (seeds five-with-one-default / elects / no-op).
    `TagRepositoryTests.cs` (14) — the parallel set for tags (name-only reactivation, A→Z sort, no
    default/color).
  - **Integration — bootstrap/backfill (5, app DI):** `CategoryBootstrapTests.cs` — register seeds
    exactly the five suggested categories with one default = "Ăn uống" (🍜 `#F97316`), atomically
    alongside the owner-rep member; a forced bootstrap failure rolls back user + categories together;
    backfill gives a category-less user the five-with-one-default and is idempotent (re-run creates no
    duplicate); an already-seeded user is untouched (same default row). Backfill assertions are
    per-seeded-user, never on the global return count.
  - **Endpoint — HTTP (22, WebApplicationFactory):** `CategoriesEndpointTests.cs` (13) — register →
    `GET /categories` has the five seeded with "Ăn uống" default (emoji/hex, default-first sort);
    create appears with color/icon; update persists name/color/icon; duplicate active name → 400 `4001`;
    accent/case-insensitive collision vs the seed → 400 `4001`; `PUT /{uuid}/default` swaps (old default
    cleared); delete non-default hides from default list but shows with `?includeDeleted=true`; delete
    default → 400 `4002` and it remains; reactivation over HTTP (same uuid, color/icon overwritten, not
    duplicated); another user's UUID → 404 `4000` on get/put/delete/set-default (never 403); invalid
    color / empty name → 400 `1001` with camelCase `error.fields` (`color`/`name`); anonymous → 401
    wrapped. `TagsEndpointTests.cs` (9) — the parallel tag set incl. reactivation, duplicate → `5001`,
    resource-owned 404 `5000`, and `GET /tags` empty on a fresh account (tags are not seeded).
- **Harness reuse:** all DB-dependent tests are `[SkippableFact]` gated on `DatabaseFixture.SkipIfNoDb()`;
  each suite carries a unique lowercase `t<hex>_` username prefix and cleans up on dispose via
  `AuthDbTestBase`/`AuthApiTestBase` (deleting the prefix's users cascades to their `categories`/`tags`
  rows through the FK). All category/tag suites use `[Collection("AuthIntegration")]` so they serialize
  with auth (the global `EnsureSuggestedCategoriesForAllAsync` backfill must not run concurrently). No
  harness bug needed fixing — the M3 infrastructure worked unchanged for M4.
- **Result:** `dotnet build` clean (only the pre-existing pinned-AutoMapper NU1903 warnings).
  `dotnet test` **337 passed, 0 failed, 0 skipped** (202 existing + 135 M4; MariaDB + Redis reachable so
  nothing skipped). **Determinism confirmed** — two consecutive full runs both 337/0/0. **DB verified
  clean afterwards** — 0 leftover `t*_` test users/categories/tags (all tables empty), mirroring the M3
  sweep. **No production code was modified; no production bugs found.**

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

- **Code-reviewer verdict: APPROVE, 0 blocking findings** (first pass, no fix loop). All 12 OQ
  decisions verified against the code; no silent deviations. Adversarial pass confirmed:
  - **Resource-owned scoping on every path** — list/get/create/update/delete **and** `PUT
    /{uuid}/default` are all scoped by `User.Uuid`; an ownership miss → 404 (`4000` categories /
    `5000` tags), never 403; the set-default swap is user-scoped so it can never clear another user's
    default (no cross-user clobbering).
  - **Atomic check-then-act create/update trees** — the create/update decision trees run inside one
    `ExecuteTransactionAsync` (active collision → `4001`/`5001`; soft-deleted name match → reactivate;
    else insert); no stray `SaveChanges`.
  - **Default-category invariant un-violable** — atomic user-scoped swap; delete-of-default raises
    `4002` **before** any soft-delete; the update body carries no `isDefault`; reactivation leaves the
    default flag untouched; a soft-deleted row can never carry `is_default = true`, so no
    double-default state is reachable.
  - **Bootstrap + backfill** — `SuggestedCategoriesBootstrapStep` on the shared M3 seam
    (`Multiple = true`, inside the user-creation transaction) + an idempotent own-scope
    `SuggestedCategoriesBackfillHostedService` mirroring the owner-rep one on a disjoint table (no
    destructive race).
  - **Conventions** — `Uuid.NewV7()`, snake_case, `IEntityDeletable`, primary constructors, Vietnamese
    strings, `AppController` untouched, `AppDbContext.partial.cs` still filters-only, model snapshot in
    sync; `utf8mb4_unicode_ci` gives the OQ5 case/accent-insensitive matching. OQ10 respected — no
    expense FK / join table / §4.2 link validation leaked into M4.
- **4 non-blocking notes accepted (not fixed — no risk, no compounding):** (N1) the public repo surface
  `FindActiveByNameAsync`/`FindDeletedByNameAsync` is doc-sanctioned but currently unused (dead API
  that could be trimmed later); (N2) the delete default-guard is a two-transaction read-then-write with
  a theoretical same-user TOCTOU, matching the M3 owner-rep guard pattern (accepted); (N3) app-level
  "unique active name" has the documented concurrency window (no MariaDB filtered index — already in
  Assumptions/Future Improvements); (N4) validator max-length runs pre-trim, identical to the Members
  baseline (consistent, not a defect).
- Final state: build clean, **`dotnet test` = 337 passed / 0 failed / 0 skipped** (202 existing + 135
  new M4), deterministic across two consecutive runs; post-run sweep left the DB clean. Milestone 4
  complete.

### 2026-07-14 (post-M4 refactor — hosted-service registration)

- `SuggestedCategoriesBackfillHostedService` now inherits from `Microsoft.Extensions.Hosting.BackgroundService`
  (work moved to `ExecuteAsync`) and is registered **solely** via DiDecoration's `[BackgroundService]`
  attribute. Removed the manual `AddHostedService` call in `Program.cs` — which had been a **double
  registration** (the class already carried `[BackgroundService]`), so the backfill previously ran twice
  each boot and now runs once. Behavior otherwise unchanged. See
  `planning/hosted-service-di-registration.md`.

## Final Outcome

Milestone 4 (Categories + Tags) implemented per the approved plan and closed after code review. Delivered:
`categories` + `tags` tables + `AddCategoriesAndTags` migration (authored **and applied** to the dev
DB); six guarded `api/v1/categories` endpoints (list with `includeDeleted`, get, create, update, delete,
`PUT /{uuid}/default`) and five guarded `api/v1/tags` endpoints (list with `includeDeleted`, get,
create, update, delete); `CategoriesService` / `TagsService` / `CategoryRepository` / `TagRepository`
(atomic uniqueness + reactivation via a shared `NameWriteResult`), `CategoryProfile` + `TagProfile`,
four validators, DTOs; the 4xxx (categories) + 5xxx (tags) error blocks. Unique-active-name enforcement
plus **accent/case-insensitive reactivation-on-name-reuse for BOTH categories and tags** (active
collision → `4001`/`5001`; soft-deleted name match → reactivate the old row, relinking history). The
default-category **always-exactly-one / not-deletable** invariant (`4002`) with an atomic user-scoped
swap via the dedicated `/default` endpoint. Suggested-category seeding on registration ("Ăn uống"
default + 4 more, with emoji icons + hex colors) via the shared M3 registration-bootstrap seam
(`SuggestedCategoriesBootstrapStep`), plus an idempotent `SuggestedCategoriesBackfillHostedService` for
pre-existing users. Resource-owned scoping returns `4000`/`5000` (404) on every ownership miss (never
403). `AuthService`, `UserRepository`, `AppController`, and the `Program.cs` pipeline order were not
modified. Build clean, **337/337 tests pass** (0 skipped), live smoke + code review (APPROVE, 0
blocking) confirmed all behaviors. No open questions remain; no unrecorded deviations.

## Future Improvements

- DB-level enforcement of "unique active name" and "exactly one default category" via a MariaDB
  generated column + unique index (e.g. a nullable marker populated only for active rows / only for the
  default row), removing reliance on app-level idempotency.
- An explicit restore/undelete endpoint for a soft-deleted category/tag (symmetric with the Members
  "undo delete" idea) — distinct from the name-reuse reactivation shipped here (OQ4=yes), for restoring
  a row without recreating it by name.
- Per-tag color/legend once charts group by tag (OQ9-b), if the Web UI needs it.
- Merge two categories/tags (re-point historical expense links) — a natural extension once expenses
  exist (M5).
