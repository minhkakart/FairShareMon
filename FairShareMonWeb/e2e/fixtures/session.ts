import { expect, type Locator, type Page } from "@playwright/test";
import { copy } from "./copy";

export type LoginOptions = {
  username?: string;
  password?: string;
};

/**
 * Click a primary-navigation entry. Scoped to the app shell's `<nav>` landmark
 * with an exact name so it never collides with the dashboard quick-links (which
 * carry the same label plus a description) or action links that contain the same
 * words as a substring (e.g. "Thêm phiếu chi tiêu" vs "Chi tiêu").
 */
export function navLink(page: Page, name: string): Locator {
  return page
    .getByRole("navigation", { name: copy.common.nav.primary })
    .getByRole("link", { name, exact: true });
}

/**
 * Below the nav-collapse breakpoint (`lg` / 64rem / 1024px) the app-shell header
 * hides its inline `<nav>` and drives navigation through the hamburger slide-in
 * drawer instead — the responsive behavior this cycle added. A raw phone
 * viewport (e.g. the Pixel 5 `mobile` project, 393px) is therefore below the
 * breakpoint and the inline nav is not in the accessibility tree.
 */
function isCollapsedNav(page: Page): boolean {
  return (page.viewportSize()?.width ?? Infinity) < 1024;
}

/**
 * Navigate via the app's primary nav in a VIEWPORT-AGNOSTIC way, so the same
 * spec runs under both the desktop (`chromium`) and phone (`mobile`) Playwright
 * projects:
 *  - at/above the nav breakpoint the inline header nav link is clicked directly;
 *  - below it the hamburger drawer is opened first, then the SAME nav link inside
 *    the drawer is clicked (the drawer auto-closes on activation).
 *
 * The element SELECTORS are identical on both paths (the `Điều hướng chính`
 * navigation landmark + the exact-name link) — only the mechanism to reach a
 * collapsed drawer differs. Navigation stays client-side (no `page.reload()`), so
 * the in-page MSW store keeps its deterministic seed (OQ3a).
 */
export async function gotoNav(page: Page, name: string): Promise<void> {
  if (isCollapsedNav(page)) {
    // Open the drawer; below `lg` the hamburger is the trailing header control.
    await page
      .getByRole("button", { name: copy.common.nav.menu, exact: true })
      .click();
    // The open drawer exposes the primary-nav landmark (the hidden inline nav is
    // out of the a11y tree), so the same navLink selector resolves to it.
    await navLink(page, name).click();
    return;
  }
  await navLink(page, name).click();
}

/**
 * Drive the real /login form as the deterministic seed user `demo`
 * (FREE/USER — see the MSW handlers seed). Navigates to /login, fills the
 * label-associated username/password fields, submits, and awaits the client-side
 * redirect to /dashboard. No store/token poking — the spec only touches the UI.
 */
export async function login(
  page: Page,
  { username = "demo", password = "password123" }: LoginOptions = {},
): Promise<void> {
  await page.goto("/login");

  await page.getByLabel(copy.auth.login.username).fill(username);
  await page.getByLabel(copy.auth.login.password).fill(password);
  await page
    .getByRole("button", { name: copy.auth.login.submit })
    .click();

  await page.waitForURL("**/dashboard");
  // The app shell is up once the primary nav renders. Below the nav-collapse
  // breakpoint the inline nav is hidden and the hamburger is the readiness
  // signal; at/above it the inline nav link is (unchanged desktop assertion).
  if (isCollapsedNav(page)) {
    await expect(
      page.getByRole("button", { name: copy.common.nav.menu, exact: true }),
    ).toBeVisible();
  } else {
    await expect(navLink(page, copy.common.nav.expenses)).toBeVisible();
  }
}
