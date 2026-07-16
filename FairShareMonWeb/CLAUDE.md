# CLAUDE.md — FairShareMonWeb

Guidance for Claude Code when working in the FairShareMonWeb SPA. This is the
web front-end for **FairShareMonApi** (a group expense ledger / debt-splitting
app, "Sổ ghi nợ chi tiêu"). The API contract, business rules, and domain terms
are the source of truth — see `FairShareMonApi/The-ideal.md` and the backend
controllers/models. Every locked decision below comes from
`planning/frontend-foundation.md` (all 14 OQs Resolved 2026-07-16). **Adding a
dependency the foundation didn't approve is an Open Question, not a decision.**

## Commands (workspace root = `FairShareMonWeb/`, package manager = pnpm)

```bash
pnpm dev            # Vite dev server (proxies /api → http://localhost:5200)
pnpm build          # tsc -b && vite build
pnpm lint           # oxlint (type-aware: oxlint-tsgolint)
pnpm format         # prettier --write .
pnpm test           # vitest run (web-test-engineer owns the specs)
```

- To exercise auth against the real API: run the backend
  (`dotnet run --project ./FairShareMonApi/FairShareMonApi/FairShareMonApi.csproj`
  from `FairShareMonApi/`, dev port **5200**) — needs MariaDB (3306) + Redis (6379).
- To run the UI without a backend: set `VITE_ENABLE_MOCKS=true` (MSW browser
  worker serves `src/test/msw/handlers.ts`).

## Stack (LOCKED — do not swap; new deps are an Open Question)

React **19** + React Compiler (write idiomatic components — **no** manual
`useMemo`/`useCallback` the compiler already covers), Vite **8**, TypeScript **6**
strict (`verbatimModuleSyntax` → use `import type`; `noUnusedLocals/Parameters`),
pnpm. Libraries: **React Router v7** (routing), **TanStack Query v5** (server
cache), **Zustand** (session store) + **React Context** (theme/locale),
**CSS Modules + CSS-custom-property tokens + Radix primitives** (styling —
the design system), **react-i18next** (vi-VN default + en-US), **React Hook Form

- Zod** (forms), **MSW** (mocking), **oxlint + Prettier** (lint/format).

## Project structure (feature-first)

```
src/
  app/          providers.tsx (composes QueryClient·Locale·Theme·Toast·Router),
                ToastHost (useToast), useSessionBootstrap
  config/       env.ts (API base URL resolution)
  lib/
    api/        client.ts (the ONE fetch wrapper), refresh.ts, errors.ts
                (ApiError + ErrorCodes mirror), http-error-handling.ts,
                runtime.ts (locale + session-expired seams), types/
    auth/       session.ts (Zustand vanilla store), storage.ts
    query/      queryClient.ts
  i18n/         index.ts (init), useT.ts (typed hook — use instead of
                useTranslation), format.ts (VND + datetime), LocaleProvider,
                i18next.d.ts, locales/{vi-VN,en-US}/{common,auth,errors,validation}.json
  theme/        ThemeProvider (data-theme contract)
  routes/       router.tsx, RootLayout, ProtectedRoute, AdminRoute,
                PublicOnlyRoute, AppShellLayout, NotFound, Forbidden, StubPage
  features/<area>/  api/ hooks/ pages/ schemas.ts components/
  components/ui/    design-system primitives (DO NOT fork a parallel style system)
  styles/       tokens.css, global.css (design system; owned by ui-designer)
  test/         setup.ts, utils.tsx (renderWithProviders), msw/
```

## API-contract rules (non-negotiable)

- **One centralized typed client** — `src/lib/api/client.ts`. Never scatter raw
  `fetch` in components/hooks. It injects `Authorization: Bearer <access>`,
  `X-Time-Zone` (IANA), and `Accept-Language` (active locale) on every request;
  unwraps the `ApiResult<T>` envelope (`{ data, isSuccess, error }`) → returns
  `data`, else throws a typed `ApiError`.
- **Branch on the numeric `error.code`, never on message text.** The code mirror
  is `src/lib/api/errors.ts` (`ErrorCodes`); `error.message` is already localized
  by the backend — render it verbatim (via `resolveErrorMessage`), only falling
  back to i18n copy for client-synthetic network/unexpected states.
- **Refresh** lives ONLY in the client: `401 → refresh-once → retry → else clear
session + redirect to /login`, de-duped behind a single in-flight promise
  (`src/lib/api/refresh.ts`). A failed refresh (incl. reuse-detection `2002`,
  which revokes all sessions) is terminal.
