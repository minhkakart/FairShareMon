# M2 — Members

## Objective

Replace the `/members` stub with the real Members feature — the first CRUD list surface in
FairShareMonWeb — on the locked foundation and the M1 shell. Concretely M2 delivers:

1. **Members list** — the caller's members, owner-representative first then A→Z (backend order,
   rendered verbatim), with an **"show deleted" toggle** (`includeDeleted`) that reveals soft-deleted
   members for stats/export context. Loading / empty / error states.
2. **Add member** — create a member via a modal form (name only).
3. **Rename member** — edit any owned member's display name, **including the owner-representative**.
4. **Soft-delete member** — with a confirmation dialog that explains history is preserved; the
   **owner-representative is non-deletable** (no delete control, with an explanation).
5. **Free member-limit affordance** — a `13000` on create surfaces a friendly `LimitNotice`
   (informational only — Premium is a manual grant; there is no self-serve upgrade).

It also builds the design-system **`Table`** primitive (the first list surface; M3/M4/M5 all need it)
and folds in the small **`LinkButton` / `Button asChild`** fix flagged in the M1 review.

No new dependency, no backend change. Reuses the centralized API client, TanStack Query, RHF + Zod,
the design-system primitives, i18n, and the `navConfig` nav-registration pattern already shipped.

## Background

Grounded in the live SPA code and the locked docs (`feature-roadmap.md` M2; `frontend-foundation.md`;
`m1-app-shell.md`; `CLAUDE.md`) and the feature-complete backend
(`MembersController.cs`, `Models/Members/*`, `planning/members.md`, `The-ideal.md` §2/§3.2/§4).

- **The backend Members API is stable** (`api/v1/members`, all guarded, resource-owned):
  - `GET /members?includeDeleted={bool}` → `ApiResult<MemberResponse[]>` — owner-rep first, then name
    A→Z; `includeDeleted=true` adds soft-deleted rows (the same sort, interleaved).
  - `GET /members/{uuid}` → `ApiResult<MemberResponse>` — resource-owned; miss → 404 code `3000`.
  - `POST /members` (`CreateMemberRequest { name }`) → `ApiResult<MemberResponse>` — never owner-rep;
    Free limit → 400 code `13000`; invalid → 400 code `1001` (`error.fields.name`).
  - `PUT /members/{uuid}` (`UpdateMemberRequest { name }`) → `ApiResult<MemberResponse>` — rename;
    owner-rep rename **allowed**; miss → `3000`; invalid → `1001`.
  - `DELETE /members/{uuid}` → `ApiResult` success message — soft-delete; owner-rep → 400 code `3001`
    (`OwnerRepresentativeNotDeletable`); miss → `3000`.
  - `MemberResponse { uuid, name, isOwnerRepresentative, isDeleted, createdAt }`.
- **Backend rules the UI must honor** (`The-ideal.md` §3.2, §4): R1 resource-owned 404 (never leak
  existence); R7 soft-delete keeps history (deleted members stay visible under the toggle, hidden from
  the default list); owner-rep is renamable but **never deletable**; R9 Free member-limit blocks only
  **create** (`13000`) — existing data is never touched. Names are **free-form** (duplicates allowed —
  no uniqueness). Name length is **1–100 chars**. There is **no reactivate/undelete endpoint** and
  **no member-count/limit endpoint** (the Free limit number is deferred to backend planning §5 and is
  not exposed to the client).
- **Foundation seams to reuse** (verified in code):
  - Centralized client `api.get/post/put/delete` (`src/lib/api/client.ts`) — unwraps `ApiResult<T>`,
    throws `ApiError { code, message, fields, httpStatus }`, owns `401 → refresh`, injects
    `Authorization` / `X-Time-Zone` / `Accept-Language`.
  - `classifyError` / `resolveErrorMessage` / `applyFieldErrors` (`src/lib/api/http-error-handling.ts`)
    and `ErrorCodes` (`src/lib/api/errors.ts`, already carries `MemberNotFound 3000`,
    `MemberLimitReached 13000`; `NOT_FOUND_CODES` already includes `3000`, `FREE_LIMIT_CODES` includes
    `13000`). **No new error code needed** — but `3001 OwnerRepresentativeNotDeletable` is **not** in
    the mirror yet (see Step 2).
  - TanStack Query (`queryClient` retries no 4xx); mutation + toast + query-key pattern from
    `useAuth.ts` / `useCurrentUserQuery.ts` (`currentUserQueryKey = ["auth","me"]`).
  - RHF + Zod + `zodResolver`, form pattern from `ChangePasswordPage.tsx` (`applyFieldErrors` →
    `setError`, `resolveErrorMessage` fallback, `useToast().push`).
  - Design system (`@/components/ui`): `Button`, `TextField`, `Form/FieldStack/FormError/FormActions`,
    `Card/CardHeader/CardBody`, `Badge`, `Dialog/DialogContent/DialogFooter`, `Toast` (via
    `useToast`), `LimitNotice`/`UpgradePrompt`, `PageHeader`/`Stack`/`DescriptionList`, `Skeleton`,
    `EmptyState`, `ErrorState`, `TierBadge`, `cx`. **The `Table` primitive is spec-only — not built.**
  - `navConfig.tsx` already registers `{ to: "/members", labelKey: "nav.members" }`; `router.tsx`
    already routes `/members` → `<StubPage titleKey="common:nav.members" />` — M2 swaps the element.
  - i18n: per-feature namespace convention (`auth`, `settings` precedent); `useT()`, typed via
    `i18next.d.ts` off the vi-VN catalog; `formatDate` (`src/i18n/format.ts`) renders `createdAt`.

## Requirements

- **R1** — `/members` shows the caller's members in backend order (owner-rep first, then A→Z),
  rendered verbatim (no client re-sort). A **"show deleted" toggle** re-queries with
  `includeDeleted=true`; default is active-only.
