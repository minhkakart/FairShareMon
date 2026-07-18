import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright config for the FairShareMonWeb E2E harness (planning/e2e-testing.md).
 *
 * Strategy (locked): drive the REAL SPA on the Vite dev server with the API
 * mocked at the network boundary by the committed MSW browser worker. The worker
 * only starts when `import.meta.env.DEV` is true (dev server) AND
 * `VITE_ENABLE_MOCKS === "true"` — so `webServer` runs `pnpm dev` with that env
 * set and no `src/` change is needed (OQ1a / A5).
 *
 * Determinism: the browser context pins `locale: "vi-VN"` and
 * `timezoneId: "Asia/Ho_Chi_Minh"` so datetime/money formatting, the client's
 * `X-Time-Zone` header, and the date-boundary handling in the ledger-loop spec
 * (A3) are stable regardless of the host clock/zone.
 *
 * Isolation (OQ3a): default per-test fresh browser context + a full initial
 * navigation re-imports the page's module-level MSW store → the deterministic
 * seed every test. The spec must navigate via the app's own client-side routing
 * (no mid-flow `page.reload()`, which would wipe the in-memory store + token).
 */
export default defineConfig({
  // Scopes Playwright to e2e/*.spec.ts; Vitest owns src/**/*.test.* (R5, D4).
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: [["html", { open: "never" }], ["list"]],
  use: {
    baseURL: "http://localhost:5199",
    locale: "vi-VN",
    timezoneId: "Asia/Ho_Chi_Minh",
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      // Desktop project: the ledger-loop runs here at a desktop viewport. The
      // phone-only `*-responsive.spec.ts` specs are excluded — they assert
      // phone-shaped layout (the collapsed header + drawer-footer, and the
      // admin/wallet table card-stack) that only exists below the breakpoints.
      // The `-responsive.spec.ts` suffix is the single declarative convention
      // marking a spec phone-only (header-responsive, admin-users-responsive,
      // wallet-responsive); see e2e/README.md.
      testIgnore: /-responsive\.spec\.ts$/,
      use: { ...devices["Desktop Chrome"] },
    },
    {
      // Phone viewport project (Pixel 5, 393px). Re-runs the ledger-loop so the
      // full loop is proven on a real small viewport (exercising the
      // ExpensesTable card-stack reflow + the drawer-driven navigation) AND runs
      // the header-responsive spec. The Pixel 5 preset sets viewport / UA /
      // deviceScaleFactor / isMobile / hasTouch but NOT locale or timezone. The
      // top-level `use` (vi-VN + Asia/Ho_Chi_Minh) does merge into a project's
      // `use`, so these would carry over — we re-pin them here anyway to keep the
      // determinism explicit and local to the project block.
      name: "mobile",
      use: {
        ...devices["Pixel 5"],
        locale: "vi-VN",
        timezoneId: "Asia/Ho_Chi_Minh",
      },
    },
  ],
  webServer: {
    command: "pnpm dev --port 5199",
    url: "http://localhost:5199",
    env: { VITE_ENABLE_MOCKS: "true" },
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
