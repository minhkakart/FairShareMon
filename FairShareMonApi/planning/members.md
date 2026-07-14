# Members (Milestone 3: Members)

CRUD + soft-delete for ledger **members** (participants in cost-splitting, no account), plus the
deferred **owner-representative member** bootstrap on registration and its idempotent backfill for
users created during Milestone 2.

## Objective

Implement `The-ideal.md` §2 (Thành viên) and §3.2 (Quản lý thành viên) on top of the shipped Auth
skeleton:

- Add, rename, and **soft-delete** members owned by the current user.
- List members, with an option to include soft-deleted members (for stats/export per §3.2).
- Guarantee every ledger **always has exactly one owner-representative member** ("thành viên đại
  diện chính chủ sổ") — created **atomically** with the `users` row on registration, and
  **backfilled idempotently** for any user that registered during Milestone 2 without one.
- Honor the cross-cutting rules: resource-owned 404 scoping (§4.1), soft-delete preserves history
  inviolably (§4.7), deleted members are not selectable for new data (§4.8).

This milestone owns the **members** table and the **registration-bootstrap seam**. Suggested-category
bootstrap stays in Milestone 4 (Categories); this doc establishes the shared seam so M4 extends it
rather than re-inventing it.

## Background

- Milestone 2 (`planning/user-authentication.md`) shipped `users` + `auth_tokens`, the first
  migration `AddUsersAndAuthTokens`, the real opaque-token stack, and `AuthService.RegisterAsync`
  (`Services/Api/Auth/AuthService.cs`) which currently creates **only the `users` row** via
  `IUserRepository.CreateAsync` (a single `ExecuteTransactionAsync` with in-transaction uniqueness
  re-check + duplicate-key race absorption).
- **Explicit deferral obligation inherited from M2** (Decision Log + Assumptions of
  `user-authentication.md`): "Milestone 3 (Members) creates the owner-representative member on
  register … each of those milestones inherits an explicit backfill obligation for any user
  registered before it ships." The dev DB currently has **0 real users** (one harmless smoke user
  may remain), but the bootstrap and the backfill mechanism must both exist and be idempotent.