- **R2** — Add member (name), rename member (name), soft-delete member — each with success feedback
  (toast) and correct cache invalidation so the list reflects the change.
- **R3** — The owner-representative row shows **no delete control** and carries a short explanation
  that it cannot be removed; it **is** renamable like any other member.
- **R4** — Deleted members appear only under the toggle, visually distinguished (`(đã xóa)` badge,
  muted row), and are **read-only** (no rename/delete actions — no reactivate endpoint exists; OQ4).
- **R5** — `13000` on create → a friendly `LimitNotice` (informational; no self-serve upgrade action),
  rendering the backend's localized `error.message`; the create form itself is not destroyed.
- **R6** — Validation mirrors the backend: name required (non-empty after trim), max 100 chars; server
  `1001` `error.fields.name` maps onto the name field; unknown-field errors surface form-level.
- **R7** — Ownership/`3000` on rename/delete (member removed elsewhere) is handled gracefully
  (toast + refetch), never a crash. R1 ownership-404 route handling applies only if a detail route is
  added (OQ2); for M2's list-only surface `3000` is a stale-list case, not the `NotFound` page.
- **R8** — Loading (skeleton rows), error (ErrorState + retry), and empty states via existing
  primitives. (Note: the active list is never truly empty — the owner-rep always exists — so
  `EmptyState` is a defensive fallback; see Assumptions.)
- **R9** — All copy through i18n (new `members` namespace, vi-VN default + en-US parity); fixed domain
  terms (thành viên / member). No hardcoded strings.
- **R10** — Accessibility: a real `<table>` with `scope="col"` headers and an accessible name;
  row actions labeled with the member's name; the toggle is a labeled control; dialogs inherit Radix
  focus-trap/Escape; status conveyed by text+badge, not color alone.
- **R11** — Build the design-system `Table` primitive and the `LinkButton`/`Button asChild` fix
  (design-system work; see Impact + the ui-designer pass).

## Open Questions

> Each carries a one-line trade-off; the **Recommended** option is the one I would genuinely ship.
> The orchestrator auto-accepts the recommended option. None are CRITICAL — each has a safe,
> reversible default and none is security/privacy-sensitive.

> **Resolved 2026-07-17 — all five accepted at the recommended option (a):** OQ1a (build the real
> `Table` primitive), OQ2a (single `/members` list page + dialogs, no detail route), OQ3a
> (reactive-only limit — no invented cap), OQ4a (deleted rows read-only), OQ5a (modal forms).

### OQ1 — Build the real `Table` primitive now, or a card/list for Members?

- **(a) Recommended — build the design-system `Table` primitive now** (`src/components/ui/Table/*`,
  ui-designer) and render Members with it. Members is the first list; M3 (categories/tags), M4
  (expense list + shares table), M5 (events + balance table) all need a table. Building it once here
  avoids three parallel ad-hoc lists. Trade-off: a bit more design/impl in M2 than a bespoke list.
- (b) A bespoke card/list for Members now; extract a `Table` later. Ships M2 faster. Trade-off:
  near-certain rework in M3/M4/M5 and an inconsistent list surface in the meantime.
- (c) A responsive hybrid (table on wide, stacked cards on narrow) as the primitive. Best mobile UX.
  Trade-off: more design surface than M2 needs; can be layered onto (a) later without a rewrite.

### OQ2 — Members surface: single list page + dialogs, or add a `/members/:uuid` detail route?

- **(a) Recommended — a single `/members` list page; create/rename/delete via modal dialogs; do
  not consume `GET /members/{uuid}` in M2.** The list already returns every field
  (`MemberResponse`); a member has no sub-data to justify a detail page. Keeps M2 tight. Trade-off:
  the resource-owned-404 (`3000`) route pattern isn't exercised until a feature that needs a detail
  route (M4/M5) — documented, not lost.
- (b) Add a `/members/:uuid` detail route (consumes `GET /members/{uuid}`; ownership 404 →
  `NotFound`). Exercises the 404 route pattern now. Trade-off: a detail page with nothing the list
  lacks — premature for members.

### OQ3 — Proactively reflect the Free member limit — but the limit number is not exposed

The roadmap asks to "proactively reflect the Free limit where sensible." **No endpoint exposes the
Free member cap N** (it is deferred to backend planning §5; `MemberResponse`/list carry no limit
metadata; there is no tier-limits endpoint). Options:

