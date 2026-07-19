import { test, expect, copy, gotoNav } from "./fixtures/test";
import { login } from "./fixtures/session";

/**
 * Bank-picker create flow (bank-picker-vietqr): drive the REAL searchable
 * `Combobox` end-to-end to add a bank account. The wallet mutation is Premium, so
 * this logs in as the PREMIUM seed user `admin` (seeded with Vietcombank default +
 * Techcombank). The bank directory is served to the browser MSW worker by the
 * relative `/api/v1/banks` handler in `src/test/msw/handlers.ts` (VCB 970436 +
 * TCB 970407 + BIDV + MB) through our own centralized client, so filtering +
 * picking resolves against a deterministic mocked list.
 *
 * VIEWPORT: no `-responsive` suffix, so this runs under BOTH the desktop
 * `chromium` project (the create loop the coordinator asked for) AND the `mobile`
 * project — all selectors are role/label-first and layout-agnostic (the accounts
 * table exposes the same `rowheader` bank title in the desktop table and the
 * phone card-stack). Navigation is via `gotoNav` (drawer-aware); no `page.reload()`
 * so the in-page MSW store keeps its deterministic seed.
 */

// Techcombank (BIN 970407) is served by the mocked `/v1/banks` directory.
const BANK_SHORT_NAME = "Techcombank";
// A distinctive holder so the newly-created row is unambiguous (the seed already
// has one Techcombank account).
const NEW_HOLDER = "TRAN THI E2E";
const NEW_ACCOUNT_NUMBER = "0999888777";

test.beforeEach(async ({ page }) => {
  await login(page, { username: "admin" });
  await gotoNav(page, copy.common.nav.wallet);
  await page.waitForURL("**/wallet");
});

test("WalletBankPicker_CreateViaCombobox_AddsAccountWithPickedBank", async ({
  page,
}) => {
  // Baseline: the seed has exactly one Techcombank account.
  const techRows = page.getByRole("rowheader", {
    name: new RegExp(BANK_SHORT_NAME),
  });
  await expect(techRows).toHaveCount(1);

  // Open the Add-account dialog (header CTA; admin has accounts → no empty state).
  await page.getByRole("button", { name: copy.wallet.add }).first().click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();

  // Open the bank Combobox (its trigger's accessible name carries the picker
  // label), filter as-you-type, and pick the single remaining match.
  await dialog
    .getByRole("button", { name: new RegExp(copy.wallet.form.bankPicker.label) })
    .click();
  const search = dialog.getByRole("combobox");
  await expect(search).toBeFocused();
  await search.fill("techcom");
  await dialog
    .getByRole("option", { name: new RegExp(BANK_SHORT_NAME) })
    .click();

  // The picked bank is mirrored into the (now closed) trigger.
  await expect(
    dialog.getByRole("button", { name: new RegExp(BANK_SHORT_NAME) }),
  ).toBeVisible();

  // Fill the two retained text fields and submit.
  await dialog
    .getByRole("textbox", { name: copy.wallet.form.accountNumberLabel })
    .fill(NEW_ACCOUNT_NUMBER);
  await dialog
    .getByRole("textbox", { name: copy.wallet.form.holderLabel })
    .fill(NEW_HOLDER);
  await dialog
    .getByRole("button", { name: copy.wallet.form.submitCreate })
    .click();

  // Success toast fires and the dialog closes. Radix Toast mirrors the title text
  // into a duplicated aria-live announce region, so the copy can match TWO nodes
  // under parallel load — scope to the first (the visible toast) so it resolves
  // deterministically instead of tripping Playwright strict mode.
  await expect(page.getByText(copy.wallet.toast.created).first()).toBeVisible();
  await expect(dialog).toBeHidden();

  // The new row appears in the accounts table — a second Techcombank, showing the
  // picked bank's short name (re-derived from the stored BIN) and the entered
  // holder. (Assert on the short-name text, not the logo image.)
  await expect(techRows).toHaveCount(2);
  await expect(page.getByText(NEW_HOLDER)).toBeVisible();
});
