# E2E Testing — Playwright harness + the ledger-loop spec

## Objective

Wire an **end-to-end (E2E) test layer** into the FairShareMonWeb SPA so that the
broader "improve frontend UX/UI" effort can proceed with a safety net. Concretely,
this milestone delivers two things and nothing more:

1. **The harness** — Playwright installed and configured to drive the real SPA with
   the API mocked at the network boundary by **reusing the existing MSW handlers**
   (`src/test/msw/handlers.ts`), deterministic (pinned locale + timezone), coexisting
   with the existing Vitest + MSW unit suite without file-glob collision, and slotted
   into the quality bar via new `pnpm` scripts.
2. **The first spec — the full ledger loop:** login → add member → add expense (with
   shares) → create event → assign the expense to the event → close the event → verify
   the debt-balance sums to zero. One test that exercises M2 (members), M4
   (expenses/shares), and M5 (events + balance + close) together against the real
   client, real router, real forms, and the committed MSW mock backend.

The whole point is **regression protection before UX churn**: once this loop is green,
later restyling/reflowing of these screens can be validated end-to-end.

## Background

- **This is the first item of the "improve frontend UX/UI" effort.** E2E coverage lands
  first so the subsequent visual/interaction changes are safe. The scope here is
  deliberately narrow — harness + one flow — with the broader flow catalogue deferred
  to Future Improvements.
- **Three foundational decisions are already locked by the user** (recorded as Resolved
  in the Decision Log, not reopened as Open Questions):
  1. **Framework = Playwright** (`@playwright/test`). A new dev dependency, explicitly
     approved — this overrides the CLAUDE.md "new dep = Open Question" rule for this one
     case.
  2. **Run strategy = MSW-mocked.** Drive the real SPA with the API mocked at the network
     boundary by reusing the existing MSW handlers. No real backend / MariaDB / Redis.
     Must be deterministic (pinned timezone + locale).
  3. **First spec = the full ledger loop** (the 7 steps above), exercising M2/M4/M5.
- **The stack + conventions are locked** in `planning/frontend-foundation.md` (14 OQs
  Resolved 2026-07-16) and summarized in `FairShareMonWeb/CLAUDE.md`: React 19 + Vite 8
  + TS 6 strict, React Router v7, TanStack Query v5, Zustand session store,
  react-i18next (vi-VN default + en-US), RHF + Zod, MSW, oxlint + Prettier, Vitest + RTL
  for unit/component tests. Package manager **pnpm 11.14.0**, Node **>= 24.18.0**.
- **The MSW mock is already a full stateful in-memory backend.** `src/test/msw/handlers.ts`
  (2 657 lines) implements envelope-shaped handlers for auth, members, categories, tags,
  expenses + shares, events (+ `/balance`, `/close`, `/export`), expense↔event
  assign/remove, stats, bank accounts, QR, and admin. State is held in module-level
  `Map`s **keyed by username**, seeded lazily on first access. This is the same handler
  set the Vitest suite uses (`src/test/msw/server.ts`, Node) and the same set the dev
  browser worker uses (`src/test/msw/browser.ts`). **Reuse is the whole strategy — no
  parallel mock is created.**
- **How the browser worker starts today** (`src/main.tsx`):
  ```ts
  async function enableMocks(): Promise<void> {
    if (!import.meta.env.DEV || import.meta.env.VITE_ENABLE_MOCKS !== "true") return;
    const { worker } = await import("@/test/msw/browser");
    await worker.start({ onUnhandledRequest: "bypass" });
  }
  ```
  The worker script is committed at `public/mockServiceWorker.js` (registered per the
  `"msw": { "workerDirectory": ["public"] }` block in `package.json`). React does not
  mount until `enableMocks()` resolves. **Load-bearing finding:** the mock start is gated
  on `import.meta.env.DEV`, which is `true` only under `vite` (dev server) and `false`
  under `vite build` → `vite preview`. So **the E2E harness must run against the dev
  server (`vite` with `VITE_ENABLE_MOCKS=true`)** to get mocks with zero code changes;
  serving a production build via `vite preview` would silently start with mocks disabled
  and fall through to the (absent) backend proxy. This drives OQ1.
- **The ledger loop maps cleanly onto real routes + committed handlers** (verified
  against the source):

  | Step | Route / UI | Verb + path (client → MSW) | Notes |
  |------|-----------|----------------------------|-------|
  | Login | `/login` — `LoginPage` | `POST /api/v1/auth/login` then `GET /api/v1/auth/me` | seed user `demo` / `password123` (FREE, USER). Session: access in memory, refresh in localStorage. |
  | Add member | `/members` — `MembersPage` → `MemberFormDialog` | `POST /api/v1/members` | `demo` seeds 3 active members (owner-rep "Bạn (chủ sổ)", "An Nguyễn", "Bình Trần") + 1 soft-deleted; FREE cap is 5, so one add succeeds. |
  | Add expense (+ shares) | `/expenses/new` — `ExpenseCreatePage` | `POST /api/v1/expenses` (atomic: general + shares) | Owner-rep 0đ share auto-injected by the mock if omitted. On success navigates to `/expenses/:uuid`. |
  | Create event | `/events` — `EventsPage` → `EventFormDialog` | `POST /api/v1/events` | On success `onCreated` navigates to `/events/:uuid`. FREE open-event cap is 3. Range stored as whole-day UTC bounds. |
  | Assign expense → event | `/events/:uuid` — `EventExpensesSection` → `AssignExpenseDialog` ("Gán phiếu") | `PUT /api/v1/expenses/:uuid/event` | Target must be **open** and expense `expenseTime` **within** `[startDate, endDate]`; else `9002`. See the date-boundary assumption below. |
  | Close event | `/events/:uuid` — `CloseEventDialog` ("Chốt") | `PUT /api/v1/events/:uuid/close` | One-way; re-close → `9001`. |
  | Verify balance = 0 | `/events/:uuid` — `EventBalanceTable` | `GET /api/v1/events/:uuid/balance` | `balance = advanced − owed` per member; the row set **sums to zero by construction** (Σadvanced = Σowed = Σ expense totals). Deterministic integer VND. |

