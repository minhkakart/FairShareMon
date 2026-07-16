# M1 — App shell, navigation & account/settings

## Objective

Promote the placeholder authenticated shell (`AppShellLayout`) into the real, responsive app shell for
FairShareMonWeb, and ship the account/settings surface plus a minimal home landing. Concretely M1
delivers:

1. **App shell / navigation** — a single **navigation-registration pattern** (one nav config the shell
   renders + guards) covering every roadmap feature area (Members, Categories, Tags, Expenses, Events,
   Stats, Wallet) plus an Admin entry gated on `role === "ADMIN"`. Each entry routes to the existing
   `StubPage` until its milestone replaces it. Active-route highlighting, responsive/mobile-first
   navigation, and keyboard a11y.
2. **Account / settings surface** — a `/settings` page showing the user's profile (username, **tier
   badge**, role, member-since), the **theme** + **language** controls surfaced here, a
   **change-password** affordance (endpoint already wired), logout, and a **tier-status panel** that is
   **informational only** (there is no self-serve upgrade endpoint — Premium is a manual admin grant).
3. **Minimal home** — a welcome landing with quick links into each area. The data-rich dashboard is
   deferred to M6 (OQ4 of the roadmap) — **no charts here**.

No new dependencies, no backend change. This is the first feature milestone on the locked foundation;
it reuses the design-system primitives, the session store, `/auth/me` wiring, i18n, and theme that are
already shipped.

## Background

Grounded in the live SPA code (read 2026-07-17) and the locked docs (`feature-roadmap.md` M1;
`frontend-foundation.md`; `wire-current-user-profile.md`; `CLAUDE.md`):

- **The shell primitive already exists.** `src/components/ui/AppShell/AppShell.tsx` renders a
  landmarked sticky header (`brand · nav · actions`) over a width-constrained `<main>` with a
  skip-to-content link, plus a presentational `NavItem` (with `aria-current`) and `AuthLayout`. It is
  **presentational only** — it has **no mobile/responsive nav affordance** (the `nav` slot renders
  inline in the header regardless of viewport). That gap is the main net-new visual surface in M1.
- **`AppShellLayout`** (`src/routes/AppShellLayout.tsx`) is already wired: it renders `AppShell` with a
  **hardcoded** `NAV_ITEMS` array (`dashboard, members, expenses, events, stats, wallet` — **missing
  categories, tags, and admin**), `NavLink`-based active highlighting, the `LanguageToggle` +
  `ThemeToggle` + account button + logout in `actions`, and a working `doLogout`. The account button
  currently links to `/settings/change-password`.
- **Routes are already registered** in `src/routes/router.tsx`: stub routes for `/members`,
  `/categories`, `/tags`, `/expenses`, `/events`, `/stats`, `/wallet`; `/dashboard` →
  `DashboardPage`; `/settings/change-password` → `ChangePasswordPage`; and the `/admin` sub-tree behind
  `AdminRoute`. M1 does **not** add feature routes — it wires the nav to them and adds `/settings`.
- **Session + profile are wired.** The Zustand store `user` is `{ username, uuid?, tier?, role?,
createdAt? }`; `useCurrentUser()`, `useProfileStatus()`, `useSessionStatus()` selectors exist
  (`src/features/auth/hooks/useAuth.ts`). `/auth/me` populates `user` after login and boot-refresh;
  `AdminRoute` is live off `role === "ADMIN"` with a splash while `profileStatus` is `idle`/`pending`
  and a fail-safe deny on `error`. `UserResponse.role` is `USER` | `ADMIN`; `tier` is a string
  (backend default `FREE`; `PREMIUM` when granted).
- **Design-system primitives to reuse** (`src/components/ui`): `AppShell`/`NavItem`, `Card`/
  `CardHeader`/`CardBody`, `Button`, `Badge` (has dedicated `premium` + `free` tones — icon-optional,
  status not on color alone), `UpgradePrompt` (gold crown affordance, `compact` + `action` props),
  `LimitNotice`, `ThemeToggle`, `LanguageToggle`, `EmptyState`, `Skeleton`, `Alert`, `Dialog` (Radix),
  `Toast`. `formatDate`/`formatDateTime` (`src/i18n/format.ts`) render `createdAt`.
- **i18n** (`src/i18n/index.ts`): namespaces `common`, `auth`, `errors`, `validation`; typed via
  `i18next.d.ts` from the vi-VN catalog. `common:nav.*` already has `members/categories/tags/expenses/
events/stats/wallet/admin/dashboard/changePassword`. Each feature owns its own namespace (the `auth`
  namespace precedent) → M1 adds a `settings` namespace.
- **Business rules for M1** (`The-ideal.md` §3.1, §3.11; §4 R9): the admin nav item is gated on
  `role === "ADMIN"` (fail-safe hide — never fabricate a role); the tier badge is **display-only**
  (reads session `tier`); upgrade is **informational only** — no self-serve endpoint exists (§3.11
  "nâng hạng bằng thanh toán" is open/manual; the roadmap locked it as a manual admin grant, R9 limits
  block only create). M1 consumes **no** write endpoints and **no** new read endpoints.

## Requirements

- **R1** — A single navigation-registration config drives the shell nav; adding a later milestone's
  entry is a one-line change to that config. Admin entries carry a `requiresAdmin` flag and are hidden
  unless `role === "ADMIN"` (fail-safe: absent/unknown role never shows admin).
