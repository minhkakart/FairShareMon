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

## Projects — desktop + mobile viewport (responsive-mobile-polish, OQ6a)

Two Playwright projects run off the same MSW dev-server harness:

| Project    | Device                     | Runs                                              |
|------------|----------------------------|---------------------------------------------------|
| `chromium` | Desktop Chrome (~1280px)   | `ledger-loop.spec.ts` (excludes header-responsive)|
| `mobile`   | Pixel 5 (393px, touch)     | `ledger-loop.spec.ts` **+** `header-responsive.spec.ts` |

The `mobile` project re-runs the full ledger loop at a phone viewport (proving
the `ExpensesTable` card-stack reflow and the drawer-driven navigation on a real
small viewport) and adds `header-responsive.spec.ts`. The Pixel 5 preset sets
viewport / UA / `isMobile` / `hasTouch` but NOT locale or timezone, so the
`mobile` project re-pins `locale: "vi-VN"` + `timezoneId: "Asia/Ho_Chi_Minh"`
explicitly (see `playwright.config.ts`) — keep them if you edit that project.

**`header-responsive.spec.ts` is phone-only.** Its assertions describe the
*collapsed* header (brand + hamburger, secondary actions relocated to the
drawer footer), which only exists below the `lg` / 64rem nav breakpoint. It is
pinned to the `mobile` project by a `testIgnore: /header-responsive\.spec\.ts$/`
on the `chromium` project — that is the single, declarative place the split
lives (no per-spec `test.use({ viewport })`), so the desktop project never runs
phone-shaped assertions.

**Viewport-agnostic navigation.** Below the nav breakpoint the header hides its
inline `<nav>` and navigates through the hamburger drawer, so the shared
`gotoNav(page, name)` helper (`fixtures/session.ts`) opens the drawer first on a
collapsed viewport, then clicks the SAME nav-link selector inside it; at/above
`lg` it clicks the inline link directly. `login()`'s readiness assertion is
likewise viewport-aware (hamburger below `lg`, inline nav link above). This lets
the one `ledger-loop.spec.ts` run byte-identical selectors under both projects.

## Commands

```bash
pnpm test:e2e:install   # one-time: download the Chromium binary
                        # (CI: pnpm exec playwright install --with-deps chromium)
pnpm test:e2e           # run headless (both projects: chromium + mobile)
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
- `fixtures/session.ts` — `login(page, { username, password })` drives `/login`
  (viewport-aware readiness); `navLink(page, name)` (inline nav-link locator);
  `gotoNav(page, name)` (viewport-agnostic navigation — drawer below `lg`).
- `fixtures/test.ts` — extends Playwright `test` with an `appPage` fixture that
  logs in as `demo` and lands on `/dashboard`.
