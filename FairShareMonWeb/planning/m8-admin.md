# M8 — Admin suite (FairShareMonWeb)

## Objective

Build the `role === "ADMIN"`-only admin console behind the already-wired `AdminRoute` at `/admin`,
consuming the feature-complete `AdminController` (`/api/v1/admin/*`). Three surfaces:

1. **Metrics dashboard** — user/tier/role/status counts + registrations-over-time (dataviz).
2. **Revenue dashboard** — Premium-grant revenue by day/month + reference list (dataviz + `Money`).
3. **User administration** — paged/filterable/sortable user list, user detail with grant history, and
   the sensitive account actions: tier grant/revoke, disable/enable, revoke-tokens, reset-password
   (one-time temp-password reveal), role promote/demote.

This is the FINAL roadmap milestone (M8) and the second `dataviz` consumer, so it is also the trigger
to **extract the M6 KPI/chart primitives into a shared `src/components/ui/charts` module** (roadmap
Future Improvement). The non-negotiable constraint threaded through every screen and test: the admin
UI shows **only account metadata + tier-grant/payment data — never any user's ledger data**
(members/expenses/events/shares/bank accounts).

## Background

- **Roadmap placement.** `planning/feature-roadmap.md` locks M8 as the last, self-contained milestone
  (OQ3 = a): it depends only on the foundation + the `AdminRoute` guard, is `dataviz: yes` and
  `ui-designer: yes`. The endpoint list and business rules are enumerated there (§ M8) and mirror the
  backend contract.
- **Backend is feature-complete and stable.** `Controllers/AdminController.cs` carries
  `[Authorize(Policy = AuthorizationPolicies.Admin)]` at the controller level; every route is under
  `api/v1/admin/...`. The full DTO set lives in `FairShareMonApi/Models/Admin/**` (read for exact
  shapes — reflected in the endpoint table below). The semantics, privacy boundary (R10), the 14xxx
  error block, and the self/last-admin guards are locked in `FairShareMonApi/planning/admin-management.md`.
- **The privacy boundary (R10, §4.1 — non-negotiable).** Admin endpoints act ONLY on account metadata
  (`uuid`, `username`, `tier`, `role`, `status`, `createdAt`) + `tier_grants` (grant/revenue) data.
  **No admin endpoint returns any user's ledger data, and no metric is a ledger aggregate — not even
  anonymous.** The frontend must never fetch, store, or render a ledger field in the admin area. This
  is both a design constraint and a dedicated test target (below).
- **Auth guard already wired.** `src/routes/AdminRoute.tsx` admits `role === "ADMIN"` (from
  `/auth/me`, synced into the Zustand session store), holds on the boot splash while the profile
  resolves, and fails safe to `Forbidden` otherwise. `/admin` is registered in `src/routes/router.tsx`
  as `<AdminRoute>` → `<AdminPage/>` (a `StubPage` this milestone replaces). The admin nav entry is
  already registered (M1, `requiresAdmin`).
- **Locked conventions (`CLAUDE.md` + foundation).** One centralized `api` client (envelope unwrap,
  `Authorization`/`X-Time-Zone`/`Accept-Language` injection, `401 → refresh-once`, `undefined`/`null`
  query keys dropped so an omitted bound = all-time); branch on numeric `error.code`
  (`src/lib/api/errors.ts` `ErrorCodes`, already carries `14000–14003`); render `error.message`
  verbatim via `resolveErrorMessage`; `applyFieldErrors` for `1001` field errors; TanStack Query
  per-feature `api/` + `hooks/`; RHF + Zod (`schemas.ts`) mirroring backend validators; `Money` +
  `formatCount` formatters; react-i18next (vi-VN default + en-US); `@/components/ui` primitives only.
- **M6 dataviz layer to reuse/extract.** `src/features/stats/components/` holds `StatTile`,
  `OverviewKpiRow`, `CategoryBarChart` (hand-rolled CSS bars on `--fs-viz-*`, no dependency), and
  `CategoryStatsTable` (the always-present accessible data channel paired with each chart). The M6 doc
  (`planning/m6-stats-dashboard.md`, OQ5a) deferred a shared `src/components/ui/charts` extraction to
  M8 explicitly: _"M8 (Admin) revisits extraction with two real consumers."_ The `--fs-viz-*` palette
  (categorical/sequential/diverging) + the light-mode relief rule are validated and documented in
  `src/styles/README.md`.
- **Primitives available** (`src/components/ui/index.ts`): `Table` family (with `caption`,
  `captionHidden`, `numeric`, `deleted`, `total` row flags), `Dialog`/`DialogContent`
  (`tone="danger"` + severity glyph)/`DialogFooter`/`DialogClose`, `Card`/`CardBody`, `Badge` (tones),
  `Select`, `TextField`, `Button` (variants incl. `danger`, `loading`), `PageHeader`/`Stack`,
  `TierBadge`, `Money`, `Alert`, `Skeleton`/`EmptyState`/`ErrorState`, `Form`/`FieldStack`/`FormError`,
  `Toast` (via `useToast`). **No `Pagination` primitive exists yet** (no prior list was paged) — M8
  is the first paged surface (see OQ6).

## Requirements

- **R-Guard** — the whole area sits behind `AdminRoute`; the auth matrix is: anonymous → login
  redirect (client `401 → refresh` then redirect); authenticated non-admin → `Forbidden` (never sees
  the area; backend also answers `403 1004`); admin → allowed.
- **R-Privacy (R10)** — the admin feature module imports and renders ONLY account-metadata +
  tier-grant fields. No ledger type, key, or endpoint appears anywhere in `src/features/admin/**`.
  Enforced by design (typed DTOs limited to the admin DTOs) + a dedicated privacy test.
- **R-Metrics** — `/admin/dashboard`: total users KPI, tier/role/status distributions, signups over a
  date range bucketed month/day. No ledger figure of any kind.
- **R-Revenue** — `/admin/revenue`: total revenue (`Money`, verbatim) + grant count KPIs, revenue
  buckets month/day, references list. Revenue = SUM of GRANT rows only (REVOKE never counts) — the API
  computes this; the UI renders it verbatim, never client-sums money (R3).