- **Field errors:** `1001` returns `error.fields` (camelCase). Map onto RHF fields
  via `applyFieldErrors`; unknown-field errors surface form-level.
- **Ownership 404 / `1003`** → the shared `NotFound` view (never leak existence).
  Free-tier limit `400`s (`13000/13001/13002`) → `<LimitNotice>`; Premium-gate
  `403` (`13003`) → `<UpgradePrompt>`. **Closed events are immutable** — disable
  every write control except the settled toggle. **Admin area is `role == ADMIN`
  only** (see the guard seam note below) and never shows other users' ledger data.
- **Binary responses** (CSV export, QR PNG) use `api.blob(...)` (returns a `Blob`
  - filename), never the JSON path.
- **Auth session (OQ3):** access token in **memory only**; refresh token in
  `localStorage`; rehydrate on boot via `/auth/refresh`. The Zustand vanilla store
  (`src/lib/auth/session.ts`) is readable outside React by the client.

### Auth-guard seam (wired)

The backend `UserResponse` is `{ uuid, username, tier, role, createdAt }` and
`GET /v1/auth/me` returns the caller's own profile (incl. `role`: `USER` |
`ADMIN`). `useCurrentUserQuery` (`src/features/auth/hooks/`) is mounted at
`ProtectedRoute` and fetches `/auth/me` once the session is authenticated — after
both login and boot-refresh rehydrate — syncing the profile into the Zustand
session store (`setUser`), which stays the canonical read for guards + shell.
`AdminRoute` admits `role === "ADMIN"` and denies otherwise; while the profile is
still resolving (`profileStatus` `idle`/`pending`) it shows the boot splash rather
than flash `Forbidden`. A **non-401** `/auth/me` failure stays authenticated but
degraded (`profileStatus: "error"`, no `user`) → the guard fails safe (denies),
never an infinite splash; a `401` rides the client's `401 → refresh` flow. Never
fabricate a role — an absent/unknown role is never ADMIN.

## Money & time

- **VND** via `formatMoneyVnd` (`src/i18n/format.ts`) — vi-VN grouping, 0 fraction
  digits. Render the API-computed value; **never do float math on money**.
- **Datetimes** are offset-aware ISO-8601; present in the viewer's timezone via
  `formatDateTime`/`formatDate`. Never format money/time ad hoc.

## i18n

- vi-VN default + en-US. **All user-facing copy through i18n** (no hardcoded
  strings). Use `useT()` (typed across all namespaces) — `t("auth:login.title")`.
- Locale is owned by `LocaleProvider`; on change it drives i18next, the client's
  `Accept-Language`, and `<html lang>`. **Fixed domain terms:** expense (phiếu chi
  tiêu), share (phần gánh), event (đợt), wallet/bank account (ví), settled (đã
  trả), Premium/Free — never voucher/record/batch.

## Theme

`[data-theme]` contract (`src/styles/README.md`): `system` removes the attribute
(follow OS), `light`/`dark` force + win. `ThemeProvider` persists the choice; a
pre-paint script in `index.html` applies it before React mounts (no flash).

## Styling

Reuse `src/components/ui/*` primitives (import from `@/components/ui`). **Never
fork a parallel style system.** Consume semantic tokens (`--fs-color-*`,
`--fs-space-*`, …), never raw ramp steps. Charts (future Stats/Admin) use the
`--fs-viz-*` palette + the `dataviz` skill.

## Accessibility baseline

Semantic landmarks, labeled controls (`<label for>` + `aria-describedby`), visible
focus, keyboard nav, color-independent status (icon/text/sign, not color alone),
`prefers-reduced-motion`, `<html lang>` synced to locale. Long Vietnamese text:
generous line-heights, `overflow-wrap`.

## Quality bar (done = )

`pnpm lint` clean, `tsc -b` type-checks, `pnpm build` succeeds, `pnpm test` green;
run the app and exercise the feature. New tests are Vitest + RTL, network mocked
at the client boundary (MSW), deterministic (pinned TZ + locale). Path alias
`@/*` → `src/*`.

## Process

Planning-doc-before-code (mirrors the backend rules): every feature/fix gets a doc
under `FairShareMonWeb/planning/` with Open Questions resolved before
implementation, and its Progress Log kept in sync. When something the doc doesn't
cover comes up and a reasonable engineer would ask — stop, record it under Open
Questions, and report back. Don't invent requirements or pick silent defaults.

```

```
