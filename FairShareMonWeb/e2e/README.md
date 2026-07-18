# E2E tests (Playwright)

End-to-end tests that drive the **real SPA** with the API mocked at the network
boundary by the committed MSW handlers (`src/test/msw/handlers.ts`). See
`planning/e2e-testing.md` for the full rationale and locked decisions.

## How it runs

Playwright's `webServer` boots the Vite **dev server** on a dedicated port
(`5199`) with `VITE_ENABLE_MOCKS=true`. The MSW browser worker only starts when
`import.meta.env.DEV` is true (dev server) **and** that env var is set, so the
mock backend is served with **zero `src/` changes**. A production `vite preview`
build would start with mocks disabled — always run E2E on the dev server.

The browser context pins `locale: "vi-VN"` and `timezoneId: "Asia/Ho_Chi_Minh"`
so money/datetime formatting, the client's `X-Time-Zone` header, and the
date-boundary handling are deterministic regardless of the host clock/zone.

## Commands

```bash
pnpm test:e2e:install   # one-time: download the Chromium binary
                        # (CI: pnpm exec playwright install --with-deps chromium)
pnpm test:e2e           # run headless (Chromium only)
pnpm test:e2e:ui        # interactive UI mode
pnpm test:e2e:report    # open the last HTML report
```

`pnpm test` stays **Vitest-only**. The two runners never overlap:

- **unit / component** — `src/**/*.test.{ts,tsx}` (Vitest + RTL)
- **E2E** — `e2e/**/*.spec.ts` (Playwright)

Vitest's `include` in `vite.config.ts` is pinned to `src/**` so it never
collects Playwright specs; Playwright's `testDir: "./e2e"` keeps it off the unit
tests.

## Seed users (MSW)

| Username   | Password      | Tier    | Role  | Notes |
|------------|---------------|---------|-------|-------|
| `demo`     | `password123` | FREE    | USER  | 3 active members + 1 soft-deleted; 5 categories ("Ăn uống" default). The ledger-loop spec uses this user. |
| `admin`    | `password123` | PREMIUM | ADMIN | Admin surfaces. |
| `degraded` | `password123` | —       | —     | `/auth/me` 500s (degraded profile path). |

## Conventions

- **Selectors are role/label-first.** User-facing copy is imported from
  `src/i18n/locales/vi-VN/*.json` via `e2e/fixtures/copy.ts` (the single source
  of truth — no hardcoded strings, no drift when copy changes). Interpolated
  keys (`{{name}}`) are resolved with the `interpolate` helper.
- **`data-testid` only for value/role-ambiguous nodes.** The balance table
  exposes `event-balance-row`, `balance-amount`, and `event-balance-total` so the
  spec can parse the vi-VN money back to integers and assert sum-to-zero.
- **Isolation:** each test gets a fresh Playwright context; the initial
  navigation re-seeds the in-page MSW store. **Never `page.reload()` mid-flow** —
  it wipes the in-memory store and the in-memory access token. Navigate only via
  the app's own client-side routing (nav links / in-app buttons).

## Fixtures

- `fixtures/copy.ts` — vi-VN locale copy + `interpolate`.
- `fixtures/session.ts` — `login(page, { username, password })` drives `/login`.
- `fixtures/test.ts` — extends Playwright `test` with an `appPage` fixture that
  logs in as `demo` and lands on `/dashboard`.
