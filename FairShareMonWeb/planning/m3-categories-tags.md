# M3 — Categories & Tags

## Objective

Replace the `/categories` and `/tags` stubs with two real reference-data CRUD surfaces on the locked
foundation + M1 shell + the M2 `Table`/dialog patterns. M3 delivers:

1. **Categories list** (`/categories`) — the caller's categories, **default-first then A→Z** (backend
   order, rendered verbatim), each row showing its **color + icon**, an **"show deleted" toggle**
   (`includeDeleted`), and loading / empty / error states.
2. **Create / edit category** — a modal form (name + **color picker** + **icon picker**), mirroring the
   backend validators (name 1–100; color required `#RRGGBB`; icon optional ≤50).
3. **Set default category** — a per-row action calling `PUT /categories/{uuid}/default`; the default row
   shows a star/marker and cannot be re-set or deleted (R6 exactly-one-default, atomic swap server-side).
4. **Soft-delete category** — a confirm dialog explaining history is preserved; the **default category is
   not deletable** (no delete control + explanation, and a defensive `4002` toast).
5. **Tags list** (`/tags`) — name A→Z, `includeDeleted` toggle, loading/empty/error states.
6. **Create / rename / soft-delete tag** — modal form (name only), with **reactivation-on-name-reuse**
   surfaced (creating a name matching a soft-deleted tag revives it — reflect what the backend returns).

No new dependency, no backend change. Reuses the centralized API client, TanStack Query, RHF + Zod, the
`Table`/`Dialog`/`Badge`/`Button asChild`/`PageHeader`/`Stack`/`LimitNotice` primitives, toasts, i18n
namespaces, and the `navConfig` nav pattern shipped in the foundation/M1/M2. The **only** net-new visual
surface is the **color + icon picker** (+ the default-category marker) — the ui-designer's scope this cycle.

## Background

Grounded in the live SPA code and the locked docs (`feature-roadmap.md` M3; `frontend-foundation.md`;
`m1-app-shell.md`; `m2-members.md`; `CLAUDE.md`) and the feature-complete backend
(`CategoriesController.cs`, `TagsController.cs`, `Models/Categories/*`, `Models/Tags/*`,
`planning/categories-and-tags.md`, `The-ideal.md` §3.3/§3.4/§4.6/§4.7/§4.8).

- **Categories API is stable** (`api/v1/categories`, all guarded, resource-owned):
  - `GET /categories?includeDeleted={bool}` → `ApiResult<CategoryResponse[]>` — default-first, then name
    A→Z; `includeDeleted=true` adds soft-deleted rows.
  - `GET /categories/{uuid}` → `ApiResult<CategoryResponse>` — resource-owned; miss → 404 code `4000`.
  - `POST /categories` (`CreateCategoryRequest { name, color, icon? }`) → `ApiResult<CategoryResponse>` —
    active-name collision → 400 code **`4001`** (`CategoryNameDuplicate`); a name matching a **soft-deleted**
    category **reactivates** it (revives the row, overwrites its color/icon, leaves default flag untouched)
    and returns 200 with that row; invalid → 400 code `1001` (`error.fields.{name,color,icon}`).
  - `PUT /categories/{uuid}` (`UpdateCategoryRequest { name, color, icon? }`) → `ApiResult<CategoryResponse>` —
    edits name/color/icon only (**cannot** change `isDefault`); dup → `4001`; miss → `4000`; invalid → `1001`.
  - `PUT /categories/{uuid}/default` (no body) → `ApiResult` success message — **atomic swap** (clears the
    old default, sets the target) in one transaction; only an active owned category; miss/deleted → `4000`.
  - `DELETE /categories/{uuid}` → `ApiResult` success message — soft-delete; default → 400 code **`4002`**
    (`DefaultCategoryNotDeletable`); miss → `4000`.
  - `CategoryResponse { uuid, name, color, icon?, isDefault, isDeleted, createdAt }`.
- **Tags API is stable** (`api/v1/tags`, guarded, resource-owned):
  - `GET /tags?includeDeleted={bool}` → `ApiResult<TagResponse[]>` — name A→Z.
  - `GET /tags/{uuid}` → `ApiResult<TagResponse>` — miss → 404 code `5000`.
  - `POST /tags` (`CreateTagRequest { name }`) → `ApiResult<TagResponse>` — active dup → 400 code **`5001`**
    (`TagNameDuplicate`); a name matching a **soft-deleted** tag **reactivates** it (revives, keeps uuid +
    history) and returns 200; invalid → `1001` (`error.fields.name`).
  - `PUT /tags/{uuid}` (`UpdateTagRequest { name }`) → `ApiResult<TagResponse>` — rename; dup → `5001`;
    miss → `5000`; invalid → `1001`.
  - `DELETE /tags/{uuid}` → `ApiResult` success message — soft-delete; miss → `5000`.
  - `TagResponse { uuid, name, isDeleted, createdAt }` (name-only — no color/icon/default).
- **Backend seed set (registration bootstrap; `planning/categories-and-tags.md` OQ1b) — key evidence for
  the icon approach:** every new account starts with five categories whose `icon` values are **emoji
  glyphs stored directly** and `color` values are hex: Ăn uống 🍜 `#F97316` **(default)**; Đi lại 🚗
  `#3B82F6`; Khách sạn 🏨 `#8B5CF6`; Mua sắm 🛍️ `#EC4899`; Khác ⋯ `#6B7280`. So `icon` is a **free string
  (≤50), not a server enumerated catalog**, and the canonical rendering is the emoji itself. The picker
  must produce values that render alongside these seeds (→ OQ1).
- **Backend rules the UI must honor** (`The-ideal.md` §3.3/§3.4/§4.6/§4.7/§4.8): R1 resource-owned 404
  (never leak existence); **R6 exactly one default category** — never deletable, set-default swaps
  atomically server-side; unique **active** name per ledger (accent/case-insensitive, `utf8mb4_unicode_ci`)
  → distinct `4001`/`5001` codes (**not** `1001` field errors); reactivation on deleted-name reuse (both
  categories and tags); R7/R8 soft-delete keeps history + deleted rows unselectable for new data. **No Free
  tier limit** on categories/tags (no `13xxx` this cycle).
- **Foundation seams to reuse** (verified in code):
  - Centralized client `api.get/post/put/delete` (`src/lib/api/client.ts`) — unwraps `ApiResult<T>`,
    throws `ApiError { code, message, fields, httpStatus }`, owns `401 → refresh`, injects headers.
  - `classifyError` / `resolveErrorMessage` / `applyFieldErrors` (`src/lib/api/http-error-handling.ts`);
    `ErrorCodes` (`src/lib/api/errors.ts`) — **already carries `CategoryNotFound 4000`, `TagNotFound 5000`;
    `NOT_FOUND_CODES` already includes both. It does NOT yet carry `4001`, `4002`, `5001`** (see Step 2).
  - TanStack Query (`queryClient` skips 4xx retries); the `membersKeys` factory + invalidate-root pattern
    from `hooks/useMembers.ts`; the shared modal-form error pattern from `MemberFormDialog.tsx`
    (`applyFieldErrors` → `setError`, `resolveErrorMessage` fallback, `useToast().push`).
  - Design system (`@/components/ui`): `Table`/`TableHead`/`TableBody`/`TableRow` (`deleted` →
    `data-deleted` muted)/`TableHeaderCell`/`TableCell` (`actions`)/`TableEmpty`, `Badge`, `Button`
    (+ `asChild`), `Dialog`/`DialogContent`/`DialogFooter`/`DialogClose`, `Form`/`FieldStack`/`FormError`,
    `TextField`, `PageHeader`/`Stack`, `Skeleton`/`EmptyState`/`ErrorState`, `cx`. **A color picker + icon
    picker do NOT exist yet.**
  - `navConfig` already registers `/categories` + `/tags`; `router.tsx` routes both to `StubPage` — M3
    swaps the two elements.
  - i18n: per-feature namespace convention (`members` precedent); `useT()`; `formatDate` for `createdAt`.

