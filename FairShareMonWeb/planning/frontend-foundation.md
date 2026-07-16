# Frontend Foundation (FairShareMonWeb)

## Objective

Establish the technical foundation for the FairShareMonWeb SPA — the substrate every future feature
cycle (auth screens, members, categories/tags, expenses+shares, events, stats, wallet/QR, tiers,
admin) is built on. This cycle does **not** ship the full product; it locks the stack decisions and
delivers the cross-cutting plumbing plus one thin **auth vertical slice** to prove the plumbing
end-to-end against the live API.

Concretely, this cycle must lock and stand up:

1. Routing (library + route structure + protected/role gating).
2. Server-state / data-fetching layer + the centralized typed **API client** (envelope unwrapping,
   `Authorization` + `X-Time-Zone` + `Accept-Language` injection, `401 → refresh-once → retry → else
login`, error-code handling, blob handling for CSV/QR).
3. Auth token storage + session lifecycle.
4. Minimal non-server client state (auth/session, locale, theme).
5. Styling substrate + theming/token strategy handed to the ui-designer.
6. i18n (vi-VN default + en-US) synced to the backend `Accept-Language`/`?culture=`; VND money +
   timezone-aware datetime formatters.
7. Forms + schema validation mirroring the backend FluentValidation rules.
8. Project structure conventions.
9. Env/config + dev proxy vs direct CORS.
10. Test harness (Vitest + RTL + user-event + jsdom + network mocking — Vitest/RTL is **locked**).
11. Tooling/quality gates (oxlint, `tsc -b`, `pnpm build`).
12. The content that becomes `FairShareMonWeb/CLAUDE.md`.

## Background

- **Backend is feature-complete and CORS-ready.** All controllers exist under
  `FairShareMonApi/FairShareMonApi/Controllers/`: `Auth`, `Members`, `Categories`, `Tags`,
  `Expenses` (+ share sub-routes, export, QR, history), `Events` (+ balance, export, QR, close),
  `Stats`, `BankAccounts` (wallet), `Admin`, `Health`. Routes are versioned:
  `api/v{version:apiVersion}/[controller]` with default `v1` → e.g. `POST /api/v1/auth/login`.
- **Envelope** (`Models/ApiResult.cs`): every response is
  `{ data, isSuccess, error: { code, message, fields? } }`. HTTP status is derived from the error
  (200/400/401/403/404/500). `error.fields` is a per-field validation map (`{ field: string[] }`)
  present only for `ValidationFailed` (1001). `error.message` is already localized by the backend.
- **Stable numeric error codes** (`Constants/ErrorCodes.cs`) — branch logic on these, never on message
  text. Key codes for the UI:
  - Infra: `1000` internal, `1001` validation (→ `error.fields`), `1002` unauthorized, `1003`
    not-found (also every ownership miss — never 403), `1004` forbidden (policy).
  - Auth: `2000` username taken, `2001` invalid credentials, `2002` invalid/expired/revoked refresh
    token, `2003` current password incorrect.
  - Members `3xxx`, Categories `4xxx`, Tags `5xxx`, Expenses `6xxx`, Shares `7xxx`, Events `9xxx`
    (incl. `9001` event closed, `9002` expense-time out of range, `9003` range excludes assigned),
    Wallet/QR `12xxx` (incl. `12001` no bank account, `12002` event not closed for QR, `12003` no
    outstanding debt).
  - **Tiers `13xxx`:** `13000` member limit, `13001` open-event limit, `13002` monthly-expense
    limit (Free create-limit `400`s → friendly messaging), `13003` **Premium feature required**
    (`403` → upgrade affordance; deliberately distinct from generic `1004`).
  - Admin `14xxx`: `14000` user not found, `14001` cannot target self, `14002` cannot target admin,
    `14003` account disabled (login rejected).
- **Auth contract** (`AuthController.cs` + `planning/user-authentication.md`):
  - `POST /api/v1/auth/register` (anon) → `ApiResult<UserResponse>`; **no auto-login** (client calls
    login next). `UserResponse { uuid, username, tier, createdAt }`.
  - `POST /api/v1/auth/login` (anon) → `ApiResult<TokenPairResponse>`.
    `TokenPairResponse { accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt }`.
    Access lifetime **30 min**, refresh **30 days**. Raw tokens returned once — client must persist.
  - `POST /api/v1/auth/refresh` (anon) → `ApiResult<TokenPairResponse>` (returns a plain DTO
    auto-wrapped into the envelope). **Full pair rotation**: the old access+refresh are revoked
    immediately. **Reuse detection**: presenting a _revoked_ refresh token returns `2002` **and
    revokes ALL of that user's sessions** — so a failed refresh must hard-clear the session and
    route to login.
  - `POST /api/v1/auth/logout` (Bearer) → success message; revokes the presented token's pair.
  - `POST /api/v1/auth/change-password` (Bearer) → success message; revokes ALL tokens (every device
    must re-login). Requires the current password; reusing the same new password is allowed.
  - Tokens are **opaque** strings — treat as blobs, never decode.
- **Timezone** (`planning/timezone-aware-datetimes.md`): send `X-Time-Zone` (IANA, from
  `Intl.DateTimeFormat().resolvedOptions().timeZone`); the server stores UTC and returns
  offset-aware ISO-8601 to be presented in the viewer's zone.
- **Localization** (`planning/localization-subsystem.md`): default **vi-VN**, supported `{ vi-VN,
en-US }`. Culture resolved from `Accept-Language` header plus a `?culture=` query override. The UI
  locale must drive the header so backend messages return in the active language.
- **CORS** (`Extensions/CorsExtensions.cs`): `SetIsOriginAllowed` + `AllowCredentials`; configured
  origins via `App:AllowedOrigins` honored everywhere, and **localhost/loopback/private origins are
  auto-allowed in Development only**. Bearer lives in the `Authorization` header (not cookies), so
  credentialed CORS is safe.
- **Money:** VND (`decimal` server-side). Render the value from the API with vi-VN grouping and 0
  fraction digits; **never** do float math on money in the client.
- **Business rules the SPA must honor** (`The-ideal.md` §4): absolute privacy (ownership miss = 404,
  never leak existence); **closed events are immutable** (disable every write control except the
  settled toggle); Premium/Free gating (wallet mutations + QR are Premium; Free create-limits);
  soft-deleted members/categories/tags stay in historical data but are unselectable for new data;
  the admin suite is `role == ADMIN` only and never surfaces other users' ledger data.
- **Domain terms are fixed** (vi-VN-first UI copy): expense (phiếu chi tiêu), share (phần gánh),
  event (đợt), wallet/bank account (ví / tài khoản ngân hàng), settled (đã trả), Premium/Free —
  never voucher/record/batch.
- **Current scaffold:** React **19.2** (React Compiler enabled via babel plugin), Vite **8**,
  TypeScript **6** (strict; `noUnusedLocals`/`noUnusedParameters`, `verbatimModuleSyntax`,
  `moduleResolution: bundler`, `erasableSyntaxOnly`), **oxlint** 1.7x (`react`, `typescript`, `oxc`
  plugins; `react/rules-of-hooks: error`). Only the default `App.tsx`/`App.css`/`index.css` + assets
  exist. No router, API client, data layer, state, UI system, i18n, tests, or `CLAUDE.md` yet. Package
  manager is **pnpm** (evident from `node_modules/.pnpm`).
- **Team roles that consume this doc:** `web-implementer` (builds per the locked stack), `ui-designer`
  (owns the from-scratch design system on the styling substrate this doc locks), `web-test-engineer`
  (Vitest + RTL harness), `web-code-reviewer`. Their agent definitions require
  `FairShareMonWeb/CLAUDE.md` + `FairShareMonWeb/planning/frontend-foundation.md` to exist and carry
  the locked decisions.

## Requirements

- A single **centralized, typed API client** — no scattered `fetch`. It must:
  - Resolve the base URL from a Vite env var.
  - Inject `Authorization: Bearer <access>`, `X-Time-Zone`, and `Accept-Language` on every request.
  - Unwrap `ApiResult<T>`: return `data` on `isSuccess`, otherwise throw/return a typed error carrying
    the numeric `code`, localized `message`, and optional `fields`.
  - Implement `401 → refresh-once → retry → else clear session + route to login`, de-duplicating
    concurrent refreshes (single in-flight refresh shared by all queued 401s).
  - Handle binary responses (CSV export → blob download; QR → PNG blob / image; `format=payload` →
    JSON string) distinctly from JSON.