- **(a) Recommended — reactive-only: no proactive numeric limit.** Show the `13000` `LimitNotice`
  when create is rejected (rendering the backend's localized message, which states the limit), and —
  for Free users — optionally show a neutral count of active members (no denominator). Never invent
  N. Trade-off: no "3 of 5 used" progress affordance until the backend exposes the cap.
- (b) Hardcode N in the client (mirror whatever the backend currently enforces). A proactive
  "X of N" meter. Trade-off: **assumes a business number the spec explicitly leaves open** — drifts
  silently if the backend changes it; violates "never assume." Rejected.
- (c) Request a backend `GET /members/limits` (or a `tier.limits` field on `/auth/me`). Enables a
  real proactive meter across M2/M4/M5/M7. Trade-off: a backend change — out of scope for M2; record
  as a Future Improvement / cross-milestone backend ask.

### OQ4 — Deleted members: read-only, or still renamable?

- **(a) Recommended — deleted rows are read-only** (no rename/delete actions; shown for history/
  export context only). The backend technically allows renaming a deleted member, but with no
  reactivate/undelete endpoint a deleted member is a historical artifact; editing it is confusing.
  Trade-off: to "fix" a deleted member's name you'd need a restore flow that doesn't exist yet.
- (b) Allow rename on deleted rows too (backend permits it). Trade-off: implies an edit capability on
  a row the user can't otherwise act on, with no restore — inconsistent UX.

### OQ5 — Create/rename form surface: modal dialog or inline?

- **(a) Recommended — a modal `Dialog` (Radix, already used by the shell mobile menu).** Keeps the
  list the primary surface, gives create + rename one shared form component, focus-trapped and
  accessible. Matches the pattern M3/M4 will reuse. Trade-off: a click to open the form.
- (b) Inline row-edit for rename + an inline "add" row. Snappier for power users. Trade-off: more
  bespoke list state and a second form pattern; harder to keep accessible; diverges from M3/M4.

## Assumptions

- **Route already exists** — `/members` is registered in `router.tsx` (currently `StubPage`) and in
  `navConfig.tsx`. M2 only swaps the route element to `<MembersPage />`; the nav entry is unchanged.
- **The active list is never empty** — every ledger always has the owner-rep member (backend
  invariant), so the default (`includeDeleted=false`) list always has ≥1 row. `EmptyState` is wired
  defensively (e.g. a degraded/unexpected empty response) but is not the normal path.
- **Backend order is authoritative** — the client renders the list as returned (owner-rep first, then
  A→Z). Deleted rows interleave alphabetically under the toggle; the UI distinguishes them by styling,
  not by re-sorting (grouping deleted at the bottom would be a client re-sort — deferred; a light
  design choice for the ui-designer if desired, recorded under Future Improvements).
- **Name validation mirrors the backend** with one benign edge: Zod trims then checks 1–100, so a
  100-char name with surrounding spaces passes the client but the backend (which validates pre-trim,
  per its review nit N2) may reject with `1001` — the server stays authoritative and the field error
  surfaces cleanly. Acceptable.
- **Tier is read from the session `user`** (`useCurrentUser().tier`, normalized case-insensitively as
  in M1) — used only to decide whether to show the Free active-member count / phrase the limit copy;
  the actual gate is the backend `13000`, never a client tier check.
- **No new dependency, no new backend endpoint, no new error code.** `3001` is added to the
  `ErrorCodes` TS mirror only (it already exists on the backend).

## Implementation Plan

> Paths under `FairShareMonWeb/`. Concrete files assume the recommended OQ options
> (OQ1a · OQ2a · OQ3a · OQ4a · OQ5a). All copy through i18n (vi-VN default).

### Step 1 — `Table` design-system primitive (R11, OQ1a) — ui-designer + implementer

1. New `src/components/ui/Table/Table.tsx` (+ `Table.module.css`) — a themed, accessible table:
   - Composition: `Table`, `TableHead`, `TableBody`, `TableRow`, `TableHeaderCell` (`<th scope>`),
     `TableCell`. `Table` takes an accessible-name prop (`caption`/`aria-label`) and an optional
     `dense` size. Row supports a `muted`/`data-deleted` state for soft-deleted styling.
   - Tokens only (`--fs-color-*`, `--fs-space-*`); zebra/hover per the design system; long-Vietnamese
     tolerant (wrapping cells). Mobile behavior per OQ1 (a: table; optional (c) responsive hybrid).
2. Export from `src/components/ui/index.ts` (`Table`, `TableHead`, `TableBody`, `TableRow`,
   `TableHeaderCell`, `TableCell` + prop types). Add to the living `StyleGuide.tsx`.

### Step 2 — `LinkButton` / `Button asChild` fix (R11) — ui-designer + implementer

1. Resolve the M1 review nit (link-styled-as-button currently nests `<Link><Button>` → invalid nested
   interactives). Recommended: add a Radix `Slot`-based `asChild` to `Button`
   (`@radix-ui/react-slot`) **or** a thin `LinkButton` primitive that renders a single `<a>` with
   button styling. Export from the barrel.
2. Convert existing call sites (`SecurityCard.tsx` `<Link><Button>`; any others) and use it for the
   Members "Add member" affordance if it is ever a link. Note: `@radix-ui/react-slot` ships with Radix
   already present — confirm it is available; if it requires a separate install, that is a one-line
   Open Question, not a silent add.
3. Add `src/lib/api/errors.ts`: append `OwnerRepresentativeNotDeletable: 3001` to `ErrorCodes` (it is
   defensive-only — the owner-rep delete control is hidden — but the mirror should be complete for the
   toast fallback). No change to `FREE_LIMIT_CODES` / `NOT_FOUND_CODES`.

### Step 3 — Member types + API module

1. New `src/features/members/api/types.ts`:
   ```ts
   export interface MemberResponse {
     uuid: string;
     name: string;
     isOwnerRepresentative: boolean;
     isDeleted: boolean;
     createdAt: string; // ISO-8601, offset-aware
   }
   export interface CreateMemberRequest { name: string }
   export interface UpdateMemberRequest { name: string }
   ```
   (Feature-local, per the feature-first convention; auth types under `lib/api/types` were a
   foundation exception.)
2. New `src/features/members/api/membersApi.ts` (mirrors `authApi.ts`):
   ```ts
   export const membersApi = {
     list: (includeDeleted: boolean) =>
       api.get<MemberResponse[]>("/v1/members", { query: { includeDeleted } }),
     get: (uuid: string) => api.get<MemberResponse>(`/v1/members/${uuid}`), // reserved (OQ2)
     create: (body: CreateMemberRequest) => api.post<MemberResponse>("/v1/members", body),
     rename: (uuid: string, body: UpdateMemberRequest) =>
       api.put<MemberResponse>(`/v1/members/${uuid}`, body),
     remove: (uuid: string) => api.delete<MessageResponse>(`/v1/members/${uuid}`),
   };
   ```

### Step 4 — Query hooks (TanStack Query) with invalidation

1. New `src/features/members/hooks/useMembers.ts`:
   - Key factory: `export const membersKeys = { all: ["members"] as const, list: (includeDeleted:
     boolean) => ["members", "list", includeDeleted] as const };`
   - `useMembersQuery(includeDeleted: boolean)` → `useQuery({ queryKey: membersKeys.list(...),
     queryFn: () => membersApi.list(includeDeleted) })`. (`queryClient` already skips 4xx retries.)
   - `useCreateMember()`, `useRenameMember()`, `useDeleteMember()` — `useMutation` wrappers; each
     `onSuccess` invalidates `membersKeys.all` (covers both toggle states). Keep session/toast/
     navigation side-effects in the calling component (the `useAuth.ts` convention). No optimistic
     updates in M2 (deferred; Future Improvements).

### Step 5 — Zod schema (mirrors the backend validator)

1. New `src/features/members/schemas.ts` (pattern from `auth/schemas.ts`, takes `t`):
   ```ts
   export const memberFormSchema = (t: AppTFunction) =>
     z.object({
       name: z.string().trim()
         .min(1, t("validation:member.nameRequired"))
         .max(100, t("validation:member.nameTooLong")),
     });
   export type MemberFormValues = z.infer<ReturnType<typeof memberFormSchema>>;
   ```

### Step 6 — Members feature UI

1. New `src/features/members/pages/MembersPage.tsx` — the routed page:
   - `PageHeader` title `members:title`, description `members:subtitle`, and an **"Add member"**
     action (`Button variant="primary"`) that opens the create dialog.
   - The **show-deleted toggle** (a labeled control — `members:showDeleted`) driving local
     `includeDeleted` state → `useMembersQuery(includeDeleted)`.
   - States: pending → `Table` with `Skeleton` rows (or a Skeleton block); error → `ErrorState`
     (`resolveErrorMessage(error, t)`) with a retry calling `refetch()`; success → `MembersTable`;
     defensive empty → `EmptyState`.
2. New `src/features/members/components/MembersTable.tsx`:
   - Renders `Table` with columns: **Name**, **Status** (badges), **Actions**. Each row from the
     query data in backend order.
   - Owner-rep row: a `Badge` (`members:badge.ownerRep`) and a `TierBadge`-independent marker; the
     Name cell may show the crown/owner marker per the ui-designer. **No delete control**; an
     inline explanation/tooltip `members:ownerRep.notDeletable`. Rename action present.
   - Deleted rows (only visible under the toggle): `data-deleted` muted row + a `Badge`
     `members:badge.deleted` ("(đã xóa)"); **no** rename/delete actions (OQ4a).
   - Normal member rows: **Rename** and **Delete** actions, each labeled with the member name for
     screen readers (`aria-label={t("members:actions.renameNamed", { name })}`).
3. New `src/features/members/components/MemberFormDialog.tsx` — shared create/rename modal
   (`Dialog` + `DialogContent` + `Form`):
   - Props: `mode: "create" | "rename"`, `member?: MemberResponse`, `open`, `onOpenChange`.
   - RHF + `zodResolver(memberFormSchema(t))`; rename mode pre-fills `name` (`defaultValues` from the
     member). One `TextField` (`members:form.nameLabel`), submit label per mode.
   - Submit: `useCreateMember` / `useRenameMember` `mutateAsync`; on success → toast
     (`members:toast.created`/`renamed`) + close. On error:
     - `1001` → `applyFieldErrors(error, ["name"], setError)`; leftover → `FormError`.
     - `13000` (create) → render `LimitNotice` inside the dialog (`members:limit.title` +
       `error.message`), **no** navigating action (informational); keep the form mounted.
     - `3000` (rename, stale) → toast the message + close + `invalidate`.
     - else → `FormError` = `resolveErrorMessage(error, t)`.
4. New `src/features/members/components/DeleteMemberDialog.tsx` — confirm soft-delete
   (`Dialog` + `DialogContent` + `DialogFooter`):
   - Title `members:delete.title`; body `members:delete.body` (explains the member vanishes from
     new-data selection but all history is preserved — R7); confirm `Button variant="danger"`
     (`members:delete.confirmButton`), cancel (`DialogClose`).
   - Confirm → `useDeleteMember().mutateAsync(uuid)`; success → toast `members:toast.deleted` + close.
     `3001` (defensive — owner-rep, should be unreachable) → toast `error.message`; `3000` → toast +
     close. Invalidate on settle.

### Step 7 — Route wiring

1. `src/routes/router.tsx` — replace `{ path: "members", element: <StubPage titleKey=
   "common:nav.members" /> }` with `{ path: "members", element: <MembersPage /> }` (import
   `MembersPage`). Leave `StubPage` import (still used by categories/tags/expenses/events/stats/
   wallet). `navConfig.tsx` unchanged (entry already present).

### Step 8 — i18n (R9)

1. New `src/i18n/locales/vi-VN/members.json` + `en-US/members.json`; register in `src/i18n/index.ts`
   (`resources` + `NAMESPACES`) and add `"members"` to `APP_NAMESPACES` in `src/i18n/useT.ts`. Keys:
   - `members:title`, `members:subtitle`
   - `members:add`, `members:showDeleted`, `members:activeCount` (e.g. "{{count}} thành viên đang hoạt động")
   - `members:table.name`, `members:table.status`, `members:table.actions`, `members:table.caption`
   - `members:badge.ownerRep` ("Đại diện chủ sổ" / "Owner"), `members:badge.deleted` ("Đã xóa" /
     "Deleted")
   - `members:actions.rename`, `members:actions.delete`, `members:actions.renameNamed`,
     `members:actions.deleteNamed`
   - `members:ownerRep.notDeletable` ("Thành viên đại diện chủ sổ không thể xóa.")
   - `members:form.createTitle`, `members:form.renameTitle`, `members:form.nameLabel`,
     `members:form.namePlaceholder`, `members:form.submitCreate`, `members:form.submitRename`,
     `members:form.cancel`
   - `members:delete.title`, `members:delete.body` (history-preserved explanation),
     `members:delete.confirmButton`, `members:delete.cancel`
   - `members:limit.title` (Free limit reached; body renders the server `error.message`)
   - `members:toast.created`, `members:toast.renamed`, `members:toast.deleted`
   - `members:empty.title`, `members:empty.body` (defensive), `members:error.retry`
2. Add to `src/i18n/locales/{vi-VN,en-US}/validation.json`: `validation:member.nameRequired`
   ("Tên thành viên không được để trống."), `validation:member.nameTooLong` ("Tên thành viên không
   được vượt quá 100 ký tự.") — mirroring the backend messages.

### Step 9 — Tests + verification

See "Tests the web-test-engineer should write" below. Then `pnpm lint`, `tsc -b`, `pnpm build`,
`pnpm test`; exercise `/members` against the live backend: list (owner-rep first), toggle deleted,
add (+ trigger `13000` on a Free account at the cap), rename (incl. owner-rep), soft-delete + verify
it moves under the toggle, owner-rep has no delete control.

### API endpoints consumed this cycle (verb + path + DTO)

| Screen / hook           | Verb + Path                          | Request → Response (`data`)                          | Notable codes                                   |
| ----------------------- | ------------------------------------ | ---------------------------------------------------- | ----------------------------------------------- |
| `useMembersQuery`       | `GET /v1/members?includeDeleted=`    | — → `MemberResponse[]`                               | (reads only)                                    |
| `useCreateMember`       | `POST /v1/members`                   | `{ name }` → `MemberResponse`                        | `13000` limit → `LimitNotice`; `1001` → field   |
| `useRenameMember`       | `PUT /v1/members/{uuid}`             | `{ name }` → `MemberResponse`                        | `3000` stale → toast+refetch; `1001` → field    |
| `useDeleteMember`       | `DELETE /v1/members/{uuid}`          | — → success message                                 | `3001` owner-rep (defensive); `3000` stale      |
| (reserved, OQ2)         | `GET /v1/members/{uuid}`             | — → `MemberResponse`                                 | `3000` → `NotFound` (only if a detail route)    |

Envelope handling: all through the centralized client; `data` unwrapped on success; failures throw
`ApiError` carrying the numeric `code` — components branch on `code`, render `error.message`, map
`error.fields` to fields.

### Loading / empty / error states

- **Loading** — `useMembersQuery` pending: `Table` with `Skeleton` rows.
- **Empty** — defensive only (owner-rep always present): `EmptyState` (`members:empty.*`). If the
  toggle is on and there are no deleted members, the list still shows active members (never empty).
- **Error** — list load failure: `ErrorState` (message via `resolveErrorMessage`) + retry (`refetch`).
  Mutation failures: inline `FormError`/`LimitNotice` in the dialog, or a toast (delete/stale).
- **Limit** — `13000` on create: `LimitNotice` in the create dialog, informational, no action.

### Form validation rules (mirror backend `CreateMemberRequestValidator` / `UpdateMemberRequestValidator`)

- **name** — required (non-empty after trim); max **100** chars; trimmed before submit. Server `1001`
  `error.fields.name` merges onto the field; unknown-field errors → form-level `FormError`.

### Accessibility requirements

- `Table` renders a real `<table>` with an accessible name (`caption`/`aria-label` =
  `members:table.caption`) and `<th scope="col">` headers.
- Row actions are `Button`s with names disambiguated per member
  (`aria-label` includes the member name), not just an icon.
- The show-deleted control is a labeled checkbox/switch (visible label + programmatic association).
- Deleted status is conveyed by badge **text** ("Đã xóa") + muted styling, never color alone.
- Dialogs inherit Radix focus-trap / Escape / focus-restore; `DialogContent` `title` is required and
  set; the delete confirm has a clear, member-named title.
- `LinkButton`/`asChild` renders a single `<a>` (no nested interactive elements).
- `<html lang>` stays synced by `LocaleProvider`.

### Tests the web-test-engineer should write (Vitest + RTL + MSW, deterministic — pinned TZ + locale)

- **membersApi / hooks** — `list(true)` sends `?includeDeleted=true`, `list(false)` sends `false`;
  create/rename/delete hit the right verb + path with the right body; each mutation's `onSuccess`
  invalidates `["members"]` so the list refetches (assert a second GET fires).
- **MembersPage list** — renders a row per member from MSW in backend order (owner-rep first); the
  owner-rep row has **no** delete control and shows the not-deletable explanation, but **has** a
  rename action; loading shows skeleton rows; a list error shows `ErrorState` with a working retry.
- **Show-deleted toggle** — off: only active members; toggling on re-queries with
  `includeDeleted=true` and reveals deleted rows with the "Đã xóa" badge and **no** row actions
  (OQ4a); toggling off hides them again.
- **Create** — opening the dialog, valid submit creates + toasts + closes + list updates; empty/
  whitespace name blocked client-side (Zod, no request); a >100-char name blocked; server `1001`
  maps to the name field; **`13000` renders the `LimitNotice`** (message from the envelope) without
  destroying the form and issues no navigation.
- **Rename** — dialog pre-fills the current name; submit renames + toasts + closes; **owner-rep rename
  succeeds** (allowed); a `3000` (member deleted elsewhere) toasts and closes without crashing.
- **Delete** — confirm dialog opens with the member-named title + history-preserved body; cancel
  closes with no request; confirm soft-deletes + toasts + the row leaves the default list (and
  appears under the toggle); the defensive `3001` path (if forced) toasts the message.
- **i18n parity** — `members` + new `validation` keys resolve in vi-VN and en-US; toggling locale
  switches copy.
- **Table primitive** — renders `<table>` semantics (`role`/`scope`), the accessible name, and the
  `muted`/deleted row state; empty body path.
- **LinkButton / asChild** — renders a single anchor (no nested `<button>` inside `<a>`), keyboard
  and screen-reader operable; the converted `SecurityCard` link still navigates.
- All deterministic (pinned `TZ` + locale; MSW at the client boundary; no real network/wall-clock).

## Impact Analysis

- **APIs / Database / Services:** none — M2 consumes the existing, stable `api/v1/members` endpoints.
  No backend change, no new error code (the backend's `3001` is only mirrored client-side).
- **Frontend — new:** `src/features/members/{pages/MembersPage.tsx, components/{MembersTable,
  MemberFormDialog,DeleteMemberDialog}.tsx, api/{membersApi,types}.ts, hooks/useMembers.ts,
  schemas.ts}`; `src/components/ui/Table/*`; a `LinkButton` (or `Button asChild`) primitive;
  `src/i18n/locales/{vi-VN,en-US}/members.json`.
- **Frontend — edited:** `src/routes/router.tsx` (`/members` → `MembersPage`);
  `src/components/ui/index.ts` (export `Table`, `LinkButton`); `src/i18n/index.ts` + `useT.ts`
  (register `members` namespace); `src/i18n/locales/{vi-VN,en-US}/validation.json` (member keys);
  `src/lib/api/errors.ts` (add `3001` to the mirror); `src/features/settings/components/SecurityCard.tsx`
  (convert to `LinkButton`); `src/styles/StyleGuide.tsx` (Table demo).
- **Design system:** the **`Table`** primitive (materially new visual surface; the row-action pattern
  and soft-deleted row treatment) and the **`LinkButton`/`Button asChild`** fix — a ui-designer pass
  is warranted. Everything else reuses existing primitives.
- **Documentation:** this doc; roadmap Progress Log entry when M2 closes.
- **Downstream:** M3 (categories/tags), M4 (expense list + shares table), M5 (events + balance table)
  reuse the `Table` primitive, the list/dialog CRUD pattern, and the query-key/invalidation +
  `13xxx` limit-notice conventions established here.

## Decision Log

### Decision

M2 ships the Members feature as a single `/members` list page (real `Table` primitive) with
modal-dialog create/rename/soft-delete, an include-deleted toggle, a non-deletable owner-rep, and a
reactive `13000` `LimitNotice` — plus the design-system `Table` primitive and the `LinkButton`/
`Button asChild` fix. Built on the locked foundation + M1 shell; no new dependency, no backend change.

### Reason

Members is the first list surface and a reference-data dependency of expenses (payer + shares), so it
must be manageable before M4. The backend Members API is stable and resource-owned; every rule
(R1/R7/R9, owner-rep protection) maps onto existing client seams (`classifyError`, `ErrorCodes`,
`LimitNotice`, `applyFieldErrors`). Building the `Table` primitive now serves M3/M4/M5 and avoids
parallel ad-hoc lists; the `LinkButton` fix is folded in before the nested-interactive pattern
propagates. The limit number is deliberately not invented (OQ3a) because the spec leaves it open.

### Alternatives Considered

- A bespoke card/list instead of a `Table` primitive (OQ1b) — rejected; guarantees M3/M4/M5 rework.
- A `/members/:uuid` detail route (OQ2b) — rejected; a member has no sub-data to justify it.
- Hardcoding the Free member cap for a proactive meter (OQ3b) — rejected; assumes an open business
  number and drifts silently.
- Allowing rename on deleted rows (OQ4b) / inline row-edit forms (OQ5b) — rejected for consistency
  and accessibility.

## Progress Log

### 2026-07-17

- Feature-planner: required reading completed — `feature-roadmap.md` (M2 locked scope + rules),
  `frontend-foundation.md` (locked stack + client/error/i18n seams), `m1-app-shell.md` (nav-
  registration pattern, `navConfig`, `useLogoutAction`, layout primitives, the `LinkButton` review
  nit), `CLAUDE.md`, `The-ideal.md` §2/§3.2/§3.11/§4, and the backend contract
  (`MembersController.cs`, `CreateMemberRequest`/`UpdateMemberRequest`/`MemberResponse`,
  `planning/members.md` — 3000/3001, free-form names, 1–100, owner-rep non-deletable, no reactivate/
  limits endpoint). Read the live SPA: `router.tsx`, `navConfig.tsx`, `components/ui/index.ts`,
  `client.ts`, `errors.ts`, `http-error-handling.ts`, `Premium.tsx`, `Dialog.tsx`, `Button.tsx`,
  `Layout.tsx`, `ChangePasswordPage.tsx`, `useAuth.ts`, `useCurrentUserQuery.ts`, `authApi.ts`,
  `SecurityCard.tsx`, `i18n/index.ts`, `useT.ts`, `ToastHost.tsx`.
- Drafted this plan: the `/members` list page + Table primitive, create/rename/delete dialogs, the
  include-deleted toggle, owner-rep protection, the reactive `13000` `LimitNotice`, query hooks +
  invalidation, Zod mirroring the backend validators, the `LinkButton`/`Button asChild` fix, i18n
  keys (new `members` namespace + `validation` additions), a11y, and the test matrix.
- **5 Open Questions raised** (Table primitive vs card/list; single page vs detail route; proactive
  limit with no exposed cap; deleted-row read-only; dialog vs inline form), each with a recommended
  option. None CRITICAL. Awaiting the checkpoint (orchestrator auto-accepts recommendations).
- **ui-designer pass (Step 1 + Step 2 design-system work; OQs auto-accepted at recommended):**
  - New **`Table` primitive** — `src/components/ui/Table/Table.tsx` + `Table.module.css`, exported
    from the `@/components/ui` barrel and demonstrated in `src/styles/StyleGuide.tsx`. Composition:
    `Table` (scroll-wrapped `<table>`, `caption`/`captionHidden` or `aria-label` for the accessible
    name, `dense`), `TableHead`, `TableBody`, `TableRow` (`deleted` → muted + `data-deleted`),
    `TableHeaderCell` (`scope="col"|"row"`, `numeric`, `align`), `TableCell` (`numeric` → right-align
    + tabular-nums, `actions` → right-aligned gapped action row, `align`), `TableEmpty` (`colSpan`
    full-width empty-state row). Tokens only, light+dark, zebra/hover, Vietnamese-tolerant wrapping,
    reduced-motion. Built as a straightforward table (no responsive stacked-card variant — deferred).
  - **`Button asChild`** (Radix `Slot` + `Slottable`) resolves the M1 nested-interactive nit: a
    router `<Link>` renders with button styling as a single `<a>`. Converted all four nested
    `<Link><Button>` call sites (`SecurityCard.tsx`, `DashboardPage.tsx`, `AppShellLayout.tsx`,
    `NotFound.tsx`) — accessible names/labels unchanged, just the double element removed.
  - **Added `@radix-ui/react-slot@^1.3.0`** as a declared dependency (it was already resolved in the
    pnpm store as a transitive Radix dep but not hoisted to top-level `node_modules`, so a direct
    import needed the explicit declaration — no new download, approved Radix family per Step 2).
  - `tsc -b`, `pnpm lint` (only pre-existing fast-refresh warnings), and `pnpm build` all clean.
    Note: `src/lib/api/errors.ts` `3001` mirror (Step 2.3) is the implementer's API-layer work, not
    design-system — left untouched.

### 2026-07-17 — implementer (M2 feature build)

- **OQs resolved** — OQ1–OQ5 accepted at option (a) per the orchestrator.
- **Error mirror (Step 2.3):** added `OwnerRepresentativeNotDeletable: 3001` to `ErrorCodes`
  (`src/lib/api/errors.ts`) for the defensive delete toast. No change to `FREE_LIMIT_CODES` /
  `NOT_FOUND_CODES`. The `Table` primitive + `Button asChild` (Steps 1–2) were already delivered by
  the ui-designer pass and are consumed as-is (not restyled).
- **Feature built** (`src/features/members/`): `api/types.ts` + `api/membersApi.ts`
  (list/get(reserved)/create/rename/remove over `api.*`); `hooks/useMembers.ts` (`membersKeys`
  factory, `useMembersQuery(includeDeleted)`, `useCreateMember`/`useRenameMember`/`useDeleteMember`
  each invalidating `["members"]` via the singleton `queryClient` — the established
  `invalidateCurrentUser` pattern); `schemas.ts` (Zod name trim 1–100 mirroring the backend);
  `pages/MembersPage.tsx` (+ `.module.css`) with the show-deleted toggle, loading skeleton rows,
  `ErrorState` + retry, defensive `TableEmpty`/`EmptyState`, and a Free-tier active-member count;
  `components/MembersTable.tsx` (backend order verbatim, owner-rep Badge + rename-only + not-deletable
  explanation, deleted rows muted `<TableRow deleted>` + "Đã xóa" Badge + no actions, per-member
  `aria-label`ed actions); `components/MemberFormDialog.tsx` (shared create/rename modal — `1001` →
  `applyFieldErrors(name)`, `13000` → in-dialog `LimitNotice` with the server message and the form
  kept mounted, `3000` → toast+close); `components/DeleteMemberDialog.tsx` (history-preserved confirm
  copy, `3000`/`3001` → toast).
- **Wiring:** `router.tsx` `/members` → `MembersPage` (StubPage import retained for the other stubs);
  new `members` i18n namespace (vi-VN + en-US) registered in `i18n/index.ts` + `useT.ts`;
  `validation:member.nameRequired` / `nameTooLong` added to both `validation.json` catalogs.
- **MSW:** added members handlers to `src/test/msw/handlers.ts` (list with `includeDeleted`, create
  with a demonstrable `FREE_MEMBER_LIMIT=5` → `13000`, rename with `1001`/`3000`, delete with
  `3001`/`3000`, owner-rep-first + A→Z sort) so the app runs against mocks and the web-test-engineer
  has a backing store.
- **Quality gates:** `tsc -b` clean, `pnpm lint` clean (only the pre-existing fast-refresh warnings in
  `LocaleProvider`/`ThemeProvider`/`ToastHost`), `pnpm build` succeeds.
- **Verification:** drove the full flow via a throwaway RTL+MSW harness (no browser driver / live
  backend available in this environment): list renders owner-rep first then A→Z with deleted hidden;
  show-deleted toggle reveals the muted deleted row (badge, no actions) and hides it again; owner-rep
  has a rename action, no delete control, and the not-deletable explanation; add creates + toasts +
  the list refreshes; rename pre-fills and updates (incl. owner-rep path); delete confirms with the
  history-preserved copy and the row leaves the default list; the `13000` path renders the in-dialog
  `LimitNotice` (server message) without destroying the form; en-US copy resolves. Harness deleted
  after the run (the suite is the web-test-engineer's).
- **Not exercised live:** the real backend on `:5200` (needs MariaDB + Redis) and a real browser were
  not available, so verification was MSW-driven only.
- **No new dependencies** beyond the already-declared `@radix-ui/react-slot`.
- **Pre-existing test failures (not M2):** 8 specs in `guards`/`appShellLayout`/`currentUserProfile`
  fail because the ui-designer's `Button asChild` fix turned former `role="button"` links into a single
  `<a>` (`role="link"`); those specs still query `role="button"`. Updating them is the
  web-test-engineer's job and must not undo the intentional a11y fix. M2 adds zero new failures.

### 2026-07-17 — web-test-engineer (M2 test suite + Part-A a11y churn repair)

- **Part A — repaired the pre-existing role churn** from the design-system `Button asChild`
  a11y fix (former `<Link><Button>` link-buttons are now a single `<a>` = `role="link"`). Retargeted
  the 8 stale `getByRole("button", …)` queries to `role="link"` (accessible names unchanged, nothing
  weakened, the fix untouched):
  - `src/routes/guards.test.tsx` — `NotFound_Rendered_ShowsTitleBodyAndBackLink` (back-home link).
  - `src/routes/appShellLayout.test.tsx` — `Shell_ProfileResolved_AccountButtonShowsUsername`,
    `Shell_ProfilePending_AccountButtonShowsNeutralFallbackNotSkeleton` (account affordance).
  - `src/features/auth/currentUserProfile.test.tsx` — `LoginFlow_SuccessfulLogin…`,
    `BootRehydrate_TokensOnlyThenMe…`, `Degraded_Non401MeFailure…`,
    `Degraded_NeutralAccountLabel_IsI18nDrivenNotHardcoded`, `Logout_ClearsQueryCacheAndSession`
    (account link, incl. the en-US `Account` fallback). (DashboardPage quick-links + SecurityCard
    were already asserted as links — no churn there.)
- **Part B — added the M2 Members suite** (Vitest + RTL + MSW at the client boundary; real
  hooks/components; deterministic — pinned `Asia/Ho_Chi_Minh` TZ + pinned locale; per-test unique MSW
  usernames for store isolation; the singleton `queryClient` so mutation invalidation refetches):
  - `src/features/members/schemas.test.ts` (7) — Zod name 1–100 trimmed: valid, trims on parse,
    empty/whitespace → required, at-max passes, over-max → too-long, and the benign trim-then-measure
    edge (100 core + spaces passes client-side).
  - `src/features/members/useMembers.test.tsx` (6) — `membersKeys` scoping;
    `useMembersQuery(true/false)` sends the right `includeDeleted`; create/rename/delete `onSuccess`
    invalidate `["members"]` (a second GET fires).
  - `src/features/members/membersPage.test.tsx` (25) — list owner-rep-first-then-A→Z verbatim;
    deleted hidden by default; show-deleted reveals a muted `data-deleted` row + "Đã xóa" badge with
    no actions, and hides again; owner-rep rename-only + no-delete + explanation; normal-row
    rename+delete; loading skeleton rows; empty state; list error + working retry; Free active count;
    create success (add+toast+refresh+close), empty-name blocked client-side (no request),
    maxLength=100 guard, `1001`→name field, **`13000`→in-dialog `LimitNotice` (server message), form
    kept mounted, no toast/nav**; rename pre-fill+update, owner-rep rename succeeds, `3000`→toast+close;
    delete member-named title + history-preserved body, cancel = no request, confirm soft-deletes +
    row leaves default list + reappears under toggle, defensive `3001`→toast; vi-VN default + en-US
    parity on `members` + `validation:member.*`.
  - `src/components/ui/tablePrimitive.test.tsx` (8) — `Table` semantics (accessible name via
    visible/hidden `caption` and via `aria-label`, `columnheader`/`rowheader` scopes, `data-deleted`
    muted row, `TableEmpty` full-width spanning cell) + `Button asChild` (single `<a>`, no nested
    button, keyboard-focusable/operable).
- **Extra coverage beyond the checklist:** `membersKeys` scoping unit; table accessible-name +
  column-header semantics; Free active-member count; show-deleted round-trip (on→off); delete-then-
  reappears-under-toggle; en-US `validation:member.*` parity via a live form submit; the `Button
  asChild` keyboard-focus smoke.
- **Results:** `pnpm test` **183 passed / 0 failed** (was 137 with 8 pre-existing failures), run twice
  deterministically; **+46 tests** across 4 new files. `pnpm lint` exit 0 (only the pre-existing
  fast-refresh warnings in `LocaleProvider`/`ThemeProvider`/`ToastHost`); `tsc -b` exit 0.
- **Product bugs found:** none — every M2 behavior in the plan's test matrix passed against the
  implementation. No product code changed. No testability hook needed (the implementer's `data-deleted`
  row hook + per-member `aria-label`ed actions were sufficient).

## Final Outcome

**Complete.** M2 shipped the Members feature (`src/features/members/`): list (owner-rep first then A→Z, verbatim backend order) with a show-deleted toggle, create/rename via a shared modal, soft-delete with a history-preserved confirm, owner-rep protection (renamable, no delete control + explanation), deleted rows read-only with a "Đã xóa" badge, and the `13000` member-limit as an in-dialog informational `LimitNotice`. TanStack Query hooks invalidate `["members"]`; Zod mirrors the backend validator; vi-VN + en-US. Design-system additions this cycle: the reusable **`Table`** primitive and the **`Button asChild`** a11y fix (converted the 4 M1 nested `<Link><Button>` sites to single `<a>`; `@radix-ui/react-slot` declared). Consumes `GET/POST/PUT/DELETE /v1/members`. Tests +46 (suite 137→183, incl. repairing the 8 role-churn specs from the asChild fix); code review **APPROVE, 0 blocking**. Two review nits fixed before close: `DeleteMemberDialog` now uses `resolveErrorMessage` (localized network fallback), and the owner-rep note moved to a CSS-module class. All 5 OQs shipped at option (a).

## Future Improvements

- **Backend limits surface** (OQ3c): a `GET /members/limits` or a `tier.limits` field on `/auth/me`
  to enable a proactive "X of N members used" meter across M2/M4/M5/M7 — currently the cap is not
  exposed, so the limit is reactive-only.
- **Reactivate / undelete a member** — symmetric with the tag-reactivation idea; needs a backend
  endpoint. Would make deleted rows actionable (OQ4).
- **Optimistic updates** for create/rename/delete once the write patterns settle (deferred from M2).
- **Responsive table** (OQ1c) — a table→stacked-card treatment on narrow viewports, layered onto the
  `Table` primitive without a rewrite.
- **Grouping deleted members** at the bottom of the toggled list (a client presentation choice)
  instead of the backend's interleaved A→Z order, if users find interleaving confusing.
- **Member merge** (combine a mistyped/duplicate member, re-pointing historical shares) once
  expenses/shares exist — noted as a backend future improvement in `planning/members.md`.
- E2E (Playwright) coverage of the members CRUD loop once a browser driver is available.
