import { test, expect, copy, gotoNav } from "./fixtures/test";
import { login } from "./fixtures/session";

/**
 * QR destination picker (M7, OQ2a) — switching the receiving bank in the "show QR"
 * dialog. The `QrDialog`'s destination picker is a Radix `Select` that PORTALS its
 * listbox to `<body>`, while the dialog is a modal Radix `Dialog` whose overlay
 * also lives at `<body>`. This is a real-browser interaction bug that jsdom/RTL
 * cannot catch: it is purely about which portalled layer receives the pointer.
 *
 * Regression: before the fix the Select popover sat at `--fs-z-dropdown` (200),
 * BELOW the dialog overlay (`--fs-z-overlay`, 300) — so the option list rendered
 * but the overlay intercepted every click and the destination never committed.
 * The fix floats the portalled Select content on `--fs-z-popover` (320), above the
 * modal layer. This spec opens the picker and picks the NON-default bank, then
 * asserts the account block (the accessible source of truth) actually switched —
 * which only passes once the pick commits and the QR refetches for that bank.
 *
 * VIEWPORT: no `-responsive` suffix — desktop `chromium` project (and it is
 * layout-agnostic, so it also runs green under `mobile`). Logs in as the PREMIUM
 * seed user `admin`, who is seeded with TWO accounts (Vietcombank default +
 * Techcombank), i.e. the `accounts.length >= 2` condition that shows the picker.
 */

// MSW seed (src/test/msw/handlers.ts): two accounts for `admin`.
const DEFAULT_BANK = "Vietcombank"; // isDefault
const DEFAULT_NUMBER_GROUPED = "0071 0012 3456 7"; // groupAccount("0071001234567")
const OTHER_BANK = "Techcombank"; // the destination we switch to
const OTHER_NUMBER_GROUPED = "1902 4681 0123 45"; // groupAccount("19024681012345")

test("WalletQrDialog_SwitchDestination_UpdatesAccountBlockToPickedBank", async ({
  page,
}) => {
  await login(page, { username: "admin" });

  // A premium user needs an expense to open the expense QR from its detail page.
  await gotoNav(page, copy.common.nav.expenses);
  await page.waitForURL("**/expenses");
  await page
    .getByRole("link", { name: copy.expenses.add, exact: true })
    .first()
    .click();
  await page.waitForURL("**/expenses/new");
  await page.getByLabel(copy.expenses.form.nameLabel).fill("QR đích đến");
  await page.getByLabel(copy.expenses.form.timeLabel).fill("2026-07-15T12:00");
  await page
    .getByRole("button", { name: copy.expenses.form.submitCreate })
    .click();
  await page.waitForURL(/\/expenses\/(?!new$)[^/]+$/);

  // Open the QR dialog.
  await page
    .getByRole("button", { name: copy.wallet.qr.showExpense })
    .click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();

  // The account block starts on the implicit default account (Vietcombank).
  await expect(dialog.getByText(DEFAULT_NUMBER_GROUPED)).toBeVisible();

  // The destination picker (Radix Select trigger exposes role=combobox, named by
  // its visible label via aria-labelledby).
  const picker = dialog.getByRole("combobox", {
    name: new RegExp(copy.wallet.qr.destinationLabel),
  });
  await expect(picker).toBeVisible();
  await expect(picker).toContainText(DEFAULT_BANK);

  // Open the portalled listbox and pick the NON-default bank. Before the z-index
  // fix this click times out — the dialog overlay sits above the option list and
  // swallows the pointer, so the option is never actioned.
  await picker.click();
  await page
    .getByRole("option", { name: new RegExp(OTHER_BANK) })
    .click();

  // The pick committed: the trigger mirrors the chosen bank …
  await expect(picker).toContainText(OTHER_BANK);
  // … and the account block (the accessible channel + copy source) refetched for
  // the picked destination — the other bank's number now shows, the default's is
  // gone. This is the assertion the bug broke: the destination truly switched.
  await expect(dialog.getByText(OTHER_NUMBER_GROUPED)).toBeVisible();
  await expect(dialog.getByText(DEFAULT_NUMBER_GROUPED)).toHaveCount(0);
});