- **Server state** via a caching data layer with typed hooks per feature; no ad-hoc component fetching.
- **Auth session**: persist enough to survive reload, rehydrate on boot (validate/refresh), expose
  tokens to the API client outside React, and provide login/logout/change-password flows.
- **Routing**: public auth routes, an authenticated app shell, and an admin area gated on
  `role == ADMIN`; a not-found route for ownership 404s and unknown paths.
- **Styling substrate + theme tokens** the ui-designer can build the design system on; light + dark
  theming with a viewer toggle; WCAG AA contrast; long-Vietnamese-text-tolerant layout.
- **i18n**: vi-VN default + en-US, all user-facing copy through the i18n layer (no hardcoded strings);
  locale synced to the backend `Accept-Language`; shared VND + timezone-aware datetime formatters.
- **Forms**: schema validation mirroring the backend validators (e.g. username 3–32 `[a-z0-9_.-]`,
  password 8 chars–72 bytes), plus server `error.fields` surfaced per field.
- **Test harness**: Vitest + RTL + user-event + jsdom, network mocked at the client boundary,
  deterministic (pinned locale + timezone), `pnpm test` script.
- **Quality gates**: `pnpm lint` (oxlint) clean, `tsc -b` type-checks, `pnpm build` succeeds.
- **Auth vertical slice** proving the plumbing: login, register, logout, change-password, session
  rehydrate/refresh, protected redirect, error-code rendering (2000/2001/2003 + field errors), locale
  toggle, theme toggle.
- **`FairShareMonWeb/CLAUDE.md`** enumerating the locked conventions for downstream agents.

## Open Questions

> Each option lists a one-line trade-off; the **Recommended** option is marked. **All 14 were
> answered by the user at the 2026-07-16 checkpoint — every one at the recommended option (a).** Each
> question is annotated below with its resolution; the binding record is in the Decision Log.

### OQ1 — Routing library

> **Resolved 2026-07-16 — option (a): React Router v7.**

- **(a) Recommended — React Router v7 (library / "declarative" mode, `createBrowserRouter`).** Most
  mature and ubiquitous; huge ecosystem; nested layouts + loaders/actions available if wanted.
  Trade-off: route params/search are not statically typed without extra effort.
- (b) TanStack Router. Fully type-safe routes + search params, first-class with TanStack Query.
  Trade-off: smaller ecosystem, steeper learning curve, more ceremony for simple routes.
- (c) Wouter / minimal router. Tiny. Trade-off: too thin for nested layouts, guards, and an admin
  sub-tree at this app's scale.

### OQ2 — Server-state / data-fetching library

> **Resolved 2026-07-16 — option (a): TanStack Query v5 over a thin fetch client.**

- **(a) Recommended — TanStack Query v5.** The idiomatic React server-cache: caching, background
  refetch, mutations, invalidation, request de-dup; pairs cleanly with a thin fetch client. Trade-off:
  a concept to learn (query keys/invalidation), one dependency.
- (b) SWR. Lighter, simpler API. Trade-off: weaker mutation/invalidation story for a write-heavy
  ledger app (expenses/shares/events).
- (c) Hand-rolled hooks over the API client. Zero deps. Trade-off: we reinvent caching/dedup/retry —
  wasteful and error-prone given the refresh flow.

### OQ3 — Auth token storage

> Bearer is in the `Authorization` header (not cookies); the primary threat is XSS reading storage,
> and the refresh flow needs the refresh token to survive reload.

> **Resolved 2026-07-16 — option (a): access token in memory, refresh token in `localStorage`,
> rehydrate on boot via `/auth/refresh`.**

- **(a) Recommended — access token in memory only; refresh token in `localStorage`; rehydrate on boot
  by calling `/auth/refresh`.** Survives reload; access token never touches persistent storage
  (shrinks its exposure window). Trade-off: a refresh token in `localStorage` is XSS-readable —
  mitigated by strict i18n/no-`dangerouslySetInnerHTML` discipline; rotation + reuse-detection limits
  blast radius.
- (b) Both tokens in `localStorage`. Simplest; instant rehydrate without a refresh round-trip.
  Trade-off: access token also persistently XSS-readable.
- (c) Both in memory only (no persistence). Best XSS posture. Trade-off: every reload/new tab forces a
  re-login — poor UX for a 30-day-refresh mobile-first ledger.
- (d) `sessionStorage` for tokens. Per-tab isolation. Trade-off: lost on tab close; still XSS-readable;
  no cross-tab session — worse UX with no real security gain over (a). (The backend cannot issue an
  HttpOnly refresh cookie — refresh tokens are returned in the JSON body — so a cookie-based option is
  not available without a backend change.)

### OQ4 — Non-server client state tool

> **Resolved 2026-07-16 — option (a): Zustand for the auth/session store + React Context for
> theme & locale.**

- **(a) Recommended — Zustand for the auth/session store + React Context for theme & locale
  providers.** Zustand's vanilla store is readable **outside React** (the API client needs the access
  token and a "session expired" signal without hooks); Context suits rarely-changing theme/locale.
  Trade-off: one small dependency.
- (b) React Context + `useReducer` for everything. No dependency. Trade-off: exposing the token to the
  non-React API client means a parallel module ref anyway; Context re-render ergonomics are clumsier
  for frequently-read session state.
- (c) Redux Toolkit. Powerful, devtools. Trade-off: overkill — server cache lives in TanStack Query;
  only a sliver of true client state remains.

### OQ5 — Styling approach / design-system substrate

> This is the ui-designer's substrate — the single most far-reaching decision here.

> **Resolved 2026-07-16 — option (a): CSS Modules + CSS-custom-property design tokens + Radix
> primitives.**

- **(a) Recommended — CSS Modules + design tokens as CSS custom properties (`:root` / `[data-theme]`),
  with a small set of headless behaviors from Radix Primitives for complex widgets (dialog, menu,
  tabs, tooltip).** Zero runtime cost (great with React Compiler); tokens theme light/dark by swapping
  custom properties; scoped class names; accessible primitives without visual opinions. Trade-off: we
  author component CSS by hand (no utility shorthand).
- (b) Tailwind CSS v4 (+ optional Radix/Headless UI). Fast iteration, tokens via CSS vars, consistent
  spacing. Trade-off: utility-class verbosity in JSX; the design system lives in config + class
  strings rather than named component styles.
- (c) vanilla-extract (typed CSS-in-TS, zero runtime). Type-safe tokens/variants, theme contracts.
  Trade-off: extra build integration; smaller community; more upfront setup.
- (d) Runtime CSS-in-JS (styled-components / Emotion). Familiar. Trade-off: runtime cost and React 19 /
  Compiler friction; trending out of favor — not recommended.

### OQ6 — i18n library

> **Resolved 2026-07-16 — option (a): `react-i18next` (+ `i18next`), vi-VN default + en-US.**

- **(a) Recommended — `react-i18next` (+ `i18next`).** De-facto standard; namespaces, interpolation,
  plurals, lazy-loaded resources; easy to sync active language → `Accept-Language`. Trade-off: keys
  are not statically type-checked by default (add a typed-keys augmentation).
- (b) LinguiJS. Compile-time catalogs, message extraction, small runtime. Trade-off: build/macro
  tooling; smaller ecosystem.
- (c) `react-intl` (FormatJS). Strong ICU message + number/date formatting. Trade-off: heavier API,
  more boilerplate for simple key lookups.
- (d) Lightweight custom (typed dictionaries + `Intl`). Full type-safety, zero deps. Trade-off: we
  maintain plural/interpolation ourselves; grows into a mini-library.

### OQ7 — Forms + schema validation

> **Resolved 2026-07-16 — option (a): React Hook Form + Zod.**

- **(a) Recommended — React Hook Form + Zod (via `@hookform/resolvers`).** Ergonomic, performant
  (uncontrolled), Zod schemas double as TS types and mirror the backend validators; server
  `error.fields` maps cleanly onto field errors. Trade-off: two dependencies.
- (b) React Hook Form + Valibot. Same UX with a much smaller validator bundle. Trade-off: smaller
  ecosystem/resolver maturity than Zod.
- (c) TanStack Form + Zod/Valibot. Type-safe, framework-aligned with TanStack Query/Router. Trade-off:
  newer, smaller community than RHF.

### OQ8 — Project structure

> **Resolved 2026-07-16 — option (a): feature-first structure.**

