# Phase 3 — Members

## Objective
Implement member management (`The-ideal.md` §2.3, §4 Members): CRUD with soft delete, the `is_owner` invariant, pagination, and `?include_inactive`. Then revisit `Register` (minor phase 2.A) to seed the owner member.

## Background
Members are owned per user (`user_id`). Exactly one `is_owner = true` member per user represents the account holder and is the default voucher payer. Deletion is soft (`is_active = false`) to preserve historical records.

## Requirements
- List defaults to `is_active = true`; `?include_inactive=true` returns all (for stats/export).
- Cannot hard-delete; cannot delete/deactivate the `is_owner` member.
- All queries scoped by `user_id`; 404-not-403 on miss.

## Dependencies
Phases 1, 2.

---

## Stage 3.1 — Schema & entity
1. Append `members` DDL to `database-migration.sql` (`id`, `uuid`, `user_id` FK, `name`, `is_owner` bool, `is_active` bool default 1, `created_at`, `updated_at`).
2. `Database/Entities/Member.cs` + `Partials/Member.cs` (`IEntity`; note soft-delete uses `is_active`, not `IsDeleted` — apply the appropriate query-filter convention).
3. Register `ConfigureModel`.

**Acceptance:** DDL appended; entity maps; build green.

---

## Stage 3.2 — Repository
1. `Repositories/MemberRepository.cs` (`[ScopedService(typeof(IMemberRepository))]`) extending `BaseRepository`.
2. Decide soft-delete handling: add an `is_active` query filter (or filter explicitly in queries). Document choice.

**Acceptance:** repo returns only active members by default.

---

## Stage 3.3 — Service: read paths
1. `Services/Api/Members/MemberService.cs` (interface + impl).
2. `GetList(authenticatedUser, includeInactive, limit, offset)` → paginated DTO list + total, scoped by user, `ProjectTo<MemberDto>`.

**Acceptance:** pagination + include_inactive behave correctly.

---

## Stage 3.4 — Service: write paths
1. `Create` — insert member (`is_owner = false`) in a transaction.
2. `Update` — load tracked (scoped by user), update `name`; 404 if not owned.
3. `Delete` — soft delete (`is_active = false`); **reject** if `is_owner` → `400`.

**Acceptance:** owner member protected; soft delete verified in DB.

---

## Stage 3.5 — Controller
1. `Controllers/Common/MembersController.cs`: `GET /api/members` (+ query params), `POST`, `PUT /:id`, `DELETE /:id`. Swagger annotations, Vietnamese summaries.
2. `Models/Requests/`: `CreateMemberRequest`, `UpdateMemberRequest`. `Models/Dtos/MemberDto.cs`.

**Acceptance:** all four endpoints work via Swagger, scoped to the caller.

---

## Stage 3.6 — Tests
1. List excludes inactive by default / includes with flag; create; update-not-owned→404; delete-soft; delete-owner→400.

---

## Minor phase 3.A — Apply seeding to Register (executes plan 2.A)
1. Implement Minor phase **2.A**: `AuthService.Register` creates the `is_owner` member inside its transaction.
2. Update `02-auth-users.md` progress log.

---

## Impact Analysis
- **APIs:** `/api/members/*`.
- **Database:** `members`.
- **Services:** `MemberService`, `MemberRepository`; change to `AuthService`.

## Open questions / Assumptions
- Owner member name editable? (Assume yes via `PUT`, but `is_owner`/`is_active` immutable.)
- Reactivation of a soft-deleted member? (Assume out of scope for now.)

## Progress log
- (pending)

## Final outcome
- (to be completed)