- **R-Users** — `/admin/users`: paged (default page 1, size 20, cap 100) + filter (tier/status/role/
  username search) + sort (createdAt default desc; username/tier/status; asc/desc). Rows show account
  metadata + `grantCount`/`lastGrantAt`. `/admin/users/:uuid`: metadata + grant history table.
- **R-Actions** — tier grant/revoke (records amount + reference + note), disable/enable, revoke-tokens,
  reset-password (one-time temp-password reveal), role promote/demote — each with the correct guard
  behavior, confirm dialogs, loading/error states, and success toasts + cache invalidation.
- **R-Guards14xxx** — proactively reflect the self (`14001`) + admin-target/last-admin (`14002`)
  guards: the guarded actions (disable, revoke-tokens, reset-password, demote) are disabled with an
  explanatory tooltip when the target is self OR another ADMIN; still branch on `14001`/`14002` if the
  server rejects. `14000` (user not found) on the detail route → an admin-local not-found state.
  `14003` (disabled login) is a backend/login concern, surfaced only where relevant (documented).
- **R-i18n** — new `admin` namespace (vi-VN + en-US) registered in `src/i18n/index.ts`; all copy via
  `useT()`; fixed domain terms (Premium/Free, tier, wallet untouched here).
- **R-a11y** — semantic table + caption; charts are `role="img"` with a summarizing label AND a paired
  accessible data table (color-independent, relief rule); dialogs inherit Radix focus-trap; the
  reset-password reveal is keyboard-operable with an explicit copy action and a live-region confirm.
- **R-Money/time** — `Money` for every VND amount; `formatDateTime`/`formatDate` for timestamps in the
  viewer's zone; `X-Time-Zone` already sent by the client.

## Open Questions

> Each carries a recommendation the orchestrator auto-accepts unless flagged **CRITICAL / needs-user**.

> **Resolution (2026-07-17).** All seven Open Questions are **Resolved** at their
> recommended option: **OQ1 = a** (extract charts + refactor M6), **OQ2 = a**
> (typed DTOs + privacy test target + DEV `assertNoLedgerKeys` tripwire),
> **OQ3 = a** (UI generates a strong random temp password client-side, one-time
> reveal), **OQ4 = a** (shared `Pagination` primitive), **OQ5 = a** (URL-synced
> list state), **OQ6 = a** (horizontal count bars + paired tables), **OQ7 = a**
> (redirect `/admin` → `/admin/dashboard`, tabbed `AdminLayout`).

### OQ1 — Extract the M6 KPI/chart primitives to `src/components/ui/charts` now? (recommend: yes)

- **(a) Recommended — extract now, and refactor M6 to consume the shared primitives.** Create
  `src/components/ui/charts/` with `KpiTile` (from `StatTile`), `RankedBarChart` (generalized
  `CategoryBarChart` — a labeled horizontal-bar list on `--fs-viz-*`), and a net-new
  `TimeSeriesBarChart` (columns over time buckets, for signups + revenue). Re-export from
  `@/components/ui`. Refactor `src/features/stats` to import the shared `KpiTile`/`RankedBarChart`
  (M6's existing tests guard the refactor). This is exactly the roadmap Future Improvement + the M6
  OQ5a deferral ("M8 is the trigger"). Trade-off: touches M6 (bounded churn, well-tested); one
  design-system addition needing a `ui-designer` + `dataviz` pass.
- (b) Copy the M6 pattern feature-local into `src/features/admin/components` (no extraction, no M6
  change). Trade-off: zero M6 risk, but duplicates the dataviz scaffolding the roadmap explicitly
  wanted de-duplicated once two consumers exist; a third consumer later inherits the debt.
- (c) Extract only `KpiTile`/`RankedBarChart` to `ui/charts`, keep `TimeSeriesBarChart` admin-local.
  Trade-off: middle ground; the time-series chart is genuinely reusable so this under-extracts.

### OQ2 — How strictly to enforce the privacy boundary in code? (recommend: typed DTOs + test + dev assertion)

- **(a) Recommended — three layers: (1) the admin `types.ts` declares ONLY the account/grant DTOs
  (no ledger type is importable/mappable in the module); (2) a dedicated privacy test renders every
  admin screen against MSW fixtures and asserts no ledger key ever appears in the DOM/response
  (`members`, `expenses`, `events`, `shares`, `bankAccounts`, `payerMemberId`, `shareUuid`, `amount`
  outside grant/revenue, etc.); (3) a dev-only runtime assertion in the admin api layer that throws in
  DEV if an admin response object carries a forbidden key.** Trade-off: layer (3) is belt-and-braces
  (the backend already guarantees this), but admin has real privacy stakes so cheap defense-in-depth
  is warranted. Test-only cost.
- (b) Rely on typed DTOs + the privacy test only (drop the runtime assertion). Trade-off: lighter, but
  loses the DEV tripwire if a future backend change ever leaked a field.

### OQ3 — Reset-password one-time reveal: generate the temp password client-side, or admin-typed? — CRITICAL / needs-user

> **Flagged CRITICAL (reset-password reveal — security/privacy-sensitive).** `ResetPasswordRequest`
> requires `newPassword` (admin-supplied; backend applies the password rules and echoes it once in
> `ResetPasswordResponse.password`). The *reveal handling* has a safe default; the *how the temp
> password is chosen* is a preference with security weight.

- **(a) Recommended — the UI generates a strong random temp password client-side (12–16 chars, meets
  the backend rules), pre-fills a read-only field with a "Regenerate" action, and lets the admin
  optionally reveal/edit it before submit.** On success, a `tone="danger"`/emphatic dialog shows the
  temp password EXACTLY once with a copy-to-clipboard button + a "close destroys this — copy it now"
  warning; the value is held only in component state, never in TanStack cache, never persisted, never
  logged, and cleared on dialog close/unmount. Trade-off: strong password by default + one-time reveal
  is the safest support flow; the admin still relays it out-of-band.
- (b) Admin types the temp password manually (RHF field, `resetPasswordSchema` mirrors the change-
  password rules), response echo shown once. Trade-off: simpler, but admins tend to pick weak/reused
  temp passwords.
- (c) Both: generate by default (a) with a manual-override toggle (b). Trade-off: most flexible, most
  surface to design/test.

> The reveal handling (dialog, copy, no persistence, clear on close, no logging) is fixed regardless
> of the choice above; only the password-origin differs.

### OQ4 — `Pagination` control: shared primitive or admin-local? (recommend: shared primitive)

- **(a) Recommended — add a shared `src/components/ui/Pagination` primitive** (prev/next + page
  numbers + "trang X / Y", accessible `nav` with `aria-current`), since it's the first paged surface
  but clearly reusable by future lists. `ui-designer` owns its visual spec. Trade-off: one small
  design-system add.
- (b) Build pagination admin-local. Trade-off: no design-system change now, but the next paged feature
  re-invents it.

### OQ5 — User-list filter/page/sort state: URL search params or local component state? (recommend: URL search params)

- **(a) Recommended — mirror the list state into the URL (`?tier=&status=&role=&search=&page=&sort=&dir=`)
  via React Router `useSearchParams`, so an admin can deep-link/refresh/share a filtered view and the
  back button works.** Trade-off: slightly more wiring than `useState`.
- (b) Local `useState`. Trade-off: simpler, but filters reset on refresh and can't be shared.

### OQ6 — Metrics distribution visualization form (recommend: horizontal count bars + table)

- **(a) Recommended — render tier/role/status distributions as small labeled horizontal bars
  (`RankedBarChart`) each paired with a compact table (or a `DescriptionList`), honoring the relief +
  color-independence rules.** Signups + revenue use the `TimeSeriesBarChart`. Trade-off: consistent
  with M6's chart-plus-table pattern; no donuts (M6 OQ rejected pie as the primary form).
- (b) Render distributions as plain `Badge`/number rows (no bars). Trade-off: less visual, simplest;
  under-uses the dataviz layer M8 is meant to exercise.

### OQ7 — `/admin` landing (recommend: redirect to `/admin/dashboard`, tabbed `AdminLayout`)

- **(a) Recommended — `/admin` redirects to `/admin/dashboard`; an `AdminLayout` provides sub-nav tabs
  (Dashboard · Doanh thu · Người dùng) shared across the admin sub-routes.** Trade-off: introduces an
  admin sub-layout (small); clean IA for three surfaces.
- (b) A single `/admin` page with all three surfaces stacked. Trade-off: no routing, but a heavy page
  and no deep-linking to Users/detail.

## Assumptions

- No backend change is needed; all routes/DTOs/error codes exist and are stable. If a screen appears
  to need a new endpoint, that is a new Open Question, not a silent addition.
- The current admin's own `uuid` is available from the session store (`useCurrentUser()`), enabling
  the client-side self-guard. (If `/auth/me`'s `uuid` is absent in some edge, the guarded controls
  simply stay enabled and rely on the backend `14001`.)
- `14000–14003` are already mirrored in `src/lib/api/errors.ts`; no error-code change is needed.
- Admin is permitted to know a user *exists* within the admin scope, so a `14000` on the detail route
  is a legitimate "user not found" state (not the ledger-style existence-hiding 404).
- Revenue `references` are payment reference strings the operator entered; displaying them to the admin
  is in-scope (they are admin/tier-grant data, not ledger data).
- The dashboards' date range mirrors the M6 preset shape (This month / Last 30 days / This year / All
  time / Custom) plus a bucket toggle (month default / day), reusing `dateBoundToIso`.

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. New per-feature tree `features/admin/{api,hooks,pages,components}`
> + `schemas.ts` + `dateRange.ts`. Concrete names below assume the recommended OQ option.