- **(a) Recommended — feature-first: `src/features/<area>/` (components, hooks, api, types, i18n
  slices) over shared layers `src/lib/` (api client, auth), `src/components/ui/` (design system),
  `src/i18n/`, `src/routes/`.** Scales as features are added milestone-by-milestone; co-locates each
  domain. Trade-off: some upfront convention to document.
- (b) Strict layer-first (`src/components`, `src/hooks`, `src/services`, `src/pages`). Familiar.
  Trade-off: domain code scatters across layers as the app grows.

### OQ9 — Dev server: Vite proxy vs direct CORS

> **Resolved 2026-07-16 — option (a): Vite `/api` dev proxy (target confirmed by OQ14 =
> `http://localhost:5200`).**

- **(a) Recommended — Vite dev proxy: front-end calls same-origin `/api/*`, Vite proxies to the
  backend (`http://localhost:<port>`); `VITE_API_BASE_URL` selects direct URL in prod builds.** Avoids
  all CORS/preflight friction in dev; one code path (`/api`). Trade-off: dev and prod differ slightly
  (proxy vs direct) — mitigated by the env var.
- (b) Direct CORS calls in dev too (backend already auto-allows localhost in Development). Dev mirrors
  prod exactly. Trade-off: preflight round-trips; must keep the dev origin allowed.

### OQ10 — Theme scope for this cycle

> **Resolved 2026-07-16 — option (a): ship light + dark now, system default + persisted toggle.**

- **(a) Recommended — ship light + dark tokens now, default to **system** preference, with a
  persisted viewer toggle.** Matches the ui-designer's dual-theme mandate; avoids a retrofit later.
  Trade-off: the ui-designer must define both palettes up front.
- (b) Light-only now, structure tokens so dark can be added later. Less upfront design. Trade-off:
  contradicts the ui-designer brief; risks a retrofit.

### OQ11 — Test network mocking

> **Resolved 2026-07-16 — option (a): MSW at the network boundary.**

- **(a) Recommended — MSW (Mock Service Worker) with a shared handler set returning the
  `ApiResult<T>` envelope.** Mocks at the network boundary → tests exercise the real API client
  (envelope unwrap, `401 → refresh → retry`, error codes). Trade-off: one dev dependency + handler
  upkeep.
- (b) Mock the API client module directly. Simpler setup. Trade-off: bypasses the client's own logic
  (refresh/unwrap) — exactly the code most worth testing.

### OQ12 — Formatting / extra tooling

> **Resolved 2026-07-16 — option (a): oxlint type-aware rules + Prettier for formatting.**

- **(a) Recommended — enable oxlint type-aware rules (install `oxlint-tsgolint`, `typeAware: true`)
  and keep oxlint as the sole linter; add **Prettier** for formatting only.** Type-aware lint catches
  real bugs; Prettier standardizes formatting oxlint doesn't own. Trade-off: two tools (lint +
  format).
- (b) oxlint only (its formatter/`--fix`), no Prettier. One tool. Trade-off: oxlint's formatting story
  is less mature than Prettier's.
- (c) Biome (lint + format in one). Fast, unified. Trade-off: replaces the already-chosen oxlint —
  reopens a scaffold decision.

### OQ13 — Scope of this foundation cycle

> **Resolved 2026-07-16 — option (a): plumbing + thin auth vertical slice + app shell + admin guard;
> other feature screens stubbed.**

- **(a) Recommended — plumbing + a thin auth vertical slice (login, register, logout,
  change-password, session rehydrate/refresh, protected redirect, locale + theme toggle) + an empty
  authenticated app-shell layout and an admin-area route guard; other feature screens are stubbed
  placeholders.** Proves every cross-cutting concern end-to-end without pulling a feature milestone
  forward. Trade-off: not much visible product yet.
- (b) Plumbing only (no auth screens; a manual token for smoke). Leanest. Trade-off: the refresh/
  session/error-rendering paths ship unproven against the real API.
- (c) Plumbing + auth + the first real feature (e.g. Members). More product. Trade-off: front-loads a
  feature milestone; larger review surface; risks churning the foundation mid-build.

### OQ14 — Backend base URL / port for the dev proxy

> **Resolved 2026-07-16 (factual):** the dev backend runs on **`http://localhost:5200`** (the
> committed `http` launch profile; HTTPS is on `7114`). The Vite dev proxy targets
> `http://localhost:5200`. There is **no production deployment yet**, so `.env.example` ships an
> **empty `VITE_API_BASE_URL` placeholder** with a comment to set it at deploy time.

- The backend runs locally via `dotnet run`; `planning/user-authentication.md` mentions a smoke run on
  **port 5200**, but the committed launch profile port isn't pinned in this doc. **Which base URL /
  port should the dev proxy target, and what production `VITE_API_BASE_URL` (if any) should ship in
  `.env.example`?** No safe default — needs confirmation.

### OQ-D — design decisions raised by the ui-designer (2026-07-16, non-blocking)

> The design system was built from scratch on the OQ5 substrate. These are
> subjective/brand calls made to unblock the design layer; the app is fully
> functional and reviewable as-is. They need only the **user's confirmation** at
> a convenient checkpoint — none block the web-implementer.

- **OQ-D1 — Brand hue = JADE / teal-green.** Chosen to evoke "money / tiền" and
  to avoid the generic AI purple-gradient look; neutrals are a cool gray with a
  faint jade bias; gold is reserved for Premium. _Confirm the palette direction,
  or name a preferred brand color and the tokens will be re-derived._
  > **Resolved 2026-07-16 — keep jade/teal** (user checkpoint). Locked as delivered.