## Requirements

- **R1 (categories list)** — `/categories` shows the caller's categories in backend order (default-first,
  then A→Z), rendered verbatim (no client re-sort), each row showing name + color swatch + icon glyph. A
  show-deleted toggle re-queries with `includeDeleted=true`; default active-only.
- **R2 (category CRUD)** — create (name + color + icon), edit (name/color/icon), set-default, soft-delete —
  each with a success toast and correct cache invalidation.
- **R3 (default invariant, R6)** — the default row shows a clear **default marker** (star/badge), has **no
  set-default action** and **no delete control**, and carries a short explanation that the default cannot
  be deleted. Set-default on a non-default active row calls `PUT /{uuid}/default`; a defensive `4002` on
  delete surfaces as a toast.
- **R4 (unique active name → field error)** — a `4001` on create/edit maps to the **name** field (reads as
  a field-level "name already exists" error), not a form-level or toast error; likewise `5001` for tags.
- **R5 (reactivation)** — the create form surfaces (static helper text) that reusing a soft-deleted name
  revives that row; a successful create/reactivate refetches so the revived (now-active) row appears. The
  UI does not fight the backend response (which returns the revived row at 200).
- **R6 (tags list + CRUD)** — `/tags` shows tags name A→Z with a show-deleted toggle; create/rename/
  soft-delete via dialogs; name-only form.
- **R7 (soft-delete keeps history)** — deleted categories/tags appear only under the toggle, visually
  distinguished (`data-deleted` muted row + "Đã xóa" badge), and are **read-only** (no edit/delete/set-
  default actions — no reactivate-via-UI button exists; reactivation happens implicitly via create).
- **R8 (validation mirrors backend)** — name required (trim) 1–100; color required + `^#[0-9A-Fa-f]{6}$`;
  icon optional ≤50. Server `1001` `error.fields.*` maps onto the matching fields; `4001`/`5001` map onto
  name; unknown-field errors surface form-level.
- **R9 (states)** — loading (skeleton rows), error (`ErrorState` + retry), empty (`EmptyState`) via
  existing primitives. (The category active list is never empty — the seeded default always exists — so
  its `EmptyState` is defensive; the tag list can genuinely be empty.)
- **R10 (i18n)** — all copy through i18n (new `categories` + `tags` namespaces, vi-VN default + en-US
  parity); fixed domain terms (danh mục / category, nhãn / tag). No hardcoded strings.
- **R11 (a11y)** — real `<table>` with `scope` headers + accessible name; row actions labeled with the
  category/tag name; color conveyed with an accompanying icon/text (never color alone — the icon glyph +
  name carry meaning); the toggle is a labeled control; the color/icon pickers are keyboard-operable,
  labeled radio-group-style controls with visible selection; dialogs inherit Radix focus-trap/Escape.
- **R12 (design system)** — build the **`ColorPicker`** and **`IconPicker`** primitives (ui-designer) and a
  small **category color+icon marker** display used in the list and reusable by M4/M6.

## Open Questions

> Each carries a one-line trade-off; the **Recommended** option is the one I would genuinely ship. The
> orchestrator auto-accepts the recommended option. **None are CRITICAL** — each has a safe, reversible,
> backend-aligned default; none is security/privacy-sensitive or irreversible. OQ1 (icon approach) is the
> highest-impact call and is called out, but it has a safe default that matches the backend seed data, so
> it is not flagged needs-user.
>
> **All OQs Resolved 2026-07-17 (option a):** OQ1a (curated-emoji icon, stored verbatim), OQ2a
> (swatch + custom-hex color), OQ3a (transparent reactivation + static form hint), OQ4a (two sibling
> feature folders), OQ5a (no detail routes). Accepted by the orchestrator and implemented.

### OQ1 — Icon picker approach (backend stores a free string; seeds are emoji)

The backend `icon` is a **free string ≤50, not a server enum**, and the five seeded categories store
**emoji glyphs directly** (🍜 🚗 🏨 🛍️ ⋯). Whatever the picker produces must render next to those seeds.