### Step 1 — Shared chart extraction (OQ1a) — `ui-designer` + `dataviz`

1. Create `components/ui/charts/`:
   - `KpiTile.tsx` — generalized `StatTile` (label + big value + hint + `loading` skeleton on `Card`).
   - `RankedBarChart.tsx` — generalized `CategoryBarChart`: a labeled horizontal-bar list on
     `--fs-viz-cat-1..8` (9th+ → `--fs-viz-ink-muted`), each bar shipping a direct label + value so
     identity never rests on color; `role="img"` + summarizing `aria-label`; bars `aria-hidden` (a
     paired table carries the data). Props generalized: `items: { key, label, value, ratio, tone? }[]`.
   - `TimeSeriesBarChart.tsx` — **net-new**: vertical columns over ordered time buckets
     (`{ periodLabel, value }[]`), `--fs-viz-seq-*` (sequential) fill, direct axis labels + a paired
     table; honors the relief + reduced-motion rules.
   - `charts/index.ts` re-exporting them; add to `components/ui/index.ts`.
2. Refactor `features/stats`: `StatTile` → re-export/wrap `KpiTile`; `CategoryBarChart` → thin adapter
   over `RankedBarChart`. M6's existing tests (`overviewKpiRow.test.tsx`, `categoryBarChart.test.tsx`)
   guard the refactor.

### Step 2 — API layer — `features/admin/api/`

1. `types.ts` — TS mirrors of the admin DTOs ONLY (no ledger type). See the endpoint table for shapes:
   `AdminMetricsRequest/Response` (+ `MetricCount`, `PeriodMetric`), `RevenueRequest/Response`
   (+ `RevenueBucketRow`), `AdminUserListRequest`, `PagedResult<AdminUserRow>`, `AdminUserRow`,
   `AdminUserDetailResponse`, `TierGrantRow`, `GrantTierRequest`, `RevokeTierRequest`,
   `ResetPasswordRequest/Response`, `SetRoleRequest`. Union literals: `Tier = "FREE" | "PREMIUM"`,
   `Role = "USER" | "ADMIN"`, `Status = "ACTIVE" | "DISABLED"`, `Bucket = "month" | "day"`.
2. `adminApi.ts` — one object over the centralized client (envelope/auth/refresh handled there). All
   list/dashboard queries drop `undefined` bounds automatically. Include the OQ2a DEV-only
   `assertNoLedgerKeys(response)` tripwire wrapping each response.

Endpoint table (verb + path → request → response):

