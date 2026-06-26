# Phase 4 — Categories & Tags

## Objective
Implement categorization (`The-ideal.md` §2.7–2.8, §4): per-user `categories` (with one `is_default`) and free-form `tags`, both soft-deletable. Then revisit `Register` (minor phase 2.B) to seed default categories. (`voucher_tags` join table is created in phase 5 with vouchers.)

## Background
Every voucher requires exactly one category; if the client omits it, the user's `is_default` category is used. Tags are optional, many-to-many, deduped per user by name.

## Requirements
- `categories`: `UNIQUE (user_id, name)` among active rows; exactly one `is_default` per user; default cannot be deleted.
- `tags`: `UNIQUE (user_id, name)`; soft delete cascades the removal of `voucher_tags` links (handled in phase 5).
- Setting a new default clears the old one atomically.
- Soft delete only; queries scoped by user; 404-not-403.

## Dependencies
Phases 1, 2.

---

## Minor phase 4.0 — none (greenfield phase)

## Stage 4.1 — Schema & entities
1. Append DDL: `categories` (`id`, `uuid`, `user_id`, `name`, `color`, `icon`, `is_default` bool, `is_active` bool, timestamps, unique index on `(user_id, name)`); `tags` (`id`, `uuid`, `user_id`, `name`, `is_active`, timestamps, unique `(user_id, name)`).
2. `Category` + `Tag` entities (`Entities/` + `Partials/`), register `ConfigureModel`.

**Acceptance:** DDL appended; entities map; build green.

---

## Stage 4.2 — Repositories
1. `CategoryRepository`, `TagRepository` (`[ScopedService(...)]`), active-only by default.

---

## Stage 4.3 — Category service & default invariant
1. `Services/Api/Categories/CategoryService.cs` (interface + impl): `GetList(includeInactive, paging)`, `Create`, `Update(name/color/icon)`, `SetDefault(id)`, `Delete`.
2. `SetDefault` — in one transaction: clear current `is_default`, set the target; both scoped by user.
3. `Delete` — reject if `is_default` → `400`; else soft delete.
4. Create — enforce unique name (active); first-ever category may auto-become default.

**Acceptance:** exactly one default at all times; default protected from delete.

---

## Stage 4.4 — Tag service
1. `Services/Api/Tags/TagService.cs`: `GetList(paging)`, `Create` (normalize/trim, dedupe by `(user_id, name)`), `Update` (rename), `Delete` (soft).

**Acceptance:** duplicate tag name → returns existing or `409` (decide); rename respects uniqueness.

---

## Stage 4.5 — Controllers
1. `CategoriesController`: `GET`, `POST`, `PUT /:id`, `POST /:id/default`, `DELETE /:id`.
2. `TagsController`: `GET`, `POST`, `PUT /:id`, `DELETE /:id`.
3. Requests/DTOs: `CreateCategoryRequest`, `UpdateCategoryRequest`, `CategoryDto`, `CreateTagRequest`, `UpdateTagRequest`, `TagDto`.

**Acceptance:** endpoints work via Swagger, scoped to caller.

---

## Stage 4.6 — Tests
1. Unique-name; set-default-clears-old; delete-default→400; tag dedupe; pagination.

---

## Minor phase 4.A — Apply seeding to Register (executes plan 2.B)
1. Implement Minor phase **2.B**: `AuthService.Register` seeds default categories (one `is_default = true`) inside its transaction.
2. Update `02-auth-users.md` progress log.

---

## Impact Analysis
- **APIs:** `/api/categories/*`, `/api/tags/*`.
- **Database:** `categories`, `tags`.
- **Services:** `CategoryService`, `TagService`, repos; change to `AuthService`.

## Open questions / Assumptions
- Default category seed list (assume Ăn uống / Đi lại / Khách sạn / Mua sắm / Khác; default = Khác).
- Create-duplicate-tag behavior: return existing vs 409 (assume **return existing** for idempotent UX — confirm).

## Progress log
- (pending)

## Final outcome
- (to be completed)