- **(a) Recommended — a curated emoji palette; store the chosen emoji glyph directly.** The picker shows a
  fixed grid of ~24–30 expense-relevant emoji (a superset that includes the 5 seed emoji); the selected
  value is the emoji string itself; icon stays optional (a "no icon" choice → neutral fallback marker =
  the color swatch, or the name's first letter). This is the only approach that renders the seeds
  correctly with **zero mapping layer**, needs no icon-font dependency, and matches `The-ideal.md`'s
  "màu/icon phục vụ biểu đồ" intent. Trade-off: the palette is a fixed curated set (users can't pick an
  arbitrary emoji) — acceptable and easily extended; the exact glyph list is a ui-designer detail.
- (b) Free-text input accepting any emoji/short string (≤50). Maximum flexibility, still renders seeds.
  Trade-off: no guided UX, users can paste unrenderable/garbage strings, inconsistent visual language.
- (c) A curated **icon-key set mapped to an SVG/icon-font library** (e.g. lucide) via a client key→glyph
  map. Crisp, themeable icons. Trade-off: **breaks the seeds** — the seeded `🍜`/`🚗`/… are not keys in
  any map, so seeded categories would render as broken/placeholder icons; also adds an icon dependency and
  a mapping table the backend never anticipated. Rejected — fights the backend contract.

### OQ2 — Color picker approach (`color` is required hex `#RRGGBB`)

- **(a) Recommended — a curated swatch palette + an optional custom hex input.** A radio-group of ~10–12
  swatches (the 5 seed colors + a spread from the design system's reserved `--fs-viz-*` categorical
  palette) for one-click choice, plus a native `<input type="color">` (and/or a text field) for a custom
  hex. All paths yield a validated `#RRGGBB`. Best UX + consistent chart-friendly defaults + full freedom.
  Trade-off: slightly more picker surface for the ui-designer to build.
- (b) Native `<input type="color">` only. Minimal to build, always valid `#rrggbb`. Trade-off: OS-native
  chrome (inconsistent cross-platform look), no curated chart-safe defaults, weaker a11y labeling.
- (c) Curated swatch palette only (no custom). Simplest consistent look. Trade-off: users can't match a
  brand/preferred color; a fixed palette may feel limiting for many categories.

### OQ3 — Reactivation-on-name-reuse: transparent vs announced

Creating a category/tag whose name matches a **soft-deleted** one reactivates it (200 with the revived
row). The POST response is indistinguishable from a fresh create (no "wasReactivated" flag).

- **(a) Recommended — transparent + a static form hint.** On success show the generic "added" toast and
  invalidate; the revived row simply appears in the active list. The create form carries a small static
  helper line ("Nếu trùng tên một mục đã xóa, mục đó sẽ được khôi phục") so the behavior is surfaced
  proactively without unreliable detection. Trade-off: no bespoke "reactivated" toast — but the behavior
  is explained up front and the outcome (row appears) is correct.
- (b) Detect reactivation client-side (compare the returned `uuid` against a cached deleted-rows list) and
  show a distinct "khôi phục" toast. Nicer messaging. Trade-off: only reliable when the include-deleted
  list is already cached; adds fragile cache-diffing for a cosmetic toast difference. Deferred to Future
  Improvements.

### OQ4 — Feature folder organization: two folders vs one shared area

- **(a) Recommended — two sibling feature folders, `src/features/categories/` and `src/features/tags/`,
  each mirroring `src/features/members/` exactly** (`api/`, `hooks/`, `schemas.ts`, `pages/`,
  `components/`). Matches the shipped per-feature convention and the two already-stubbed routes; keeps each
  surface independently testable. Trade-off: a little structural duplication between two near-identical CRUD
  trees (mitigated by both reusing the same shared primitives + patterns).
- (b) One shared `src/features/reference-data/` folder hosting both. Less duplication. Trade-off: diverges
  from the one-feature-per-folder precedent and couples two independently-routed surfaces.

### OQ5 — Detail routes (`/categories/:uuid`, `/tags/:uuid`)?

- **(a) Recommended — no detail routes; single list page + modal dialogs each** (mirrors M2 OQ2a). The
  list returns every field (`CategoryResponse`/`TagResponse`); there is no sub-data to justify a detail
  page. `GET /{uuid}` is reserved in the api module but not consumed. Trade-off: the resource-owned-404
  route pattern still isn't exercised until M4/M5 (documented).
- (b) Add detail routes now (consume `GET /{uuid}`, ownership 404 → `NotFound`). Trade-off: a detail page
  with nothing the list lacks — premature.

## Assumptions

- **Routes already exist** — `/categories` + `/tags` are registered in `router.tsx` (currently `StubPage`)
  and in `navConfig`. M3 swaps the two route elements; nav entries unchanged.
- **The category active list is never empty** — the seeded default category always exists (backend
  registration bootstrap), so the default (`includeDeleted=false`) category list always has ≥1 row.
  `EmptyState` is defensive there. The **tag** list can genuinely be empty (no tags seeded).
- **Backend order is authoritative** — categories render default-first then A→Z; tags A→Z; deleted rows
  interleave under the toggle. The UI distinguishes deleted rows by styling, never by re-sorting.
- **`4001`/`5001` are top-level codes, not `1001` field errors** — the client catches them explicitly and
  maps them onto the name field for a field-level reading (verified against the controllers/service plan).
- **Color/icon on reactivation** — the backend overwrites the revived category's color/icon with the new
  request values (`categories-and-tags.md` OQ5a) and leaves its default flag untouched; the client just
  renders whatever the refetched list returns. Nothing special client-side.
- **No Free tier limit** on categories/tags (`categories-and-tags.md` Assumptions) — no `13xxx` handling
  this cycle; the create dialogs need no `LimitNotice`.
- **No new dependency, no backend change, no new endpoint.** `4001`/`4002`/`5001` are added to the
  `ErrorCodes` TS **mirror** only (they already exist on the backend).
- **Tier is irrelevant** to these surfaces (no gating); the session `user` is not read here.

## Implementation Plan

> Paths under `FairShareMonWeb/`. Concrete files assume the recommended OQ options (OQ1a · OQ2a · OQ3a ·
> OQ4a · OQ5a). All copy through i18n (vi-VN default).

### Step 1 — `ColorPicker` + `IconPicker` design-system primitives (R12, OQ1a/OQ2a) — ui-designer + implementer

1. New `src/components/ui/ColorPicker/ColorPicker.tsx` (+ `ColorPicker.module.css`):
   - A controlled form control: `value: string` (`#RRGGBB`), `onChange(value)`, `label`, `id`, `error?`,
     `required?`. Renders a **radio-group** of curated swatches (5 seed colors + a `--fs-viz-*` spread) with
     a visible selected ring, plus a custom-hex affordance (`<input type="color">` and/or a hex `TextField`).
   - Tokens only; keyboard-operable (arrow keys within the group, Enter/Space to select); each swatch has an
     accessible name (the hex or a color label); selection conveyed by ring + `aria-checked`, not color alone.
2. New `src/components/ui/IconPicker/IconPicker.tsx` (+ `IconPicker.module.css`):
   - Controlled: `value: string | null` (emoji glyph or empty), `onChange`, `label`, `id`. A radio-group
     grid of curated emoji (superset including the 5 seed glyphs) + a "no icon" option. `CURATED_ICONS`
     exported as a const array (ui-designer curates the final list). Keyboard-operable; each option labeled.
3. New `src/components/ui/CategoryMarker/CategoryMarker.tsx` (+ css) — a small presentational chip: color
   swatch/dot + optional icon glyph, `size` prop; used in the list Name cell and reusable by M4 (expense
   category display) and M6 (chart legends). Falls back to the color dot when icon is absent.
4. Export all three from `src/components/ui/index.ts` (+ prop types); add demos to `src/styles/StyleGuide.tsx`.

### Step 2 — `ErrorCodes` mirror additions (implementer)

`src/lib/api/errors.ts` — append to `ErrorCodes` (never renumber): `CategoryNameDuplicate: 4001`,
`DefaultCategoryNotDeletable: 4002`, `TagNameDuplicate: 5001`. No change to `NOT_FOUND_CODES` (4000/5000
already present) or `FREE_LIMIT_CODES`. These are branched on in the form/delete flows (Step 6/7).

### Step 3 — Categories: types + API module + hooks + schema

1. New `src/features/categories/api/types.ts`:
   ```ts
   export interface CategoryResponse {
     uuid: string; name: string; color: string; icon?: string | null;
     isDefault: boolean; isDeleted: boolean; createdAt: string; // ISO-8601, offset-aware
   }
   export interface CreateCategoryRequest { name: string; color: string; icon?: string | null }
   export interface UpdateCategoryRequest { name: string; color: string; icon?: string | null }
   ```
2. New `src/features/categories/api/categoriesApi.ts` (mirrors `membersApi.ts`):
   ```ts
   export const categoriesApi = {
     list: (includeDeleted: boolean) =>
       api.get<CategoryResponse[]>("/v1/categories", { query: { includeDeleted } }),
     get: (uuid: string) => api.get<CategoryResponse>(`/v1/categories/${uuid}`), // reserved (OQ5)
     create: (body: CreateCategoryRequest) => api.post<CategoryResponse>("/v1/categories", body),
     update: (uuid: string, body: UpdateCategoryRequest) =>
       api.put<CategoryResponse>(`/v1/categories/${uuid}`, body),
     setDefault: (uuid: string) => api.put<MessageResponse>(`/v1/categories/${uuid}/default`),
     remove: (uuid: string) => api.delete<MessageResponse>(`/v1/categories/${uuid}`),
   };
   ```
   (Confirm `api.put` accepts a bodyless call for `setDefault`; if it requires a body arg, pass `undefined`/
   `{}` — a one-liner, not a design choice.)
3. New `src/features/categories/hooks/useCategories.ts`:
   - `categoriesKeys = { all: ["categories"] as const, list: (includeDeleted) => ["categories","list",includeDeleted] as const }`.
   - `useCategoriesQuery(includeDeleted)`; `useCreateCategory`, `useUpdateCategory`, `useSetDefaultCategory`,
     `useDeleteCategory` — each `useMutation` whose `onSuccess` invalidates `categoriesKeys.all` (covers both
     toggle states + the default swap). Side-effects (toast/close) stay in the calling component.
4. New `src/features/categories/schemas.ts` — mirrors `CreateCategoryRequestValidator`/`UpdateCategoryRequestValidator`:
   ```ts
   export const CATEGORY_NAME_MAX = 100; export const CATEGORY_ICON_MAX = 50;
   export const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;
   export const categoryFormSchema = (t: AppTFunction) => z.object({
     name: z.string().trim().min(1, t("validation:category.nameRequired")).max(CATEGORY_NAME_MAX, t("validation:category.nameTooLong")),
     color: z.string().regex(HEX_COLOR, t("validation:category.colorInvalid")),
     icon: z.string().trim().max(CATEGORY_ICON_MAX, t("validation:category.iconTooLong")).optional().or(z.literal("")),
   });
   export type CategoryFormValues = z.infer<ReturnType<typeof categoryFormSchema>>;
   ```
   (Empty icon → send `null`/omit; a default color is seeded into `defaultValues` so `color` is never empty
   on create — e.g. the first curated swatch.)

### Step 4 — Tags: types + API module + hooks + schema

1. New `src/features/tags/api/types.ts`: `TagResponse { uuid, name, isDeleted, createdAt }`,
   `CreateTagRequest { name }`, `UpdateTagRequest { name }`.
2. New `src/features/tags/api/tagsApi.ts`: `list(includeDeleted)`, `get(uuid)` (reserved), `create(body)`,
   `rename(uuid, body)` (`PUT /v1/tags/{uuid}`), `remove(uuid)`.
3. New `src/features/tags/hooks/useTags.ts`: `tagsKeys` factory, `useTagsQuery(includeDeleted)`,
   `useCreateTag`, `useRenameTag`, `useDeleteTag` (each invalidates `tagsKeys.all`).
4. New `src/features/tags/schemas.ts`: `tagFormSchema(t)` → `{ name: trim 1–100 }` mirroring
   `CreateTagRequestValidator` (messages `validation:tag.nameRequired` / `validation:tag.nameTooLong`).

### Step 5 — Categories feature UI

1. New `src/features/categories/pages/CategoriesPage.tsx` (+ `.module.css`, reuse the M2 toolbar css shape):
   - `PageHeader` (`categories:title` / `categories:subtitle`) + an "Add category" primary `Button`
     opening the create dialog.
   - Show-deleted toggle (labeled control, `categories:showDeleted`) → `useCategoriesQuery(includeDeleted)`.
   - States: pending → `Table` + `Skeleton` rows; error → `ErrorState` (`resolveErrorMessage`) + retry
     (`refetch`); success → `CategoriesTable`; defensive empty → `TableEmpty`/`EmptyState`.
2. New `src/features/categories/components/CategoriesTable.tsx` (+ css):
   - `Table` columns: **Category** (`CategoryMarker` swatch/icon + name), **Status** (badges), **Actions**.
     Rows in backend order (default-first then A→Z), verbatim.
   - **Default row:** a default `Badge`/star marker (`categories:badge.default`); **no** set-default action,
     **no** delete control; an inline `categories:default.notDeletable` explanation; **Edit** action present.
   - **Deleted rows** (toggle only): `<TableRow deleted>` muted + `data-deleted` + `Badge`
     `categories:badge.deleted`; **no** actions (read-only).
   - **Normal rows:** **Edit**, **Set default**, **Delete** actions, each `aria-label`ed with the category
     name (`categories:actions.editNamed` / `setDefaultNamed` / `deleteNamed`).
3. New `src/features/categories/components/CategoryFormDialog.tsx` — shared create/edit modal:
   - Props `mode: "create" | "edit"`, `category?`, `open`, `onOpenChange`. RHF + `zodResolver(categoryFormSchema(t))`;
     edit pre-fills name/color/icon; create seeds a default color + no icon. Fields: `TextField` (name),
     `ColorPicker` (color), `IconPicker` (icon), a static `categories:form.reactivateHint` (OQ3a).
   - Submit → `useCreateCategory`/`useUpdateCategory` `mutateAsync`; success → toast
     (`categories:toast.created`/`updated`) + close. On error, branch on `code`:
     - **`4001`** (`CategoryNameDuplicate`) → `setError("name", { message: error.message })` (field-level).
     - **`1001`** → `applyFieldErrors(error, ["name","color","icon"], setError)`; leftover → `FormError`.
     - **`4000`** (edit, stale) → toast + close.
     - else → `FormError` = `resolveErrorMessage(error, t)`.
4. New `src/features/categories/components/DeleteCategoryDialog.tsx` — confirm soft-delete:
   - Title `categories:delete.title` (named); body `categories:delete.body` (history preserved). Confirm
     `Button variant="danger"`. Confirm → `useDeleteCategory().mutateAsync(uuid)`; success → toast
     `categories:toast.deleted` + close; **defensive `4002`** (default-not-deletable, should be unreachable
     since the default row has no delete control) → toast `error.message`; `4000` → toast + close.
5. Set-default: triggered from the row action (no dialog needed — it is a safe, reversible swap) →
   `useSetDefaultCategory().mutateAsync(uuid)`; success → toast `categories:toast.defaultSet`; `4000` (stale)
   → toast + refetch. (An optional lightweight confirm is a ui-designer call; default = no confirm.)

### Step 6 — Tags feature UI

1. New `src/features/tags/pages/TagsPage.tsx` (+ css) — mirrors CategoriesPage: `PageHeader` (`tags:*`),
   "Add tag" button, show-deleted toggle, loading/error/empty states (tags **can** be genuinely empty →
   `EmptyState` with an "add tag" affordance).
2. New `src/features/tags/components/TagsTable.tsx` — `Table` columns **Tag** (name), **Status**,
   **Actions**. Normal rows: **Rename** + **Delete** (named `aria-label`s). Deleted rows: muted, read-only,
   "Đã xóa" badge.
3. New `src/features/tags/components/TagFormDialog.tsx` — shared create/rename modal (one `TextField` name +
   the static `tags:form.reactivateHint`). Errors: **`5001`** → `setError("name")`; `1001` →
   `applyFieldErrors(["name"])`; `5000` (rename stale) → toast + close; else `FormError`.
4. New `src/features/tags/components/DeleteTagDialog.tsx` — confirm soft-delete (history-preserved copy);
   `5000` → toast + close.

### Step 7 — Route wiring

`src/routes/router.tsx` — replace the two `StubPage` entries: `{ path: "categories", element: <CategoriesPage /> }`
and `{ path: "tags", element: <TagsPage /> }` (import both). Keep the `StubPage` import (still used by
expenses/events/stats/wallet). `navConfig` unchanged.

### Step 8 — i18n (R10)

1. New `src/i18n/locales/{vi-VN,en-US}/categories.json` + `tags.json`; register both in `src/i18n/index.ts`
   (`resources` + `NAMESPACES`) and add `"categories"`, `"tags"` to `APP_NAMESPACES` in `src/i18n/useT.ts`.
2. **categories keys:** `title`, `subtitle`, `add`, `showDeleted`, `table.{caption,category,status,actions}`,
   `badge.{default,deleted}`, `default.notDeletable`, `actions.{edit,setDefault,delete,editNamed,setDefaultNamed,deleteNamed}`,
   `form.{createTitle,editTitle,nameLabel,namePlaceholder,colorLabel,iconLabel,iconNone,reactivateHint,submitCreate,submitEdit,cancel}`,
   `delete.{title,body,confirmButton,cancel}`, `toast.{created,updated,deleted,defaultSet}`,
   `empty.{title,body}`, `error.{title,retry}`.
3. **tags keys:** `title`, `subtitle`, `add`, `showDeleted`, `table.{caption,tag,status,actions}`,
   `badge.deleted`, `actions.{rename,delete,renameNamed,deleteNamed}`,
   `form.{createTitle,renameTitle,nameLabel,namePlaceholder,reactivateHint,submitCreate,submitRename,cancel}`,
   `delete.{title,body,confirmButton,cancel}`, `toast.{created,renamed,deleted}`, `empty.{title,body}`,
   `error.{title,retry}`.
4. **validation additions** (`{vi-VN,en-US}/validation.json`): `category.{nameRequired,nameTooLong,colorInvalid,iconTooLong}`,
   `tag.{nameRequired,nameTooLong}` — mirroring the backend validator messages.

### API endpoints consumed this cycle (verb + path + DTO)

| Screen / hook             | Verb + Path                              | Request → Response (`data`)                | Notable codes                                    |
| ------------------------- | ---------------------------------------- | ------------------------------------------ | ------------------------------------------------ |
| `useCategoriesQuery`      | `GET /v1/categories?includeDeleted=`     | — → `CategoryResponse[]`                    | (reads only)                                      |
| `useCreateCategory`       | `POST /v1/categories`                    | `{ name, color, icon? }` → `CategoryResponse` | `4001` → name field; `1001` → fields; may reactivate |
| `useUpdateCategory`       | `PUT /v1/categories/{uuid}`              | `{ name, color, icon? }` → `CategoryResponse` | `4001` → name; `1001` → fields; `4000` stale      |
| `useSetDefaultCategory`   | `PUT /v1/categories/{uuid}/default`      | — → success message                        | `4000` stale → toast+refetch                      |
| `useDeleteCategory`       | `DELETE /v1/categories/{uuid}`          | — → success message                        | `4002` default (defensive); `4000` stale          |
| (reserved, OQ5)           | `GET /v1/categories/{uuid}`             | — → `CategoryResponse`                      | `4000` → `NotFound` (only if a detail route)      |
| `useTagsQuery`            | `GET /v1/tags?includeDeleted=`          | — → `TagResponse[]`                         | (reads only)                                      |
| `useCreateTag`            | `POST /v1/tags`                          | `{ name }` → `TagResponse`                  | `5001` → name field; `1001` → field; may reactivate |
| `useRenameTag`            | `PUT /v1/tags/{uuid}`                    | `{ name }` → `TagResponse`                  | `5001` → name; `1001` → field; `5000` stale       |
| `useDeleteTag`            | `DELETE /v1/tags/{uuid}`                | — → success message                        | `5000` stale                                      |

Envelope handling: all through the centralized client; `data` unwrapped on success; failures throw
`ApiError` — components branch on the numeric `code`, render `error.message`, map `error.fields` to fields.

### Loading / empty / error states

- **Loading** — query pending: `Table` + `Skeleton` rows (mirror `MembersPage` `LoadingRows`).
- **Empty** — categories: defensive only (seeded default always present); tags: genuine empty →
  `EmptyState` with an "Add tag" affordance.
- **Error** — list failure: `ErrorState` (`resolveErrorMessage`) + retry (`refetch`). Mutation failures:
  inline `FormError` in the dialog (name-dup / validation) or a toast (set-default / delete / stale).

### Form validation rules (mirror backend validators)

- **Category name** — required (non-empty after trim), max 100. **Color** — required, `^#[0-9A-Fa-f]{6}$`.
  **Icon** — optional, max 50 (empty → omitted/null). Server `1001` maps onto `name`/`color`/`icon`; `4001`
  maps onto `name`.
- **Tag name** — required (trim) 1–100; `1001` → `name`; `5001` → `name`.

### Accessibility requirements

- `Table` renders a real `<table>` with an accessible name (`caption`) and `<th scope>` headers.
- Row actions are named `Button`s (`aria-label` includes the category/tag name), not icon-only.
- **Color is never the sole signal** — the `CategoryMarker` pairs the swatch with the icon glyph, and the
  name carries meaning; the default row is marked with a text/star badge, not color.
- `ColorPicker`/`IconPicker` are labeled radio-group controls: keyboard-navigable, `aria-checked`/selected
  ring on the choice, each option with an accessible name (hex/color label; emoji has a name or is marked
  `aria-hidden` with a labeled wrapper).
- The show-deleted control is a labeled checkbox; deleted status conveyed by badge text + muted styling.
- Dialogs inherit Radix focus-trap / Escape / focus-restore; `DialogContent` `title` set (named for delete).
- `<html lang>` synced by `LocaleProvider`.

### Tests the web-test-engineer should write (Vitest + RTL + MSW, deterministic — pinned TZ + locale)

**Categories — `categoriesApi`/hooks (`useCategories.test.tsx`):**
- `list(true/false)` sends the right `includeDeleted`; create/update/setDefault/delete hit the right verb +
  path + body; each mutation's `onSuccess` invalidates `["categories"]` (a second GET fires).

**Categories — page (`categoriesPage.test.tsx`):**
- List renders a row per category in **default-first then A→Z** order (verbatim); the default row shows the
  **default badge**, an **Edit** action, **no set-default**, **no delete**, and the not-deletable note.
- Normal row shows Edit + Set default + Delete. Each row shows its color swatch + icon glyph (assert the
  `CategoryMarker` / icon text is present).
- Loading → skeleton rows; list error → `ErrorState` + working retry; (defensive) empty → `EmptyState`.
- Show-deleted toggle: off hides deleted; on reveals a muted `data-deleted` row + "Đã xóa" badge with **no
  actions**; toggling off hides again.
- **Create**: valid submit (name + picked color + picked icon) adds the row + toast + closes + list
  refreshes; empty name blocked client-side (no request); invalid/empty color blocked; `1001` maps onto the
  matching field; **`4001` maps onto the name field** ("name already exists") with the form kept mounted.
- **Reactivation**: creating a name that the MSW store holds as soft-deleted returns 200 and the revived
  row appears active after refetch (assert same uuid re-activated, no duplicate); the static reactivate hint
  is present in the form.
- **Edit**: dialog pre-fills name/color/icon; submit updates + toast; `4001` → name field; `4000` (stale) →
  toast + close.
- **Set default**: clicking Set default on a non-default row calls `PUT /{uuid}/default`, toasts, and the
  default marker moves to that row (old default loses it) after refetch.
- **Delete**: default row has no delete control; a normal-row delete confirms (history-preserved copy) →
  soft-deletes + toast + row leaves the default list (+ reappears under the toggle); a **defensive `4002`**
  (forced) toasts the server message.

**Tags — `tagsApi`/hooks + page (`useTags.test.tsx`, `tagsPage.test.tsx`):**
- List renders name A→Z; loading/error/empty (genuine empty → `EmptyState`); show-deleted round-trip.
- Create valid → adds + toast + closes; empty name blocked; `1001` → name; **`5001` → name field**;
  **reactivation** of a soft-deleted tag name → 200, revived row appears (same uuid), no duplicate.
- Rename pre-fills + updates; `5001` → name; `5000` (stale) → toast + close.
- Delete confirm (history-preserved) → soft-deletes + row leaves default list + reappears under toggle.

**Design-system pickers (`components/ui/*.test.tsx`):**
- `ColorPicker` — renders swatches as a labeled radio group, keyboard-selectable, emits a `#RRGGBB` on
  choice, custom-hex path yields a valid value, invalid custom hex surfaces the field error.
- `IconPicker` — renders the curated emoji grid + a "no icon" option, keyboard-selectable, emits the emoji
  glyph (or null); selection has an accessible name.
- `CategoryMarker` — renders the color swatch + icon; falls back to the swatch/first-letter when icon absent.

**i18n parity:** `categories` + `tags` + new `validation:{category,tag}.*` keys resolve in vi-VN and en-US;
toggling locale switches copy (mirror the M2 en-US parity tests).

All deterministic: pinned `Asia/Ho_Chi_Minh` TZ + pinned locale; MSW at the client boundary; per-test unique
MSW usernames for store isolation; the singleton `queryClient` so invalidation refetches.

### Step 9 — Verification

`pnpm lint`, `tsc -b`, `pnpm build`, `pnpm test`; then exercise both surfaces against the live backend:
categories list starts with the 5 seeded categories (default = "Ăn uống", emoji + colors render), create/
edit with the pickers, set-default swaps the marker, delete a non-default (moves under the toggle), confirm
the default has no delete control, trigger `4001` (duplicate active name), and reactivate a soft-deleted name;
tags create/rename/delete + reactivation + `5001`.

## Impact Analysis

- **APIs / Database / Services:** none — M3 consumes the existing, stable `api/v1/categories` +
  `api/v1/tags` endpoints. No backend change, no new error code (`4001`/`4002`/`5001` are only mirrored
  client-side).
- **Frontend — new:** `src/features/categories/{pages/CategoriesPage,components/{CategoriesTable,
  CategoryFormDialog,DeleteCategoryDialog},api/{categoriesApi,types},hooks/useCategories,schemas}`;
  `src/features/tags/{pages/TagsPage,components/{TagsTable,TagFormDialog,DeleteTagDialog},api/{tagsApi,
  types},hooks/useTags,schemas}`; `src/components/ui/{ColorPicker,IconPicker,CategoryMarker}/*`;
  `src/i18n/locales/{vi-VN,en-US}/{categories,tags}.json`.
- **Frontend — edited:** `src/routes/router.tsx` (`/categories`, `/tags` → real pages);
  `src/components/ui/index.ts` (export the 3 primitives); `src/i18n/index.ts` + `useT.ts` (register 2
  namespaces); `src/i18n/locales/{vi-VN,en-US}/validation.json` (category/tag keys);
  `src/lib/api/errors.ts` (add 4001/4002/5001); `src/styles/StyleGuide.tsx` (picker demos).
- **Design system:** the **`ColorPicker`** + **`IconPicker`** (+ `CategoryMarker`) are materially new
  visual surfaces — **a ui-designer pass is warranted** (Step 1). All list/table/dialog/badge surfaces
  reuse existing M2 primitives.
- **Documentation:** this doc; roadmap Progress Log entry when M3 closes.
- **Downstream:** M4 (expense create/edit needs a category select defaulting to the default category, and a
  tag multi-select; both reuse `CategoryMarker` + these types/hooks and the R8 "deleted unselectable, shown
  in history" treatment); M6 (charts reuse category color + `CategoryMarker` legends).

## Decision Log

### Decision

M3 ships Categories and Tags as two sibling `/categories` + `/tags` list pages (real `Table` + modal
dialogs) with an include-deleted toggle each. Categories add a **color + icon picker**, a **set-default**
row action with the exactly-one-default marker + not-deletable default guard, and `4001` name-duplicate
mapped to the name field. Tags are name-only with reactivation-on-name-reuse surfaced. New design-system
primitives: `ColorPicker`, `IconPicker`, `CategoryMarker`. Built on the locked foundation + M1/M2; no new
dependency, no backend change; `4001`/`4002`/`5001` added to the client `ErrorCodes` mirror only.

### Reason

Categories and tags are reference-data referenced by expenses (category select + tag set + expense-list
filters), so they must exist before M4. The backend contract is stable and resource-owned; every rule
(R1/R6/R7/R8, name-duplicate, reactivation) maps onto existing client seams. The backend seeds emoji glyphs
directly, so a curated **emoji** picker (not an icon-font key map) is the only approach that renders the
seeds with no mapping layer — hence OQ1a. Categories and tags share the M2 CRUD/list/dialog pattern, so the
work is small per surface; the picker is the sole net-new design surface.

### Alternatives Considered

- Icon-font key set (OQ1c) — rejected; breaks the emoji-glyph seeds and adds an unneeded dependency.
- Native-only color input (OQ2b) / swatch-only (OQ2c) — rejected in favor of swatches + custom hex.
- Announced/detected reactivation (OQ3b) — deferred; fragile cache-diffing for a cosmetic toast.
- One shared reference-data folder (OQ4b) — rejected; diverges from one-feature-per-folder + two routes.
- Detail routes (OQ5b) — rejected; no sub-data to justify a detail page.

## Progress Log

### 2026-07-17

- Feature-planner: required reading completed — `feature-roadmap.md` (M3 locked scope + rules),
  `frontend-foundation.md`, `m1-app-shell.md`, `m2-members.md` (the reference CRUD template), `CLAUDE.md`;
  the backend contract `CategoriesController.cs` + `TagsController.cs`, `Models/Categories/*` +
  `Models/Tags/*`, and `planning/categories-and-tags.md` (4000/4001/4002/5000/5001, default invariant +
  atomic swap, unique active name accent/case-insensitive, reactivation on deleted-name reuse for BOTH
  categories and tags, the emoji-glyph seed set, no Free limit). Read the live SPA: `router.tsx`,
  `components/ui/index.ts`, `errors.ts`, `http-error-handling.ts`, the full `src/features/members/**`
  (api/hooks/schemas/pages/components/tests), `i18n/index.ts`, `i18n/locales/vi-VN/{members,validation}.json`.
- Drafted this plan: two list pages + `Table`/dialogs, the color/icon pickers + `CategoryMarker`, set-
  default with the one-default marker + not-deletable guard, `4001`/`5001` → name-field mapping, `4002`
  defensive toast, reactivation surfaced via a static form hint, query hooks + invalidation, Zod mirroring
  the backend validators, i18n (2 new namespaces + validation keys), a11y, and the test matrix.
- **5 Open Questions raised** (icon approach; color approach; reactivation UX; folder org; detail routes),
  each with a recommended, backend-aligned, reversible option. **None CRITICAL** — OQ1 (icon) is the
  highest-impact but has a safe default that matches the backend's emoji-glyph seeds. Awaiting the
  checkpoint (orchestrator auto-accepts recommendations).

- **ui-designer (Step 1 — design primitives, OQ1a/OQ2a accepted):** added the three net-new
  design-system primitives, tokens-only + theme-aware (light+dark) + WCAG-AA + Vietnamese-tolerant, no new
  dependency; everything else reuses M2 primitives.
  - **New files:** `src/components/ui/ColorPicker/{ColorPicker.tsx,ColorPicker.module.css}`,
    `src/components/ui/IconPicker/{IconPicker.tsx,IconPicker.module.css}`,
    `src/components/ui/CategoryMarker/{CategoryMarker.tsx,CategoryMarker.module.css}`.
  - **Edited:** `src/components/ui/index.ts` (barrel exports the 3 primitives + `CURATED_COLORS` /
    `CURATED_ICONS` consts + prop types); `src/styles/StyleGuide.tsx` +
    `src/styles/StyleGuide.module.css` (a live M3 section: both pickers wired to state, a marker preview,
    and the categories-list treatment with the default star + "Mặc định" badge, set-default affordance,
    and a soft-deleted read-only row).
  - **`IconPicker`** — curated 30-emoji radiogroup (superset incl. all 5 seed glyphs 🍜🚗🏨🛍️⋯) + a
    "no icon" option; `value: string | null` = the emoji glyph verbatim (or null); roving-tabindex arrow
    keys; each option accessible-named.
  - **`ColorPicker`** — 12-swatch radiogroup (5 seed hexes + a chart-friendly spread, literal hex
    constants so the stored value renders identically on charts + both themes) + a custom-hex path (native
    `<input type=color>` + a validated hex text field); `value: string` / `onChange(#RRGGBB)`; selection
    shown by a shape halo + corner check on a surface disc (contrast-safe over any color), never color
    alone.
  - **`CategoryMarker`** — color tile (`color-mix` tint + solid ring) + emoji glyph (falls back to a solid
    color dot when icon absent) + optional name; `isDefault` adds a star pip; used by the list Name cell,
    reusable by M4/M6.
  - **Verified:** `tsc -b` clean, `pnpm lint` exit 0 (only the pre-existing fast-refresh HMR warnings that
    the repo already carries where a module exports a const beside a component), `pnpm build` succeeds.

- **web-implementer (Steps 2–8 — feature build, OQ1a–OQ5a):** built both surfaces on the M2 CRUD
  template, reusing the ui-designer's `ColorPicker` / `IconPicker` / `CategoryMarker` (no restyle) and the
  existing `Table`/`Dialog`/`Badge`/`Button`/`PageHeader`/`Stack`/feedback primitives. No new dependency.
  - **`ErrorCodes` mirror (Step 2):** appended `CategoryNameDuplicate 4001`, `DefaultCategoryNotDeletable
    4002`, `TagNameDuplicate 5001` to `src/lib/api/errors.ts` (backend already returns them). `4001`/`5001`
    map onto the name field; `1001` → `applyFieldErrors`; `4000`/`5000` stale → toast; `4002` defensive
    toast.
  - **Categories feature (`src/features/categories/`):** `api/{types,categoriesApi}.ts`,
    `hooks/useCategories.ts` (keys + list/create/update/setDefault/delete, all invalidate `["categories"]`),
    `schemas.ts` (Zod: name 1–100, color `#RRGGBB`, icon optional ≤50, default color seeded),
    `pages/CategoriesPage.tsx`, `components/{CategoriesTable,CategoryFormDialog,DeleteCategoryDialog}`. List
    renders backend order verbatim with `CategoryMarker`; default row shows the star pip + "Mặc định"
    `Badge` and has Edit only (no set-default, no delete) + not-deletable note; set-default is a ghost
    Button with an outline star doing the atomic swap directly (no confirm); deleted rows are muted +
    read-only; show-deleted toggle; loading skeletons / `ErrorState` + retry / defensive `EmptyState`.
  - **Tags feature (`src/features/tags/`):** `api/{types,tagsApi}.ts`, `hooks/useTags.ts` (invalidate
    `["tags"]`), `schemas.ts` (name 1–100), `pages/TagsPage.tsx`,
    `components/{TagsTable,TagFormDialog,DeleteTagDialog}`. Name-only create/rename/soft-delete; show-deleted
    toggle; genuine `EmptyState`; the create/rename form carries the static reactivate hint (OQ3a) and the
    success path shows the generic "added"/"renamed" toast (no reactivation detection).
  - **Routing (Step 7):** `src/routes/router.tsx` swaps the two `StubPage` entries for `<CategoriesPage />`
    and `<TagsPage />` (`StubPage` import kept for expenses/events/stats/wallet); `navConfig` unchanged.
  - **i18n (Step 8):** new `categories` + `tags` namespaces (vi-VN + en-US), registered in `i18n/index.ts`
    + `useT.ts`; added `validation:{category.*,tag.*}` keys in both locales.
  - **MSW:** added categories + tags stores/handlers to `src/test/msw/handlers.ts` mirroring the live
    backend (seeded 5 categories default-first, a soft-deleted row, reactivation on name reuse, 4001/4002 /
    5001, atomic default swap) for dev mocks + the web-test-engineer's specs. No product test suite written.
  - **Verified:** `pnpm exec tsc -b` clean; `pnpm lint` exit 0 (only the pre-existing fast-refresh HMR
    warnings — LocaleProvider/ToastHost/ThemeProvider/ColorPicker/IconPicker); `pnpm build` succeeds (372
    modules). **Live API contract exercised end-to-end against the running backend on :5200** (fresh
    account): categories list default-first→A→Z with emoji + hex verbatim; create stores the emoji glyph
    (☕) verbatim; duplicate active name → `4001`; invalid color → `1001` `fields.color`; set-default atomic
    swap; delete default → `4002`; soft-delete + `includeDeleted`; reactivation of a soft-deleted name
    revives the **same uuid** with overwritten color/icon and `isDeleted=false`. Tags: empty list, create,
    `5001` duplicate, delete + reactivation (same uuid). **Not exercised live:** the rendered React UI in a
    browser — no browser driver (chromium-cli/Playwright) is available in this Windows agent environment;
    coverage rests on the successful production build (all new modules transformed), the verified API
    contract, and the MSW handlers for the RTL suite.

### 2026-07-17 (test — web-test-engineer: M3 suite added, all green)

- **web-test-engineer** wrote the M3 Vitest + RTL + MSW suite on the locked harness
  (`src/test/setup.ts` pinned `Asia/Ho_Chi_Minh` TZ, `renderWithProviders`, the singleton `queryClient`,
  and the implementer's categories/tags MSW handlers — seeded default-first, a soft-deleted row,
  reactivation on name reuse, `4001`/`4002`/`5001`, atomic default swap). Mocked at the MSW boundary;
  exercised the real hooks/components. Followed the M2 members suite as the template. Per-test unique MSW
  usernames for store isolation; vi-VN default with en-US parity where the plan calls for it.
- **New test files (8, +97 tests):**
  - `src/features/categories/schemas.test.ts` (13) — Zod: name 1–100 (trim), color `#RRGGBB` valid/invalid
    set, `HEX_COLOR` regex, icon optional ≤50.
  - `src/features/tags/schemas.test.ts` (6) — Zod: tag name 1–100 (trim).
  - `src/features/categories/useCategories.test.tsx` (7) — `categoriesKeys` scoping;
    `useCategoriesQuery(true/false)` param wiring; create/update/setDefault/delete hit the right verb +
    path + body and each `onSuccess` invalidates `["categories"]` (a second GET fires).
  - `src/features/tags/useTags.test.tsx` (6) — same coverage for tags (`["tags"]` invalidation).
  - `src/features/categories/categoriesPage.test.tsx` (24) — default-first→A→Z verbatim + a non-alphabetical
    server order rendered without client re-sort; per-row `CategoryMarker` glyph; default badge + Edit but
    NO set-default/NO delete + not-deletable note; set-default atomic swap (exactly one default after);
    set-default `4000` toast; loading skeletons / error+retry / defensive empty; show-deleted round-trip
    (muted `data-deleted` read-only row + "Đã xóa"); create (picked color+icon, toast, close, refetch),
    static reactivate hint, empty-name client block (no request), `4001`→name field, `1001`→color field,
    reactivation of a soft-deleted name (revived active row, no duplicate); edit prefill (name+color+icon
    checked radios) + update, `4001`→name, `4000` stale toast+close; delete confirm history-preserved copy
    + soft-delete moves row under the toggle, defensive `4002` toast; en-US chrome + validation parity.
  - `src/features/tags/tagsPage.test.tsx` (23) — A→Z list; hidden-deleted default; table a11y name+headers;
    loading/error+retry/genuine empty (with add affordance); show-deleted round-trip; rename+delete controls;
    create+toast+close, reactivate hint, empty-name block, `5001`→name, `1001`→name, reactivation revives
    (no duplicate); rename prefill+update, `5001`→name, `5000` stale toast+close; delete confirm + moves
    under toggle; en-US chrome + validation parity.
  - `src/components/ui/colorPicker.test.tsx` (7) — swatches as a labeled radiogroup with hex accessible
    names; aria-checked selection; click + arrow-key emit an uppercased `#RRGGBB`; custom-hex valid emits,
    invalid surfaces the field error and does not emit; `error` prop renders an alert.
  - `src/components/ui/iconPicker.test.tsx` (5) — curated emoji grid + "no icon" option; click emits the
    glyph verbatim / null; arrow-key navigation; aria-checked selection; each option accessible-named.
  - `src/components/ui/categoryMarker.test.tsx` (4) — glyph+name; color-dot fallback when icon absent;
    icon-only `role="img"` accessible name; default appends the default label to the accessible name.
- **Extras beyond the plan's checklist:** the verbatim-order test using a deliberately non-alphabetical
  server payload (proves no client re-sort, R1); explicit verb+path+body assertions on every mutation hook;
  the set-default `4000` stale toast; the exactly-one-default assertion after the swap.
- **Results:** `pnpm test` run twice — **280 passed / 280** (29 files), deterministic across both runs
  (baseline before M3 specs: 183). `pnpm lint` exit 0 (only the pre-existing fast-refresh HMR warnings on
  LocaleProvider/ToastHost/ThemeProvider/ColorPicker/IconPicker). `pnpm exec tsc -b` clean. No product bugs
  surfaced; no product code changed. No coverage gaps against the M3 test checklist.

## Final Outcome

**Complete.** M3 shipped two sibling reference-data surfaces mirroring M2. **Categories** (`src/features/categories/`): list default-first→A→Z with `CategoryMarker` (color swatch + emoji glyph verbatim), create/edit with the color + icon pickers, atomic set-default swap + "Mặc định" badge, default not-deletable (no control + explanation + defensive `4002`), soft-delete + deleted toggle. **Tags** (`src/features/tags/`): name-only CRUD + soft-delete + transparent reactivation-on-name-reuse with a form hint. Design-system additions: `ColorPicker`, `IconPicker` (curated emoji, stored verbatim to match backend seeds), `CategoryMarker`. Error mapping: `4001`/`5001`→name field, `4002`→toast, `4000`/`5000`→toast, `1001`→field errors. Consumes the categories/tags controllers incl. `PUT /{uuid}/default`. No new deps, no backend change. The implementer verified the live API contract end-to-end on :5200 (emoji verbatim, atomic swap, same-uuid reactivation, `4001/4002/5001`). Tests +97 (suite 183→280); code review **APPROVE, 0 blocking**. All 5 OQs shipped at option (a).

## Future Improvements

- **Detected/announced reactivation** (OQ3b) — a distinct "khôi phục" toast once the include-deleted list
  is reliably cached, or a backend `wasReactivated` flag on the create response.
- **Delete dialogs close on any error** (M3 review nit, also present in M2's DeleteMemberDialog): `onOpenChange(false)` in `finally` closes the confirm dialog even on a transient network failure. Consider closing only on success + terminal codes (`4000`/`4002`/`5000`), leaving network/transient errors open for in-place retry. Apply consistently across all delete dialogs.
- **Emoji SR labels** (M3 review nit): `IconPicker` glyph options rely on the emoji's own Unicode name; a per-option localized label map would read better (esp. the `⋯` "Khác" seed). Cosmetic.
- **Explicit restore/undelete** action on deleted category/tag rows (symmetric with the implicit
  reactivation) — needs a backend endpoint; would make deleted rows actionable.
- **Reorder / custom sort** of categories (drag to reorder) if users want a non-alphabetical selection
  order — needs a backend order field.
- **Optimistic updates** for set-default and create/edit once the write patterns settle (deferred).
- **Shared reference-data list abstraction** — if M4/M5 add more reference-data CRUD surfaces, extract the
  list+toggle+dialog scaffolding shared by members/categories/tags.
- **Extended icon set / custom emoji** (OQ1b path) if the curated palette proves too limiting.
- E2E (Playwright) coverage of the categories/tags CRUD loops once a browser driver is available.