| Screen | Verb + path | Request → Response |
|---|---|---|
| Metrics | `GET /v1/admin/dashboard` | `{ from?, to?, bucket }` → `AdminMetricsResponse { from?, to?, totalUsers, tierDistribution: MetricCount[], roleDistribution, statusDistribution, signups: PeriodMetric[] }` |
| Revenue | `GET /v1/admin/revenue` | `{ from?, to?, bucket }` → `RevenueResponse { from?, to?, bucket, buckets: { periodLabel, total, grantCount }[], totalRevenue, grantCount, references: string[] }` |
| Users list | `GET /v1/admin/users` | `{ tier?, status?, role?, search?, page, pageSize, sort, direction }` → `PagedResult<AdminUserRow>` (`AdminUserRow { uuid, username, tier, role, status, createdAt, grantCount, lastGrantAt? }`) |
| User detail | `GET /v1/admin/users/{uuid}` | → `AdminUserDetailResponse { uuid, username, tier, role, status, createdAt, grants: TierGrantRow[] }`; miss → `14000` |
| Grant | `POST /v1/admin/users/{uuid}/tier/grant` | `GrantTierRequest { amount, currency?, reference?, note? }` → `TierGrantRow` |
| Revoke | `POST /v1/admin/users/{uuid}/tier/revoke` | `RevokeTierRequest { note? }` → `TierGrantRow` |
| Disable | `POST /v1/admin/users/{uuid}/disable` | → message (`ApiResult`); guards `14001`/`14002` |
| Enable | `POST /v1/admin/users/{uuid}/enable` | → message |
| Revoke tokens | `POST /v1/admin/users/{uuid}/revoke-tokens` | → message; guards `14001`/`14002` |
| Reset password | `POST /v1/admin/users/{uuid}/reset-password` | `ResetPasswordRequest { newPassword }` → `ResetPasswordResponse { username, password }` (one-time); guards `14001`/`14002` |
| Set role | `POST /v1/admin/users/{uuid}/role` | `SetRoleRequest { role }` → message; guards `14001`/`14002` |

`ApiResult<T>` handling: the client unwraps `data`; on failure throws `ApiError` with numeric `code`.
Screens branch on `code` (14000/14001/14002/1001/1003/1004) and render `error.message` verbatim.

### Step 3 — Hooks — `features/admin/hooks/`

1. `useAdminDashboard.ts` — `adminKeys` factory (`all`, `metrics(req)`, `revenue(req)`); `useMetricsQuery`,
   `useRevenueQuery` (query keys embed the request; omitted bound = all-time via the client).
2. `useAdminUsers.ts` — `useAdminUsersQuery(listReq)`, `useAdminUserQuery(uuid)`; mutations
   `useGrantTier`, `useRevokeTier`, `useDisableUser`, `useEnableUser`, `useRevokeTokens`,
   `useResetPassword`, `useSetRole`. Each mutation `onSuccess` invalidates `["admin","users"]` (and the
   detail key) so the list/detail refetch; dashboards invalidate on tier grant/revoke + role change
   (`["admin","metrics"]`, `["admin","revenue"]`). **`useResetPassword` never caches its response** —
   the temp password is returned to the caller and held in component state only.

### Step 4 — Schemas — `features/admin/schemas.ts` (Zod, mirroring backend validators)

- `grantTierSchema` — `amount: number >= 0` (integer VND; use `MoneyInput`), `currency?` (optional,
  defaults VND, length 3), `reference?` (max 255), `note?` (max 500). Mirrors `GrantTierRequestValidator`.
- `revokeTierSchema` — `note?` (max 500).
- `resetPasswordSchema` — `newPassword` mirroring the change-password rules (reuse the auth password
  constraints); only used if OQ3 lands on (b)/(c) or as validation on the generated value.
- `adminUserListSchema` / range: `dateRange.ts` provides preset→request + a `bucket` toggle, reusing
  `dateBoundToIso`; a `customRangeSchema`-style guard blocks `from > to` client-side (like M6).

### Step 5 — Routing + layout (OQ7a) — `routes/router.tsx` + `features/admin/pages/`

Replace the single `/admin` index with:

```
{ path: "admin", element: <AdminRoute />, children: [
  { element: <AdminLayout />, children: [
    { index: true, element: <Navigate to="/admin/dashboard" replace /> },
    { path: "dashboard", element: <AdminDashboardPage /> },
    { path: "revenue",   element: <AdminRevenuePage /> },
    { path: "users",     element: <AdminUsersPage /> },
    { path: "users/:uuid", element: <AdminUserDetailPage /> },
  ]},
]}
```

- `AdminLayout.tsx` — `PageHeader` + tab sub-nav (`NavItem`/links): Bảng chỉ số · Doanh thu · Người dùng.
- Remove the old `AdminPage` stub.

### Step 6 — Dashboards — `features/admin/pages` + `components`

1. `AdminDashboardPage.tsx` — `AdminRangeControl` (presets + bucket toggle) drives `useMetricsQuery`;
   renders `MetricsKpiRow` (Tổng người dùng KPI), `DistributionPanel` ×3 (tier/role/status →
   `RankedBarChart` + paired table, OQ6a), and `SignupsPanel` (`TimeSeriesBarChart` + table).
   Loading → skeleton tiles/bars; empty (zero users/no signups) → valid `0`/EmptyState; error →
   `ErrorState` + retry.
2. `AdminRevenuePage.tsx` — same range control drives `useRevenueQuery`; `RevenueKpiRow`
   (Tổng doanh thu via `<Money>` + Số lượt cấp count), `RevenueChart` (`TimeSeriesBarChart` of bucket
   totals + paired money table), `ReferencesList` (references, newest-first). All money verbatim (R3).
3. Components: `components/dashboard/{AdminRangeControl,MetricsKpiRow,DistributionPanel,SignupsPanel,RevenueKpiRow,RevenueChart,ReferencesList}.tsx`.

### Step 7 — User administration — `features/admin/pages` + `components/users`