- Conventions confirmed by reading the live code:
  - Entities: partial-class POCO (`Database/Entities/<Name>.cs`) + `Database/Entities/Partials/<Name>.cs`
    (ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt = AppDateTime.Now`; static `ConfigureModel(ModelBuilder)`
    invoked from `AppDbContext.OnModelCreating`). `IEntity` = `ulong Id`, `string Uuid` (unique, max
    64), `CreatedAt`, `UpdatedAt` (`ValueGeneratedOnAddOrUpdate` + `current_timestamp(6)` default).
    Timestamps are UTC (`AppDateTime.Now`, OQ10 of M2). Snake_case columns.
  - Soft delete: `IEntityDeletable { bool IsDeleted }`; `BaseRepository.Query<T>(tracking,
    includeDeleted)` auto-applies `AsNoTracking` and excludes `IsDeleted` rows unless
    `includeDeleted: true`. `IQueryRepository<T>.Query(...)` is the per-repo surface.
  - Repositories mirror `UserRepository` / `AuthTokenRepository`: interface + sealed impl in one
    file, `[ScopedService(typeof(IX))]`, extend `BaseRepository`, writes via `ExecuteTransactionAsync`
    with `TransactionContext.NoCommit()` on business failure.
  - Controllers derive from `AppController` (LOCKED); routes `api/v{version:apiVersion}/[controller]`;
    `[ResponseWrapped]` auto-wraps into `ApiResult<T>`; `AuthenticatedUser.Id` = current user's UUID
    string (accessed via the base property, which throws 401 when anonymous).
  - Errors: `ErrorCodes` 1xxx infra, 2xxx auth. `ErrorException(code, message)` maps to an HTTP
    status via `GetDefaultHttpStatus`. Vietnamese for every user-facing message + Swagger summary.
  - `ApiResult<T>.Success(...)`, `ApiResult.SuccessMessage(...)`, `ApiResult.Failure(...)`.
  - Validation: manual FluentValidation; services inject `IValidator<T>` and call
    `ValidateAndThrowAsync` (→ `ValidationException` → 400 with `error.fields`).

## Requirements

From `The-ideal.md` §2, §3.2, §4.1/§4.7/§4.8, and CLAUDE.md / rules.md conventions:

- **Member concept (§2):** a participant in cost-splitting, named/managed by the ledger owner, with
  **no account**. Belongs to exactly one user (`user_id` FK). Every ledger has **exactly one**
  owner-representative member.
- **Owner-representative bootstrap (§2, §3.1):** created automatically and atomically when a user
  registers. A user must never exist without one.
- **Add / rename (§3.2):** create a member (name); rename an existing member.
- **Soft-delete (§3.2, §4.7, §4.8):** deleting a member is a soft-delete — the member vanishes from
  selection lists used to build **new** data, but **all historical data (expenses, shares, debt
  balance, stats) is preserved and still shows the member's name**. Deleted members are **not
  selectable for new data**.
- **List (§3.2):** list the current user's members; a flag includes soft-deleted members (for
  stats/export).
- **Absolute privacy / resource-owned (§4.1):** every query is scoped
  `WHERE uuid = :uuid AND user_id = :current_user_id`; an ownership miss returns **404, never 403**
  (do not leak existence).
- **Backfill:** an idempotent mechanism ensures every existing user has an owner-representative
  member, for users created during M2 before this milestone shipped.
- **Conventions:** entity per rules.md; schema via **EF migration only**; writes via
  `ExecuteTransactionAsync`; `Async` suffix + `CancellationToken` threaded; Vietnamese messages;
  claim a **new 3xxx error-code block** for Members.

## Open Questions

> **All 9 answered by the user at the 2026-07-14 checkpoint — every recommended option (a) was
> accepted.** The struck questions below carry the binding answers inline; the full options/trade-offs
> are preserved for the record and mirrored in the Decision Log. No open questions remain —
> implementation can start.

**OQ1 — Registration-bootstrap seam (how the owner-rep member is created atomically with the user).**
> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** extensible bootstrap seam — `IUserRepository` gains a
> `CreateWithBootstrapAsync` overload that runs bootstrap steps inside the **same
> `ExecuteTransactionAsync`** after `user.Id` is assigned; M3 registers the owner-rep-member step,
> **M4 will register the suggested-category step on the same seam.**
`ExecuteTransactionAsync` cannot nest (recorded in the M2 test-isolation note), so a plain "call
`MembersService.Create…` after `UserRepository.CreateAsync`" would be **two transactions — not
atomic** and is rejected: a crash between them would leave a user with no owner-rep member. The member
insert must share the user-creation transaction. Options:
- **(a) [recommended] Extensible bootstrap-steps seam.** Introduce `IRegistrationBootstrap` (or a
  `Multiple = true` set of `IRegistrationBootstrapStep`) and give `IUserRepository` a
  `CreateWithBootstrapAsync(User, Func<AppDbContext, User, CancellationToken, Task> bootstrap, ct)`
  overload that inserts the user and then runs the bootstrap **inside the same
  `ExecuteTransactionAsync`** (after `SaveChanges` assigns `user.Id`). M3 registers the owner-rep
  step; **M4 registers the suggested-category step on the same seam.** Trade-off: one layer of
  indirection with a single consumer today, but it is exactly the seam M4 reuses, and it keeps
  member-building logic out of `UserRepository`.
- **(b) Direct extension of `UserRepository.CreateAsync`.** `UserRepository` itself inserts the
  owner-rep member row in the same transaction (references the `Member` entity directly). Simplest
  now, but couples the user repository to the members domain, and M4 must re-open the same method to
  add categories.
- **(c) Dedicated `IRegistrationService` orchestrator.** A new service owns one transactional method
  that inserts user + owner-rep member (+ later categories). Centralizes bootstrap, but adds a new
  abstraction and moves the uniqueness/race handling out of `AuthService`/`UserRepository`.

**OQ2 — Backfill mechanism for users lacking an owner-rep member.**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** idempotent startup `IHostedService` that creates a
> missing owner-rep member for any user lacking one; a no-op when none are missing.
Options:
- **(a) [recommended] Idempotent startup `IHostedService`.** On boot, find users with no active
  owner-rep member and create one; a no-op when none exist (one cheap query per boot). Runs regardless
  of environment, self-heals.
- **(b) Lazy ensure-on-access.** `MembersService` ensures the owner-rep member exists on the first
  member-list/login per user. Spreads cost, but complicates every read path and needs a write on a
  nominally read call.
- **(c) One-off manual invocation** (admin endpoint or a documented script run once). Least code, but
  not self-healing and easy to forget.

**OQ3 — Soft-delete flag naming/semantics.**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** `IEntityDeletable` with an `is_deleted` column,
> reusing `BaseRepository.Query`'s built-in soft-delete filter and the `includeDeleted` parameter
> ("view deleted members" = `includeDeleted: true`).
Options:
- **(a) [recommended] `IEntityDeletable` with an `is_deleted` column.** Reuses `BaseRepository.Query`'s
  automatic soft-delete filter and the existing `includeDeleted` parameter directly; §3.2's
  "view deleted members" maps to `includeDeleted: true` with zero new plumbing.
- **(b) `is_active` column (the spec's vocabulary).** Matches §2/CLAUDE.md wording literally, but has
  inverted semantics vs `IEntityDeletable.IsDeleted`, so it needs a custom query filter and bypasses
  the built-in machinery. (CLAUDE.md explicitly allows either: "`members.is_active` … and/or an
  `IsDeleted`/`IEntityDeletable` flag".)

**OQ4 — Owner-representative member invariants.**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** renamable **YES**, deletable **NO** — a delete
> attempt returns error `3001 OwnerRepresentativeNotDeletable` with a clear Vietnamese message.
Options:
- **(a) [recommended] Renamable YES, deletable NO.** Analogous to the default-category invariant
  ("luôn phải tồn tại đúng một"). The owner sets their real display name, but the ledger can never
  lose its one owner-rep member. Delete attempt → a clear Vietnamese error.
- **(b) Both locked** (name fixed too). Simpler invariant, but the owner cannot show their real name
  on shares/reports.
- **(c) Both allowed** (deletable). Contradicts "mỗi sổ luôn có đúng một thành viên đại diện" — not
  recommended.

**OQ5 — Owner-rep member's default name at registration.**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option b):** the owner-rep member is created with the fixed
> name **"Tôi"** at registration (the owner can rename it afterward per OQ4).
The spec does not state it. Options:
- **(a) the account username**, **(b) a fixed "Tôi"**, **(c) a fixed "Chủ sổ"**, **(d) empty
  placeholder** prompting the owner to rename. Recommendation deferred to the user (display-preference
  decision). If OQ4 = (a), the owner can rename afterward regardless.

**OQ6 — Member name uniqueness per ledger.**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** free-form — duplicate member names are allowed, no
> uniqueness enforcement. Error code `3002 MemberNameDuplicate` is **dropped** from the plan (unused).
Options:
- **(a) [recommended] Free-form; duplicates allowed.** §3.2 is silent on uniqueness (unlike §3.3 for
  categories), and real people share names ("hai bạn tên An"). No uniqueness code needed.
- **(b) Unique among active members** (mirroring categories §3.3, active-only). Cleaner selection
  lists, but needs error code `3002` and **application-level** enforcement — MariaDB has no partial/
  filtered unique index, so "unique among not-deleted" cannot be a plain DB unique index.

**OQ7 — Member fields beyond display name, and name length.**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** display name only this milestone (no note, avatar,
> or color); name max length **100 chars**.
Options:
- **(a) [recommended] Display name only this milestone; max length 100 chars.** §3.2 mentions only
  add/rename; members (unlike categories) have no color/icon in the spec.
- **(b) Add an optional free-text note.** **(c) Add avatar/color** for nicer UI/reports. Either adds
  columns + DTO fields the spec doesn't ask for yet. Also confirm the name max length (recommended 100).

**OQ8 — Default sort of the member list.**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** owner-rep member first, then name A→Z.
Options:
- **(a) [recommended] Owner-rep first, then name A→Z** (owner-rep is the natural top of every
  selection list). **(b) creation order** (`created_at`). **(c) name A→Z only**.

**OQ9 — Guard when soft-deleting a member referenced by open data.**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** no guard now and none later — soft-delete only
> hides the member from new-data selection; it never hides or blocks historical data.
No guard is *possible* in M3 —
expenses/events (which reference members) are Milestones 5/6 and their tables do not exist yet.
Options:
- **(a) [recommended] No guard now, and none later.** §3.2/§4.7 explicitly keep all historical data
  intact and merely hide the member from **new**-data selection; a referenced member is *supposed* to
  stay visible in old data after deletion. Confirm this stance so later milestones don't add a block.
- **(b) A future warning/block** (e.g., warn when the member is a payer in an open event). Contradicts
  the "delete = hide from new data only" spec model; flagged only for completeness.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 9 Open Questions — these
> are now decisions, not vetoable assumptions. Each is derived from spec/prior decisions.

- Members are **soft-deletable** (spec §3.2) — the `Member` entity implements `IEntityDeletable` with
  an `is_deleted` column (OQ3, confirmed).
- `users` are **not** soft-deletable (confirmed M2), so member `user_id` FK cascade delete is inert in
  practice; kept for referential integrity and consistency with `auth_tokens`.
- All member endpoints except none are **guarded** (require a valid access token); there is no
  anonymous member operation.
- The owner-rep member participates in cost-splitting exactly like any member downstream (§3.5:
  "Thành viên đại diện chủ sổ luôn xuất hiện trong danh sách phần gánh"); that share-list behavior is
  an **Expenses (M5)** concern — M3 only guarantees the member exists and is flagged.
- Suggested-category bootstrap is **out of scope** (M4); M3 only builds the shared seam it will reuse.
- The one leftover M2 smoke user (if still present) is dev-only data; the backfill will simply give it
  an owner-rep member idempotently like any other user.
- DB-level "exactly one owner-rep per user" and "unique active name" cannot be enforced by a plain
  MariaDB unique index (no filtered indexes); these invariants are enforced in application code and
  kept idempotent by the bootstrap/backfill. (See Future Improvements for a generated-column option.)

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repos use DiDecoration
> `[ScopedService]`. All user-facing strings Vietnamese. Concrete field/flag names below reflect the
> **confirmed** Open-Question answers (all option (a) except OQ5 = "Tôi").

### Step 1 — Entity

1. `Database/Entities/Member.cs` (POCO, `partial`, implements `IEntity` + `IEntityDeletable`):
   `ulong Id`, `string Uuid`, `ulong UserId` (FK → `users.id`), `required string Name`,
   `bool IsOwnerRepresentative`, `bool IsDeleted`, `DateTime CreatedAt`, `DateTime UpdatedAt`;
   navigation `User User`.
2. `Database/Entities/Partials/Member.cs`: ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt =
   AppDateTime.Now`; static `ConfigureModel(ModelBuilder)`:
   - Table `members`. `id` PK; `uuid` (max 64, unique index); `user_id` (indexed);
     `name` (max 100, OQ7); `is_owner_representative` (bool, default `false`);
     `is_deleted` (bool, default `false`, OQ3); `created_at`; `updated_at`
     (`ValueGeneratedOnAddOrUpdate` + `current_timestamp(6) ON UPDATE current_timestamp(6)`).
   - FK: `HasOne(User).WithMany().HasForeignKey(UserId).OnDelete(Cascade)` (mirrors `AuthToken`).
3. `Database/AppDbContext.cs`: add `DbSet<Member> Members => Set<Member>();` and invoke
   `Member.ConfigureModel(modelBuilder)` in `OnModelCreating`. `AppDbContext.partial.cs` untouched
   (soft-delete filtering already handled generically by `BaseRepository.Query`).

### Step 2 — EF migration

- `dotnet ef migrations add AddMembers --project .\FairShareMonApi\FairShareMonApi.csproj` (offline via
  the pinned design-time factory). **Migration name: `AddMembers`.**
- Review: `members` table, utf8mb4/unicode_ci, unique index on `uuid`, index on `user_id`, FK cascade,
  bool defaults, `updated_at` default. Keep the model snapshot in sync. Apply to the dev DB during the
  Test step per the orchestration protocol (dev DB is disposable).

### Step 3 — Error codes + messages

Append to `Constants/ErrorCodes.cs` (never renumber). **3xxx block claimed for Members:**

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `3000` | `MemberNotFound` | 404 | "Không tìm thấy thành viên." |
| `3001` | `OwnerRepresentativeNotDeletable` | 400 | "Không thể xóa thành viên đại diện chủ sổ." |

- `3002 MemberNameDuplicate` is **dropped** (OQ6 = free-form; no uniqueness enforcement).
- Extend `ErrorException.GetDefaultHttpStatus` for `3000`→404, `3001`→400.
- `3000` is preferred over the generic `NotFound (1003)` so clients get a member-specific signal; it
  still carries HTTP 404 and is used for every resource-owned miss (never 403).
- Success messages: create "Thêm thành viên thành công."; rename "Cập nhật thành viên thành công.";
  delete "Đã xóa thành viên." (delivered via the endpoint contract below).

### Step 4 — Repository

`Repositories/MemberRepository.cs` — `IMemberRepository : IBaseRepository, IQueryRepository<Member>`
+ sealed impl (`[ScopedService(typeof(IMemberRepository))]`, extends `BaseRepository`):
- `Query(bool tracking = false, bool includeDeleted = false)` → `Query<Member>(...)`.
- `ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken)` — scoped to the user;
  default sort per OQ8 (recommended: owner-rep first, then `Name`).
- `GetByUuidAsync(string userUuid, string memberUuid, CancellationToken)` — **resource-owned**
  (`uuid == memberUuid && User.Uuid == userUuid`); returns the row incl. deleted (callers decide).
- `CreateAsync(string userUuid, Member member, CancellationToken)` — resolves `user_id` from
  `userUuid` (mirror `AuthTokenRepository.ResolveUserIdAsync`); `NoCommit()` + null when the user is
  unknown. (No uniqueness check — OQ6 = free-form.)
- `RenameAsync(string userUuid, string memberUuid, string name, CancellationToken)` — tracked update
  scoped to the user; `NoCommit()` + false on miss.
- `SoftDeleteAsync(string userUuid, string memberUuid, CancellationToken)` — set `IsDeleted = true`
  scoped to the user; `NoCommit()` + false on miss. (Owner-rep guard lives in the service so the
  distinct `3001` error can be raised — see Step 5.)
- **Bootstrap/backfill support:**
  - `HasOwnerRepresentativeAsync(string userUuid, CancellationToken)` — exists check (idempotency).
  - `GetUserUuidsWithoutOwnerRepresentativeAsync(CancellationToken)` — for the backfill (OQ2).
  - The atomic-with-user insert is provided through the OQ1 seam, not a standalone transaction (a
    standalone `CreateOwnerRepresentativeAsync` would break atomicity).

### Step 5 — Service

`Services/Api/Members/MembersService.cs` — `IMembersService` + sealed impl
(`[ScopedService(typeof(IMembersService))]`, primary constructor injecting `IMemberRepository`,
`IMapper`, `IValidator<CreateMemberRequest>`, `IValidator<UpdateMemberRequest>`):
- `ListAsync(string userUuid, bool includeDeleted, CancellationToken)` → `IReadOnlyList<MemberResponse>`.
- `GetAsync(string userUuid, string memberUuid, CancellationToken)` → `MemberResponse`; miss →
  `ErrorException(MemberNotFound)`.
- `CreateAsync(string userUuid, CreateMemberRequest, CancellationToken)` — validate → create (never
  owner-rep) → `MemberResponse`. (No uniqueness check — OQ6.)
- `RenameAsync(string userUuid, string memberUuid, UpdateMemberRequest, CancellationToken)` — validate
  → repo rename (owner-rep rename **allowed**, OQ4); miss → `MemberNotFound`.
- `DeleteAsync(string userUuid, string memberUuid, CancellationToken)` — fetch resource-owned; miss →
  `MemberNotFound`; if `IsOwnerRepresentative` → `ErrorException(OwnerRepresentativeNotDeletable)`
  (OQ4 — owner-rep not deletable); else soft-delete.
- **Bootstrap seam consumer (OQ1):** the owner-rep creation logic (build a `Member` with
  `IsOwnerRepresentative = true` and the fixed name **"Tôi"** — OQ5) exposed as the registration
  bootstrap step / callback that `AuthService.RegisterAsync` runs inside the user-creation transaction.
- **Backfill (OQ2):** `EnsureOwnerRepresentativeForAllAsync(CancellationToken)` — idempotent; consumed
  by the chosen backfill trigger.

`Mappings/MemberProfile.cs` — `CreateMap<Member, MemberResponse>()` (and map `IsDeleted` /
`IsOwnerRepresentative` into the response as needed).

### Step 6 — DTOs + validators

- `Models/Members/`: `CreateMemberRequest { string Name }`, `UpdateMemberRequest { string Name }`,
  `MemberResponse { string Uuid, string Name, bool IsOwnerRepresentative, bool IsDeleted, DateTime
  CreatedAt }`.
- `Validators/Members/CreateMemberRequestValidator.cs`, `UpdateMemberRequestValidator.cs` (manual
  FluentValidation, auto-registered by the existing `AddValidatorsFromAssembly`): `Name` required
  ("Tên thành viên không được để trống."), trimmed, length 1–100 (per OQ7)
  ("Tên thành viên không được vượt quá 100 ký tự.").

### Step 7 — Controller

`Controllers/MembersController.cs` (derives from `AppController`; `AppController` stays LOCKED). All
actions guarded (no `[AllowAnonymous]`), all with Vietnamese `[SwaggerOperation]`/`[SwaggerResponse]`.
`userUuid` = `AuthenticatedUser.Id`.

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/members?includeDeleted=false` | `bool includeDeleted` (query) → `ApiResult<IReadOnlyList<MemberResponse>>` | §3.2 include-deleted option; default excludes deleted |
| `GET api/v1/members/{uuid}` | route uuid → `ApiResult<MemberResponse>` | resource-owned; miss → 404 (`3000`) |
| `POST api/v1/members` | `CreateMemberRequest` → `ApiResult<MemberResponse>` | never owner-rep |
| `PUT api/v1/members/{uuid}` | `UpdateMemberRequest` → `ApiResult<MemberResponse>` | rename; resource-owned; (OQ4) |
| `DELETE api/v1/members/{uuid}` | route uuid → `ApiResult` success message | soft-delete; owner-rep → `3001`; miss → `3000` |

### Step 8 — Registration bootstrap wiring (OQ1) + backfill (OQ2)

1. Wire the chosen OQ1 seam so `AuthService.RegisterAsync` creates the owner-rep member **inside the
   same transaction** as the `users` insert (extend `IUserRepository.CreateAsync` /
   `CreateWithBootstrapAsync` per the answer). Preserve the existing duplicate-username re-check +
   `DbUpdateException` race absorption. A registration that rolls back must leave **neither** a user
   **nor** a member.
2. Wire the chosen OQ2 backfill trigger (recommended: idempotent `IHostedService` in a new
   `Startup/…` or `HostedServices/…` file, registered in `Program.cs`) that ensures every existing
   user has an owner-rep member; a no-op when none are missing.
3. `AuthProfile`/`UserResponse` unchanged — register still returns `UserResponse` only (M2 OQ5). The
   member is a side effect, not part of the register response (unless the user asks otherwise).

### Step 9 — Tests (owned by the test-engineer; definitive list)

**Unit (no DB):**
- `CreateMemberRequestValidator` / `UpdateMemberRequestValidator` — required, trim, max-length; exact
  Vietnamese messages; `error.fields` key = camelCase `name`.
- `MembersService` (fake `IMemberRepository`) — create never sets owner-rep; delete of an owner-rep
  member throws `OwnerRepresentativeNotDeletable (3001)`; miss on get/rename/delete throws
  `MemberNotFound (3000)`; duplicate names are **accepted** (OQ6 — no uniqueness); list passes
  `includeDeleted` through.

**Integration (real MariaDB, rollback/cleanup harness — skippable):**
- `MemberRepositoryTests` — create sets uuid/timestamps (UTC)/`user_id`; **resource-owned**: another
  user's member is invisible to `GetByUuidAsync`/`ListByUserAsync`/`RenameAsync`/`SoftDeleteAsync`
  (returns null/false, not the row); soft-delete sets `is_deleted` and the row **still exists**
  (`includeDeleted: true` returns it, default list hides it); rename persists; default sort matches
  OQ8; `HasOwnerRepresentativeAsync` / `GetUserUuidsWithoutOwnerRepresentativeAsync` correctness.
- Bootstrap atomicity — registering a user creates **exactly one** owner-rep member in the same
  transaction; a forced failure in the bootstrap step rolls back the user too (no orphan user, no
  orphan member).
- Backfill idempotency — a user with no owner-rep member gets exactly one after the backfill; running
  the backfill again creates **no** duplicate; a user that already has one is untouched.

**Endpoint (WebApplicationFactory, real HTTP — skippable):**
- `GET /members` returns only the caller's members; **another user's member UUID → 404 envelope
  (`3000`)** on GET/PUT/DELETE (resource-owned, never 403).
- Register → the new account's `GET /members` already contains **one owner-rep member**
  (`isOwnerRepresentative = true`, name "Tôi").
- Create → appears in the default list; soft-delete → **absent** from the default list but **present**
  with `?includeDeleted=true` (history-preserving hide-from-selection).
- Delete the owner-rep member → **400 envelope (`3001`)**; it remains in the list.
- Rename works, including renaming the owner-rep member (allowed per OQ4).
- Invalid create payload → 400 with `error.fields` (camelCase `name`, Vietnamese message).
- All member endpoints require auth → anonymous call → 401 wrapped envelope.

### Step 10 — Wrap-up

- `dotnet build` clean; `dotnet test` green (DB tests skip only when MariaDB unreachable). Live smoke:
  register → new account has one owner-rep member → add/rename/soft-delete → include-deleted list →
  owner-rep delete rejected → resource-owned 404 for another user's member.
- `dotnet ef database update` per protocol. Update this doc's Progress Log + Final Outcome; note in
  `agent-dev-team.md` that M3 closed the M2 owner-rep backfill obligation and established the shared
  registration-bootstrap seam for M4.

## Impact Analysis

- **APIs:** five new endpoints under `api/v1/members` (list, get, create, rename, delete). No existing
  endpoint changes shape. Register's response is unchanged; it gains an atomic side effect (owner-rep
  member).
- **Database:** new migration `AddMembers` — table `members` (FK cascade to `users`, unique index on
  `uuid`, index on `user_id`, soft-delete flag, owner-rep flag). No data migration, but a one-time
  **backfill** ensures owner-rep members for pre-existing users (idempotent).
- **Infrastructure:** possibly a new `IHostedService` for the backfill (OQ2-a); registered in
  `Program.cs`. No new packages. No Redis involvement.
- **Services:** new `MembersService`, `MemberRepository`, `MemberProfile`, two validators; a
  registration-bootstrap seam touching `AuthService.RegisterAsync` + `IUserRepository`/`UserRepository`
  (per OQ1). `AppController`, `ApiResult`, middleware untouched.
- **UI:** none (API only).
- **Documentation:** this doc; `ErrorCodes` XML docs (3xxx); a note in `agent-dev-team.md` on the
  closed backfill obligation + the shared seam for M4.

## Decision Log

### Decision
**User checkpoint 2026-07-14 — all 9 Open Questions resolved; every recommended option (a) accepted,
except OQ5 where the user chose the fixed name "Tôi".**

1. **Registration-bootstrap seam (OQ1a):** `IUserRepository` gains a `CreateWithBootstrapAsync`
   overload that inserts the user and then runs registered bootstrap step(s) **inside the same
   `ExecuteTransactionAsync`** (after `SaveChanges` assigns `user.Id`). M3 registers the
   owner-rep-member step; **M4 will register the suggested-category step on the same seam.**
   *Reason:* `ExecuteTransactionAsync` cannot nest, so the member insert must share the user-creation
   transaction to be atomic; the step seam keeps member-building out of `UserRepository` and is the
   exact extension point M4 reuses instead of re-inventing bootstrap.
2. **Backfill (OQ2a):** an idempotent startup `IHostedService` creates a missing owner-rep member for
   any user lacking one; a no-op when none are missing. *Reason:* automatic and self-healing across
   environments; the guard query is cheap and pre-release the set is empty.
3. **Soft-delete flag (OQ3a):** `IEntityDeletable` with an `is_deleted` column, reusing
   `BaseRepository.Query`'s built-in soft-delete filter and `includeDeleted` ("view deleted members"
   = `includeDeleted: true`). *Reason:* zero new plumbing; §3.2's include-deleted list maps straight
   onto the existing parameter.
4. **Owner-rep invariants (OQ4a):** renamable **YES**, deletable **NO** — delete attempt →
   `3001 OwnerRepresentativeNotDeletable`. *Reason:* mirrors the default-category "exactly one always"
   invariant; the owner can set a real display name but the ledger never loses its representative.
5. **Owner-rep default name (OQ5 = "Tôi"):** the bootstrap creates the owner-rep member with the fixed
   name **"Tôi"**. *Reason:* immediately meaningful in Vietnamese share/report lists; renamable later.
6. **Name uniqueness (OQ6a):** free-form — duplicate member names allowed, no uniqueness enforcement;
   error code `3002` dropped as unused. *Reason:* §3.2 is silent on uniqueness and real people share
   names.
7. **Fields/length (OQ7a):** display name only this milestone (no note/avatar/color); name max length
   **100 chars**. *Reason:* §3.2 mentions only add/rename; members carry no color/icon in the spec.
8. **List sort (OQ8a):** owner-rep member first, then name A→Z. *Reason:* owner-rep is the natural top
   of every selection list.
9. **Soft-delete guard (OQ9a):** no guard now and none later — soft-delete only hides the member from
   new-data selection; it never hides or blocks historical data. *Reason:* §3.2/§4.7 make historical
   data inviolable and expect deleted members to remain visible in old data.

### Reason
User answers at the Milestone-3 planning checkpoint (2026-07-14), brought by the orchestrator per the
Clarification-First protocol; recorded verbatim so the implementer needs no other source.

### Alternatives Considered
The full option sets (b)/(c) with trade-offs, as presented to the user, are preserved in the struck
Open Questions above.

### Decision (inherited — NOT reopened)
Users are not soft-deletable; timestamps are UTC; entity/repo/controller conventions; resource-owned
404 scoping; the M2 deferral that assigns owner-rep bootstrap + its backfill to this milestone and the
suggested-category bootstrap to M4 (M3 builds the shared seam M4 extends).

## Progress Log

### 2026-07-14

- Feature-planner: required reading completed — `The-ideal.md` §2/§3.2/§4.1/§4.7/§4.8/§5,
  `CLAUDE.md`, `.claude/rules/rule.md`, `planning/agent-dev-team.md` (Milestone 3 line + protocol),
  `planning/user-authentication.md` (deferral/backfill obligation, conventions, decisions), and the
  live code: `AuthService`, `UserRepository`, `AuthTokenRepository`, `User` entity + partial,
  `AuthToken` partial, `AppDbContext`, `BaseRepository`/`IQueryRepository`/`IEntity`/`IEntityDeletable`,
  `AppController`/`AuthenticatedUser`/`IContextAuthenticated`, `AuthController`, `ErrorCodes`,
  `ErrorException`, `RegisterRequestValidator`, `UserResponse`, `AuthProfile`.
- Drafted this plan: `members` entity + `AddMembers` migration, five `api/v1/members` endpoints,
  `MembersService`/`MemberRepository`/validators, the atomic registration-bootstrap seam + idempotent
  backfill, 3xxx error block, full test list.
- **9 Open Questions raised** (bootstrap seam, backfill mechanism, soft-delete flag naming, owner-rep
  invariants, owner-rep default name, name uniqueness, member fields/length, list sort order,
  soft-delete guard) — awaiting user answers at the checkpoint before implementation starts.

### 2026-07-14 (checkpoint — all Open Questions answered, plan unblocked)

- **User answered all 9 Open Questions; every recommended option (a) accepted, except OQ5 = fixed name
  "Tôi"** (see the consolidated Decision Log entry): OQ1 extensible `CreateWithBootstrapAsync`
  same-transaction bootstrap seam (M4 extends it); OQ2 idempotent startup `IHostedService` backfill;
  OQ3 `IEntityDeletable`/`is_deleted`; OQ4 owner-rep renamable-yes/deletable-no (`3001`); OQ5 default
  name "Tôi"; OQ6 free-form names (dropped unused `3002`); OQ7 name-only, max 100; OQ8 owner-rep first
  then name A→Z; OQ9 no soft-delete guard ever.
- Plan synchronized with the answers: error-code table (3002 dropped), Steps 4/5 (no uniqueness check,
  owner-rep rename allowed, "Tôi" bootstrap name), test list, Assumptions moved to confirmed, Decision
  Log recorded. Open Questions struck and marked answered. **No open questions remain — implementation
  can start.**

### 2026-07-14 (implementation — api-implementer)

- **Step 1 (Entity):** added `Database/Entities/Member.cs` (POCO: `IEntity` + `IEntityDeletable`,
  `UserId` FK, `Name`, `IsOwnerRepresentative`, `IsDeleted`, timestamps, `User` nav) and
  `Database/Entities/Partials/Member.cs` (ctor `Uuid.NewV7()`/`AppDateTime.Now`; `ConfigureModel`:
  table `members`, snake_case, `uuid` unique index, `user_id` index, `name` max 100, bool defaults
  `false`, `updated_at` computed default, FK `HasOne(User).WithMany().OnDelete(Cascade)`). Added a
  `Member.OwnerRepresentativeDefaultName = "Tôi"` const (OQ5, shared by bootstrap step + backfill).
  Wired `DbSet<Member> Members` + `Member.ConfigureModel(...)` into `AppDbContext.cs`.
- **Step 2 (Migration):** authored `AddMembers` offline via the design-time factory
  (`Migrations/20260714020445_AddMembers.cs` + Designer + snapshot). Reviewed: `members` table,
  utf8mb4/unicode_ci, `IX_members_uuid` unique, `IX_members_user_id`, FK cascade to `users`, bool
  defaults, `updated_at` `current_timestamp(6) ON UPDATE ...`. **Applied to the dev DB** with
  `dotnet ef database update` (applied cleanly).
- **Step 3 (Error codes):** appended the 3xxx block to `Constants/ErrorCodes.cs` —
  `MemberNotFound = 3000`, `OwnerRepresentativeNotDeletable = 3001` (3002 dropped per OQ6); extended
  `ErrorException.GetDefaultHttpStatus` (3000→404, 3001→400).
- **Step 4 (Repository):** `Repositories/MemberRepository.cs` — `IMemberRepository` + sealed impl
  (`[ScopedService]`): `ListByUserAsync` (owner-rep first then name A→Z, OQ8), resource-owned
  `GetByUuidAsync` (incl. deleted), `CreateAsync`, `RenameAsync` (returns the updated `Member?` for
  mapping), idempotent `SoftDeleteAsync`, `HasOwnerRepresentativeAsync`,
  `GetUserUuidsWithoutOwnerRepresentativeAsync`.
- **Step 5 (Service + mapping):** `Services/Api/Members/MembersService.cs` (`IMembersService`:
  list/get/create/rename/delete + backfill `EnsureOwnerRepresentativeForAllAsync`; create never
  owner-rep, trims name; delete blocks owner-rep with 3001; misses → 3000);
  `Services/Api/Members/OwnerRepresentativeBootstrapStep.cs` (the OQ1 seam consumer, `Multiple = true`);
  `Mappings/MemberProfile.cs` (`Member`→`MemberResponse`).
- **Step 6 (DTOs + validators):** `Models/Members/` (`CreateMemberRequest`, `UpdateMemberRequest`,
  `MemberResponse`); `Validators/Members/` (both validators: required + max 100, Vietnamese
  messages, field key camelCase `name`).
- **Step 7 (Controller):** `Controllers/MembersController.cs` — five guarded endpoints under
  `api/v1/members`, Vietnamese Swagger annotations, `AppController` untouched.
- **Step 8 (Bootstrap seam + backfill):** added `Services/Registration/IRegistrationBootstrapStep.cs`;
  `IUserRepository.CreateWithBootstrapAsync` runs steps inside the user-creation transaction after an
  intermediate `SaveChanges` assigns `user.Id` (preserving the in-tx uniqueness re-check +
  duplicate-key race absorption); `CreateAsync` now delegates to it with a no-op bootstrap (signature
  unchanged, so existing tests untouched). `AuthService.RegisterAsync` runs the registered steps via
  the seam. Backfill: `HostedServices/OwnerRepresentativeBackfillHostedService.cs` (idempotent, scoped,
  swallows startup failures), registered in `Program.cs` (`AddHostedService`, service section only —
  pipeline order untouched).
- **Build/test:** `dotnet build` clean (only the pre-existing pinned-AutoMapper NU1903 advisory);
  `dotnet test` **145/145 passing, 0 skipped**. No existing test required changes.
- **Live smoke (port 5199, then cleaned up):** register → new account already had exactly one
  owner-rep member "Tôi" (atomic bootstrap); create (name trimmed) → rename (UTF-8) → get →
  soft-delete → absent from default list but present with `?includeDeleted=true` (isDeleted true);
  owner-rep rename allowed; owner-rep delete → **400 code 3001**; another user's member → **404 code
  3000** on GET and DELETE (never 403); anonymous → 401; empty name → 400 code 1001 field `name`.
  Smoke users deleted by username via MySqlConnector; FK cascade left the members table empty (0 rows).
- **Design choices within the doc's latitude (not deviations):** `RenameAsync` returns `Member?`
  (not `bool`) so the service maps the response without a re-query; `GetByUuidAsync`/`GetAsync` return
  soft-deleted members (doc: "callers decide", and `MemberResponse` exposes `IsDeleted`);
  `SoftDeleteAsync` is idempotent (re-deleting an already-deleted owned member succeeds).

### 2026-07-14 (Step 9 — test suite complete, 202/202 green)

- **Test-engineer: full Members suite written per the Step-9 list; suite grew 145 → 202 tests (57 added), `dotnet test` = Failed 0 / Passed 202 / Skipped 0** (MariaDB + Redis live; every DB-dependent test is `[SkippableFact]` and skips cleanly when the servers are down). Verified deterministic across two consecutive full runs; post-run sweep confirmed **0 leftover test users, 0 members, 0 auth_tokens rows**. No production bugs found; production code untouched.
- **Unit (no I/O):** `MembersValidatorsTests` (8 across 2 classes — required/whitespace-only rejected, exactly-100 passes, >100 → max-length message; Vietnamese texts pinned), `MembersServiceTests` (18, fake `IMemberRepository` + real `MemberProfile` + real validators — create never owner-rep + trims + invalid throws `ValidationException` + unknown-user → 3000; **duplicate names accepted (OQ6)**; get/rename/delete miss → 3000; **owner-rep rename allowed**, rename trims; **owner-rep delete → 3001 without calling SoftDelete**; regular delete soft-deletes; `includeDeleted` passthrough theory; backfill creates "Tôi" per missing user / no-op when none).
- **Integration — real MariaDB (`MemberRepositoryTests`, 16):** create sets uuid/UTC/user_id + active/non-owner-rep flags; unknown user → null; **resource-owned** — another user's member invisible to `GetByUuidAsync`/`ListByUserAsync`/`RenameAsync` (null) and `SoftDeleteAsync` (false, left active); `GetByUuidAsync` returns a soft-deleted owned row (callers decide); default list excludes deleted, `includeDeleted:true` returns them with `isDeleted:true` (history preserved); **OQ8 sort** owner-rep-first-then-A→Z; rename persists; soft-delete sets the flag but keeps the row; soft-delete idempotent; `HasOwnerRepresentativeAsync` true/false; `GetUserUuidsWithoutOwnerRepresentativeAsync` includes lacking users, excludes equipped, and **treats a soft-deleted owner-rep as missing**.
- **Integration — bootstrap/backfill (`MemberBootstrapTests`, 5, app DI):** register creates **exactly one** owner-rep member named "Tôi" (atomic); **`CreateWithBootstrapAsync` with a throwing bootstrap rolls back BOTH the (already-flushed) user and the member** — atomicity proven directly; success path persists user + member atomically; backfill gives a bootstrap-less user exactly one owner-rep and is **idempotent** (second run creates no duplicate); a user that already has one is untouched.
- **Endpoint — real HTTP (`MembersEndpointTests`, 10):** register → `GET /members` has one owner-rep "Tôi"; create appears in the default list; delete → hidden from default list but present with `?includeDeleted=true` (`isDeleted:true`); **owner-rep delete → 400 code 3001** and it remains; rename works incl. the owner-rep; empty name → **400 code 1001 with camelCase `name` field**; duplicate names allowed; **another user's member UUID → 404 code 3000 on GET/PUT/DELETE (never 403)**; anonymous → 401 wrapped.
- **Cleanup strategy (reused from the auth suite):** repository/service writes run `ExecuteTransactionAsync` (own transaction, can't nest in the rollback harness), so Members integration tests reuse `AuthDbTestBase` (own connections) and `AuthApiTestBase` (app DI / real HTTP) with a unique lowercase username prefix per class and dispose-time cleanup — deleting the prefix's users **cascades to their `members` rows via the FK** (and to `auth_tokens`), plus best-effort Redis `auth:token:{hash}` deletion. All classes share `[Collection("AuthIntegration")]` so they serialize with the auth suite — required because the backfill exercises a **global** `EnsureOwnerRepresentativeForAllAsync`; the backfill tests therefore assert per-seeded-user (never on the global return count) to stay robust against any concurrently-visible rows.
- One test-harness bug found and fixed in my own code (not production): `MembersEndpointTests.ListAsync` returned `JsonElement`s backed by a `JsonDocument` disposed on return — fixed with `.Clone()`.

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

- **Code-reviewer verdict: APPROVE, 0 blocking findings.** All 9 OQ decisions and the 3 recorded in-latitude design choices verified against the code; no silent deviations. Adversarial pass confirmed: resource-owned scoping present on every path (list/get/**rename**/**delete** all scoped by `User.Uuid`, miss → 404 code 3000, never 403 — the common unscoped-update/delete hole is absent); owner-rep delete guard un-bypassable (fetches the resource-owned row fresh, raises 3001 before touching the repo); no hard-delete of members anywhere; **bootstrap seam confirmed atomic** (seam runs after `user.Id` is assigned, throw rolls back both rows, `CreateAsync` race-absorption preserved, genuinely extensible for M4); backfill hosted service creates its own DI scope, is idempotent, and swallows startup failure. Migration, conventions, and the 202 tests' assertions all verified genuine.
- **2 nits accepted (not fixed — no risk, no compounding):** (N1) `MemberRepository.HasOwnerRepresentativeAsync` is production-dead — kept as plausible M4/idempotency reuse rather than removed; (N2) name length is validated pre-trim, so a 100-char name with surrounding spaces is rejected rather than trimmed-then-accepted — safe direction (never silently truncates), minor UX edge.
- **2 informational (recorded, not defects):** the "exactly one owner-rep per user" invariant is app-level only (a boot-time backfill racing a simultaneous registration could theoretically double it — already in Future Improvements as a generated-column fix; single-instance boot makes it practically impossible pre-release); and the out-of-scope hard-deleted-user cached-token item (users aren't deletable in production).
- Final state: build clean, **`dotnet test` = 202 passed / 0 failed / 0 skipped**. Milestone 3 complete.

### 2026-07-14 (post-M4 refactor — hosted-service registration)

- `OwnerRepresentativeBackfillHostedService` now inherits from `Microsoft.Extensions.Hosting.BackgroundService`
  (work moved to `ExecuteAsync`) and is registered via DiDecoration's `[BackgroundService]` attribute
  instead of a manual `AddHostedService` call in `Program.cs`. Behavior (idempotent, own-scope,
  log-and-swallow) is unchanged. See `planning/hosted-service-di-registration.md`.

## Final Outcome

Milestone 3 (Members) implemented per the approved plan. Delivered: `members` table + `AddMembers`
migration (applied to dev DB); five guarded `api/v1/members` endpoints (list with `includeDeleted`,
get, create, rename, delete); `MembersService` + `MemberRepository` + two validators + DTOs +
`MemberProfile`; the 3xxx error block (3000/3001 only). The registration-bootstrap seam
(`IRegistrationBootstrapStep` + `IUserRepository.CreateWithBootstrapAsync`) creates the owner-rep
member "Tôi" atomically with the user (closing the M2 deferral) and is the shared extension point M4
reuses for suggested categories. An idempotent startup `IHostedService` backfills owner-rep members
for pre-existing users. Owner-rep is renamable but not deletable (3001); names are free-form
(duplicates allowed); resource-owned scoping returns 3000/404 on every ownership miss. Build clean,
145/145 tests pass, live smoke confirmed all behaviors. No open questions remain; no unrecorded
deviations.

## Future Improvements

- DB-level enforcement of "exactly one owner-rep per user" and (if OQ6 = unique) "unique active name"
  via a MariaDB generated column + unique index (e.g., a nullable `owner_rep_marker` populated only for
  the owner-rep row), removing reliance on app-level idempotency.
- Member merge (combine a duplicate/mistyped member into another, re-pointing historical shares) —
  a natural extension once expenses/shares exist.
- Reactivating a soft-deleted member (undo delete) — symmetric with the tag-reactivation idea in M4.
- Per-member contact info / avatar / color once the Web UI needs richer member cards (deferred; see
  OQ7).