- **OQ-D2 — Typography = system-font stack** (`system-ui` → Segoe UI on Windows,
  excellent Vietnamese coverage), zero web-font dependency. A brand webfont
  (candidate: **Be Vietnam Pro**) can be adopted later by prepending it to one
  token (`--fs-font-sans`) + shipping the font asset. _Confirm whether to stay
  system-only (default) or add the webfont (new asset/dependency → approval)._
  > **Resolved 2026-07-16 — system font now** (user checkpoint). Webfont deferred
  > to a later cycle (a documented future improvement, not this cycle's scope).
- **OQ-D3 — Data-viz palette** adopts the validated documented dataviz palette
  (blue-anchored categorical, blue sequential, blue↔red diverging), validated
  against the app's chart surfaces in both themes. It is intentionally its own
  plane (not the jade brand hue). _Confirm this is acceptable for the future
  Stats/Admin dashboards._
  > **Resolved 2026-07-16 — accept the blue-anchored plane** (user checkpoint).

## Assumptions

- Package manager is **pnpm** (lockfile/`node_modules/.pnpm` present); all scripts assume `pnpm`.
- The scaffold's locked baseline stays: React 19 + React Compiler, Vite 8, TS 6 strict, oxlint. We
  add libraries, not swap these.
- Vitest + React Testing Library is **locked** (team decision) — the harness is planned, not
  re-opened.
- The design system is built **from scratch** by the ui-designer on whatever substrate OQ5 locks;
  this doc defines only the token contract + theming mechanism, not the visual language.
- Money values arrive from the API already computed; the client formats but never arithmetically
  combines them.
- The refresh endpoint is anonymous and rotates the full pair; a failed refresh (incl. reuse
  detection revoking all sessions) is terminal → clear session, route to login.
- No SSR — this is a client-rendered SPA served as static assets; deployment origin is configured via
  `App:AllowedOrigins` on the backend (out of scope here beyond the env var).
- Admin role is exposed to the client via the authenticated user's role; **pending confirmation the
  login/refresh/user payload carries a role field** — see OQ note below. (`UserResponse` currently
  documents `{ uuid, username, tier, createdAt }`; role gating in the admin cycle will need a role
  source. The implementer must confirm the role field against the live payload before wiring the admin
  guard; if absent, that becomes an Open Question in the admin cycle, not here.)

## Implementation Plan

> Paths are under `FairShareMonWeb/`. Order matters — later steps depend on earlier ones. All
> user-facing strings go through i18n (vi-VN default). Concrete library names below assume the
> **recommended** OQ options; if the user picks otherwise, substitute accordingly.

### Step 0 — Dependencies + tooling

1. Add runtime deps (per OQ answers): `react-router-dom` (OQ1a), `@tanstack/react-query` (OQ2a),
   `zustand` (OQ4a), `@radix-ui/react-*` primitives as needed (OQ5a), `i18next` + `react-i18next`
   (OQ6a), `react-hook-form` + `zod` + `@hookform/resolvers` (OQ7a).
2. Add dev deps: `vitest`, `@testing-library/react`, `@testing-library/user-event`,
   `@testing-library/jsdom` (`jsdom`), `@testing-library/dom`, `@vitest/coverage-v8`, `msw` (OQ11a),
   `oxlint-tsgolint` (OQ12a), `prettier` (OQ12a).
3. `package.json` scripts: `"test": "vitest run"`, `"test:watch": "vitest"`, `"format": "prettier
--write ."`; keep `dev`/`build`/`lint`/`preview`.
4. `.oxlintrc.json`: add `"options": { "typeAware": true }` (per the README guidance) keeping the
   existing plugins/rules.
5. `vite.config.ts`: add the dev proxy (OQ9a) — `server.proxy['/api'] → VITE_API_BASE_URL` (dev
   default from `.env.development`), and the Vitest config block (`test.environment = 'jsdom'`,
   `test.setupFiles`, `test.globals`).

### Step 1 — Env & config

1. `.env.development` (committed): `VITE_API_BASE_URL=http://localhost:5200` (OQ14) — used as the Vite
   proxy target; client code calls same-origin `/api`.
2. `.env.example` (committed): an **empty** `VITE_API_BASE_URL=` placeholder with a comment to set the
   production API origin at deploy time (no production deployment exists yet — OQ14).
3. `src/config/env.ts` — a typed accessor over `import.meta.env` (base path `/api` by default;
   validates presence). `src/vite-env.d.ts` augmenting `ImportMetaEnv`.

### Step 2 — Styling substrate + theme (hand-off scaffold for the ui-designer)

1. `src/styles/tokens.css` — CSS custom properties for color/spacing/radii/shadows/typography, with
   `:root` (light) and `[data-theme="dark"]` overrides (OQ5a/OQ10a). **The ui-designer owns the actual
   values;** this step lays down the file + naming contract only.
2. `src/styles/global.css` — reset/base, imports `tokens.css`; replaces `App.css`/`index.css` usage.
3. `src/theme/ThemeProvider.tsx` + `useTheme()` — reads system preference, applies `data-theme` on
   `<html>`, persists the toggle (`localStorage`), exposes `theme`/`setTheme`.
4. `src/components/ui/` — placeholder barrel for design-system primitives (Button, Input, Field,
   Dialog, Toast, Spinner, EmptyState, ErrorState) the ui-designer fills in. This cycle ships minimal
   unstyled-but-accessible versions so the auth slice renders.

### Step 3 — i18n

1. `src/i18n/index.ts` — configure `i18next` + `react-i18next`; default `vi-VN`, supported
   `['vi-VN','en-US']`, fallback `vi-VN`; resources loaded from locale files.
2. `src/i18n/locales/vi-VN/*.json` + `en-US/*.json` — namespaces: `common`, `auth`, `errors`,
   `validation` (feature namespaces added per cycle). `errors` maps numeric codes → optional client
   fallback copy (primary source is `error.message` from the API; this covers network/no-response).
3. `src/i18n/format.ts` — shared formatters:
   - `formatMoneyVnd(value)` → `Intl.NumberFormat('vi-VN', { style:'currency', currency:'VND',
maximumFractionDigits:0 })` (or grouping-only variant) — formats the API value, no arithmetic.
   - `formatDateTime(iso)` / `formatDate(iso)` → `Intl.DateTimeFormat` in the viewer's zone (the API
     returns offset-aware ISO-8601).
   - `getTimeZone()` → `Intl.DateTimeFormat().resolvedOptions().timeZone` (for `X-Time-Zone`).
4. `src/i18n/LocaleProvider.tsx` + `useLocale()` — active locale state (persisted); on change,
   `i18next.changeLanguage` **and** update the value the API client reads for `Accept-Language`.
5. Type augmentation `src/i18n/i18next.d.ts` for key type-safety.

### Step 4 — Auth session store

1. `src/lib/auth/session.ts` — a Zustand (vanilla) store (OQ4a): `{ accessToken, accessExpiresAt,
refreshToken, refreshExpiresAt, user }`, actions `setSession`, `clearSession`, plus a plain
   getter/subscribe usable **outside React** by the API client. Persistence per **OQ3** (access in
   memory, refresh in `localStorage`, rehydrate on boot).
2. `src/lib/auth/storage.ts` — read/write the persisted refresh token (per OQ3a) with a single
   storage key; guards JSON parse.
3. `types` for `UserResponse`, `TokenPairResponse` in `src/lib/api/types/auth.ts` mirroring the DTOs.

### Step 5 — Centralized API client

1. `src/lib/api/client.ts` — a thin `fetch` wrapper:
   - `request<T>(method, path, { body, query, headers, signal, responseType })`.
   - Injects `Authorization: Bearer <access>` (from the session store), `X-Time-Zone`
     (`getTimeZone()`), `Accept-Language` (active locale), `Content-Type: application/json` for JSON
     bodies.
   - Reads the `ApiResult<T>` envelope: on `isSuccess` returns `data`; else throws `ApiError`
     (`code`, `message`, `fields?`, `httpStatus`).
   - `responseType: 'blob'` path for CSV/PNG (returns `Blob` + filename from `Content-Disposition`);
     `format=payload` QR returns the JSON string via the normal envelope path.
2. `src/lib/api/refresh.ts` — the `401 → refresh-once → retry → else login` orchestration:
   - On a `401`/`1002` from a non-refresh request, call `POST /api/v1/auth/refresh` **once**;
     de-duplicate concurrent refreshes behind a single shared in-flight promise.
   - On refresh success: update the session, retry the original request once.
   - On refresh failure (incl. `2002` reuse-detection): `clearSession()` and signal a redirect to
     `/login` (via a navigation callback registered by the router shell, so the client stays
     framework-agnostic).
3. `src/lib/api/errors.ts` — `ApiError` class + `isApiError`, plus `ErrorCodes` TS constant mirror of
   `Constants/ErrorCodes.cs` (documented as the single source for code-based branching).
4. `src/lib/api/http-error-handling.ts` — a helper mapping common codes to UX intents for reuse
   (e.g. `1003` → not-found, `13000/13001/13002` → limit toast, `13003` → upgrade affordance),
   consumed by feature hooks/error boundaries.

### Step 6 — Data layer (TanStack Query)

1. `src/lib/query/queryClient.ts` — configured `QueryClient` (sane retry: **do not** retry `4xx`;
   let the API client own the refresh retry).
2. `src/app/providers.tsx` — composes `QueryClientProvider`, `ThemeProvider`, `LocaleProvider`, and
   the router.
3. `src/features/auth/api/authApi.ts` + `useAuth.ts` — mutations `useLogin`, `useRegister`,
   `useLogout`, `useChangePassword` over the API client; `useCurrentUser` selector over the session
   store.

### Step 7 — Routing + guards

1. `src/routes/router.tsx` (OQ1a) — route tree:
   - Public: `/login`, `/register` (redirect to app if already authenticated).
   - Authenticated shell `/` (layout with nav, locale + theme toggles, logout): child routes
     `/dashboard` (placeholder), `/settings/change-password`, and **stub placeholders** for
     `/members`, `/categories`, `/tags`, `/expenses`, `/events`, `/stats`, `/wallet` (OQ13a).
   - Admin area `/admin/*` gated on `role == ADMIN` (stub) — see Assumptions on the role source.
   - `*` → NotFound.
2. `src/routes/ProtectedRoute.tsx` — redirects unauthenticated users to `/login` (preserving the
   intended path); renders a boot splash while the session rehydrates.
3. `src/routes/AdminRoute.tsx` — additionally requires `role == ADMIN`, else a forbidden/redirect
   view.
4. `src/routes/NotFound.tsx` — the shared not-found view reused for ownership `1003`/`404`.
5. Register the router's `navigate` with the API client (Step 5.2) so refresh-failure redirects work.

### Step 8 — Auth vertical slice (proves the plumbing)

1. `src/features/auth/pages/LoginPage.tsx` — RHF + Zod form (username/password); on submit `useLogin`;
   render `error.message`; branch on `2001` (invalid credentials) vs field errors (`1001` →
   `error.fields`); on success set session + redirect.
2. `src/features/auth/pages/RegisterPage.tsx` — RHF + Zod (username 3–32 `^[a-z0-9_.-]+$`, password
   8 chars–72 bytes mirroring the backend validators); handle `2000` username taken; on success route
   to `/login` (no auto-login).