1. `AdminUsersPage.tsx` — `AdminUserFilters` (tier/status/role `Select` + username search `TextField`,
   URL-synced per OQ5a) + `AdminUserTable` + `Pagination` (OQ4a). Table columns: Tên đăng nhập ·
   `TierBadge` · `RoleBadge` · `StatusBadge` · Ngày tạo · Số lượt cấp (`grantCount`) · Cấp gần nhất
   (`lastGrantAt`) · Thao tác (row action menu / link to detail). Sortable headers (createdAt default
   desc). Loading → skeleton rows; empty → `EmptyState`; error → `ErrorState`.
2. `AdminUserDetailPage.tsx` — metadata `DescriptionList` (username/uuid/tier/role/status/createdAt) +
   `GrantHistoryTable` (`TierGrantRow[]`: action badge, tier, `<Money>` amount, reference, note,
   grantedBy, createdAt) + the action bar. `14000` → admin-local `NotFoundInline` (not the global
   existence-hiding NotFound).
3. `AdminUserActions.tsx` — the action set, computing the client-side guard: `isSelf = row.uuid ===
   me.uuid`; `isAdminTarget = row.role === "ADMIN"`. Guarded actions (disable, revoke-tokens,
   reset-password, demote) are disabled with an explanatory tooltip when `isSelf || isAdminTarget`;
   tier grant/revoke + promote always enabled. Each opens its dialog.
4. Dialogs (`components/users/`):
   - `TierGrantDialog.tsx` — RHF + `grantTierSchema`; `MoneyInput` amount, reference, note; `applyFieldErrors`
     for `1001`; success toast + invalidate users+detail+dashboards.
   - `TierRevokeDialog.tsx` — `tone="danger"`-lite confirm + optional note.
   - `DisableUserDialog.tsx` — `tone="danger"` confirm (kills tokens + blocks login), ack copy; handles
     `14001`/`14002` inline. `EnableUserDialog` — plain confirm.
   - `RevokeTokensDialog.tsx` — `tone="danger"` confirm ("đăng xuất mọi thiết bị"); handles `14001`/`14002`.
   - `ResetPasswordDialog.tsx` — OQ3a: generate strong temp password (Regenerate), submit, then a
     one-time reveal panel with copy-to-clipboard + "copy now, closing destroys this" warning; value in
     state only, cleared on close; a live-region confirms the copy; handles `14001`/`14002`/`1001`.
   - `SetRoleDialog.tsx` — promote (USER→ADMIN, `tone="default"`) / demote (ADMIN→USER, `tone="danger"`);
     demote of self/other-admin already blocked upstream; handles `14002` (last-admin) inline.
   - `RoleBadge.tsx`, `StatusBadge.tsx` — `Badge` tones (ADMIN accent; DISABLED danger).

### Step 8 — i18n — new `admin` namespace

- Add `locales/{vi-VN,en-US}/admin.json`; register in `src/i18n/index.ts` (`resources`, `NAMESPACES`).
- Key groups: `admin:nav.*` (dashboard/revenue/users), `admin:dashboard.*` (kpis, distributions,
  signups, states), `admin:revenue.*` (total, grantCount, chart, references), `admin:users.*`
  (columns, filters, sort, pagination, empty), `admin:detail.*` (metadata labels, grantHistory,
  notFound), `admin:actions.*` (grant/revoke/disable/enable/revokeTokens/resetPassword/setRole titles,
  bodies, confirm/cancel, toasts, guard tooltips `guardSelf`/`guardAdmin`/`guardLastAdmin`),
  `admin:resetPassword.*` (generate, regenerate, revealTitle, copy, copied, warning). Backend
  `error.message` (14xxx) is rendered verbatim; i18n only for client-synthetic copy + guard tooltips.

### Step 9 — Privacy tripwire + `Pagination` primitive

- `components/ui/Pagination/Pagination.tsx` (OQ4a) + export.
- `features/admin/api/privacy.ts` (OQ2a) — `LEDGER_KEYS` set + `assertNoLedgerKeys(obj)` throwing in
  `import.meta.env.DEV`; wrap admin responses in `adminApi`.

### Step 10 — MSW handlers + tests

- Extend `src/test/msw/handlers.ts` with admin fixtures (metadata + grant/revenue only — deliberately
  NO ledger fields, so the privacy test is meaningful).

## Impact Analysis

- **APIs/Database/Services:** none — consumes existing stable `/admin/*` endpoints.
- **Frontend (new):** `src/features/admin/{api,hooks,pages,components}`, `schemas.ts`, `dateRange.ts`,
  `privacy.ts`; new routes `/admin/dashboard|revenue|users|users/:uuid` + `AdminLayout`; new `admin`
  i18n namespace.
- **Design system (net-new / changed):** `components/ui/charts/{KpiTile,RankedBarChart,TimeSeriesBarChart}`
  (extraction + one net-new time-series chart) and `components/ui/Pagination` — both need a
  `ui-designer` + `dataviz` pass. `RoleBadge`/`StatusBadge` are thin `Badge` wrappers (feature-local).
- **M6 refactor:** `features/stats` `StatTile`/`CategoryBarChart` re-point to the shared primitives
  (guarded by existing M6 tests).
- **Routing:** `routes/router.tsx` admin subtree expanded; old `AdminPage` stub removed.
- **Docs:** this doc; update roadmap Future Improvement (chart extraction) to "done in M8" at close.
- **Downstream:** the extracted `ui/charts` + `Pagination` become reusable for future lists/dashboards.

## Reuse vs net-new (for the ui-designer)

- **Reuse:** `Table` family, `Dialog`/`DialogContent (danger)`, `Card`, `Badge`, `Select`, `TextField`,
  `MoneyInput`, `Button`, `PageHeader`/`Stack`/`DescriptionList`, `TierBadge`, `Money`, `Alert`,
  `Skeleton`/`EmptyState`/`ErrorState`, `Toast`, the `--fs-viz-*` palette + relief rule, the M6
  KPI/bar/table pattern.
- **Net-new (design needed):** `TimeSeriesBarChart` (columns over time), `Pagination`, the tabbed
  `AdminLayout`, the user-admin table + filter bar, the reset-password one-time-reveal panel, and the
  guarded/destructive action dialogs (severity tiers: routine confirm vs danger vs one-time-secret).
- **Extraction (design advice needed):** promoting `KpiTile`/`RankedBarChart` from feature-local M6 to
  `components/ui/charts` (OQ1) — the designer confirms the generalized prop contract.

