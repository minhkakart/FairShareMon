import { test, expect, copy, interpolate, gotoNav } from "./fixtures/test";
import { login } from "./fixtures/session";

/**
 * Phone-viewport regression net for cycle-2 sub-cycle 2a (OQ4=a): the wallet
 * bank-accounts table (`BankAccountsTable`) adopting the additive
 * `stackOnMobile` card-stack. Below the `sm` stop (30rem / 480px) each account
 * row reflows into a labeled card — the `<thead>` is hidden, the bank+BIN block
 * (`scope="row"`) becomes the card title, and the account#/holder/default value
 * cells render their `wallet:table.*` labels from `data-label`. The per-row
 * reveal (eye) toggle stays inline with the (now card-line) masked number. The
 * Pixel 5 `mobile` project (393px) is below `sm`, so the card-stack is active.
 *
 * VIEWPORT: runs ONLY under the Playwright `mobile` project — the desktop
 * `chromium` project excludes every `*-responsive.spec.ts` via `testIgnore`. See
 * e2e/README.md.
 *
 * SESSION: logs in as the seed user `admin`, which the MSW handlers seed as
 * PREMIUM with two bank accounts (Vietcombank default + Techcombank). PREMIUM →
 * the wallet renders the managed (`mode="premium"`) table; the reveal toggle is
 * present in both tiers. Navigation is viewport-agnostic (`gotoNav`) and stays
 * client-side — no mid-flow `page.reload()`, so the in-page MSW bank-account
 * store keeps its deterministic seed (OQ3a).
 */

// Vietcombank is the seeded default account (sorts first): number 0071001234567
// → masked `•••• 4567`, grouped `0071 0012 3456 7` (see wallet/format.ts).
const BANK = "Vietcombank";
const MASKED = "•••• 4567";
const GROUPED = "0071 0012 3456 7";
const HOLDER = "NGUYEN VAN MINH";

/** The `wallet:table.*` keys that carry a `data-label` on their cell. */
const LABELED_COLUMNS = ["accountNumber", "holder", "default"] as const;

test.beforeEach(async ({ page }) => {
  await login(page, { username: "admin" });
  await gotoNav(page, copy.common.nav.wallet);
  await page.waitForURL("**/wallet");
});

test("BankAccountsTable_PhoneViewport_RendersAccountsAsLabeledCards", async ({
  page,
}) => {
  // The bank+BIN block is the card title (a `rowheader`); its accessible name is
  // the bank name plus the "BIN …" line.
  const rowHeader = page.getByRole("rowheader", { name: new RegExp(BANK) });
  await expect(rowHeader).toBeVisible();

  // Card-stack is active: the column-header row is hidden below `sm`.
  await expect(page.getByRole("columnheader")).toHaveCount(0);

  const row = page.getByRole("row").filter({ has: rowHeader });

  // Each value cell carries its i18n `data-label` (the card-stack label source).
  for (const key of LABELED_COLUMNS) {
    const label = copy.wallet.table[key];
    await expect(row.locator(`td[data-label="${label}"]`)).toHaveCount(1);
  }

  // The holder label is actually rendered (drawn from `data-label` via `::before`).
  const holderCell = row.locator(
    `td[data-label="${copy.wallet.table.holder}"]`,
  );
  const holderLabel = await holderCell.evaluate(
    (el) => getComputedStyle(el, "::before").content,
  );
  expect(holderLabel).toContain(copy.wallet.table.holder);

  // The holder value + the default marker badge (icon+text, not color-alone) show.
  await expect(row.getByText(HOLDER).first()).toBeVisible();
  await expect(row.getByText(copy.wallet.badge.default)).toBeVisible();

  // A phone card must not push the page into a horizontal scroll.
  const doc = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(doc.scrollWidth).toBeLessThanOrEqual(doc.clientWidth);
});

test("BankAccountRevealToggle_PhoneViewport_IsPresentAndOperable", async ({
  page,
}) => {
  // The reveal toggle is a labeled `aria-pressed` button, reachable by its
  // accessible name and starting collapsed (number masked).
  const revealBtn = page.getByRole("button", {
    name: interpolate(copy.wallet.table.reveal, { bank: BANK }),
  });
  await expect(revealBtn).toBeVisible();
  await expect(revealBtn).toHaveAttribute("aria-pressed", "false");
  await expect(page.getByText(MASKED)).toBeVisible();

  // Activating it reveals the grouped full number and flips the pressed state +
  // the accessible name to the "hide" affordance (operable, keyboard-reachable).
  await revealBtn.click();

  const hideBtn = page.getByRole("button", {
    name: interpolate(copy.wallet.table.hide, { bank: BANK }),
  });
  await expect(hideBtn).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByText(GROUPED)).toBeVisible();
});