- **R2** — The shell nav covers every roadmap area: Members, Categories, Tags, Expenses, Events, Stats,
  Wallet, + Admin (gated), plus the home entry. Each points at its existing route (stub for now).
- **R3** — Active-route highlighting (via `NavLink` `isActive` → `NavItem active` + `aria-current`),
  full keyboard operability, and a **responsive/mobile-first** nav (see OQ1).
- **R4** — A `/settings` account page: profile (username, tier badge, role, member-since), theme +
  language controls, a change-password affordance, logout, and the tier-status panel.
- **R5** — Tier-status panel: show Free vs Premium clearly; for Free, an **informational** upgrade
  explanation (Premium is granted manually — no self-serve purchase), reusing `UpgradePrompt` with no
  navigating action. For Premium, a confirmation state (no upgrade prompt).
- **R6** — Minimal home (`/dashboard`): a welcome/greeting + quick-link cards into each area
  (respecting admin visibility). No charts, no data fetching beyond the already-loaded session `user`.
- **R7** — Loading / empty / error via existing primitives (Skeleton for the pending profile label;
  the shell renders immediately per the locked OQ2a — do not gate paint on `/auth/me`).
- **R8** — All copy through i18n (vi-VN default + en-US parity), fixed domain terms; no hardcoded
  strings; `<html lang>` already synced by `LocaleProvider`.
- **R9** — No new dependency; reuse `src/components/ui/*`, the session hooks, and the existing formatters.
- **R10** — Accessibility: semantic landmarks (already in `AppShell`), labeled controls, visible focus,
  color-independent tier/role status (Badge label + icon, not color alone), keyboard-reachable mobile
  nav, `prefers-reduced-motion` honored by any drawer transition.

## Open Questions

> Each option carries a one-line trade-off; the **Recommended** option is the one I would genuinely
> ship. None are CRITICAL — all have a safe default, none is irreversible or security/privacy-sensitive.
> The orchestrator auto-accepts the recommended option.

### OQ1 — Responsive/mobile navigation pattern — Resolved 2026-07-17 (option a)

The `AppShell` primitive renders the nav inline in the header with no mobile affordance, yet the app is
mobile-first and M1 introduces up to ~9 nav entries. How should the nav behave on narrow viewports?

- **(a) Recommended — extend the `AppShell` primitive with a proper mobile menu: below a breakpoint the
  nav collapses behind a labeled hamburger/menu button that opens a slide-in panel (built on the
  already-approved Radix `Dialog`/disclosure — no new dependency), full nav inline on wider viewports.**
  The right mobile-first UX for this many destinations; keyboard + focus-trap come from Radix; the
  visual is a light ui-designer pass. Trade-off: touches the shared `AppShell` primitive (ui-designer
  owns it) and adds a small amount of CSS/state.
- (b) Keep the nav inline and let it wrap / horizontally scroll on small screens (CSS only, no primitive
  change, no drawer). Cheapest; ships fastest. Trade-off: a wrapping/scrolling row of 9 items is a poor
  mobile-first experience and looks unfinished on phones.
- (c) Move primary nav into a persistent left sidebar (desktop) that collapses to a drawer (mobile).
  Most "app-like". Trade-off: a larger layout re-architecture of `AppShell` than M1's "light shell"
  scope warrants; better revisited once the feature areas exist.

### OQ2 — Placement of the theme + language controls — Resolved 2026-07-17 (option a)

They exist today as toggles in the shell header `actions`. The roadmap says "relocated/surfaced" into
settings.

- **(a) Recommended — keep the compact toggles in the header AND surface them in a labeled
  "Preferences" section on `/settings`, both driving the same `useTheme`/`useLocale` controlled
  primitives (no duplicated state).** Header keeps the one-tap convenience; settings gives a
  discoverable, labeled home — the standard pattern. Trade-off: the control appears in two places
  (acceptable; single source of truth, no state divergence).
- (b) Move them exclusively into `/settings`, removing them from the header. Cleaner header. Trade-off:
  a two-tap detour for a frequently-used control (theme/locale switch), and it changes established
  header behavior the auth slice already ships.

### OQ3 — Change-password: link vs embed on the settings page — Resolved 2026-07-17 (option a)

A dedicated `/settings/change-password` route + `ChangePasswordPage` (with its own tests) already ships.

- **(a) Recommended — keep the dedicated `/settings/change-password` route and link to it from a
  "Security" section on `/settings`.** Zero churn to the existing page/tests; a destructive,
  all-devices-logout action deserves its own focused screen; keeps `/settings` compact. Trade-off: one
  extra navigation to change the password.
- (b) Embed the `ChangePasswordPage` form inline in `/settings` and redirect the standalone route to it.
  One-stop settings. Trade-off: churns the existing route/tests, lengthens the settings page, and mixes
  a heavyweight destructive form into the account overview.

### OQ4 — Nav treatment of Categories & Tags (and overall nav density) — Resolved 2026-07-17 (option a)

Categories and Tags are one combined milestone (M3) but two distinct routes/stubs.