## Tests (for the web-test-engineer; Vitest + RTL, MSW at the client boundary, pinned TZ + locale)

**Auth matrix (guard):**
- anon → `/admin/*` triggers the login redirect path (no admin content rendered).
- authenticated non-admin (`role: "USER"`) → `Forbidden`, never admin content.
- admin (`role: "ADMIN"`) → admin content renders.

**Privacy boundary (R10 — headline target):**
- Render `AdminUsersPage`, `AdminUserDetailPage`, `AdminDashboardPage`, `AdminRevenuePage` against MSW
  fixtures and assert NO ledger key/field ever appears in the DOM or response
  (`members/expenses/events/shares/bankAccounts/payerMemberId/shareUuid` and money outside grant/
  revenue). Assert `assertNoLedgerKeys` throws in DEV when a forbidden key is injected.

**Guards (14001/14002):**
- `AdminUserActions`: guarded actions disabled (with tooltip) for self and for an ADMIN target; enabled
  for a non-admin target; tier grant/revoke + promote always enabled.
- Dialogs surface `14001`/`14002` inline when the server rejects (e.g. last-admin demote → `14002`).

**Dashboards:**
- Metrics: KPI + distributions + signups render from fixtures; zero-state renders `0`/EmptyState, not
  error; bucket toggle + range change refetch; error → `ErrorState` + retry; charts expose a paired
  accessible table (`role="img"` + label present).
- Revenue: `totalRevenue`/bucket totals render via `<Money>` (verbatim, never client-summed); references
  list renders; REVOKE-only fixture → 0 revenue with grants shown.

**User administration:**
- List: pagination (page change refetches), filters (tier/status/role/search) + sort update the query
  and URL (OQ5a); empty/loading/error states.
- Detail: metadata + grant-history table render; `14000` → admin-local not-found state.
- Grant dialog: `grantTierSchema` blocks `amount < 0`; `1001` field errors map onto fields; success
  toast + invalidation.
- Reset-password (OQ3): generated temp password meets rules; the one-time reveal shows once, copy works,
  and the value is gone after close (not in cache/DOM afterward); response never persisted.
- Disable/enable/revoke-tokens/set-role: confirm → success toast + cache invalidation; danger tone on
  the destructive ones.

**i18n:** `admin` namespace has vi-VN + en-US parity for all keys (a `statsI18n.test.ts`-style parity test).

## Decision Log

### Decision
Adopt the recommended options (OQ1a extract charts, OQ2a triple-layer privacy, OQ4a shared Pagination,
OQ5a URL-synced list state, OQ6a bars+table, OQ7a tabbed AdminLayout) as the working plan; **OQ3
(reset-password reveal origin) is flagged CRITICAL/needs-user** with recommendation (a) generate
client-side + one-time reveal.

### Reason
M8 is the roadmap-designated trigger for the `ui/charts` extraction (M6 OQ5a deferred it here); the
privacy boundary is the milestone's defining constraint and warrants defense-in-depth + a dedicated
test; the reset-password reveal is the one genuinely security-sensitive UX choice, so it is surfaced
for explicit confirmation despite having a safe default.

### Alternatives Considered
- Copying M6 charts feature-local (OQ1b) — rejected; re-introduces the duplication the roadmap wanted
  removed once two consumers exist.
- Admin-typed-only reset password (OQ3b) — weaker default password hygiene; kept as an option.
- Single stacked `/admin` page (OQ7b) — rejected; no deep-linking, heavy page.

## Progress Log

### 2026-07-17

- Feature-planner drafted this M8 plan. Required reading completed: `planning/feature-roadmap.md`
  (M8 scope + locked OQs), `FairShareMonWeb/CLAUDE.md`, `FairShareMonApi/Controllers/AdminController.cs`
  + all `Models/Admin/**` DTOs, `FairShareMonApi/planning/admin-management.md` (privacy boundary R10,
  14xxx block, self/last-admin guards), the M6 stats dataviz layer (`StatTile`/`OverviewKpiRow`/
  `CategoryBarChart`/`CategoryStatsTable` + `dateRange`), the shipped design-system index, the api
  client + `errors.ts` (14xxx already mirrored) + `http-error-handling.ts`, the router + `AdminRoute`
  + `AdminPage` stub, the members CRUD/hooks pattern, the `Dialog`/`CloseEventDialog` destructive
  pattern, and the i18n namespace registration.
- Produced the routes/pages/components/hooks/schemas/endpoints plan, the chart-extraction decision, the
  privacy-boundary constraint + its test target, the guard-reflection design, i18n key groups, and the
  test list (auth matrix, privacy assertion, 14xxx guards). Recorded 7 Open Questions (one CRITICAL:
  reset-password reveal origin).
- Awaiting the checkpoint: OQ3 needs user confirmation; the rest auto-accept the recommended option.

### 2026-07-17 — web-implementer (M8 built end-to-end)

All seven OQs resolved at the recommended option (OQ3 = a, UI-generated temp
password), per the orchestrator's direction.

**M6 refactor (guarded by M6 tests):** re-pointed `features/stats/components/`
onto the shared `components/ui/charts` primitives — `StatTile.tsx` now re-exports
`KpiTile`; `OverviewKpiRow.tsx` uses `KpiRow`/`KpiTile`/`KpiValue`;
`CategoryBarChart.tsx` became a thin `CategoryStatRow[] → RankedBarItem[]` adapter
over `RankedBarChart` (label slot = `CategoryMarker` + `(đã xóa)`, value =
`<Money>`, meta = share %, ratio = total/maxTotal); `CategoryStatsTable` kept as-is;
dropped the now-dead `.statTile/.kpiRow/.bar{Value,Share,Track,Fill}/.compactChart`
rules from `stats.module.css`. M6 tests stay green (`overviewKpiRow.test.tsx`,
`categoryBarChart.test.tsx`, and the rest).

