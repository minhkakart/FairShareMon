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
  // The app shell is up once the primary nav renders.
  await expect(navLink(page, copy.common.nav.expenses)).toBeVisible();
}