3. `src/features/auth/pages/ChangePasswordPage.tsx` — RHF + Zod (current + new password); handle
   `2003`; on success clear session + route to `/login` with a "re-login required" notice (all
   sessions revoked).
4. `src/features/auth/schemas.ts` — Zod schemas shared by forms + tests, mirroring backend rules.
5. App-shell `Logout` action → `useLogout` → clear session → `/login`.
6. Locale toggle + theme toggle wired in the shell header.

### Step 9 — Test harness + slice tests

1. `src/test/setup.ts` — RTL/jsdom setup, `@testing-library/jest-dom`, **pin `TZ` and locale**
   deterministically; start/stop MSW server.
2. `src/test/msw/handlers.ts` + `server.ts` — envelope-shaped handlers for auth endpoints (success +
   `2000/2001/2003/1001` + a `401 → refresh → retry` scenario + a refresh-reuse `2002`).
3. `src/test/utils.tsx` — `renderWithProviders` (QueryClient + Theme + Locale + MemoryRouter).
4. Tests the web-test-engineer writes (component + interaction) — see the Tests subsection below.

### Step 10 — Docs + quality gates + verification

1. Author **`FairShareMonWeb/CLAUDE.md`** (see the dedicated subsection) with the locked stack +
   conventions.
2. Remove/repurpose the default `App.tsx` demo; wire `src/main.tsx` → providers → router.
3. Run `pnpm lint` (clean), `tsc -b` (types), `pnpm build` (succeeds), `pnpm test` (green); run the
   app against the live backend and verify: register → login → protected access → change-password →
   forced re-login → logout; a `401` after access expiry silently refreshes; a revoked refresh routes
   to login; locale toggle changes both UI copy and backend message language; theme toggle persists.
4. Update this doc's Progress Log + Final Outcome.

### API endpoints consumed this cycle (verb + path + DTO)

| Screen/hook               | Verb + Path                         | Request DTO                        | Response (`data`)                                                                              | Notable codes                                |
| ------------------------- | ----------------------------------- | ---------------------------------- | ---------------------------------------------------------------------------------------------- | -------------------------------------------- |
| Register                  | `POST /api/v1/auth/register`        | `{ username, password }`           | `UserResponse { uuid, username, tier, createdAt }`                                             | `2000` username taken; `1001` field errors   |
| Login                     | `POST /api/v1/auth/login`           | `{ username, password }`           | `TokenPairResponse { accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt }` | `2001` invalid credentials; `14003` disabled |
| Refresh (client-internal) | `POST /api/v1/auth/refresh`         | `{ refreshToken }`                 | `TokenPairResponse`                                                                            | `2002` invalid/revoked → clear session       |
| Logout                    | `POST /api/v1/auth/logout`          | (none, Bearer)                     | success message                                                                                | `1002`                                       |
| Change password           | `POST /api/v1/auth/change-password` | `{ currentPassword, newPassword }` | success message                                                                                | `2003` current wrong; `1001`                 |

Envelope handling: all go through the centralized client; `data` is unwrapped on success; failures
throw `ApiError` carrying the numeric `code` — screens branch on `code`, render `error.message`, and
map `error.fields` to form fields.

### Form validation rules (mirroring backend validators)

- **Username** (register): required; 3–32 chars; `^[a-z0-9_.-]+$` (lowercased before submit);
  vi-VN messages.
- **Password** (register/change): required; min 8 chars; max **72 bytes** (UTF-8 byte length, not char
  count — mirror the BCrypt limit). Same-password reuse on change is allowed (no client block).
- **Change password**: `currentPassword` + `newPassword` both required.
- Server `error.fields` (camelCase keys) merges onto the corresponding RHF fields; unknown-field
  server errors surface as a form-level message.

### Loading / empty / error states (baseline primitives)

- **Loading**: route-level boot splash during session rehydrate; per-query `Spinner`; form submit
  disabled + inline spinner.
- **Empty**: shared `EmptyState` primitive (used by future list screens).
- **Error**: inline form error (`error.message`); toast for mutation failures; `NotFound` view for
  `1003`/404; a global error boundary for unexpected throws; a distinct **upgrade affordance**
  component for `13003` and a **limit-reached** notice for `13000/13001/13002` (built now as reusable
  primitives even though their screens come later).

### i18n keys (initial; vi-VN + en-US)

- `common.appName`, `common.loading`, `common.save`, `common.cancel`, `common.retry`,
  `common.logout`, `common.theme.light`, `common.theme.dark`, `common.theme.system`,
  `common.locale.vi`, `common.locale.en`, `common.notFound.title`, `common.notFound.body`,
  `common.forbidden.title`.
- `auth.login.title/username/password/submit/registerLink`,
  `auth.register.title/.../loginLink`, `auth.changePassword.title/current/new/submit/reloginNotice`,
  `auth.logout.success`.
- `errors.network`, `errors.unexpected`, `errors.premiumRequired`, `errors.limit.member`,
  `errors.limit.openEvent`, `errors.limit.monthlyExpense` (client fallbacks; API `error.message` is
  primary).
- `validation.username.required/format/length`, `validation.password.required/min/maxBytes`,
  `validation.required`.

### Accessibility requirements

- Semantic landmarks (`header`/`nav`/`main`), labeled form controls (`<label htmlFor>` /
  `aria-describedby` for errors), visible focus rings from tokens, full keyboard nav (Radix primitives
  provide focus management for dialog/menu), color-independent status (icon + text, not color alone),
  `prefers-reduced-motion` respected, `<html lang>` synced to the active locale, and error summaries
  associated with fields via `aria-invalid`/`aria-describedby`.

### Tests the web-test-engineer should write

- **API client**: success unwraps `data`; `isSuccess:false` throws `ApiError` with the right `code`;
  `X-Time-Zone` + `Accept-Language` + `Authorization` headers present; `401 → refresh → retry`
  succeeds and the retried request carries the new token; concurrent 401s share **one** refresh;
  refresh `2002`/failure clears session + triggers the login redirect; blob path returns a `Blob`.
- **LoginPage**: valid submit sets session + navigates; `2001` renders the invalid-credentials
  message; `1001` maps `error.fields` to fields; submit disabled while pending; keyboard-submittable.
- **RegisterPage**: client validation blocks bad username/password (mirrors backend rules incl. the
  72-byte cap); `2000` renders username-taken; success routes to `/login` (no auto-login).
- **ChangePasswordPage**: `2003` renders; success clears session + shows the re-login notice.
- **ProtectedRoute**: unauthenticated → redirect to `/login`; rehydrating → boot splash;
  authenticated → renders child.
- **AdminRoute**: non-admin is blocked/redirected (admin UI hidden for non-admins).
- **i18n**: vi-VN default copy renders; toggling to en-US switches copy and the client's
  `Accept-Language`; `formatMoneyVnd` renders VND with vi-VN grouping; `formatDateTime` renders in a
  pinned timezone deterministically.
- **Theme**: toggle applies `data-theme` and persists.
- All tests deterministic (pinned `TZ` + locale, MSW; no real network/wall-clock).

### What becomes `FairShareMonWeb/CLAUDE.md`

Enumerate the locked conventions so downstream agents need no other source:

- **Stack (locked):** React 19 + React Compiler (no manual `useMemo`/`useCallback` the compiler
  covers), Vite 8, TS 6 strict; router (OQ1), TanStack Query (OQ2), Zustand + Context (OQ4), styling
  substrate + tokens (OQ5), react-i18next (OQ6), RHF + Zod (OQ7), pnpm. **Adding a dependency the
  foundation didn't approve is an Open Question, not a decision.**
- **API contract rules:** one centralized typed client; `Authorization` + `X-Time-Zone` +
  `Accept-Language` on every request; unwrap `ApiResult<T>`; branch on numeric `error.code` (never
  message text) — mirror table in `src/lib/api/errors.ts`; `401 → refresh-once → retry → else login`
  lives only in the client; ownership `404`/`1003` → not-found (no existence leak); Free-limit `400`s
  (`13000/13001/13002`) → friendly limit UI; Premium `403` (`13003`) → upgrade affordance; closed
  events immutable (disable every write control except the settled toggle); admin area `role == ADMIN`
  only, never other users' ledger data; binary responses (CSV/QR) as blobs.
- **Money & time:** VND via the shared formatter, never float math; datetimes offset-aware ISO-8601
  rendered in the viewer's zone via the shared formatter.