**Admin feature** under `features/admin/{api,hooks,pages,components}` + `schemas.ts`
+ `dateRange.ts` + `generatePassword.ts`, behind `/admin` (tabbed `AdminLayout` →
dashboard/revenue/users/users/:uuid; `/admin` → redirect dashboard; `AdminPage`
stub removed). Types mirror ONLY the account/grant DTOs; the DEV-only
`assertNoLedgerKeys` tripwire (OQ2a) wraps every admin read. New `admin` i18n
namespace (vi-VN + en-US) registered in `i18n/index.ts` + `useT.ts`. Branch on
14xxx numeric codes; loading/empty/error states throughout; the reset-password
one-time reveal holds the temp password in component state only (gated on `open`,
`showClose={false}`, copy-to-clipboard + live-region + destroy-on-close). Extended
`test/msw/handlers.ts` with admin fixtures (metadata + grant/revenue only —
deliberately no ledger fields).

**Quality gate:** `tsc -b` clean; `pnpm lint` clean (only pre-existing
fast-refresh warnings); `pnpm build` succeeds; full suite **661/661 across 75
files** (incl. M6 after the refactor).

**Live verification against the backend on :5200** (registered `m8admin`, promoted
to ADMIN via SQL; `m8user` a plain user; both plus the test grant cleaned up
afterward): non-admin `/admin/*` → **403 `1004`**; dashboard/revenue/users
list+detail/grant/reset-password reveal/disable/enable/revoke-tokens/promote all
returned the exact shapes the UI consumes (revenue reflected a new grant; money
as verbatim JSON numbers); guards confirmed live — **14001** (self disable),
**14002** (disable/reset/demote another admin), **1001** (negative grant amount,
`fields.amount`), **14000/404** (unknown uuid). Privacy boundary confirmed live —
admin payloads carry only account/grant fields. App boots in mock mode; all admin
modules + router + the refactored stats module transform without runtime errors.
Not exercised live: a rendered click-through in a real browser (no Playwright in
the headless env) — mitigated by the RTL suite rendering real components against
MSW + the Vite transform smoke + the exhaustive live-API contract check.

### 2026-07-17 — ui-designer (design pass, OQs accepted; OQ3 = a: UI-generated temp password)

