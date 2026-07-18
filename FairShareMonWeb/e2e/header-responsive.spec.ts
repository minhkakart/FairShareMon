import { test, expect, copy } from "./fixtures/test";

/**
 * Phone-viewport regression net for the responsive/mobile-polish cycle
 * (planning/responsive-mobile-polish.md — R3/R4, OQ5a/OQ6a).
 *
 * These specs prove the app-shell header's mobile treatment: below the nav
 * breakpoint (`lg` / 64rem) the header collapses to brand + hamburger only (the
 * root-cause fix for the 320–390px header overflow — the cycle's top defect),
 * and the secondary actions (language toggle, theme toggle, account link,
 * logout) relocate into the nav-drawer footer where they stay reachable.
 *
 * VIEWPORT: this file runs ONLY under the Playwright `mobile` project (Pixel 5,
 * 393px) — the desktop `chromium` project excludes it via `testIgnore` in
 * playwright.config.ts, because these assertions describe the collapsed header,
 * which only exists below `lg`. See e2e/README.md.
 *
 * Every test uses the shared `appPage` fixture (logged in as the seed user
 * `demo`, landed on /dashboard) and the vi-VN `copy` fixture; selectors are
 * role/label-first. Navigation stays client-side (no mid-flow `page.reload()`)
 * so the in-page MSW store keeps its deterministic seed (OQ3a).
 */

test("AppShellHeader_PhoneViewport_DoesNotOverflowHorizontally", async ({
  appPage: page,
}) => {
  // The header landmark's own content must fit its box (no clipped/overflowing
  // controls) — this is the defect the cycle fixed at its root.
  const header = page.getByRole("banner");
  await expect(header).toBeVisible();
  const headerBox = await header.evaluate((el) => ({
    scrollWidth: el.scrollWidth,
    clientWidth: el.clientWidth,
  }));
  expect(headerBox.scrollWidth).toBeLessThanOrEqual(headerBox.clientWidth);

  // And the page as a whole must not scroll sideways at a phone width.
  const doc = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(doc.scrollWidth).toBeLessThanOrEqual(doc.clientWidth);
});

test("AppShellHeader_BelowNavBreakpoint_ShowsBrandAndHamburgerOnly", async ({
  appPage: page,
}) => {
  const header = page.getByRole("banner");

  // Brand + hamburger are the only visible header affordances on a phone.
  await expect(
    header.getByRole("link", { name: copy.common.appName }),
  ).toBeVisible();
  await expect(
    header.getByRole("button", { name: copy.common.nav.menu, exact: true }),
  ).toBeVisible();

  // The desktop inline actions (logout / toggles) are hidden below `lg`: the
  // only logout in the DOM is the header-inline one (the drawer is closed, so
  // its footer copy is not mounted), and it is not shown.
  await expect(
    page.getByRole("button", { name: copy.common.logout }),
  ).toBeHidden();
  await expect(
    page.getByRole("radiogroup", { name: copy.common.locale.label }),
  ).toBeHidden();
  await expect(
    page.getByRole("radiogroup", { name: copy.common.theme.label }),
  ).toBeHidden();
});

test("NavDrawer_Opened_ExposesRelocatedSecondaryActionsInFooter", async ({
  appPage: page,
}) => {
  // Open the slide-in drawer via the hamburger.
  await page
    .getByRole("button", { name: copy.common.nav.menu, exact: true })
    .click();

  const drawer = page.getByRole("dialog");
  await expect(drawer).toBeVisible();

  // The relocated secondary actions are all present + reachable in the footer.
  await expect(
    drawer.getByRole("radiogroup", { name: copy.common.locale.label }),
  ).toBeVisible();
  await expect(
    drawer.getByRole("radiogroup", { name: copy.common.theme.label }),
  ).toBeVisible();
  // Account link — targeted by its stable route (/settings is NOT a primary-nav
  // entry, so this anchor is unambiguously the footer account link).
  await expect(drawer.locator('a[href="/settings"]')).toBeVisible();
  await expect(
    drawer.getByRole("button", { name: copy.common.logout }),
  ).toBeVisible();

  // A11y smoke: the toggles expose their radio options with accessible names,
  // so the relocated controls stay operable by keyboard/AT in the drawer.
  await expect(
    drawer.getByRole("radio", { name: copy.common.locale.vi }),
  ).toBeVisible();
  await expect(
    drawer.getByRole("radio", { name: copy.common.theme.system }),
  ).toBeVisible();
});

test("NavDrawerAccountLink_Activated_ClosesDrawer", async ({
  appPage: page,
}) => {
  await page
    .getByRole("button", { name: copy.common.nav.menu, exact: true })
    .click();

  const drawer = page.getByRole("dialog");
  await expect(drawer).toBeVisible();

  // The account link is a real router <a>, so activating it navigates AND the
  // drawer auto-dismisses (closeOnFooterLinkActivate). No page.reload().
  await drawer.locator('a[href="/settings"]').click();

  await page.waitForURL("**/settings");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