- **i18n:** vi-VN default + en-US, all copy through i18n, fixed domain terms
  (expense/share/event/wallet/settled/Premium/Free — never voucher/record/batch).
- **Structure:** feature-first (OQ8); design-system primitives live in `src/components/ui/` (reuse,
  never fork a parallel style system); API client/hooks/auth in `src/lib/`.
- **Quality bar (done = ):** `pnpm lint` clean, `tsc -b` passes, `pnpm build` succeeds, `pnpm test`
  green; new tests are Vitest + RTL, network mocked at the boundary, deterministic.
- **Accessibility baseline** (as above).
- **Planning-doc-before-code** process mirrored from the backend rules.

## Impact Analysis

- **New tooling/deps:** router, TanStack Query, Zustand, Radix primitives, i18next/react-i18next, RHF,
  Zod, Vitest + RTL + user-event + jsdom, MSW, oxlint-tsgolint, Prettier (final list per OQ answers).
- **Config:** `vite.config.ts` (proxy + Vitest), `.oxlintrc.json` (type-aware), `.env.development` +
  `.env.example`, `package.json` scripts, `tsconfig` types for Vitest/testing-library if needed.
- **Source:** new `src/` tree (`app/`, `routes/`, `lib/`, `features/auth/`, `components/ui/`,
  `styles/`, `theme/`, `i18n/`, `config/`, `test/`); default `App.tsx` demo removed/repurposed;
  `main.tsx` rewired.
- **Backend:** none required (feature-complete + CORS-ready). Depends on the dev backend running and
  the base URL/port from **OQ14**.
- **Design system:** this cycle lays the token contract + theming mechanism; the ui-designer fills in
  values/visuals on the OQ5 substrate.
- **Docs:** new `FairShareMonWeb/CLAUDE.md`; this planning doc.
- **Downstream:** every future frontend feature cycle inherits these locked choices; changing them
  later is a costly migration — hence the checkpoint before implementation.

## Decision Log

### Decision (pre-locked, not preference-dependent)

- Vitest + React Testing Library is the test stack (team decision, stated in the assignment).
- The scaffold baseline (React 19 + Compiler, Vite 8, TS 6 strict, oxlint, pnpm) is retained.

### Decision — stack locked at the 2026-07-16 checkpoint (all 14 OQs at the recommended option)

The user answered all 14 Open Questions at the 2026-07-16 checkpoint, accepting the recommended
option (a) for every one. These are now binding for the whole SPA; downstream agents build against
them. Full options/trade-offs are preserved in the annotated Open Questions above.

| OQ                   | Locked choice                                                                                                                                                                     |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| OQ1 Routing          | **React Router v7** (library mode)                                                                                                                                                |
| OQ2 Server state     | **TanStack Query v5** over a thin fetch client                                                                                                                                    |
| OQ3 Token storage    | **Access token in memory + refresh token in `localStorage`**, rehydrate on boot via `/auth/refresh`                                                                               |
| OQ4 Client state     | **Zustand** (auth/session store) + **React Context** (theme/locale)                                                                                                               |
| OQ5 Styling          | **CSS Modules + CSS-custom-property design tokens + Radix primitives**                                                                                                            |
| OQ6 i18n             | **react-i18next** (vi-VN default + en-US)                                                                                                                                         |
| OQ7 Forms            | **React Hook Form + Zod**                                                                                                                                                         |
| OQ8 Structure        | **Feature-first** (`src/features/<area>/` over shared `src/lib/`, `src/components/ui/`, `src/i18n/`, `src/routes/`)                                                               |
| OQ9 Dev networking   | **Vite `/api` dev proxy** (target `http://localhost:5200`)                                                                                                                        |
| OQ10 Theme           | **Light + dark now**, system default + persisted toggle                                                                                                                           |
| OQ11 Test mocking    | **MSW** at the network boundary                                                                                                                                                   |
| OQ12 Tooling         | **oxlint type-aware rules + Prettier** (formatting only)                                                                                                                          |
| OQ13 Cycle scope     | **Plumbing + thin auth vertical slice** + app shell + admin guard; other feature screens stubbed                                                                                  |
| OQ14 Dev backend URL | **`http://localhost:5200`** (committed `http` profile; HTTPS on `7114`); no prod deployment yet → **empty `VITE_API_BASE_URL` placeholder** in `.env.example`, set at deploy time |

**Reason:** each recommended option is the well-maintained, idiomatic choice for React 19 + Vite +
TS strict, and the API-client-centric, envelope/error-code, refresh, i18n, and business-rule
constraints from the API contract are all satisfiable on this stack (rationale per option in the
annotated Open Questions).

**Consequence:** implementation is unblocked. The ui-designer builds the design system on the OQ5
substrate (CSS Modules + CSS-custom-property tokens + Radix), and the web-implementer builds the Step
0–10 plan. Introducing any dependency not on this list is a new Open Question, not a silent decision.

## Progress Log

### 2026-07-16

- Feature-planner: completed required reading — `The-ideal.md` (§2/§3/§4), the `ApiResult` envelope
  (`Models/ApiResult.cs`), `ErrorCodes.cs` (full code map incl. tier `13xxx` + admin `14xxx`),
  `Program.cs` (auth scheme, CORS, localization, versioning), `CorsExtensions.cs` (credentialed
  dynamic-origin + dev localhost auto-allow), the auth contract (`AuthController.cs` +
  `planning/user-authentication.md`: token lifetimes, full pair rotation + reuse-detection, DTO
  shapes), representative controllers (`ExpensesController.cs`, `AdminController.cs`) and all
  controller routes, the localization + timezone planning docs (culture header/`?culture=`,
  `X-Time-Zone`), the current scaffold (`package.json`, `vite.config.ts`, `tsconfig*`, `.oxlintrc.json`,
  `src/`), and the sibling frontend agent definitions (web-implementer/-test-engineer/ui-designer/
  -code-reviewer) to match the doc structure they consume.
- Drafted this foundation plan: centralized API client, TanStack-Query data layer, auth session store,
  routing + guards, styling/token substrate for the ui-designer, i18n + VND/timezone formatters, RHF +
  Zod forms mirroring backend validators, Vitest + RTL + MSW harness, quality gates, and the
  `CLAUDE.md` contents.
- **14 Open Questions raised** (router, data-fetching, token storage, client-state tool, styling
  substrate, i18n lib, forms/schema, project structure, dev proxy vs CORS, theme scope, test mocking,
  formatting/tooling, cycle scope, and the dev backend base URL/port), each with 2–4 concrete options,
  one-line trade-offs, and a recommended option. Awaiting user answers at the checkpoint before
  implementation starts.

### 2026-07-16 (checkpoint — stack locked, plan unblocked)

- Checkpoint held; **the user answered all 14 Open Questions, accepting the recommended option (a) for
  every one.** Choices recorded in the Decision Log and annotated inline on each Open Question:
  React Router v7 · TanStack Query v5 + thin fetch client · access token in memory + refresh token in
  `localStorage` (rehydrate via `/auth/refresh`) · Zustand (session) + Context (theme/locale) · CSS
  Modules + CSS-custom-property tokens + Radix primitives · react-i18next (vi-VN default + en-US) ·
  React Hook Form + Zod · feature-first structure · Vite `/api` dev proxy · light + dark now (system
  default + persisted toggle) · MSW · oxlint type-aware + Prettier · plumbing + thin auth vertical
  slice.
- **OQ14 resolved (factual):** dev backend runs on `http://localhost:5200` (committed `http` launch
  profile; HTTPS on `7114`); the Vite dev proxy targets `http://localhost:5200`. No production
  deployment yet → `.env.example` ships an empty `VITE_API_BASE_URL` placeholder to set at deploy time
  (Step 1 updated accordingly).
- `UserResponse` still documents no `role` field — kept as a flag in Assumptions to confirm against
  the live login/refresh/user payload during the admin cycle (not blocking this foundation cycle).
- **No open questions remain.** Ready for Design (ui-designer builds the design system on the OQ5
  substrate) → Implement (web-implementer, Step 0–10).

### 2026-07-16 (design — ui-designer: design system established)

- **ui-designer** built the FairShareMon design system from scratch on the OQ5
  substrate (CSS Modules + CSS-custom-property tokens + Radix primitives), loaded
  the `artifact-design` and `dataviz` skills, and read `The-ideal.md` + this plan
  so the system covers every product surface (auth, members, expenses/shares,
  events, stats, wallet/QR, tiers, admin).