Delivered the M8 design assets: the shared chart extraction, the `Pagination`
primitive, and the living-style-guide specs for every admin surface. **Design +
specs only — no admin feature data/hooks/routing/i18n (implementer's).** Used the
`dataviz` skill and the reserved `--fs-viz-*` palette throughout; no `--fs-viz-*`
hue changed, so no re-step needed — re-ran the validator to confirm the categorical
palette still passes (light worst adjacent CVD ΔE 9.1 / dark 8.4) and verified the
new time-series column fill `--fs-viz-seq-500` clears 3:1 on both surfaces (5.39
light / 5.75 dark). `tsc -b` + `pnpm lint` + `pnpm build` clean.

**Chart extraction (OQ1a) — new `src/components/ui/charts/`:**
- `KpiTile.tsx` — `KpiTile` (from M6 `StatTile`) + `KpiValue` (big tabular display
  for counts) + `KpiRow` (responsive auto-fit grid).
- `RankedBarChart.tsx` — generalized `CategoryBarChart`; `items: RankedBarItem[]`
  with a **slot** `label` (so `CategoryMarker`/text/`Badge` all work), `value`,
  caller-computed `ratio`, optional `meta` + `color`; `--fs-viz-cat-*` by rank
  (9th+ muted), relief rule via direct label+value+paired table, `role="img"`.
- `TimeSeriesBarChart.tsx` — **net-new** vertical columns over time; single
  sequential hue (`--fs-viz-seq-500`), `showValues` cap toggle, paired table,
  `role="img"`, reduced-motion honored.
- `charts/index.ts` + re-export from `components/ui/index.ts`.
- **M6 NOT refactored here (my call):** shipped the extracted primitives + specs
  and left the M6 `features/stats` re-point to the implementer so M6's tests
  (`overviewKpiRow.test.tsx` / `categoryBarChart.test.tsx`) guard the wiring
  refactor. Exact M6 call sites flagged in the final report.

**Net-new primitive:** `src/components/ui/Pagination/Pagination.{tsx,module.css}`
(controlled prev/next + windowed numbered pages + `role="status"` "Trang X / Y";
`nav` landmark, native buttons, `aria-current`, disabled at ends, injected copy).

**Living style guide:** `src/styles/M8Showcase.{tsx,module.css}` (mounted in
`StyleGuide.tsx`, light+dark) — the tabbed `AdminLayout` console shell, the
metrics + revenue dashboards (KPI rows + `RankedBarChart` distributions +
`TimeSeriesBarChart`, each paired with a table; revenue via `Money`, **no ledger
data**), the user-admin filter bar + sortable table + `Pagination` + user detail
(metadata + grant history + 14000 admin-local not-found), and the **three-tier
sensitive-action dialogs** — routine (enable), danger (disable/revoke-tokens/demote
with self-14001 / last-admin-14002 guard messaging), and the **one-time
reset-password reveal** (OQ3a: client-generated strong temp password + Regenerate,
reveal ONCE with copy-to-clipboard + "copy now — closing destroys this", value in
component state only) + the tier grant/revoke dialogs. Feature-local `RoleBadge`/
`StatusBadge` treatments specced (icon+text, never color alone).

**Docs:** `src/styles/README.md` gained "Shared chart primitives (M8)",
"Pagination (M8)", and "Admin console (M8)" sections.

### 2026-07-17 — web-test-engineer (M8 test suite)

Added the M8 test suite (Vitest + RTL, MSW at the client boundary, pinned
Asia/Ho_Chi_Minh TZ + vi-VN locale, per-test session/store isolation). **18 new
test files, +116 tests; full suite 777/777 across 93 files** (was 661/75) — green
twice consecutively, `tsc -b` clean, `pnpm lint` clean (only the pre-existing
fast-refresh warnings). The M6 refactor stayed green (all `features/stats` specs
pass unchanged).

Coverage added, by area:

- **Shared chart primitives** (`components/ui/charts/*`, `Pagination`):
  `kpiTile` / `rankedBarChart` / `timeSeriesBarChart` / `pagination` (30 tests) —
  `role="img"` + summarizing label, bars/columns `aria-hidden` (paired table is
  the data channel), `--fs-viz-cat-*`-by-rank→muted fill + caller `color`
  override, caller-computed ratio widths/heights, empty→renders-nothing;
  Pagination single-page→null, prev/next disable at ends, `aria-current`,
  `role="status"` summary, click + keyboard, `disabled`.
- **Auth matrix** (`adminAuthMatrix`) — anonymous→login redirect, non-admin
  (incl. Premium USER)→Forbidden, ADMIN→admits; fail-safe on pending (splash),
  settled error, and unknown role.
- **Privacy boundary R10** (`privacy`) — `assertNoLedgerKeys` trips on every
  ledger key (nested/array) and is inert for grant/revenue money; all four admin
  reads return no ledger key; no ledger key reaches the rendered DOM of any admin
  surface; and the tripwire rejects when a (mock) backend leaks `expenses`.
- **Dashboards** (`adminDashboardPage`, `adminRevenuePage`) — KPIs + tier/role/
  status `RankedBarChart` distributions each paired with a table + signups
  `TimeSeriesBarChart`; revenue via `<Money>` **verbatim** (a total inconsistent
  with the bucket sum proves it is never client-summed); zero/REVOKE-only→valid
  `0`/empty (not error); bucket + preset drive refetch; error→ErrorState+retry.
- **User admin** (`adminUsersPage`, `adminUserDetailPage`, `adminUserActions`) —
  paged list (20/pg, 2 pages), filters/sort/page URL-synced + query-param driven,
  empty/loading/error; detail metadata + grant history + `14000`→admin-local
  not-found; client-side self/other-admin guard disables the guarded actions with
  the right tooltip, leaves grant/revoke/promote enabled.
- **Dialogs** (`tierGrantDialog`, `resetPasswordDialog`, `confirmActionDialogs`) —
  grant blocks missing amount client-side, `1001 fields.amount` maps onto the
  field, success→toast+close+`["admin","users"]` invalidation; reset-password
  generates a strong temp pw (charset/length) + regenerate, one-time reveal +
  copy (clipboard + live-region), **secret never in the TanStack cache or
  localStorage and cleared on close**, `14002`→inline (no reveal);
  disable/enable/revoke-tokens/promote success→toast+invalidation with the danger
  consequence callout, and `14001`/`14002` (incl. last-admin demote) surface
  inline verbatim with the dialog kept open.
- **Pure logic + i18n** (`generatePassword`, `dateRange`, `schemas`, `adminI18n`)
  — password rules/clamp/ambiguous-glyph exclusion/uniqueness; range presets /
  all-time / inclusive custom ISO bounds at +07 / inverted-range guard; grant /
  revoke / reset-password Zod rules (+ a localized-message case); `admin`
  namespace vi-VN/en-US parity (key shape, no empty leaf, validation keys, fixed
  Premium/Free terms).

**Harness changes (test-only, additive):** added `resetAdminStore()` to
`test/msw/handlers.ts` (the admin fixture store is a module singleton mutated by
the action handlers — reset in `beforeEach` for per-test isolation), and raised
the RTL async-util timeout to 5000ms in `test/setup.ts` so `findBy*`/`waitFor`
tolerate CPU saturation under full-suite parallelism (one run initially flaked on
network-bound specs at the 1000ms default; deterministic since). No product code
was changed.

**No product bugs found.** The privacy boundary and the reset-password secret
lifecycle both held: no ledger key reaches any admin payload or the DOM, and the
temp password lives only in component state (absent from cache + storage, cleared
on close). No coverage gaps outstanding for the plan's test list.

## Final Outcome

**Complete — the final milestone; the frontend roadmap is done.** M8 shipped the ADMIN-only console (`src/features/admin/`) behind the now-live `AdminRoute` (role from `/auth/me`): a tabbed `AdminLayout` over `/admin/dashboard` (metrics: KPIs + tier/role/status distributions + signups time-series, each paired with a table), `/admin/revenue` (revenue = server SUM of GRANT rows via `Money`, verbatim; buckets + references), `/admin/users` (paged/filterable/sortable, URL-synced), and `/admin/users/:uuid` (metadata + grant history) with the action dialogs: tier grant/revoke (`MoneyInput` + reference/note), disable/enable, revoke-tokens, role promote/demote, and the **reset-password one-time reveal** (client-generated CSPRNG temp password, state-only, never cached/logged, cleared on close). The **privacy boundary (R10)** is enforced three ways — admin-only DTOs, the `assertNoLedgerKeys` DEV tripwire wrapping every admin read, and a dedicated privacy test — no ledger data is fetched or rendered. The **14001 self / 14002 last-admin/any-admin-target** guards are reflected client-side + branched server-side. Also **extracted the shared `src/components/ui/charts` module** (`KpiTile`/`RankedBarChart`/`TimeSeriesBarChart`) + a `Pagination` primitive, and refactored M6 to consume them (M6 tests green). No new dependency. Verified live on :5200 (non-admin→403/1004; all admin flows; the 14001/14002/1001/14000 guards; privacy boundary confirmed on real payloads). Tests +116 (suite 661→777); code review **APPROVE, 0 blocking**. All 7 OQs shipped at recommended (OQ3 = UI-generated temp password, confirmed at checkpoint).

## Future Improvements

- A queryable admin-audit surface if the backend later adds `admin_audit_logs` (disable/enable/reset/
  role are logs-only today, per backend OQ12).
- **`Pagination` default labels are hardcoded vi-VN** (M8 review nit): the admin caller injects localized props, but a future consumer omitting them gets untranslated defaults — consider requiring the label props.
- **Validate admin user-list URL params against the known enums** (M8 review nit): `tier`/`sort` are cast directly from search params (bogus values caught server-side as `1001`); whitelist + fall back to defaults for a cleaner failure mode.
- **`generatePassword` modulo bias** (M8 review nit): negligible for the charset sizes; add a rejection-sampling note if provable uniformity is wanted.
- Optimistic updates for the user-admin actions once the read/refetch cost is felt at scale.
- Route-level lazy-loading of the admin bundle (it is admin-only and self-contained — a good split
  point).
- CSV export of the user list / revenue (if the backend adds an admin export endpoint).
- Once `ui/charts` is extracted, migrate any future dashboards onto it directly.