- **Selector reality on the pages** (grep: only 5 `data-testid` in the whole `src` tree,
  all in existing Vitest specs): the app is built role/label-first — controls carry
  i18n copy, forms use `<label>`-associated inputs (`TextField`), tables use
  `caption`/`scope`, dialogs are Radix. The event balance table (`EventBalanceTable`) and
  a few dynamic-value cells do **not** yet expose stable hooks. This milestone adds a
  **small, curated set of `data-testid`s** only where role/text is ambiguous or value-
  dependent (see Implementation Plan). This is a test-affordance addition owned by the
  feature/test engineer, not a redesign.
- **Vitest glob collision risk (load-bearing).** `vite.config.ts` sets no explicit
  Vitest `include`, so Vitest uses its default `**/*.{test,spec}.?(c|m)[jt]s?(x)` — which
  **would collect Playwright `*.spec.ts` files** wherever they live. Every existing unit
  spec is named `*.test.ts(x)` under `src/`. The plan pins Vitest to `src/**` and puts
  Playwright specs under `e2e/**` with `*.spec.ts`, so neither runner sees the other's
  files.
- **Type/lint reality:** `tsconfig.app.json` includes only `src` (so `e2e/` is already
  outside the `tsc -b` app build); `tsconfig.node.json` includes only `vite.config.ts`.
  oxlint currently lints the repo with `ignorePatterns: ["dist","node_modules",
  "public/mockServiceWorker.js"]` — it will start linting `e2e/**` once that dir exists,
  which we want (with a Playwright-aware tweak, below).

## Requirements

### Functional (harness)

- **R1** — Add `@playwright/test` as a dev dependency; document browser install
  (`playwright install chromium`, `--with-deps` in CI). No other runtime dep added.
- **R2** — A `playwright.config.ts` at the `FairShareMonWeb/` root that:
  - starts the SPA under test via `webServer` (see OQ1: dev server + `VITE_ENABLE_MOCKS`),
  - sets a `baseURL`,
  - **pins `locale: "vi-VN"` and `timezoneId: "Asia/Ho_Chi_Minh"`** on the browser context
    (mirrors the Vitest `process.env.TZ` pin so datetime/money formatting and the client's
    `X-Time-Zone` / `Accept-Language` headers are deterministic and match the vi-VN-first
    UI copy the specs assert on),
  - runs **Chromium only** first (OQ2),
  - has sane CI settings (`retries`, `forbidOnly`, single worker or bounded, trace on
    first retry).
- **R3** — Reuse the committed MSW handlers **unchanged** via the existing dev browser
  worker path. No new handler file; no fork. Any genuinely missing capability is called
  out as a gap (none found for the ledger loop — see Impact Analysis).
- **R4** — Per-test determinism and isolation: each test starts from the deterministic
  seed with no cross-test bleed (see the isolation strategy in the Implementation Plan
  and OQ3).
- **R5** — No collision with the Vitest suite: Playwright never collects `src/**/*.test.*`;
  Vitest never collects `e2e/**/*.spec.*`. Both `pnpm test` (Vitest) and `pnpm test:e2e`
  (Playwright) run their own files only.
- **R6** — New `pnpm` scripts: `test:e2e`, `test:e2e:ui`, `test:e2e:report`, and a browser
  install helper. The existing `pnpm test` stays Vitest-only (the E2E layer is additive to
  the quality bar, not folded into the unit command).
- **R7** — A documented, stable **selector strategy** (role/label-first, i18n copy pulled
  from the locale JSON as the single source of truth, `data-testid` only for dynamic/
  ambiguous values), plus fixtures/helpers so specs are readable and not copy-brittle.

### Functional (the ledger-loop spec)

- **R8** — One spec, `e2e/ledger-loop.spec.ts`, that performs the 7 steps end-to-end and
  asserts: the member appears, the expense is created and reachable, the event is created,
  the expense shows under the event, the event flips to closed (write controls disappear),
  and the balance table's rows **sum to zero**.
- **R9** — The spec drives only through the UI (no direct MSW/store poking); it reads the
  seed user `demo`. It must be green repeatably and not depend on wall-clock time (see the
  date-boundary assumption).

### Non-functional

- **R10** — Deterministic and hermetic: no network beyond the local dev server; no real
  backend, DB, or Redis. CI-runnable headless.
