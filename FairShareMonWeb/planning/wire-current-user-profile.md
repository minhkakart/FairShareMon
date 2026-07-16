# Wire current-user profile (`/auth/me`) + activate the admin guard

## Objective

The backend shipped the current-user source the foundation cycle was blocked on
(`FairShareMonApi/planning/expose-current-user-profile-and-role.md`, Final Outcome —
committed on origin/master): `UserResponse` now carries **`role`** and there is a
guarded **`GET api/v1/auth/me`** → `ApiResult<UserResponse>` returning the caller's
own `{ uuid, username, tier, role, createdAt }` from a live DB read.

This cycle wires that source into the SPA so the session `user` is populated with
the real identity/authorization after **both** entry paths, and the admin guard and
account label stop running on placeholder data:

1. Fetch `/auth/me` after a successful **login** and after a successful **boot-refresh
   rehydrate**, populating the Zustand session `user` (`uuid`/`username`/`tier`/`role`).
2. **Activate `AdminRoute`** off the now-present `role` (today it is a fail-safe
   deny-all seam — `user?.role` is always `undefined`).
3. Fix the **reload account-label nit** (foundation close-checkpoint nit #3): after a
   reload the shell shows the generic `common:account` fallback because boot rehydrate
   (`/auth/refresh`) returns tokens only; `/auth/me` restores the real username/identity.
4. Handle the `/auth/me` failure and edge paths (token valid but the profile fetch
   fails; anonymous/revoked → the existing refresh flow).

No new dependencies. Reuse the centralized API client, the Zustand session store, and
TanStack Query already locked by `frontend-foundation.md`.

## Background

Grounded in the live SPA code (read 2026-07-16):

- **Session store** (`src/lib/auth/session.ts`): the vanilla Zustand store is the
  source of truth the guards + shell read. `SessionUser = { username, uuid?, tier?,
  role?, createdAt? }`. It already exposes `setSession(tokens, user?)`,
  **`setUser(user)`** (attach/replace the user payload without touching tokens),
  `clearSession()`, and `markUnauthenticated()`. `status` is `idle` (boot, pre-rehydrate)
  → `authenticated` | `unauthenticated`.
- **Login** (`src/features/auth/pages/LoginPage.tsx`) currently does
  `getSession().setSession(tokens, { username })` — only the typed username; `uuid`,
  `tier`, `role` stay `undefined`.
- **Boot rehydrate** (`src/app/useSessionBootstrap.ts` → `refreshOnce()` in
  `src/lib/api/refresh.ts`) calls `setSession(tokens)` with **no** user, so after a
  reload `user` is `null` → the shell label falls back to `common:account` ("Tài khoản").
- **`AdminRoute`** (`src/routes/AdminRoute.tsx`) reads `user?.role === "ADMIN"` and
  renders `<Forbidden />` otherwise — correct once a role source exists, but always
  denying today.
- **`AppShellLayout`** (`src/routes/AppShellLayout.tsx`) renders the account button as
  `user?.username ?? t("common:account")`.
- **API client** (`src/lib/api/client.ts`): `api.get<T>(path)` unwraps `ApiResult<T>`;
  a `401` on an authenticated request triggers the de-duped `401 → refresh-once →
  retry → else clear + redirect` flow already. `/auth/me` is a guarded GET, so it
  rides that flow for free.
- **Auth API surface** (`src/features/auth/api/authApi.ts`): `register`/`login`
  (anonymous), `logout`, `changePassword` — **no `me` yet**.
- **Auth hooks** (`src/features/auth/hooks/useAuth.ts`): `useLogin/useRegister/
  useLogout/useChangePassword` mutations + `useCurrentUser()`/`useSessionStatus()`/
  `useIsAuthenticated()` selectors over the store.
- **DTO type** (`src/lib/api/types/auth.ts`): `UserResponse` already declares an
  optional `role?: string` (added as the seam placeholder). The backend now populates
  it — the field can become non-optional.
- **Query client** (`src/lib/query/queryClient.ts`): no `4xx` retry (client owns the
  refresh); one retry only for genuine network errors.
- **Logout** (`AppShellLayout.doLogout`) already calls `queryClient.clear()` +
  `getSession().clearSession()`. **Change-password** clears the session and forces
  re-login. Both naturally drop any cached current-user.
- **Backend contract** (`expose-current-user-profile-and-role.md`): `GET
  api/v1/auth/me` guarded → `ApiResult<UserResponse>`; anonymous/revoked → **401
  `1002`**; live tier/role. Login/refresh `TokenPairResponse` contracts untouched.

## Requirements

- **R1** — Add an `authApi.me()` call: `GET /v1/auth/me` → `UserResponse`, through the
  centralized client (authenticated; rides the existing `401 → refresh` flow).
- **R2** — After a successful **login**, populate the session `user` with the full
  `/auth/me` profile (`uuid`/`username`/`tier`/`role`/`createdAt`).
- **R3** — After a successful **boot-refresh rehydrate**, populate the session `user`
  from `/auth/me` so a reload restores the real identity (fixes nit #3).
- **R4** — **Activate `AdminRoute`**: admit `role === "ADMIN"`, deny otherwise, with a
  correct behavior while the profile is still loading (must not flash `Forbidden` at an
  admin who deep-links to `/admin`).
- **R5** — Handle `/auth/me` edge paths: (a) `401`/revoked → the client's refresh flow
  (terminal on failure → clear + login); (b) a **non-401** failure (network/500) while
  the tokens are still valid — defined, non-destructive behavior (see OQ3).
- **R6** — Fail-safe role preserved: an absent/unknown `role` never yields ADMIN
  (mirrors the backend M11 invariant).
- **R7** — No new dependency; reuse the client, session store, and TanStack Query.
  Branch on the numeric `error.code`, never message text; all copy through i18n.
- **R8** — Code-adjacent seam cleanup (implementer to-dos, tracked here): drop the
  "Auth-guard seam (flagged)" note in `FairShareMonWeb/CLAUDE.md` and the seam comments
  in `src/lib/api/types/auth.ts`, `src/lib/auth/session.ts`, `src/routes/AdminRoute.tsx`
  once wired.

## Open Questions

> Each option lists a one-line trade-off; the **Recommended** option is marked. These
> are preference-dependent and go to the user at the checkpoint. Nothing is silently
> defaulted.
>
> **ALL 5 RESOLVED 2026-07-16 — every question answered at the recommended option (a):
> OQ1a · OQ2a · OQ3a · OQ4a · OQ5a. Implemented as specified below.**

### OQ1 — Where is the `/auth/me` fetch orchestrated? — **Resolved 2026-07-16 (a)**

Both entry paths (post-login, post-boot-refresh) need the same fetch-and-populate.

- **(a) Recommended — a TanStack Query hook (`useCurrentUserQuery`, key
  `["auth","me"]`) mounted at the authenticated boundary (`ProtectedRoute`), `enabled`
  when `status === "authenticated"`; on success it syncs the profile into the Zustand
  store via `setUser`.** One code path covers login AND boot (both just transition the
  store to `authenticated`, which enables the query); free de-dup, retry, and
  `queryClient.clear()`-on-logout invalidation from the layer we already run; the store
  stays the single source of truth the non-React client/guards read. Trade-off: two
  representations of the user (query cache + store) kept in sync by one effect — must
  keep the store as the canonical read and the query as the fetch/cache.
- (b) Imperative fetch inside the flows: call `authApi.me()` in `LoginPage.onSubmit`
  after `setSession`, and in `useSessionBootstrap` after `refreshOnce`, then `setUser`.
  No query cache, no dual representation. Trade-off: duplicated orchestration in two
  places, hand-rolled retry/loading, and it bypasses TanStack Query (which the
  foundation locked as the server-cache layer) for a server read.
- (c) Fetch inside `refreshOnce`/`setSession` itself (couple identity to token
  acquisition). Single choke point. Trade-off: pushes an app-level concern into the
  low-level client/refresh module (which is deliberately framework- and feature-
  agnostic), and couples every token rotation to a profile round-trip.

### OQ2 — Gate the first authenticated paint on `/auth/me`? — **Resolved 2026-07-16 (a)**

- **(a) Recommended — do NOT gate the whole shell; render immediately after the token
  is valid and let the account label + `AdminRoute` show a brief loading state until
  `/auth/me` resolves (see OQ5).** Fastest paint; the common case (non-admin, dashboard)
  needs no profile to render. Trade-off: a sub-second placeholder account label on the
  first frame after login/boot; admin deep-links wait on a splash (OQ5).
- (b) Extend the `idle` boot state until BOTH `/auth/refresh` and `/auth/me` resolve, so
  the guards always see a fully-populated user. Simplest correctness (no loading-state
  handling anywhere downstream). Trade-off: adds one serial round-trip before the first
  authenticated paint on every reload; a slow/failed `/auth/me` delays or blocks boot
  (couples app availability to the profile fetch).
- (c) Gate only after login (block the post-login navigate on `/auth/me`) but not on
  boot. Trade-off: asymmetric behavior between the two entry paths; still needs the
  loading-state handling on boot anyway.

### OQ3 — `/auth/me` fails with a **non-401** (network/500) while tokens are valid — **Resolved 2026-07-16 (a)**

- **(a) Recommended — stay authenticated with `user = null` (degraded): the app works
  for all non-admin surfaces, the account label shows a neutral fallback, `AdminRoute`
  denies (fail-safe), and the query auto-retries (network) / offers a manual retry.** A
  transient profile-fetch blip does not evict a user holding valid tokens. Trade-off: an
  admin cannot reach `/admin` until `/auth/me` succeeds (acceptable — fail-safe), and the
  account label is generic until then.
- (b) Treat any `/auth/me` failure as terminal → `clearSession()` + redirect to login.
  Simplest state machine (authenticated ⇔ profile-known). Trade-off: a transient 500/
  network on one endpoint logs out a user with perfectly valid tokens — harsh and
  surprising.
- (c) Block the app on a full-screen error+retry until `/auth/me` succeeds. Guarantees a
  known user before any app use. Trade-off: a single degraded endpoint takes the whole
  app down even for surfaces that never need the profile.

### OQ4 — Freshness / invalidation of the current-user query mid-session — **Resolved 2026-07-16 (a)**

`tier`/`role` can change server-side (admin grants Premium / flips role); the backend
busts the token cache so the next `/auth/refresh` reads live values.

- **(a) Recommended — fetch once per login and once per boot (staleTime effectively for
  the session; no `refetchOnWindowFocus`); rely on the next boot/refresh to pick up a
  changed tier/role. Expose a manual invalidation seam (`invalidate ["auth","me"]`) for
  future flows (e.g. after a successful upgrade-to-Premium) to call.** Matches the
  backend doc's "once per session/boot" expectation; no polling noise. Trade-off: a
  tier/role change made mid-session is not reflected until the next boot or an explicit
  invalidation — acceptable now (no in-app upgrade or self-role-change flow exists yet).
- (b) `refetchOnWindowFocus: true` for the current-user query so returning to the tab
  re-checks tier/role. Catches mid-session changes sooner. Trade-off: an extra `/auth/me`
  round-trip on every focus for a value that rarely changes; diverges from the app-wide
  `refetchOnWindowFocus: false` default.
- (c) Poll `/auth/me` on an interval. Always near-live. Trade-off: needless load for a
  rarely-changing value; not justified at this stage.

### OQ5 — `AdminRoute` behavior while the profile is still loading (only if OQ2 ≠ b) — **Resolved 2026-07-16 (a)**

- **(a) Recommended — while `status === "authenticated"` and the profile has not yet
  settled, `AdminRoute` renders the shared boot/loading splash (not `Forbidden`); once
  the profile settles it admits ADMIN or denies.** No false-negative flash for an admin
  deep-linking to `/admin`. Requires a definite "profile settled" signal (resolved OR
  failed) so a failed `/auth/me` (OQ3a) settles into a deny rather than an infinite
  splash — surfaced via the query's status or a small `profileStatus` flag on the store.
  Trade-off: `AdminRoute` gains a loading branch and a dependency on the profile-status
  signal.
- (b) `AdminRoute` denies whenever `role !== "ADMIN"` including while loading (current
  behavior). Simplest. Trade-off: an admin who reloads on `/admin` or deep-links there
  briefly sees `Forbidden` before the profile arrives, then must re-navigate — poor UX.

## Assumptions

- The locked session model (foundation OQ3) is unchanged: access token in memory,
  refresh token in `localStorage`, boot rehydrate via `/auth/refresh`. `/auth/me` layers
  on top; no storage change.
- `/auth/me` returns the caller's own account metadata only; exposing the caller's own
  `role`/`tier` to themselves breaches no privacy rule (validated in the backend doc's
  Decision Log against `The-ideal.md` §4 rule 1).
- The Zustand store remains the canonical `user` read for guards/shell (readable outside
  React); TanStack Query is the fetch/cache that feeds it (per OQ1a).
- Login keeps the optimistic `{ username }` from the form so the account label is correct
  immediately; `/auth/me` reconciles it with the full profile (same username, plus
  uuid/tier/role).
- A `401`/revoked `/auth/me` is already handled by the client's `401 → refresh` flow;
  this cycle adds no new 401 handling — only the non-401 path (OQ3).
- No new backend work, no new error code, no new dependency.

## Implementation Plan

> Paths under `FairShareMonWeb/`. Concrete files assume the **recommended** OQ options
> (OQ1a · OQ2a · OQ3a · OQ4a · OQ5a); re-sync after the checkpoint if the user chooses
> otherwise. All user-facing copy through i18n (vi-VN default).

### Step 1 — API call + DTO (R1)

1. `src/features/auth/api/authApi.ts` — add `me: () => api.get<UserResponse>("/v1/auth/me")`.
2. `src/lib/api/types/auth.ts` — make `role: string` non-optional on `UserResponse`
   (backend now always populates it; `USER`/`ADMIN`) and remove the seam caveat comment
   (R8). Keep `SessionUser.role` optional in the store (it is still absent until
   `/auth/me` resolves).

### Step 2 — Current-user query hook (R2, R3, OQ1a)

1. `src/features/auth/hooks/useCurrentUserQuery.ts` (new):
   - `useQuery({ queryKey: ["auth","me"], queryFn: authApi.me, enabled: status ===
     "authenticated", staleTime: Infinity, retry: (n, e) => isApiError(e) && e.isNetwork
     && n < 1 })` — reads `status` via `useSessionStatus()`.
   - Sync into the store on success: an effect writes `getSession().setUser({ uuid,
     username, tier, role, createdAt })` when `data` changes. (Do not depend on the
     manual-memo the React Compiler covers.)
   - On non-401 error (OQ3a): leave the session authenticated; the store `user` stays
     `null` (degraded). The query's `isError`/`isSuccess` provide the "settled" signal
     for OQ5.
2. Export a light selector/mount helper as needed; re-export from `useAuth.ts` if it
   keeps the auth hooks discoverable.

### Step 3 — Mount the query at the authenticated boundary (R2, R3, OQ1a/OQ2a)

1. `src/routes/ProtectedRoute.tsx` — call `useCurrentUserQuery()` here (or in a small
   `CurrentUserBoundary` rendered inside it) so it runs for every authenticated route
   (shell + admin) exactly once, mounting as soon as `status` flips to `authenticated`
   (covers both login-navigate and boot-rehydrate uniformly). Do **not** block the
   `<Outlet />` on it (OQ2a) — render immediately.
2. `src/app/useSessionBootstrap.ts` — **no change** under OQ1a (bootstrap still only does
   `refreshOnce`; the query fires off the resulting `authenticated` status). (Under OQ1b
   this is where the imperative boot fetch would live.)
3. `src/features/auth/pages/LoginPage.tsx` — keep `setSession(tokens, { username })`
   (optimistic label); the mounted query fetches `/auth/me` and reconciles. No imperative
   fetch here under OQ1a.

### Step 4 — Activate the admin guard (R4, R6, OQ5a)

1. `src/routes/AdminRoute.tsx` — three-way:
   - profile not yet settled (`status === "authenticated"` && profile pending) →
     `<BootSplash />` (reuse the existing splash);
   - `role === "ADMIN"` → `<Outlet />`;
   - otherwise → `<Forbidden />`.
   Source the "settled/pending" signal from `useCurrentUserQuery()`'s status (or a
   `profileStatus` selector) so a failed `/auth/me` (OQ3a) settles into deny, never an
   infinite splash. Remove the fail-safe-seam comment (R8).

### Step 5 — Account label + degraded copy (R3, R5)

1. `src/routes/AppShellLayout.tsx` — the label already reads `user?.username ??
   t("common:account")`; once `/auth/me` populates the store this shows the real
   username on reload with no code change. (Optional: show a subtle skeleton/`…` while
   the profile is pending — minor, ui-designer's `Skeleton` primitive is available.)
2. i18n: no new key strictly required (the existing `common:account` fallback covers the
   degraded/pending label). If OQ3a's degraded state wants a distinct affordance (e.g. a
   toast on `/auth/me` failure), add `errors:profileUnavailable` (vi-VN + en-US) rather
   than reusing `errors:unexpected`. Decide at implementation; keep minimal.

### Step 6 — Seam cleanup (R8, implementer to-dos)

- `FairShareMonWeb/CLAUDE.md` — replace the "Auth-guard seam (flagged)" section with the
  now-wired behavior (`role` comes from `/auth/me`; `AdminRoute` is live).
- Remove the seam caveat comments in `src/lib/api/types/auth.ts`,
  `src/lib/auth/session.ts` (the `SessionUser` doc), and `src/routes/AdminRoute.tsx`.
- (These are code-adjacent — done by the web-implementer during wiring, not by the
  planner.)

### Step 7 — Test harness fixtures

1. `src/test/msw/handlers.ts` — add `GET /v1/auth/me` handlers returning the envelope:
   a normal user (`role: "USER"`), an admin (`role: "ADMIN"`), a `401`/`1002` case, and a
   non-401 failure (`500`/network) for the degraded path.

### API endpoints consumed this cycle (verb + path + DTO)

| Screen/hook | Verb + Path | Request | Response (`data`) | Notable codes |
|---|---|---|---|---|
| `useCurrentUserQuery` (mounted in `ProtectedRoute`) | `GET /api/v1/auth/me` | (none, Bearer) | `UserResponse { uuid, username, tier, role, createdAt }` | `1002` (→ client refresh flow); non-401 → degraded (OQ3a) |

Envelope handling: unwrapped via `api.get`; `401` rides the client's `refresh-once →
retry → else clear+login`; a non-401 `ApiError` is classified (`classifyError`) and,
under OQ3a, leaves the session authenticated with `user = null`.

### Loading / empty / error states

- **Loading:** `AdminRoute` shows `<BootSplash />` while the profile is pending (OQ5a);
  the shell account label shows the optimistic username (login) or a neutral fallback/
  skeleton (boot) until `/auth/me` resolves.
- **Error (401/revoked):** existing client flow → clear session → `/login`.
- **Error (non-401):** degraded-authenticated (OQ3a) — `user = null`, `AdminRoute`
  denies, network errors auto-retry once, optional manual retry / toast.

### Form validation rules

None — this cycle adds no forms (read-only profile fetch).

### i18n keys (vi-VN + en-US)

- Reuse `common:account` (fallback/pending label), `common:booting` (splash), and the
  existing `errors:*`.
- Add only if OQ3a's degraded state gets its own affordance: `errors:profileUnavailable`
  (vi-VN + en-US). No other new keys.

### Accessibility

- `AdminRoute`'s loading branch reuses the accessible `BootSplash`.
- The account button keeps its label semantics; a pending skeleton (if added) must carry
  an accessible name (not an empty button). Status changes announced via the existing
  toast host if a degraded toast is shown.

### Tests the web-test-engineer should write (component + interaction, Vitest + RTL + MSW)

- **`authApi.me`** — `GET /auth/me` unwraps `UserResponse` (incl. `role`).
- **`useCurrentUserQuery`** — disabled while `unauthenticated`/`idle`; enabled + fetches
  once `authenticated`; on success `getSession().user` is populated with
  `uuid/tier/role`; a second consumer shares one fetch (dedup / cached).
- **Login flow** — after a successful login, `/auth/me` is fetched and the store `user`
  gains `role`; `AdminRoute` then admits an `ADMIN` and denies a `USER`.
- **Boot rehydrate** — after `/auth/refresh` on a fresh load, `/auth/me` populates the
  store; the shell account label renders the real username (regression for nit #3 — no
  longer the generic fallback after reload).
- **`AdminRoute` timing (OQ5a)** — while the profile is pending, renders the splash (not
  `Forbidden`); settles to admit ADMIN / deny USER; a **failed** `/auth/me` (OQ3a)
  settles to deny (no infinite splash).
- **`/auth/me` 401** — rides the client refresh; a failed refresh clears the session +
  redirects (existing behavior, assert unchanged).
- **`/auth/me` non-401 failure (OQ3a)** — session stays authenticated, `user` stays
  `null`, `AdminRoute` denies, non-admin surfaces still render; network error retries once.
- **Logout / change-password** — `queryClient.clear()` + `clearSession()` drop the
  cached current-user; a subsequent login re-fetches a fresh `/auth/me`.
- Deterministic (pinned TZ + locale, MSW at the boundary) — consistent with the existing
  suite.

## Impact Analysis

- **APIs:** consumes one new backend endpoint (`GET /api/v1/auth/me`); no backend change.
- **Frontend source:** new `src/features/auth/hooks/useCurrentUserQuery.ts`; edits to
  `authApi.ts` (+`me`), `types/auth.ts` (`role` non-optional), `ProtectedRoute.tsx`
  (mount the query), `AdminRoute.tsx` (loading/admit/deny), optionally
  `AppShellLayout.tsx` (pending label), `useAuth.ts` (re-export). `useSessionBootstrap.ts`
  and `refresh.ts` unchanged under OQ1a. Session store unchanged unless a `profileStatus`
  flag is chosen over reading query status (OQ5a).
- **Tests:** MSW `/auth/me` handlers; new/updated specs above.
- **Docs:** this plan; `frontend-foundation.md` unblock note (added); `CLAUDE.md` seam
  section rewrite (implementer).
- **Business rules:** activates `role == ADMIN` gating (never leaks other users' data —
  the endpoint is self-only); preserves the fail-safe (unknown role ≠ ADMIN).
- **Risk:** low — additive, read-only; the destructive 401 path is unchanged, and OQ3a
  keeps a transient profile-fetch failure non-destructive.

## Decision Log

### Decision
This cycle is the frontend follow-up the backend doc
(`expose-current-user-profile-and-role.md`, Future Improvements) explicitly hands to the
SPA team: wire `/auth/me`, activate `AdminRoute`, fix the reload account label.

### Reason
The backend closed the role/current-user gap (committed on origin/master); the remaining
work — consuming it, guard activation, label fix, edge-path handling — is SPA-owned.

### Decision (confirmed 2026-07-16)
All 5 Open Questions were confirmed by the user at the **recommended** option: OQ1a
(TanStack Query hook synced to the store), OQ2a (no boot gating), OQ3a
(degraded-authenticated on non-401), OQ4a (once per session/boot + manual-invalidation
seam), OQ5a (`AdminRoute` splash while pending). No re-sync was needed — the plan matched
the confirmed options and was implemented as written.

## Progress Log

### 2026-07-16

- Feature-planner: required reading completed — the backend contract
  (`FairShareMonApi/planning/expose-current-user-profile-and-role.md`: `role` on
  `UserResponse`, guarded `GET /auth/me`, live DB read, unchanged login/refresh),
  `frontend-foundation.md` (locked stack + the carried-forward role seam + nit #3),
  `FairShareMonWeb/CLAUDE.md` (auth-guard seam note), and the live SPA code:
  `session.ts` (`setUser`/`setSession`/status), `authApi.ts`, `useAuth.ts`,
  `useSessionBootstrap.ts`, `refresh.ts`, `client.ts` (401→refresh flow), `runtime.ts`,
  `router.tsx`, `ProtectedRoute.tsx`, `AdminRoute.tsx`, `AppShellLayout.tsx`,
  `RootLayout.tsx`, `queryClient.ts`, `providers.tsx`, `errors.ts`,
  `http-error-handling.ts`, `types/auth.ts`, and the i18n `common`/`errors` resources.
- Recorded the unblock in `frontend-foundation.md` (dated Progress Log entry pointing at
  the backend doc; CLAUDE.md seam note flagged as an implementer to-do).
- Drafted this plan: `authApi.me()`, a `useCurrentUserQuery` synced into the Zustand
  store, mounted at `ProtectedRoute`; `AdminRoute` activated with a loading branch;
  reload account-label fix; `/auth/me` edge-path handling.
- **5 Open Questions raised** (orchestration location; gate first paint; non-401 failure
  handling; freshness/invalidation; `AdminRoute` while loading), each with options,
  trade-offs, and a recommendation. Awaiting the user's answers at the checkpoint before
  implementation.

### 2026-07-16 — Implementation (web-implementer)

- Checkpoint confirmed: all 5 OQs at the recommended option (a). Implemented per plan.
- **R1** — `authApi.me()` → `GET /v1/auth/me` (`src/features/auth/api/authApi.ts`);
  `UserResponse.role` made non-optional (`src/lib/api/types/auth.ts`), seam comment dropped.
- **R2/R3/OQ1a** — new `src/features/auth/hooks/useCurrentUserQuery.ts`: `useQuery` key
  `["auth","me"]`, `enabled` on `status === "authenticated"`, `staleTime: Infinity`,
  network-only single retry; success syncs the profile into the store via `setUser`,
  non-401 error calls `markProfileUnavailable`. Exported `currentUserQueryKey` +
  `invalidateCurrentUser()` seam (OQ4a). Mounted once at `ProtectedRoute` (OQ2a — not
  gating the `<Outlet/>`). Login/boot both flip the store to `authenticated`, enabling the
  one query — no imperative fetch added to `LoginPage`/`useSessionBootstrap`.
- **Store** (`src/lib/auth/session.ts`) — added `profileStatus: "idle"|"pending"|
  "resolved"|"error"`; `setSession` → `pending`, `setUser` → `resolved`, new
  `markProfileUnavailable` → `error`, `clearSession`/`markUnauthenticated` → `idle`. Guards
  read the store (canonical), so there is no query→store render gap.
- **R4/R6/OQ5a** — `src/routes/AdminRoute.tsx`: splash while `authenticated` &&
  `profileStatus` in {`idle`,`pending`}; admit `role === "ADMIN"`; deny otherwise. A failed
  `/auth/me` settles to `error` → deny (no infinite splash). Fail-safe preserved.
- **R3/R5** — `AppShellLayout` account label unchanged (reads `user?.username ??
  common:account`): now shows the real username on reload; falls back neutrally when
  degraded. No new i18n key added (degraded state reuses `common:account`, per the plan's
  minimal option).
- **R8** — seam comments removed from `types/auth.ts`, `session.ts` (`SessionUser` doc),
  `AdminRoute.tsx`, `router.tsx`; `CLAUDE.md` "Auth-guard seam" note rewritten to "wired".
- **Harness** — MSW `GET /v1/auth/me` handler + seeded `demo` (USER), `admin` (ADMIN),
  `degraded` (no profile → 500) users and profiles (`src/test/msw/handlers.ts`).
- **Verification** — `pnpm lint` clean (only pre-existing fast-refresh warnings in
  untouched files); `tsc -b` clean; `pnpm build` succeeds. Live backend on :5200 (MariaDB +
  Redis up) confirmed the real contract via curl: anonymous `/auth/me` → 401 `1002`; admin
  login → `/auth/me` `{...role:"ADMIN"}`; registered user → `role:"USER"` (camelCase
  envelope). A throwaway jsdom+RTL harness drove the real `ProtectedRoute →
  useCurrentUserQuery → AdminRoute → AppShellLayout` wiring through MSW (all 5 scenarios
  green: ADMIN admitted after resolve with real label; USER denied; boot-rehydrate restores
  the label — nit #3 fixed; non-401 failure stays authenticated-degraded and denies admin;
  degraded session still renders non-admin surfaces), then removed. No real-browser drive
  was possible (no Chromium/Playwright in the environment) — see Final Outcome.

### 2026-07-17 — Tests (web-test-engineer)

- Added `src/features/auth/currentUserProfile.test.tsx` (17 specs) — the real hook
  → Zustand store → route guards → shell driven against MSW at the network boundary
  (never mocks the client/hook). Coverage by checklist item:
  - **authApi.me** — `AuthApiMe_SuccessEnvelope_UnwrapsUserResponseIncludingRole`:
    `/auth/me` unwraps the full `UserResponse` incl. `role: "ADMIN"`.
  - **useCurrentUserQuery** —
    `UseCurrentUserQuery_IdleThenAuthenticated_DisabledUntilAuthenticatedThenFetches`
    (disabled while `idle`/`unauthenticated`; the single query fires on the
    `authenticated` transition — one code path for login AND boot);
    `UseCurrentUserQuery_Success_SyncsUuidTierRoleIntoStoreAndResolves` (store `user`
    gains `uuid`/`tier`/`role`, `profileStatus` `pending → resolved`);
    `UseCurrentUserQuery_TwoConsumers_ShareASingleFetch` (dedup — two consumers, one
    round-trip).
  - **Login path** — `LoginFlow_SuccessfulLogin_FetchesProfileAndPopulatesRoleAndLabel`:
    a real login through `LoginPage` lands in the shell, `/auth/me` reconciles the
    store (`role: "ADMIN"`, `uuid`), and the account label shows the username.
  - **Boot-rehydrate path** —
    `BootRehydrate_TokensOnlyThenMe_RestoresRealUsernameLabel`: the tokens-only
    (no-user) authenticated shape first shows the neutral fallback then `/auth/me`
    restores the real username (regression for foundation nit #3).
  - **AdminRoute timing (OQ5a)** —
    `AdminRoute_AdminDeepLinkWhilePending_ShowsSplashThenAdmitsWithoutForbiddenFlash`
    (splash while pending, admits ADMIN, no Forbidden flash on a deep-link);
    `AdminRoute_UserProfileResolves_DeniesWithForbidden`;
    `AdminRoute_ProfileFetchFails_SettlesToDenyNotInfiniteSplash` (failed `/auth/me`
    settles to `error` → fail-safe deny, splash gone).
  - **Degraded non-401 (OQ3a)** —
    `Degraded_Non401MeFailure_StaysAuthenticatedUserNullNeutralLabelSurfacesRender`
    (stays authenticated, `user` null, tokens intact — session NOT cleared, neutral
    label, non-admin surface renders);
    `Degraded_NeutralAccountLabel_IsI18nDrivenNotHardcoded` (en-US → `Account`,
    proving the fallback is i18n-driven — extra);
    `Degraded_NetworkMeFailure_RetriesOnceThenSettlesError` (genuine network error
    auto-retries exactly once → 2 attempts → settles `error`).
  - **401 path** — `Me401ThenValidRefresh_RetriesAndResolvesNotTreatedAsDegraded`
    (401 rides the client refresh→retry, token rotates, profile resolves — NOT the
    degraded `error` path); `Me401ThenFailedRefresh_ClearsSessionAndRedirects`
    (terminal: hard-clear + redirect to login — asserts the existing flow unchanged).
  - **Freshness/invalidation (OQ4a)** — `Freshness_Remount_DoesNotRefetch`
    (`staleTime: Infinity` → served from cache on remount);
    `Invalidation_InvalidateCurrentUser_TriggersRefetch` (the manual seam forces a
    fresh read); `Logout_ClearsQueryCacheAndSession` (`queryClient.clear()` +
    `clearSession()` drop the cached current-user and end the session).
- **Updated** `src/routes/guards.test.tsx` — the 3 stale AdminRoute specs (written
  for the old fail-safe deny-all seam) now reflect the profile-aware contract: the
  `setStatus` helper sets `profileStatus`, and the specs are `..._NoRoleResolved_...`,
  `..._NonAdminRoleResolved_...`, `..._AdminRoleResolved_...` (admit/deny once the
  profile has settled).
- **Harness** — extended `renderWithProviders` with an optional `queryClient` so the
  freshness/invalidation/logout specs can exercise the app's singleton
  `queryClient` (the client `invalidateCurrentUser()` targets). Backwards-compatible;
  default remains a fresh retry-off client. No product code changed.
- **Extras beyond the plan's list:** the network-retry-once case, the en-US
  i18n-driven-label assertion, and the explicit "401 ≠ degraded" assertion.
- **Result:** `pnpm test` — 12 files, **98 passed / 0 failed** (green on two
  consecutive runs); `pnpm lint` clean (only pre-existing fast-refresh warnings in
  untouched `ThemeProvider`/`ToastHost`/`LocaleProvider`); `tsc -b` clean. No product
  bugs found.

## Final Outcome

Delivered per plan at the confirmed options (OQ1a·OQ2a·OQ3a·OQ4a·OQ5a); no deviations.

**Files changed:** `src/features/auth/api/authApi.ts` (`me()`), `src/lib/api/types/auth.ts`
(`role` non-optional), `src/features/auth/hooks/useCurrentUserQuery.ts` (new),
`src/features/auth/hooks/useAuth.ts` (`useProfileStatus` + re-exports), `src/lib/auth/
session.ts` (`profileStatus` + `markProfileUnavailable`), `src/routes/ProtectedRoute.tsx`
(mount the query), `src/routes/AdminRoute.tsx` (splash/admit/deny), `src/routes/router.tsx`
(comment), `src/test/msw/handlers.ts` (`/auth/me` + seeds), `FairShareMonWeb/CLAUDE.md`
(seam note rewrite).

**Endpoint consumed:** `GET /api/v1/auth/me` → `UserResponse { uuid, username, tier, role,
createdAt }` (camelCase); `401`/`1002` rides the client refresh flow; non-401 → degraded.

**Verified:** lint/tsc/build all green; live contract via curl against :5200; full
component wiring via a jsdom+MSW harness (all done-bar scenarios). **Not exercised live:**
a real-browser click-through (no Chromium/Playwright/chromium-cli available in this
environment) — the jsdom harness rendered the actual route components as the substitute;
the degraded non-401 path was exercised via MSW (the live backend does not 500 on demand).

**No Open Questions added; no deviations from the doc.**

## Future Improvements

- Invalidate `["auth","me"]` after a future in-app upgrade-to-Premium flow so the tier
  reflects immediately without waiting for the next boot/refresh (OQ4 seam).
- If a self-service "your account" surface grows (display name, preferences), promote the
  current-user query into a small `features/account` area and consider a `PATCH
  /users/me` (noted as a backend future improvement).
- Surface `tier` (Free/Premium) in the shell via the ui-designer's `Badge`/`Premium`
  primitives once the tier/upgrade UX cycle begins.
