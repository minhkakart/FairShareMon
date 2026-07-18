import { test as base, type Page } from "@playwright/test";
import { login, navLink } from "./session";

/**
 * Test fixtures for the E2E harness.
 *
 * `appPage` — a `page` that has already logged in as the seed user `demo` and
 * landed on the app shell (/dashboard). Every test gets Playwright's default
 * fresh browser context, so the in-page MSW store re-seeds to the deterministic
 * state on the initial navigation (OQ3a). Specs must keep navigating via the
 * app's own client-side routing — never `page.reload()` mid-flow.
 */
export const test = base.extend<{ appPage: Page }>({
  appPage: async ({ page }, use) => {
    await login(page);
    await use(page);
  },
});

export { expect } from "@playwright/test";
export { copy, interpolate } from "./copy";
export { navLink } from "./session";