- **R11** — Fits the quality bar: `pnpm lint`, `tsc -b`, `pnpm build`, `pnpm test` all stay
  green; `pnpm test:e2e` is the new gate. Playwright artifacts (`test-results/`,
  `playwright-report/`, `.playwright/`) are gitignored.
- **R12** — Zero production-bundle impact: no E2E/Playwright code is importable from
  `src/`; the mock path stays `import.meta.env.DEV`-gated exactly as today.

## Open Questions

> Locked decisions (framework = Playwright, run strategy = MSW-mocked, first spec = ledger
> loop) are **not** reopened here — see the Decision Log. Only genuinely
> preference-dependent choices follow, each with a recommendation.

- **OQ1 — Which server does Playwright drive: the dev server or a preview build?**
  - **(a, Recommended)** `webServer.command = "pnpm dev"` with `env: { VITE_ENABLE_MOCKS:
    "true" }`. Works today with **zero code change** because the mock start is
    `import.meta.env.DEV`-gated. Fast startup (no build step), and it is still "the real
    SPA" (same components/router/client). Trade-off: it is the dev bundle, not the exact
    production artifact (no minify/React-Compiler-prod path), and HMR/websocket noise
    exists (harmless — MSW `bypass` lets Vite's own requests through).
  - **(b)** `vite preview` of a production `vite build`. Closest to the shipped artifact,
    but **requires a small `src/main.tsx` change** to also start the worker when
    `VITE_ENABLE_MOCKS === "true"` in a non-DEV build (e.g. gate on the env var alone, or
    add a dedicated `--mode e2e`). That risks shipping mock-enabling code into prod builds
    unless carefully mode-scoped, and pulls `src/` into the change (against the "harness
    only" scope). Defer to Future Improvements if we later want prod-parity E2E.
  - *Impact:* picks the `webServer` block, the port, and whether any `src/main.tsx` tweak
    is in scope.

- **OQ2 — Browser matrix: Chromium-only first, or add WebKit/Firefox now?**
  - **(a, Recommended)** Chromium-only for this milestone (matches "single-browser-first").
    Fastest to land and to run in CI; the goal is flow-regression safety, not cross-engine
    coverage.
  - **(b)** Add WebKit + Firefox projects now. More coverage, ~3× runtime and 3× browser
    downloads in CI, and cross-engine flakiness to babysit before the harness has even
    proven itself.
  - *Impact:* the `projects` array and CI install/runtime budget.

- **OQ3 — Per-test state isolation: rely on Playwright context isolation + fresh page
  load, or add an explicit in-browser mock-reset seam?**
  - **(a, Recommended)** Rely on Playwright's default per-test **fresh browser context +
    fresh document load**. The MSW store is module-level JS in the page; a new context and
    a full navigation re-import the module → stores reset to the deterministic seed. The
    spec logs in as `demo` and gets the clean seed every test. **Constraint the spec must
    honor:** navigate between steps via the app's own client-side routing (no
    `page.reload()` mid-flow, which would wipe the in-memory store and the in-memory access
    token). No code change.
  - **(b)** Add a dev-only `POST /__mock/reset` handler (or a `window.__mockReset()` hook)
    to reset the member/expense/event stores on demand. More explicit and enables multi-
    flow specs that share a context, but it is **new mock code** and the current handlers
    export resets only for the Node/admin path (`resetAdminStore`, `registerTestProfile`) —
    there is no browser-facing reset for members/expenses/events today. Defer unless (a)
    proves flaky.
  - *Impact:* whether any handler is added, and the per-test setup helper shape.

- **OQ4 — How do specs reference user-facing copy (vi-VN)?**
  - **(a, Recommended)** Import the vi-VN locale JSON
    (`src/i18n/locales/vi-VN/*.json`) into a small selector-helper module and resolve keys
    there, so `getByRole('button', { name: common.someKey })` tracks the same source of
    truth the app renders — no drift when copy changes.
  - **(b)** Hard-code vi-VN strings in the spec. Simplest, but brittle: any copy edit
    silently breaks E2E.
  - **(c)** Rely purely on `data-testid`/roles and avoid copy entirely. Most robust to copy
    changes but couples tests to structure and needs more testids sprinkled into
    components.
  - *Recommendation:* **(a)** for text-bearing controls, **plus** a curated few
    `data-testid`s (per R7) for value-dependent cells (balance rows/total). Mostly (a),
    selectively (c).
  - *Impact:* the helper module and how many `data-testid`s land in components.

- **OQ5 — Does E2E gate CI now, and how?**
  - **(a, Recommended)** Add `pnpm test:e2e` (Chromium, headless, `--with-deps` install) as
    a **CI job in the same pipeline that runs lint/tsc/build/test**, blocking merges once
    green. *Caveat:* there is **no `.github/workflows` (or other CI config) visible in this
    repo** — deployment is Docker-based per recent commits — so the concrete wiring depends
    on where the frontend CI actually lives. Need the user/orchestrator to point at the
    pipeline.
  - **(b)** Keep E2E local-only initially (developer-run + pre-merge manual), wire CI in a
    follow-up once the harness is proven.
  - *Impact:* CI job definition, cache of the Playwright browser binary, and whether the
    gate is blocking.

- **OQ6 — Dev-server port for `webServer`.**
  - **(a, Recommended)** A dedicated fixed port (e.g. `5199`) distinct from Vite's default
    `5173`, with `reuseExistingServer: !process.env.CI`, so a developer's running `pnpm
    dev` on 5173 doesn't collide and Playwright can boot its own instance.
  - **(b)** Reuse `5173`. Simpler config, but clashes with a hand-run dev server.
  - *Impact:* `webServer.url` + `use.baseURL`. (Minor — folded here rather than assumed
    silently because it touches config the reviewer will see.)

## Assumptions

- **A1** — Playwright's default per-test context isolation + a full initial navigation
  yield a clean MSW seed per test (basis for OQ3a). If observed flaky, escalate to OQ3b.
- **A2** — The `demo` seed user (`password123`, FREE/USER, 3 active members, 5 categories
  with "Ăn uống" default, 2 tags) is stable and sufficient for the ledger loop; the spec
  does not need `admin`/`degraded`.
- **A3 — Date-boundary determinism (load-bearing).** The assign step requires the
  expense's `expenseTime` to fall inside the event's whole-day UTC bounds
  (`[YYYY-MM-DDT00:00:00.000Z, YYYY-MM-DDT23:59:59.999Z]`). The `ExpenseCreatePage` defaults
  `expenseTime` to **now** (local, Asia/Ho_Chi_Minh = UTC+7). To avoid the midnight-UTC
  edge (where "today" local can be a different UTC calendar day), the spec will **set an
  explicit expense time at midday** (e.g. `12:00`) of a **fixed in-range date**, and create
  the event with `startDate`/`endDate` on that same date. At noon +07 the UTC instant
  (05:00Z) is the same calendar day, so it is safely inside the day bounds regardless of
  when the suite runs. (Optionally, Playwright `page.clock` can freeze time, but an explicit
  in-form value is simpler and sufficient.)
- **A4** — The balance-sums-to-zero invariant holds structurally in the mock
  (`computeBalance`): `Σ balance = Σ advanced − Σ owed = Σ expenseTotal − Σ expenseTotal =
  0`, in integer VND — so the assertion is exact, not approximate.
- **A5** — `import.meta.env.DEV` is `true` under `vite`/`pnpm dev` and drives the mock
  worker start; this is why OQ1a needs no code change.
- **A6** — The MSW `onUnhandledRequest: "bypass"` in `main.tsx` is acceptable for E2E: all
  `*/api/v1/*` calls in the loop are handled; Vite's own asset/HMR requests are the only
  bypassed traffic and hit the dev server legitimately. (A stricter "warn"/"error" mode
  would need a `main.tsx` change — out of scope; noted in Future Improvements.)
- **A7** — Node 24 + pnpm 11 is the toolchain; `@playwright/test` and the Chromium binary
  install cleanly there.

## Implementation Plan

> Files are relative to `FairShareMonWeb/`. Steps 1–6 are the harness; steps 7–9 are the
> ledger-loop spec + affordances; step 10 is docs/quality-bar wiring.

### 1. Add the dependency + browser install

- `pnpm add -D @playwright/test`.
- Browsers: local devs run `pnpm exec playwright install chromium`; CI runs
  `pnpm exec playwright install --with-deps chromium`. Add a convenience script
  `"test:e2e:install": "playwright install chromium"` (CI overrides with `--with-deps`).
- Do **not** add any Playwright runtime import to `src/`.

### 2. Directory layout

```
FairShareMonWeb/
  playwright.config.ts          # NEW — Playwright config (root)
  e2e/
    tsconfig.json               # NEW — extends base, types: ["@playwright/test","node"]
    fixtures/
      test.ts                   # NEW — extends @playwright/test `test` with app fixtures
      session.ts                # NEW — login helper (drives /login as `demo`)
      copy.ts                   # NEW — loads src/i18n/locales/vi-VN/*.json for selectors (OQ4a)
    ledger-loop.spec.ts         # NEW — the one spec (R8)
    README.md                   # NEW — how to run, seed users, selector convention
```

- No new files under `src/test/msw/` — the browser worker (`src/test/msw/browser.ts`) is
  reused as-is via `VITE_ENABLE_MOCKS=true`.

### 3. `playwright.config.ts`

- `testDir: "./e2e"` (scopes Playwright away from `src/**/*.test.*` — R5).
- `use: { baseURL: "http://localhost:5199", locale: "vi-VN", timezoneId:
  "Asia/Ho_Chi_Minh", trace: "on-first-retry" }` (R2; pinned locale/TZ per A3).
- `webServer: { command: "pnpm dev --port 5199", url: "http://localhost:5199",
  env: { VITE_ENABLE_MOCKS: "true" }, reuseExistingServer: !process.env.CI,
  timeout: 120_000 }` (OQ1a + OQ6a).
- `projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }]` (OQ2a).
- CI knobs: `forbidOnly: !!process.env.CI`, `retries: process.env.CI ? 2 : 0`,
  `workers: process.env.CI ? 1 : undefined`, `reporter: [["html", { open: "never" }],
  ["list"]]`.

### 4. Vitest / Playwright separation (the collision fix — R5)

- Edit `vite.config.ts` Vitest block to pin the include to source only:
  `test: { ...existing, include: ["src/**/*.{test,spec}.{ts,tsx}"] }`. This stops Vitest
  from ever collecting `e2e/**/*.spec.ts`.
- Playwright's `testDir: "./e2e"` (step 3) already stops Playwright from collecting the
  `src` unit tests.
- Net convention: **unit/component = `src/**/*.test.tsx` (Vitest)**, **E2E =
  `e2e/**/*.spec.ts` (Playwright)**. Document in `e2e/README.md` and CLAUDE.md's test note.

### 5. TypeScript + lint wiring

- `e2e/tsconfig.json`: extends the repo base, `compilerOptions.types:
  ["@playwright/test","node"]`, `include: ["**/*.ts"]`. Keeps E2E types isolated from the
  DOM/Vitest app types and keeps `e2e/` out of the app `tsc -b` (which includes only `src`).
- oxlint: `e2e/**` will be linted once created (no ignore added). If Playwright's
  `test`/`expect` globals or `*.spec.ts` patterns trip a rule, scope an override in
  `.oxlintrc.json` for `e2e/**` (e.g. relax `only-export-components`, which is
  React-specific and irrelevant to spec files). Prefer the narrowest override.

### 6. `.gitignore` additions

- Append `test-results/`, `playwright-report/`, `blob-report/`, `playwright/.cache/` (R11).

### 7. Fixtures + helpers

- `e2e/fixtures/copy.ts` — import `vi-VN` namespace JSON
  (`common`, `auth`, `members`, `expenses`, `events`, …) and expose typed accessors so
  specs select by the exact rendered copy (OQ4a). (Interpolated keys like
  `events:expensesSection.removeNamed` are resolved with the same `{{name}}` substitution.)
- `e2e/fixtures/session.ts` — `login(page, { username = "demo", password =
  "password123" })`: goes to `/login`, fills the username/password `TextField`s (by label
  `auth:login.username` / `auth:login.password`), submits `auth:login.submit`, and awaits
  the redirect to `/dashboard`.
- `e2e/fixtures/test.ts` — `export const test = base.extend<{ appPage: Page }>(...)`
  bundling the login + a `page` that has already navigated to the app shell, so specs read
  cleanly.

### 8. Test-affordance additions (small, curated — R7)

Add stable hooks **only** where role/text can't uniquely + robustly target a value:

- `EventBalanceTable` (`src/features/events/components/EventBalanceTable.tsx`): add
  `data-testid="event-balance-row"` on each member row and `data-testid="balance-amount"`
  on the balance cell; if a total/footer row exists, `data-testid="event-balance-total"`.
  The spec reads all `balance-amount` cells, parses the vi-VN-formatted VND back to
  integers, and asserts the sum is `0` (A4) — or, if a total row is present, asserts it
  renders `0` (`0 ₫`).
- (Only if needed after a first pass) a `data-testid` on the created member row and the
  event-expenses row; prefer the existing `<Link>`/`scope="row"` text selectors first.

These are additive `data-testid` attributes (no behavior/markup change) owned by the
feature/test engineer, consistent with the 5 pre-existing testids in the codebase.

### 9. `e2e/ledger-loop.spec.ts`

One `test("ledger loop: member → expense → event → assign → close → balance = 0", …)`:

1. **Login** — `login(page)` (helper). Assert the app shell renders (`/dashboard`).
2. **Add member** — navigate to `/members` (nav link), click `members:add`, fill the
   `MemberFormDialog` name field with a fixed name (e.g. `"Chi Lê"`), submit. Assert the
   new row appears in the members table. (`POST /api/v1/members` → member response; on
   success the members query invalidates and the row renders.)
3. **Add expense (+ shares)** — go to `/expenses/new`, fill `name` (e.g. `"Ăn tối nhóm"`),
   **set `expenseTime` to a fixed midday in-range value** (A3), keep payer = owner-rep
   default, keep default category, add shares in the `ShareEditor` (e.g. split a total
   across two members so at least one member ends non-zero), submit `expenses:form.
   submitCreate`. Assert redirect to `/expenses/:uuid` and the expense detail shows the
   name + total. (`POST /api/v1/expenses`, atomic.)
4. **Create event** — go to `/events`, click `events:add`, fill the `EventFormDialog`
   name + `startDate`/`endDate` covering the expense's date (A3), submit. Assert redirect
   to `/events/:uuid`. (`POST /api/v1/events`.)
5. **Assign expense → event** — on the event detail, in `EventExpensesSection` click
   `events:expensesSection.assign` ("Gán phiếu"), pick the expense in `AssignExpenseDialog`,
   confirm. Assert the expense now appears in the event's expenses table.
   (`PUT /api/v1/expenses/:uuid/event`; in-range so no `9002`.)
6. **Close event** — click `events:detail.close` ("Chốt"), confirm in `CloseEventDialog`.
   Assert the status badge flips to closed and the write controls (edit/delete/close/assign/
   remove) are gone — this exercises the closed-event immutability UI directly.
   (`PUT /api/v1/events/:uuid/close`.)
7. **Verify balance = 0** — read the `EventBalanceTable` (`GET /api/v1/events/:uuid/
   balance`). Assert there is ≥1 row, and the sum of the `balance-amount` cells (parsed
   from vi-VN VND) is exactly `0` (A4). If a total row exists, additionally assert it shows
   `0 ₫`.

Error/loading handling in the spec: rely on Playwright web-first assertions
(`await expect(locator).toBeVisible()`) which auto-wait for TanStack Query settle →
render, rather than fixed sleeps.

### 10. Scripts + docs + quality bar

- `package.json` scripts (R6): `"test:e2e": "playwright test"`,
  `"test:e2e:ui": "playwright test --ui"`, `"test:e2e:report": "playwright show-report"`,
  `"test:e2e:install": "playwright install chromium"`. Leave `"test": "vitest run"`
  unchanged.
- `e2e/README.md`: how to install browsers, run headed/headless, the seed users, the
  role/copy/testid selector convention, and the "no mid-flow reload" isolation rule (OQ3a).
- CLAUDE.md test note: one line pointing at the split (unit = `src/**/*.test.*` Vitest;
  E2E = `e2e/**/*.spec.*` Playwright, MSW-mocked, vi-VN + Asia/Ho_Chi_Minh pinned).
- CI: wire `test:e2e` per OQ5 once the pipeline location is confirmed.

## Impact Analysis

- **Additive, low blast radius.** New files (`playwright.config.ts`, `e2e/**`) plus two
  tiny config edits (`vite.config.ts` Vitest `include`, `.gitignore`, `.oxlintrc.json`
  override if needed) and a handful of additive `data-testid`s on `EventBalanceTable`
  (and possibly member/expense rows). No runtime `src/` logic changes, no API-contract
  changes, no production-bundle impact (R12).
- **The one behavioral config change is the Vitest `include` narrowing** — verify the
  existing Vitest suite still collects all `src/**/*.test.tsx` after the change (it will;
  every unit spec lives under `src/`). Without this change, adding `e2e/*.spec.ts` would
  break `pnpm test` by pulling Playwright files into Vitest — this is the single most
  important guardrail.
- **MSW handler reuse verified sufficient for the ledger loop** — every step maps to a
  committed handler (see the Background table). **No gap found**; no handler edit required.
  The only nuance is date-boundary alignment, handled in the spec (A3), not in the mock.
- **OQ1 is the pivotal decision.** Choosing (a) dev server keeps this milestone strictly
  additive (harness-only). Choosing (b) preview pulls `src/main.tsx` into scope and risks
  prod mock leakage — larger blast radius; recommended deferred.
- **Determinism risks** are: (i) date boundaries (mitigated by A3), (ii) cross-test store
  bleed (mitigated by OQ3a context isolation + no mid-flow reload), (iii) copy drift
  (mitigated by OQ4a locale-JSON selectors), (iv) money parsing from vi-VN formatting
  (mitigated by adding `data-testid` to balance cells and parsing digits).
- **CI cost:** one Chromium download + one headless run added to the pipeline; bounded by
  Chromium-only (OQ2a) and a single spec. Browser binary should be cached in CI.
- **Quality bar:** `pnpm lint`/`tsc -b`/`pnpm build`/`pnpm test` remain green (E2E is out
  of `tsc -b` app scope and out of the Vitest include); `pnpm test:e2e` is the new,
  separate gate.

## Decision Log

| # | Decision | Status | Rationale |
|---|----------|--------|-----------|
| D1 | **Framework = Playwright** (`@playwright/test`) | **Resolved (user, locked)** | Explicitly approved by the user; overrides the CLAUDE.md "new dep = OQ" rule for this case. |
| D2 | **Run strategy = MSW-mocked**, driving the real SPA, reusing `src/test/msw/handlers.ts` at the network boundary; no real backend/DB/Redis; deterministic (pinned TZ + locale) | **Resolved (user, locked)** | User decision. Reuse confirmed viable: committed handlers are a full stateful in-memory backend already served to the browser via `VITE_ENABLE_MOCKS=true` + `public/mockServiceWorker.js`. |
| D3 | **First spec = the full ledger loop** (login → add member → add expense w/ shares → create event → assign → close → balance = 0), exercising M2/M4/M5 | **Resolved (user, locked)** | User decision. Every step maps to committed routes + handlers (Background table). |
| D4 | Unit/component tests stay **Vitest + RTL** under `src/**/*.test.*`; E2E is **Playwright** under `e2e/**/*.spec.*`; Vitest `include` narrowed to `src/**` to prevent glob collision | **Resolved (planner)** | Vitest's default glob would otherwise collect `*.spec.ts`; this is the concrete separation. |
| D5 | E2E `data-testid`s added **only** where role/copy can't robustly target a value (starting with `EventBalanceTable`) | **Resolved (planner)** | Keeps the app's role/label-first convention; adds minimal, curated hooks for value assertions. |
| D6 | Server-under-test, browser matrix, isolation seam, copy-selector strategy, CI gating, dev port | **Resolved (user, 2026-07-18)** | Checkpoint held; user accepted the recommended option for OQ1/OQ2/OQ3/OQ4/OQ6 (all **a**) and chose **OQ5 (b) — local-only** (defer CI gating; no pipeline in repo yet). See below. |
| D7 | **OQ1 = a** (dev server + `VITE_ENABLE_MOCKS=true`, no `src/main.tsx` change), **OQ2 = a** (Chromium-only), **OQ3 = a** (Playwright context isolation, no new mock-reset code; spec routes via the app, no mid-flow reload), **OQ4 = a** (import vi-VN locale JSON for selectors + curated `data-testid`s), **OQ6 = a** (dedicated port 5199) | **Resolved (user, 2026-07-18)** | Keeps scope strictly to the harness — **zero `src/` product-logic changes** (only additive `data-testid`s per D5). |
| D8 | **OQ5 = b** — E2E is **local-only** (`pnpm test:e2e`, developer/pre-merge). No CI job now. | **Resolved (user, 2026-07-18)** | No CI config exists in the repo (Docker-based deploy); CI gating deferred to a follow-up once the harness is proven. |

## Progress Log

- **2026-07-18** — Drafted the plan. Read CLAUDE.md, `frontend-foundation.md`, the M5 doc
  (template/tone), `package.json`, `vite.config.ts`, `tsconfig*.json`, `.gitignore`,
  `.oxlintrc.json`, the full MSW handler set (`src/test/msw/{handlers,browser,server}.ts`),
  `src/main.tsx`, `src/routes/router.tsx`, and the ledger-loop pages/components
  (`LoginPage`, `MembersPage`, `ExpenseCreatePage`, `EventsPage`, `EventDetailPage`,
  `EventExpensesSection`). Confirmed: (1) the mock worker start is `import.meta.env.DEV`-
  gated → E2E must run on the dev server (drives OQ1); (2) Vitest's default glob would
  collect Playwright `*.spec.ts` → `include` must be pinned to `src/**` (D4); (3) every
  ledger-loop step maps to a committed handler with no gap; (4) the balance invariant sums
  to zero structurally; (5) the assign step needs date-boundary care (A3). Six Open
  Questions recorded with recommendations. **Status: awaiting checkpoint on OQ1–OQ6.**
- **2026-07-18 (checkpoint — OQs resolved)** — User accepted the recommended option for
  OQ1/OQ2/OQ3/OQ4/OQ6 (all **a**) and chose **OQ5 = b (local-only, defer CI)**. Net: dev
  server + `VITE_ENABLE_MOCKS=true` (no `main.tsx` change), Chromium-only, Playwright
  context-isolation (no new mock-reset code), vi-VN locale-JSON selectors + curated
  `data-testid`s, dedicated port 5199, E2E local-only. **No open questions remain — cleared
  for implementation (harness-only; zero `src/` product-logic changes).** Next:
  web-implementer builds steps 1–10, then web-test-engineer/reviewer close the cycle.

- **2026-07-18 (implementation — steps 1–10 complete)** — web-implementer built
  the harness + spec per the plan.
  - **Step 1** — `pnpm add -D @playwright/test` (v1.61.1); `pnpm exec playwright
    install chromium` (Chromium 149 headless shell downloaded; no `--with-deps` on
    Windows).
  - **Steps 2–6** — created `playwright.config.ts` (root), `e2e/tsconfig.json`,
    `e2e/fixtures/{copy,session,test}.ts`, `e2e/ledger-loop.spec.ts`,
    `e2e/README.md`. Pinned Vitest `include: ["src/**/*.{test,spec}.{ts,tsx}"]` in
    `vite.config.ts` (collision fix). Added `.gitignore` entries (`test-results`,
    `playwright-report`, `blob-report`, `playwright/.cache`).
  - **oxlint override REQUIRED (anticipated by §5).** Playwright's fixture
    callback `({ page }, use) => …` calling `use(page)` tripped
    `react/rules-of-hooks` (the `use` heuristic). Added the narrowest override:
    `overrides: [{ files: ["e2e/**"], rules: { "react/rules-of-hooks": "off" } }]`.
    Lint then clean (exit 0; only pre-existing `only-export-components` warnings in
    `src/`).
  - **Step 7** — `copy.ts` imports the vi-VN namespace JSON (needs
    `with { type: "json" }` import attributes for Playwright's Node ESM loader) +
    an `interpolate({{name}})` helper; `session.ts` `login()` + a `navLink()`
    landmark-scoped nav helper; `test.ts` `appPage` fixture.
  - **Step 8** — added `data-testid` `event-balance-row`, `balance-amount`,
    `event-balance-total` to `EventBalanceTable.tsx` (additive attributes only, no
    behavior/markup change). Member/expense rows needed NO testids — role/text
    selectors sufficed.
  - **Step 9** — the 7-step spec, honoring A3 (fixed `2026-07-15T12:00` midday
    expense time + matching whole-day event range) and A4 (parses the vi-VN
    `balance-amount` cells to signed integers, asserts Σ = 0, plus the total row's
    settled label). Two selector fixes were needed against the real page (spec-only,
    no product change): scope nav clicks to the `<nav>` landmark with `exact: true`
    (dashboard quick-links + "Thêm phiếu chi tiêu" collided as substrings), and
    `exact: true` on the closed-banner text (the expenses-section closed note
    repeats "Đợt đã chốt").
  - **Step 10** — added `test:e2e`, `test:e2e:ui`, `test:e2e:report`,
    `test:e2e:install` scripts (left `"test": "vitest run"` unchanged); wrote
    `e2e/README.md`; added the test-split note to `FairShareMonWeb/CLAUDE.md`.
  - **Verification (all green):** `pnpm test:e2e` → **1 passed** (headless
    Chromium, 13.6s). `pnpm test` (Vitest) → **93 files / 777 tests passed** (the
    `include` narrowing preserved every `src/**` unit spec and excludes `e2e/**`).
    `pnpm lint` → clean (exit 0). `tsc -b` → exit 0; `pnpm exec tsc -p
    e2e/tsconfig.json --noEmit` → exit 0. `pnpm build` → exit 0.

- **2026-07-18 (review — APPROVE, 0 blocking)** — web-code-reviewer statically
  verified every load-bearing claim: scope discipline (only `EventBalanceTable.tsx`
  in `src/`, three additive `data-testid`s, footer cell correctly untagged so the
  sum-to-zero assertion doesn't double-count); the Vitest `include` guardrail (all
  93 unit specs are `*.test.ts(x)` under `src/`, zero `*.spec.*` under `src/`, none
  dropped); spec determinism (fixed midday date safe from the midnight-UTC edge,
  sound vi-VN VND parsing incl. U+2212, web-first assertions, no mid-flow reload);
  selectors (every referenced vi-VN locale key resolves; nav scoped to the landmark);
  config, conventions (zero production-bundle impact), and doc sync. Verdict
  **APPROVE — ship it.** Three cosmetic nits raised; orchestrator resolved two
  (dropped the dead `paths` mapping from `e2e/tsconfig.json`; moved the misplaced
  `testDir` comment in `playwright.config.ts`) and accepts the third
  (`fullyParallel: true`, a harmless standard default, inert with one spec).
- **Recorded deviations:** (1) `e2e/tsconfig.json` is a **standalone** config (not
  `extends` the repo base, as §5's wording implied) — chosen for clean type
  isolation of the E2E harness from the app/Vitest DOM types; (2) the two loader-
  required specifics already logged (oxlint `e2e/**` override; `with { type: "json" }`
  import attributes). No product-behavior deviation.

## Final Outcome

**Delivered (2026-07-18).** The Playwright E2E harness and the ledger-loop spec
are in place and green, exactly per the plan (OQ1a/OQ2a/OQ3a/OQ4a/OQ5b/OQ6a). The
harness drives the real SPA on the Vite dev server (`pnpm dev --port 5199`,
`VITE_ENABLE_MOCKS=true`) against the committed MSW handlers — no `src/`
product-logic change (only additive `data-testid`s per D5), no CI wiring (D8),
Chromium-only, vi-VN + Asia/Ho_Chi_Minh pinned.

**Files created:** `playwright.config.ts`, `e2e/tsconfig.json`,
`e2e/fixtures/{copy,session,test}.ts`, `e2e/ledger-loop.spec.ts`, `e2e/README.md`.
**Files changed:** `vite.config.ts` (Vitest `include`), `.gitignore`,
`.oxlintrc.json` (e2e override), `package.json` (scripts + dep),
`src/features/events/components/EventBalanceTable.tsx` (3 `data-testid`s),
`FairShareMonWeb/CLAUDE.md` (test-split note).

**Two deviations from the literal plan text, both anticipated and within scope:**
(1) the oxlint `e2e/**` override was needed (§5 said "ONLY if lint trips" — it
tripped on Playwright's `use`); (2) `copy.ts` JSON imports carry
`with { type: "json" }` attributes (required by Playwright's Node ESM loader — the
plan said "import the vi-VN JSON" without specifying the attribute). No product
behavior changed. The regression net for the M2/M4/M5 ledger loop is now live —
later UX/UI restyling of these screens can be validated end-to-end via
`pnpm test:e2e`.

## Future Improvements

- **Prod-parity E2E** — a `vite preview`-of-build run mode (OQ1b) once we decide to
  validate the exact shipped artifact; requires a mode-scoped mock-enable tweak in
  `main.tsx`.
- **Broader flow catalogue** (the rest of the "improve UX/UI" effort's safety net):
  - Auth edge flows: register, `401 → refresh-once → retry`, refresh-reuse `2002`
    terminal logout, change-password revoke, degraded `/auth/me`.
  - M2/M3: member rename/delete + owner-rep protection; category/tag create/rename/
    delete/reactivate/default.
  - M4: expense edit, per-share add/edit/delete, settled toggle, history/audit, CSV export.
  - M5: event edit/delete (expenses go loose), range-excludes-assigned `9003`,
    out-of-range assign `9002`, closed-event immutability across every write control.
  - M6: stats overview + by-category (time-range XOR event).
  - M7: wallet CRUD + QR — the Premium gate (`13003` → `UpgradePrompt`) and Free-limit
    (`13000/13001/13002` → `LimitNotice`) affordances; blob→image QR path.
  - M8: admin suite behind `role == ADMIN`, and the R10 privacy assertion (no ledger data).
- **Cross-browser matrix** (OQ2b): WebKit + Firefox projects once the harness is proven.
- **Visual regression / a11y** — Playwright screenshot snapshots and an `axe` integration
  to guard the upcoming UX/UI restyle.
- **Stricter mock hygiene** — flip the browser worker's `onUnhandledRequest` to `warn`/
  `error` under an E2E flag so a missing mock fails loudly (needs a small `main.tsx`
  change).
- **In-browser mock-reset seam** (OQ3b) if shared-context multi-flow specs are wanted.
- **CI hardening** — cache the Playwright browser binary; shard specs as the suite grows;
  publish the HTML report as a CI artifact.