- **(a) Recommended — two separate top-level nav entries (Danh mục / Categories, Nhãn / Tags), each to
  its own route.** Simplest, matches the distinct routes, no grouping UI to build now; the mobile menu
  (OQ1a) absorbs the density. Trade-off: two of ~9 flat entries are closely related reference data.
- (b) A single grouped "Danh mục & Nhãn" entry (or a submenu). Tidier top level. Trade-off: needs a
  submenu/group affordance that neither the `AppShell` nor `NavItem` primitive has yet — extra design
  for little M1 gain; better deferred until nav density is a real problem.

## Assumptions

- **Tier values** are the strings `FREE` / `PREMIUM` (backend default `FREE`). The UI branches on a
  case-insensitive compare (`tier?.toUpperCase() === "PREMIUM"`) so casing drift never mislabels a user;
  an absent/unknown tier renders as Free (fail-safe, non-privileged).
- **Settings i18n** follows the established per-feature-namespace convention (the `auth` namespace
  precedent): M1 adds a `settings` namespace registered in `src/i18n/index.ts` + `NAMESPACES` (typed
  automatically via `i18next.d.ts`). This is a locked convention, not an open question.
- **Member-since** renders with `formatDate` (date only, viewer's timezone) from `user.createdAt`; if
  `createdAt` is absent (pre-`/auth/me`), the field shows a Skeleton then the value.
- **The account button target** moves from `/settings/change-password` to `/settings` (the account
  page becomes the settings home; change-password is reached from within it per OQ3a).
- **`/dashboard` stays the home route** (`/` already redirects to it); M1 enriches `DashboardPage`
  in-place rather than adding a new `/home` route (avoids churning the router + redirect + tests).
- The shell renders immediately after the token is valid (locked OQ2a from `wire-current-user`); the
  account label / tier badge show a Skeleton until `/auth/me` resolves. M1 adds no gating.
- No new backend endpoint, no new error code, no new dependency; all data comes from the already-loaded
  session `user`.

## Implementation Plan

> Paths under `FairShareMonWeb/`. Concrete files assume the recommended OQ options (OQ1a · OQ2a · OQ3a ·
> OQ4a). All user-facing copy through i18n (vi-VN default).

### Step 1 — Navigation-registration pattern (R1, R2)

1. New `src/routes/navConfig.tsx` — the single source of truth for the primary nav:
   ```ts
   export interface NavEntry {
     to: string; // route path
     labelKey: TFuncKey; // e.g. "common:nav.members"
     requiresAdmin?: boolean; // admin-only entries (fail-safe hidden)
     end?: boolean; // NavLink exact-match for index-like routes
     // icon?: ReactNode;        // reserved — ui-designer may add per-entry icons
   }
   export const NAV_ENTRIES: readonly NavEntry[] = [
     { to: "/dashboard", labelKey: "common:nav.dashboard", end: true },
     { to: "/members", labelKey: "common:nav.members" },
     { to: "/categories", labelKey: "common:nav.categories" },
     { to: "/tags", labelKey: "common:nav.tags" },
     { to: "/expenses", labelKey: "common:nav.expenses" },
     { to: "/events", labelKey: "common:nav.events" },
     { to: "/stats", labelKey: "common:nav.stats" },
     { to: "/wallet", labelKey: "common:nav.wallet" },
     { to: "/admin", labelKey: "common:nav.admin", requiresAdmin: true },
   ];
   ```
   Later milestones do nothing but keep their entry here (already present) — the "registration" is this
   one array. Documented at the top of the file so each milestone's implementer knows where to look.
2. Export a small `useNavEntries()` helper (in `navConfig.tsx` or `AppShellLayout`) that reads
   `useCurrentUser()`/`useProfileStatus()` and filters out `requiresAdmin` entries unless
   `role === "ADMIN"` (fail-safe: while `profileStatus !== "resolved"` or role unknown, admin entries
   are hidden — mirrors `AdminRoute`).

### Step 2 — Rewire `AppShellLayout` to the config (R2, R3)

1. `src/routes/AppShellLayout.tsx` — replace the hardcoded `NAV_ITEMS` with `useNavEntries()`; map each
   `NavEntry` to `<NavLink to={entry.to} end={entry.end}>{({isActive}) => <NavItem
active={isActive}>{t(entry.labelKey)}</NavItem>}</NavLink>`. Behavior (active highlight, logout,
   toggles) unchanged.
2. Change the account button `Link` target from `/settings/change-password` to `/settings`; keep the
   label `user?.username ?? t("common:account")` and add an optional small tier indicator
   (a `Badge tone="premium"` crown) beside it only when Premium — light, may be deferred to the
   ui-designer pass. Show a `Skeleton` for the label while `profileStatus` is `pending` (R7).

### Step 3 — Responsive mobile nav (R3, R10, OQ1a) — ui-designer + implementer

1. `src/components/ui/AppShell/AppShell.tsx` (+ `AppShell.module.css`) — **ui-designer** extends the
   primitive: above a breakpoint the `nav` slot renders inline (today's behavior); below it, the nav
   collapses behind a labeled menu button that opens a Radix `Dialog`-backed slide-in panel containing
   the same nav nodes. New optional props: `mobileMenuLabel?: string`, `mobileMenuCloseLabel?: string`.
   Focus-trap, Escape-to-close, and `aria-expanded` come from Radix; honor `prefers-reduced-motion` on
   the slide transition. No new dependency (`@radix-ui/react-dialog` already present).
2. `AppShellLayout` passes the localized labels; the same `NavLink` nodes render in both the inline nav
   and the mobile panel (closing the panel on navigation).

### Step 4 — Settings feature (`/settings`) (R4, R5)

1. New route in `src/routes/router.tsx`: nest under `settings` —
   ```tsx
   { path: "settings", children: [
       { index: true, element: <SettingsPage /> },
       { path: "change-password", element: <ChangePasswordPage /> },
   ]}
   ```
   (replaces the current flat `settings/change-password`). `ChangePasswordPage` unchanged.
2. New `src/features/settings/pages/SettingsPage.tsx` composing (top → bottom) `Card`-based sections:
   - **ProfileCard** (`src/features/settings/components/ProfileCard.tsx`) — username, `TierBadge`,
     role label (a subtle `Badge` — shown for all; only ADMIN differs visibly), member-since
     (`formatDate(user.createdAt)`); Skeleton fields while `profileStatus === "pending"`.
   - **PreferencesCard** (`.../PreferencesCard.tsx`) — labeled Theme + Language controls reusing
     `ThemeToggle` + `LanguageToggle` wired to `useTheme`/`useLocale` (OQ2a; same controlled
     primitives as the header, no duplicated state).
   - **SecurityCard** (`.../SecurityCard.tsx`) — a `Link` `Button` to `/settings/change-password`
     with a one-line explanation that changing the password signs out all devices (OQ3a).
   - **TierStatusPanel** (`.../TierStatusPanel.tsx`) — see Step 5.
   - **A logout action** (reuse the shell's `doLogout` logic; extract a tiny `useLogoutAction()` hook
     into `src/features/auth/hooks/` so both the shell and settings share one implementation rather
     than duplicating the mutate → clear → toast → navigate sequence).
3. New `src/features/settings/components/TierBadge.tsx` — `<Badge tone={isPremium ? "premium" :
"free"} icon={isPremium ? <CrownGlyph/> : undefined}>{t(isPremium ? "settings:tier.premium" :
"settings:tier.free")}</Badge>`. Reusable by later milestones (and optionally the shell account
   button).

### Step 5 — Tier-status panel (informational only) (R5)

1. `TierStatusPanel` branches on `isPremium`:
   - **Free** → `<UpgradePrompt title={t("settings:tier.upgradeTitle")}
description={t("settings:tier.upgradeInfo")} />` with **no navigating action** (informational).
     The description explains Premium unlocks wallet + QR + extra exports and that upgrading is done by
     contacting the operator / a manual grant (no self-serve purchase). Optionally list what Premium
     adds (`The-ideal.md` §3.11 "mở rộng").
   - **Premium** → a confirmation state (a `Card`/`Alert` "Bạn đang dùng Premium" with the crown), no
     upgrade prompt.
2. This is the M1 slice of the dissolved Tiers milestone (roadmap OQ2a). It renders no `13xxx` codes
   (those surface at create-time in M2/M4/M5/M7); it only reflects the session `tier`.

### Step 6 — Minimal home (R6)

1. `src/features/dashboard/pages/DashboardPage.tsx` — enrich in place: a welcome/greeting header
   (`t("common:home.welcome", { name: user?.username })`) + a responsive grid of quick-link `Card`s,
   one per nav area (reuse `NAV_ENTRIES` filtered the same way as the shell, so admin's tile appears
   only for admins). Each card = area label + one-line description + link. No charts, no queries.
2. Keep the route/label as `common:nav.dashboard` ("Tổng quan"/"Overview"); the rich dashboard replaces
   this content in M6.

### Step 7 — i18n (R8)

1. New `src/i18n/locales/vi-VN/settings.json` + `en-US/settings.json`; register in
   `src/i18n/index.ts` (imports + `resources` + `NAMESPACES`). Keys:
   - `settings:title`, `settings:profile.title`, `settings:profile.username`,
     `settings:profile.role`, `settings:profile.roleUser`, `settings:profile.roleAdmin`,
     `settings:profile.memberSince`,
   - `settings:preferences.title`, `settings:preferences.theme`, `settings:preferences.language`,
   - `settings:security.title`, `settings:security.changePassword`, `settings:security.changePasswordHint`,
   - `settings:tier.title`, `settings:tier.free`, `settings:tier.premium`,
     `settings:tier.currentIsPremium`, `settings:tier.upgradeTitle`, `settings:tier.upgradeInfo`,
     `settings:tier.premiumPerks`,
   - `settings:logout`.
2. Add to `common.json` (both locales): `nav.settings` ("Cài đặt" / "Settings"),
   `home.welcome` ("Chào {{name}}" / "Welcome, {{name}}"), `home.subtitle`,
   `home.quickLinks.<area>` descriptions (or reuse `nav.*` labels + a generic `home.open` CTA),
   `nav.menu` ("Menu" — mobile menu button label), `nav.closeMenu`.

### Step 8 — Tests + verification (R7–R10)

See "Tests the web-test-engineer should write" below. Then run `pnpm lint`, `tsc -b`, `pnpm build`,
`pnpm test`; exercise the shell (nav highlight, mobile menu, admin visibility), `/settings`, and the
home quick links.

### API endpoints consumed this cycle (verb + path + DTO)

**None new.** M1 reads only the already-loaded session `user` (populated by the previously-wired
`GET /api/v1/auth/me` → `UserResponse { uuid, username, tier, role, createdAt }`). No writes. The
`ApiResult<T>` envelope / `error.code` handling is not exercised by M1 (no new requests); logout reuses
the existing `useLogout` mutation + its established error handling (best-effort revoke, clear locally
regardless).

### Loading / empty / error states

- **Loading** — `profileStatus === "pending"`: `Skeleton` for the account label (shell) and the profile
  fields (settings ProfileCard). The shell chrome and nav render immediately (OQ2a).
- **Degraded** (`profileStatus === "error"`, non-401 `/auth/me` failure): account label falls back to
  `common:account`; the ProfileCard shows a neutral "profile unavailable" `Alert` with the values that
  are known (username may be the optimistic login value); tier badge falls back to Free (non-privileged);
  admin nav hidden (fail-safe). No new error copy required beyond an optional `settings:profile.unavailable`.
- **Empty** — n/a (no lists in M1).
- **Error** — logout mutation failure is swallowed (clear locally + toast success), unchanged from the
  existing shell behavior.

### Form validation rules

None — M1 adds no forms (change-password is the existing page, unchanged; its validators mirror the
backend rules already).

### Accessibility requirements

- Nav: `NavItem` already sets `aria-current="page"` on the active entry; keep it. Mobile menu button
  is a labeled `<button>` with `aria-expanded`/`aria-controls` (Radix), Escape-closes, focus-trapped,
  focus returns to the trigger on close; transition honors `prefers-reduced-motion`.
- Tier/role status is conveyed by Badge **label text + icon**, never color alone (R10).
- Settings controls are labeled (`ThemeToggle`/`LanguageToggle` take `groupLabel`); the change-password
  link is a real link/button with discernible text.
- Home quick-link cards are keyboard-focusable links with accessible names (area label, not "open").
- `<html lang>` stays synced by `LocaleProvider` (existing).

### Tests the web-test-engineer should write (Vitest + RTL + MSW, deterministic — pinned TZ + locale)

- **navConfig / `useNavEntries`** — returns all non-admin entries for a `USER`; includes the admin
  entry only when `role === "ADMIN"`; admin entry hidden while `profileStatus` is `pending`/`error`
  and for unknown role (fail-safe).
- **AppShellLayout nav** — renders a nav item per visible entry; the entry matching the current route
  gets `aria-current="page"`; clicking an entry navigates; the account button links to `/settings`;
  the label shows the username once resolved and a Skeleton while pending.
- **Admin visibility (interaction)** — an ADMIN session shows the Admin nav item and can reach `/admin`;
  a USER session does not render the Admin item.
- **Mobile nav (OQ1a)** — below the breakpoint the menu button is present, opening it reveals the nav
  panel, it is keyboard-operable (Enter/Space to open, Escape to close, focus trapped, focus returns to
  trigger), navigating closes it; above the breakpoint the inline nav renders and no menu button shows.
- **SettingsPage** — renders username, member-since (deterministic under the pinned TZ), and the tier
  badge; a `FREE` session shows the informational `UpgradePrompt` with **no** navigating action; a
  `PREMIUM` session shows the confirmation state and **no** upgrade prompt; the Security section links
  to `/settings/change-password`; theme + language controls are present and change the app state.
- **Tier normalization** — lower/mixed-case `tier` ("premium") still renders as Premium; absent/unknown
  tier renders as Free.
- **Home** — welcome greeting includes the username; a quick-link card renders per visible area; the
  admin card appears only for admins; no chart/query is issued.
- **i18n parity** — settings + new common keys resolve in both vi-VN and en-US; toggling locale in
  settings switches copy (and the client `Accept-Language`, via the existing LocaleProvider).
- **Logout** — from both the shell and the settings logout action, `useLogoutAction` clears the session
  - query cache and navigates to `/login` (shared implementation, one behavior).

## Impact Analysis

- **APIs / Database / Services:** none — M1 consumes only the already-loaded session `user`; no new
  request, no backend change, no new error code.
- **Frontend — new:** `src/routes/navConfig.tsx`; `src/features/settings/pages/SettingsPage.tsx` +
  `components/{ProfileCard,PreferencesCard,SecurityCard,TierStatusPanel,TierBadge}.tsx`; a shared
  `useLogoutAction` hook (`src/features/auth/hooks/`); `src/i18n/locales/{vi-VN,en-US}/settings.json`.
- **Frontend — edited:** `src/routes/AppShellLayout.tsx` (consume `navConfig`, account button →
  `/settings`, pending-label Skeleton, mobile-menu labels); `src/routes/router.tsx` (nest `/settings`);
  `src/components/ui/AppShell/AppShell.tsx` + `AppShell.module.css` (**ui-designer** — mobile menu);
  `src/features/dashboard/pages/DashboardPage.tsx` (home content); `src/i18n/index.ts` (register
  `settings` namespace); `src/i18n/locales/{vi-VN,en-US}/common.json` (new nav/home/menu keys).
- **Design system:** the `AppShell` primitive gains a responsive mobile-menu affordance (the one
  materially new visual surface); everything else reuses existing primitives (Card, Badge, UpgradePrompt,
  toggles). No new dependency.
- **Documentation:** this doc; roadmap Progress Log entry when M1 closes.
- **Downstream:** every later milestone (M2–M8) registers its nav entry in `navConfig` (already present
  as stubs) and hangs its screens on this shell; the settings tier surface is the M1 slice of the
  dissolved Tiers milestone (roadmap OQ2a).

## Decision Log

### Decision

M1 establishes a single `navConfig`-driven navigation-registration pattern, promotes `AppShellLayout`
onto it, adds a `/settings` account surface (profile + tier-status + preferences + change-password link

- logout), and enriches the existing `/dashboard` into a minimal home — all on the locked foundation
  with no new dependency and no new API call.

### Reason

The shell primitive, session/`/auth/me` wiring, guards, i18n, theme, and every feature route (as stubs)
already exist; M1's job is to connect them behind one nav registry and give the tier badge + account
controls a home, deferring the data-rich dashboard to M6 (roadmap OQ4) and folding the informational
tier surface in here (roadmap OQ2a). Keeping the change-password route and enriching `/dashboard`
in-place minimizes churn to shipped, tested code.

### Alternatives Considered

- Building the rich dashboard now — rejected (roadmap OQ4a defers it to M6; no data yet).
- A left-sidebar layout re-architecture (OQ1c) — rejected for M1's "light shell" scope.
- Embedding change-password into the settings page (OQ3b) — rejected; churns tested code for little gain.

## Progress Log

### 2026-07-17

- Feature-planner: required reading completed — `feature-roadmap.md` (M1 locked scope + roadmap OQ
  resolutions), `frontend-foundation.md` (locked stack + primitives), `wire-current-user-profile.md`
  (session `user`, `/auth/me`, `AdminRoute`, `profileStatus`), `CLAUDE.md`, `The-ideal.md` §3.1/§3.11/§4,
  `AuthController.cs` (`/auth/me`, change-password), `UserResponse.cs`; and the live SPA code:
  `router.tsx`, `AppShellLayout.tsx`, `StubPage.tsx`, `AppShell.tsx`, `Premium.tsx`, `Badge.tsx`,
  `useAuth.ts`, `session.ts`, `ChangePasswordPage.tsx`, `DashboardPage.tsx`, `AdminPage.tsx`,
  `i18n/index.ts` + `i18next.d.ts` + `common.json` (vi-VN/en-US).
- Drafted this plan: the `navConfig` registration pattern, the shell rewire + responsive mobile menu,
  the `/settings` composition (profile · preferences · security · tier-status · logout), the minimal
  home, i18n keys (new `settings` namespace + common additions), a11y, and the test matrix.
- **4 Open Questions raised** (responsive nav pattern; theme/language control placement;
  change-password link vs embed; Categories & Tags nav treatment), each with a recommended option.
  None CRITICAL. Awaiting the checkpoint (orchestrator auto-accepts recommendations).

### 2026-07-17 — ui-designer (light design pass, OQ1a/2a/3a/4a accepted)

Added the few net-new visual surfaces M1 needs, all on the existing tokens/primitives,
no new dependency. `tsc -b`, `pnpm lint`, `pnpm build` all clean.

- **Responsive nav on the `AppShell` primitive** (`src/components/ui/AppShell/AppShell.tsx`
  - `.module.css`): mobile-first. Below **64rem** the inline nav is hidden and a labeled
    hamburger opens a Radix-`Dialog`-backed slide-in drawer holding the **same** `nav` nodes;
    at/above 64rem the inline nav shows, button hidden. Radix supplies focus trap, Escape,
    focus restore, `aria-modal`, and the trigger's `aria-expanded`/`aria-controls`. The drawer
    closes when a nav entry is activated (pointer or keyboard). Slide honors reduced-motion.
    New optional props: `mobileMenuLabel`, `mobileMenuCloseLabel`, `navLabel` (localized by the
    implementer). `active`/`aria-current` styling carries into the drawer unchanged.
- **`TierBadge`** primitive (`src/components/ui/TierBadge/TierBadge.tsx`): display-only; wraps
  `Badge` (gold `premium` + crown vs neutral `free`), normalizes `tier` case-insensitively,
  absent/unknown → Free. Labels passed in. Color-independent (text + crown glyph).
- **`UpgradePrompt` variants** (`src/components/ui/Premium/*`): new `variant` prop — `cta`
  (default, unchanged gold CTA), `info` (informational Free state, no navigating action —
  "Premium is granted manually"), `active` (subtle check-marked "Premium active" confirmation).
  Back-compatible: existing callers default to `cta`.
- **Page-layout primitives** (`src/components/ui/Layout/Layout.tsx` + `.module.css`):
  `PageHeader` (page `<h1>` + description + actions), `Stack` (token-gap vertical rhythm), and
  `DescriptionList`/`DescriptionRow` (semantic `dl` for profile rows). The `/settings` page =
  `PageHeader` + a `Stack` of the implementer's `Card`s; ProfileCard rows use `DescriptionList`.
- Barrel (`src/components/ui/index.ts`), living style guide (`src/styles/StyleGuide.tsx`), and
  design-system docs (`src/styles/README.md`) updated with the new surfaces.

**Handoff notes (non-blocking):** (1) the shell owns the drawer open-state internally — the
implementer supplies the same `nav` NavLink nodes once via the `nav` prop and localized menu
labels; no drawer state to wire. (2) On phones the header `actions` (two toggles + account +
logout) sit alongside the new hamburger; if that reads crowded once wired, consider moving some
actions into the drawer in a later pass — out of scope for this light pass.

### 2026-07-17 — web-implementer (M1 built)

OQ1–OQ4 accepted at the recommended option (a) by the orchestrator; headings marked Resolved above.
Implemented the milestone on the ui-designer's primitives, no new dependency, no new API call.

- **Nav-registration pattern** — new `src/routes/navConfig.tsx`: a typed `NAV_ENTRIES` array (one
  entry per area: dashboard/members/categories/tags/expenses/events/stats/wallet + admin with
  `requiresAdmin`) and a `useNavEntries()` hook that filters admin entries unless the profile has
  resolved to `role === "ADMIN"` (fail-safe: pending/error/unknown role hides admin, mirroring
  `AdminRoute`). Keys are bare `common`-namespace keys; a new `AppCommonKey` type (`src/i18n/useT.ts`,
  `ParseKeys<"common">`) types `labelKey`/`descriptionKey` so `t(...)` accepts them directly.
- **Shell rewire** — `src/routes/AppShellLayout.tsx` now maps `useNavEntries()` to
  `NavLink`+`NavItem` (render-prop `isActive` → `active`/`aria-current`), passes the localized
  `navLabel`/`mobileMenuLabel`/`mobileMenuCloseLabel` to the shell, and retargets the account button
  from `/settings/change-password` to `/settings`. Logout now goes through the shared hook.
- **Shared logout** — `src/features/auth/hooks/useLogoutAction.ts` de-dupes the
  revoke → clear session → clear query cache → toast → navigate("/login") sequence for the shell and
  settings.
- **Settings feature** — `src/features/settings/pages/SettingsPage.tsx` (`PageHeader` + `Stack` of
  Cards + logout action) composing `components/{ProfileCard,PreferencesCard,SecurityCard,
TierStatusPanel}.tsx`. ProfileCard uses `DescriptionList` + `TierBadge` + a role `Badge`
  (User/Administrator) + `formatDate(createdAt)`, with Skeletons while `profileStatus === "pending"`
  and a degraded `Alert` on `error`. TierStatusPanel: Free → `UpgradePrompt variant="info"` (no
  action, "Premium granted manually") + perks line; Premium → `variant="active"` confirmation.
  PreferencesCard reuses the same controlled `ThemeToggle`/`LanguageToggle` as the header (OQ2a).
  SecurityCard links to the unchanged `/settings/change-password` (OQ3a).
- **Router** — `/settings` nested: index → `SettingsPage`, `change-password` → `ChangePasswordPage`
  (unchanged).
- **Minimal home** — `src/features/dashboard/pages/DashboardPage.tsx` enriched: welcome greeting
  (`common:home.welcome`) + a responsive grid of quick-link Cards from `useNavEntries()` (dashboard
  tile excluded; admin tile only for admins). No charts, no queries.
- **i18n** — new `settings` namespace (vi-VN + en-US), registered in `src/i18n/index.ts` +
  `APP_NAMESPACES`; `common.json` gains `nav.settings/menu/closeMenu/primary` and a `home.*` block
  (welcome/subtitle/open/quickLinks.*) in both locales.

**Deviation from the plan (recorded):** R7 / Assumption line 166 ask the shell account-button label to
show a **Skeleton** while `profileStatus === "pending"`. That directly contradicts a deliberate, shipped
foundation decision — `currentUserProfile.test.tsx`'s
`BootRehydrate_TokensOnlyThenMe_RestoresRealUsernameLabel` asserts that during boot-rehydrate
(pending, `user === null`) the button shows the neutral `common:account` fallback ("foundation nit #3"),
then the real username after `/auth/me`. Rather than silently break that tested foundation behavior, the
shell keeps the `user?.username ?? t("common:account")` fallback (no Skeleton). The Skeleton loading
state is still delivered where there is no conflict — the settings **ProfileCard** fields Skeleton while
pending. Net effect: the shell never regresses the foundation flow; the settings page carries the loading
affordance. If the orchestrator prefers the shell skeleton, the foundation test would need to be revised
by its owner first.

**Verification.** `pnpm lint` clean (only pre-existing fast-refresh warnings), `tsc -b` clean,
`pnpm build` succeeds, existing suite green (98/98). No browser driver was available in the environment
(no chromium-cli/Playwright), so the feature was exercised via a throwaway Vitest+RTL smoke (deleted
after — the committed test suite is the web-test-engineer's) that drove the REAL components against the
session store + i18n and confirmed: USER nav shows every area with admin hidden and the account button
linking to `/settings`; ADMIN nav shows the admin entry; active-route `aria-current`; the mobile drawer
(Radix Dialog) opens with the same nav nodes and closes on navigation; `/settings` renders profile
(username, Free/Premium `TierBadge`, User/Admin role, `formatDate` member-since), the Free
informational upgrade prompt (no action) vs the Premium active confirmation, case-insensitive tier
normalization (`"premium"` → Premium), and the change-password link; the home welcome greeting +
role-filtered quick-link cards; and vi-VN ⇄ en-US copy switching. The `<64rem` drawer _collapse_ is
CSS/media-query-driven (built + verified by the ui-designer) and not observable in jsdom — not exercised
live here for want of a browser.

### 2026-07-17 — web-test-engineer (M1 tests)

Added 39 Vitest + RTL tests across 4 new spec files, all deterministic (pinned TZ
`Asia/Ho_Chi_Minh` from `setup.ts`, per-test pinned locale). Full suite: **137/137
passing** (was 98), twice; `pnpm lint` clean (only the pre-existing fast-refresh
warnings in product files); `tsc -b` clean. No product code changed — components
were driven directly against the session store + i18n/theme providers (M1 issues
no new API call, so most specs need no network; the two logout paths hit the MSW
`/auth/logout` boundary).

- `src/routes/navConfig.test.tsx` (6) — registry shape + `useNavEntries` role
  filter: every roadmap area for a USER with admin hidden; admin included only when
  the profile RESOLVED to `role === "ADMIN"`; fail-safe hide on pending / error /
  unknown role.
- `src/routes/appShellLayout.test.tsx` (13) — nav renders one item per visible
  entry (admin hidden for USER, shown + reachable for ADMIN), active-route
  `aria-current="page"`, click-to-navigate, account button → `/settings`, resolved
  username label, pending neutral-fallback (honors the locked deviation — no
  Skeleton), mobile drawer open/close (pointer + keyboard Enter/Escape, focus
  restore to trigger, close-on-nav-activation), and logout clears session +
  redirects. **jsdom caveat recorded in the file:** the inline nav is
  `display:none` until the 64rem media query (which jsdom does not apply), so the
  nav registration/active-state/navigation assertions run through the
  jsdom-observable mobile drawer, per the plan's "assert the drawer behavior, not
  the breakpoint" note.
- `src/features/settings/settingsPage.test.tsx` (16) — ProfileCard username/role/
  member-since (`formatDate`, incl. a timezone-boundary case proving +07 rendering)
  + pending Skeletons + degraded Alert; TierBadge Free/Premium with case-insensitive
  normalization and absent/unknown → Free; TierStatusPanel informational
  UpgradePrompt with NO navigating action (Free/unknown) vs the active confirmation
  (Premium); SecurityCard → `/settings/change-password`; Preferences theme
  (`[data-theme]`) + language (copy switch) controls; en-US parity + in-page locale
  switch; logout clears session + redirects.
- `src/features/dashboard/dashboardPage.test.tsx` (6) — welcome greeting with
  username (+ generic fallback), one quick-link card per visible area (dashboard
  tile excluded, admin tile admin-only), card links target their routes, no charts,
  and en-US greeting/cards parity.

Extra edge cases added beyond the plan's checklist: unknown-tier → Free fail-safe
(TierStatusPanel), profile pending-Skeleton and degraded-Alert states on
ProfileCard, admin-role-label on ProfileCard, and the generic-greeting branch on
the home.

No product bugs surfaced. Coverage gap (documented, not closable in jsdom): the
`<64rem` CSS media-query collapse of the inline nav vs. the drawer is not
observable in jsdom — deferred to future Playwright E2E (already noted under Future
Improvements). Because the inline nav is `display:none` in jsdom, the inline-nav
active-highlight styling is only asserted via the drawer's shared NavLink nodes.

## Final Outcome

**Complete.** M1 delivered the authenticated shell on a single nav-registration pattern (`navConfig.tsx` → `useNavEntries`, admin fail-safe filtered), the `/settings` account page (profile + `TierBadge` + relocated theme/language + change-password link + informational tier-status panel), a shared `useLogoutAction`, and a minimal home with role-filtered quick links. Responsive mobile drawer added to the `AppShell` primitive (Radix Dialog). No new dependencies, no new API calls (reads the session `user`). Design pass added `TierBadge`, `UpgradePrompt` info/active variants, and `PageHeader`/`Stack`/`DescriptionList` layout primitives. Tests +39 (suite 98→137, deterministic); code review **APPROVE, 0 blocking**. All 4 OQs shipped at option (a).

## Future Improvements

- A left-sidebar shell (OQ1c) once the feature areas exist and the top nav feels crowded.
- Grouped/submenu nav (OQ4b) if nav density grows (e.g. a "Reference data" group for Categories & Tags).
- **Design-system `LinkButton` / `Button asChild`** (review nit): today `<Link><Button>` nests interactive elements (pre-existing pattern, now at a few sites); add a Radix `Slot`-based `asChild` or a `LinkButton` primitive so link-styled-as-button renders a single `<a>`, then convert call sites. Do before it propagates through later milestones.
- Keep repo-wide Prettier reformatting out of feature commits (isolate format-only sweeps) — a review nit from M1.
- Surface the tier badge in the shell header/account button (a small crown for Premium) once the tier
  UX is exercised across M2/M4/M5/M7 — the end-of-work tier-UX consistency sweep (roadmap) can align it.
- A self-service "your account" surface (display name, preferences) if the backend adds `PATCH
/users/me` (noted as a backend future improvement in `wire-current-user-profile.md`).
- Invalidate `["auth","me"]` from the settings tier panel once/if a self-serve upgrade flow ever exists
  (the invalidation seam already exists — `invalidateCurrentUser()`).
- E2E (Playwright) coverage of the shell + settings once the feature areas land.
