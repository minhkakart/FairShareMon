import { test, expect, copy, interpolate, gotoNav } from "./fixtures/test";
import { login } from "./fixtures/session";

/**
 * Phone-viewport regression net for cycle-2 sub-cycle 2a (OQ4=a): the admin
 * user table (`AdminUserTable`) adopting the additive `stackOnMobile`
 * card-stack. Below the `sm` stop (30rem / 480px) the 8-column table reflows
 * each body row into a labeled card — the `<thead>` column-header row is hidden
 * (`display:none`), the `scope="row"` username becomes the card title, and every
 * value cell renders its `admin:users.columns.*` label from `data-label` via a
 * `::before`. The Pixel 5 `mobile` project (393px) is below `sm`, so the
 * card-stack is active here.
 *
 * VIEWPORT: runs ONLY under the Playwright `mobile` project — the desktop
 * `chromium` project excludes every `*-responsive.spec.ts` via `testIgnore` in
 * playwright.config.ts (the card-stack only exists below `sm`). See
 * e2e/README.md.
 *
 * SESSION: logs in as the seed ADMIN user `admin` (PREMIUM/ADMIN — the only seed
 * whose resolved profile makes the "Quản trị" primary-nav entry + the admin
 * console visible). Navigation is viewport-agnostic (`gotoNav` opens the drawer
 * below `lg`) and stays client-side — no mid-flow `page.reload()`, so the in-page
 * MSW admin store keeps its deterministic seed (OQ3a).
 *
 * SEED ANCHOR: `nguyen.van.a` (uuid-nguyen-a) is the newest seed user
 * (createdAt 2026-07-16), so under the default `createdAt desc` sort it is the
 * first row on page 1 — a stable, always-visible card. It is PREMIUM / USER /
 * ACTIVE with 2 grants.
 */

const ADMIN_USER = "nguyen.van.a";
const ADMIN_USER_UUID = "uuid-nguyen-a";

/** The `admin:users.columns.*` keys that carry a `data-label` on their cell. */
const LABELED_COLUMNS = [
  "tier",
  "role",
  "status",
  "createdAt",
  "grantCount",
  "lastGrantAt",
] as const;

test.beforeEach(async ({ page }) => {
  await login(page, { username: "admin" });
  // Primary-nav → /admin (redirects to /admin/dashboard), then the admin
  // console's own tab sub-nav → the Users tab. Both are client-side links.
  await gotoNav(page, copy.common.nav.admin);
  await page.waitForURL("**/admin/dashboard");
  await page
    .getByRole("navigation", { name: copy.admin.console.navLabel })
    .getByRole("link", { name: copy.admin.nav.users })
    .click();
  await page.waitForURL("**/admin/users");
});

test("AdminUserTable_PhoneViewport_RendersRowsAsLabeledCards", async ({
  page,
}) => {
  // The username row-header is the card title (still a `rowheader`, now
  // block-level as the card title).
  const rowHeader = page.getByRole("rowheader", { name: ADMIN_USER });
  await expect(rowHeader).toBeVisible();

  // Card-stack is active: the column-header row is hidden below `sm`, so no
  // `columnheader` is in the accessibility tree (this is also the OQ6 behavior —
  // the sort headers are not exposed on a phone).
  await expect(page.getByRole("columnheader")).toHaveCount(0);

  const row = page.getByRole("row").filter({ has: rowHeader });

  // Every value cell carries its i18n `data-label` (the card-stack label source).
  for (const key of LABELED_COLUMNS) {
    const label = copy.admin.users.columns[key];
    await expect(row.locator(`td[data-label="${label}"]`)).toHaveCount(1);
  }

  // The labels are actually RENDERED to the user — the card-stack draws them from
  // `data-label` via a `::before`. Assert the resolved pseudo-element content for
  // the tier cell carries the "Hạng" label text (proves the labeled line shows,
  // not merely that the attribute exists).
  const tierCell = row.locator(
    `td[data-label="${copy.admin.users.columns.tier}"]`,
  );
  const tierLabel = await tierCell.evaluate(
    (el) => getComputedStyle(el, "::before").content,
  );
  expect(tierLabel).toContain(copy.admin.users.columns.tier);

  // The values themselves render as color-independent icon+text badges (R10):
  // tier=Premium, role=Người dùng, status=Hoạt động — scoped to this card's row.
  await expect(row.getByText(copy.admin.tierBadge.premium)).toBeVisible();
  await expect(row.getByText(copy.admin.roleBadge.user)).toBeVisible();
  await expect(row.getByText(copy.admin.statusBadge.active)).toBeVisible();

  // A phone card must not push the page into a horizontal scroll.
  const doc = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(doc.scrollWidth).toBeLessThanOrEqual(doc.clientWidth);
});

test("AdminUserCard_PhoneViewport_StillNavigatesToUserDetail", async ({
  page,
}) => {
  // The per-card view action stays operable in card mode (its cell is label-less)
  // and is reachable by its accessible name.
  await page
    .getByRole("link", {
      name: interpolate(copy.admin.users.viewLabel, { name: ADMIN_USER }),
      exact: true,
    })
    .click();

  await page.waitForURL(`**/admin/users/${ADMIN_USER_UUID}`);

  // The detail page renders: the account-metadata panel + the username value.
  await expect(
    page.getByRole("heading", { name: copy.admin.detail.metadata.title }),
  ).toBeVisible();
  await expect(page.getByText(ADMIN_USER).first()).toBeVisible();
});