- **Tokens** (`src/styles/tokens.css`): a two-layer system (primitive ramps →
  semantic role tokens, `--fs-*` naming) covering color (light + dark, all key UI
  pairs verified AA — primary button 4.99:1, links 7.26:1, body 17.95:1, dark
  equivalents ≥ 4.76:1), typography (VN-tolerant scale + generous line-heights),
  spacing (4px grid), radii, shadows, motion/duration+easing, z-index, layout,
  and a **validated data-viz palette** (categorical/sequential/diverging + chart
  chrome) reserved for Stats/Admin — no chart components yet.
- **Data-viz palette validated** with the `dataviz` skill's script against the
  app's chart surfaces (light `#ffffff`, dark `#151d1b`): categorical passes both
  modes (worst adjacent CVD ΔE 9.1 light / 8.4 dark; normal-vision 19.6 / 19.3).
  Light-mode relief rule recorded (slots 3/4/5 < 3:1 → charts need direct labels
  or a table view).
- **Theme contract** (`[data-theme]` + `prefers-color-scheme`) implemented in CSS
  and documented in `src/styles/README.md`: light default, OS-dark auto unless
  forced, explicit toggle wins both ways. The React state that stamps the
  attribute + persists it is the **implementer's** (contract documented).
- **Primitives** (`src/components/ui/*`, CSS Modules, accessible, themed): Button,
  Spinner, TextField, Form/FieldStack/FormError/FormActions, Card, Badge, Money
  (VND, balance sign + color, tabular figures), Alert, Skeleton, EmptyState,
  ErrorState, UpgradePrompt + LimitNotice (tier affordances), Dialog + Toast
  (Radix), AppShell + AuthLayout + NavItem, ThemeToggle + LanguageToggle
  (presentational/controlled). Exported via `src/components/ui/index.ts`.
- **Deps added:** `@radix-ui/react-dialog`, `@radix-ui/react-toast` (the design
  layer's only additions; both on the OQ5-approved Radix list). Also did a clean
  `pnpm install` — the pre-existing `node_modules` was a stale symlink tree from
  the pre-monorepo path and blocked all installs.
- **Living style guide** (`src/styles/StyleGuide.tsx`) showcases every token group
  - primitive in both themes with real vi-VN copy; temporarily mounted in
    `App.tsx` (flagged — the implementer replaces it with the router in Step 7/10).
    Entry switched to `src/styles/global.css`; the scaffold demo `App.css`/
    `index.css` removed.
- **Gates green:** `tsc -b` clean, `pnpm lint` (oxlint) clean, `pnpm build`
  succeeds. Three non-blocking design decisions raised for user confirmation
  (OQ-D1 brand hue, OQ-D2 typography, OQ-D3 data-viz palette).

### 2026-07-16 (implement — web-implementer: foundation cycle Step 0–10 built)

- **web-implementer** built the full cross-cutting plumbing + the thin auth
  vertical slice (OQ13a) strictly per this plan, consuming the ui-designer's
  design system (no parallel style system introduced).
- **Step 0–1 (tooling/deps/config/env):** installed the locked stack only
  (react-router-dom · @tanstack/react-query · zustand · i18next + react-i18next ·
  react-hook-form + zod + @hookform/resolvers · dev: vitest + RTL + user-event +
  jsdom + @testing-library/{dom,jest-dom} + @vitest/coverage-v8 · msw ·
  oxlint-tsgolint · prettier). No unapproved deps. `.oxlintrc.json` →
  `options.typeAware: true` (verified against the schema; type-aware
  `no-floating-promises` fires). Added `.prettierrc.json`/`.prettierignore`,
  `format`/`test` scripts. `vite.config.ts` → `/api` dev proxy → `VITE_API_BASE_URL`
  (default `http://localhost:5200`), `@`→`src` alias, Vitest jsdom config.
  `.env.development` (proxy target + `VITE_ENABLE_MOCKS`) and `.env.example`
  (empty prod placeholder) committed. `src/config/env.ts` + `src/vite-env.d.ts`.
  MSW worker copied to `public/`; `pnpm-workspace.yaml` acknowledges the msw build
  so `pnpm <script>` runs clean.
- **Step 2–3 (theme/i18n):** `ThemeProvider` implements the `[data-theme]`
  contract (system/light/dark, persisted, pre-paint script in `index.html`).
  `LocaleProvider` + typed `useT()` hook (all 4 namespaces), react-i18next init
  (vi-VN default), typed-keys augmentation (`i18next.d.ts`), locale synced to
  `Accept-Language` + `<html lang>`, shared `formatMoneyVnd`/`formatDateTime`
  formatters + `getTimeZone`. vi-VN + en-US resources for common/auth/errors/validation.
- **Step 4–5 (session + API client):** Zustand **vanilla** session store (access
  in memory, refresh in localStorage, boot rehydrate) readable outside React;
  centralized typed client (`Authorization` + `X-Time-Zone` + `Accept-Language`
  injection, `ApiResult<T>` unwrap, typed `ApiError` with numeric `code` + camelCase
  `fields`, blob path for CSV/QR); `401 → refresh-once → retry → else clear +
redirect`, **de-duped behind one in-flight promise**; `ErrorCodes` TS mirror;
  `classifyError`/`resolveErrorMessage`/`applyFieldErrors` helpers.
- **Step 6–7 (data + routing):** configured `QueryClient` (no 4xx retry; client
  owns refresh). React Router v7 tree: public `/login`·`/register` (redirect if
  authed), authenticated `<AppShell>` layout with `/dashboard` + stubbed
  members/categories/tags/expenses/events/stats/wallet + `/settings/change-password`,
  admin sub-tree gated by `AdminRoute`, shared `NotFound` (`*` + ownership 1003),
  `ProtectedRoute` boot-splash + redirect. The router registers the client's
  session-expired → navigate('/login') seam.
- **Step 8 (auth slice):** Login/Register/ChangePassword pages with RHF+Zod schemas
  mirroring the backend validators (username 3–32 `^[a-zA-Z0-9_.-]+$`, password
  8 chars–**72 bytes** via `TextEncoder`); TanStack Query mutations; server
  `error.fields` mapped per field; `2001`/`2000`/`2003` branched on code; locale +
  theme toggles + logout wired in the shell.
- **Step 9–10 (harness + docs):** MSW handlers (envelope-shaped, dev + tests) +
  `setup.ts`/`renderWithProviders` scaffolding (specs are the web-test-engineer's).
  Authored `FairShareMonWeb/CLAUDE.md`. Replaced the temporary StyleGuide harness
  in the entry with the router/providers.
- **Gates green:** `pnpm lint` clean (exit 0; only 4 idiomatic fast-refresh
  `only-export-components` warnings on co-located provider+hook files, matching the
  plan's file layout), `tsc -b` clean, `pnpm build` succeeds.
- **Verified against the LIVE backend** (started on :5200 with the already-running
  MariaDB 3306 + Redis 6379). Drove the real UI headless over CDP (13/13 checks):
  boot redirect + vi-VN default → invalid-credentials (2001) message → client Zod
  validation → register (no auto-login) → login → dashboard → language toggle
  (EN copy + `<html lang>`) → theme toggle (`data-theme` + persisted) →
  change-password wrong (2003) → change-password success → forced re-login →
  protected redirect → NotFound. Separately verified **session rehydration on a
  fresh document load** (boot `/auth/refresh` re-authenticates; two consecutive
  fresh loads both stay on /dashboard, proving the rotated refresh token is
  persisted and reuse-detection isn't tripped). Confirmed via curl the full API
  contract: envelope shape, codes 2000/2001/2002/2003/1001 (+ camelCase `fields`),
  refresh rotation + reuse→2002, and `Accept-Language` flips backend message
  language (vi ↔ en).
- **No new Open Questions.** The `UserResponse` role-field gap is already tracked
  in Assumptions; the admin guard is built as a fail-safe seam and flagged in code
  - CLAUDE.md. **No deviations from the plan.**

### 2026-07-16 (test — web-test-engineer: automated coverage for the foundation cycle)

- **web-test-engineer** wrote the automated Vitest + RTL + MSW suite over the
  shipped foundation, reusing the implementer's harness unchanged (`src/test/
setup.ts` TZ pin `Asia/Ho_Chi_Minh`, `renderWithProviders`, MSW node server).
  Network is mocked only at the boundary (MSW) — every test exercises the REAL
  API client/refresh/session code. **11 test files, 81 tests, all green; run
  twice for determinism. `pnpm lint` exit 0 (only the 4 pre-existing
  fast-refresh warnings on product provider files), `tsc -b` clean.**
- **API client** (`src/lib/api/client.test.ts`, 13): success unwraps `data`;
  `isSuccess:false` throws typed `ApiError` with numeric `code` + camelCase
  `fields` + `httpStatus`; unexpected shape → 1000; network failure → Network
  code; header injection (`Authorization` present when authed / absent when
  `anonymous`, `X-Time-Zone`, `Accept-Language`); `401 → refresh-once → retry`
  succeeds and the retry carries the rotated token; concurrent 401s share ONE
  refresh (dedup counter); refresh `2002`/no-refresh-token clears session +
  fires the session-expired redirect once; blob path returns a `Blob` + parsed
  filename and still throws typed errors on failure.
- **Session store** (`src/lib/auth/session.test.ts`, 8): storage round-trip;
  `setSession` keeps access in memory + persists refresh only (access never in
  localStorage); `clearSession` wipes tokens/storage/user; boot rehydrate via
  `/auth/refresh` becomes authenticated + persists the rotated token; no-token
  rehydrate clears + signals redirect.
- **Error mapping** (`src/lib/api/http-error-handling.test.ts`, 12):
  `classifyError` (1003/ownership → notFound, 13003 → premiumRequired,
  13000/13001/13002 → limit, 1001 → validation, 1002 → unauthorized, network,
  1004/unknown → unexpected); `resolveErrorMessage` renders the localized
  server message verbatim, client copy only for network/non-ApiError;
  `applyFieldErrors` maps camelCased known fields onto RHF + collects unknowns
  form-level.
- **Auth schemas** (`src/features/auth/schemas.test.ts`, 13): username 3–32 +
  `^[a-zA-Z0-9_.-]+$`, password min-8 chars / **max-72 BYTES** (ASCII 73 fails,
  25×3-byte multibyte fails, exactly-72 passes), login presence-only, change
  new-password min.
- **Auth pages** (Login 6 / Register 4 / ChangePassword 3): valid login sets
  session + navigates + submits lowercased username; `2001` renders server
  message; `1001` maps field errors; empty submit blocked with no network;
  pending disables the submit (aria-busy); Enter submits. Register blocks bad
  username/72-byte password client-side, renders `2000`, and on success routes
  to `/login` with NO auto-login. ChangePassword renders `2003`, and on success
  clears the session + shows the re-login notice; short new password blocked.
- **Guards** (`src/routes/guards.test.tsx`, 10): ProtectedRoute idle→boot
  splash / unauth→login / authed→child; PublicOnlyRoute authed→dashboard,
  unauth→child; AdminRoute denies non-admin + no-role (fail-safe seam) and
  documents ADMIN admission once a role source exists; NotFound title/ownership
  body/back link.
- **Formatters** (`src/i18n/format.test.ts`, 8): VND vi-VN grouping + `₫`, 0
  fraction digits, string input, non-numeric passthrough; datetime rendered in
  the pinned UTC+7 zone (incl. a date that rolls forward), invalid ISO
  passthrough.
- **Locale** (`src/i18n/locale.test.tsx`, 3): vi-VN default copy; toggle to
  en-US switches UI copy + `getActiveLocale()` (Accept-Language) + `<html lang>`
  - persists; toggle back restores.
- **Theme** (`src/theme/theme.test.tsx`, 4): light/dark stamp `[data-theme]` +
  persist; system removes the attribute; radiogroup checked-state a11y.
- **Extras beyond the plan checklist:** the error-mapping unit suite, the
  standalone Zod-schema suite (multibyte 72-byte edge cases), PublicOnlyRoute,
  the AdminRoute ADMIN-admission seam test, and blob-path error handling.
- **No product bugs found** — all product code behaved to contract. No product
  code was modified; only test files were added. **Coverage gaps:**
  `useSessionBootstrap` is not driven directly (its module-level one-shot
  `bootstrapped` guard makes it un-repeatable across tests) — its mechanism is
  covered instead via `refreshOnce` rehydration in the session suite. The Radix
  Dialog primitive and the Premium `UpgradePrompt`/`LimitNotice` visuals are
  unwired this cycle (no screen consumes them yet) so only their code→intent
  classification is tested, not rendering.

### 2026-07-16 (unblock — backend shipped the current-user/role source)

- The carried-forward gap in this doc's **Final Outcome** and **Assumptions** (the
  admin `role` source, plus the `AdminRoute` fail-safe deny-all seam and the
  boot-rehydrate account-label nit — nit #3 from the close checkpoint) now has a
  **backend source, committed + pushed on origin/master.** See
  `FairShareMonApi/planning/expose-current-user-profile-and-role.md` (Final Outcome).
- What landed: `UserResponse` now carries **`role`** — the DTO is
  `{ uuid, username, tier, role, createdAt }` (camelCase JSON). A new **guarded
  `GET api/v1/auth/me`** → `ApiResult<UserResponse>` returns the caller's own
  profile from a **live DB read** (always-fresh tier/role); anonymous / revoked
  token → **401 `1002`**. **Login and refresh contracts are UNCHANGED**
  (`ApiResult<TokenPairResponse>`); `register` now also returns `role` (benign,
  always `USER` at registration).
- **Next move owned by the frontend team:** wiring `/auth/me` into the SPA is
  planned in a dedicated doc — see
  `FairShareMonWeb/planning/wire-current-user-profile.md` (fetch `/auth/me` after
  login + after boot-refresh rehydrate to populate the session `user`, activate
  `AdminRoute` off the now-present `role`, fix the reload account label, handle the
  `/auth/me` edge paths). The code-adjacent `CLAUDE.md` "Auth-guard seam (flagged)"
  note and the seam comments in `src/lib/api/types/auth.ts` / `src/lib/auth/session.ts`
  / `src/routes/AdminRoute.tsx` are tracked as implementer to-dos in that plan.

### 2026-07-16 — Close checkpoint

- Review returned **APPROVE, 0 blocking** (4 non-blocking nits). At the user
  checkpoint the design questions were resolved: **OQ-D1 keep jade/teal**,
  **OQ-D2 system font now** (webfont deferred), **OQ-D3 accept the blue-anchored
  data-viz plane** — all at the recommended option.
- Review nit #1 fixed (5 redundant `useCallback` wrappers removed from
  `ThemeProvider`/`LocaleProvider`/`ToastHost` per the no-manual-memoization
  convention); behavior unchanged. Gates re-verified: `pnpm lint` exit 0 (4 known
  fast-refresh warnings, no new), `tsc -b` clean, `pnpm test` **81/81**,
  `pnpm build` succeeds. Nits #2–#4 accepted (documented). Orchestrator committed
  the cycle. **Foundation cycle closed — the frontend team is ready for its first
  feature milestone.**

## Final Outcome

The frontend foundation cycle is **complete**. FairShareMonWeb now has the full
cross-cutting substrate — centralized typed API client (envelope unwrap, header
injection, de-duped 401→refresh→retry, blob path, error-code mirror), Zustand
session store (memory access + localStorage refresh + boot rehydrate), TanStack
Query data layer, React Router v7 routing with protected/admin/public guards +
shared NotFound, theme (`[data-theme]`) and locale (react-i18next, vi-VN default,
Accept-Language sync) providers, VND + timezone formatters, RHF+Zod forms
mirroring the backend validators, and an MSW test harness — plus the auth vertical
slice (login/register/logout/change-password) proving every concern end-to-end
against the live API. `pnpm lint`/`tsc -b`/`pnpm build` all pass and the flow was
exercised in a real browser. `FairShareMonWeb/CLAUDE.md` documents the locked
conventions for downstream agents. The one carried-forward item is the admin
`role` source (backend `UserResponse` exposes none yet) — implemented as a
fail-safe guard seam, to be wired in the admin cycle.

## Future Improvements

- Route-level code-splitting / lazy feature bundles as the app grows.
- Optimistic updates + granular cache invalidation for write-heavy screens (expenses/shares/events).
- A typed OpenAPI/Swagger client-generation step to auto-derive DTO types from the backend contract.
- Storybook (or equivalent) for the ui-designer's design-system components.
- E2E tests (Playwright) layered on top of the component/interaction suite.
- Error monitoring (Sentry or similar) and a global toast/notification center.
- Offline/PWA support for the mobile-first ledger use case.
- Secret-scanning-friendly handling and a "sessions/devices" management screen if the backend adds the
  session-list endpoint noted in the auth Future Improvements.
