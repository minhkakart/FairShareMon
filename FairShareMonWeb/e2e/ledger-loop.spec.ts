import { test, expect, copy, interpolate, gotoNav } from "./fixtures/test";
import type { Locator } from "@playwright/test";

/**
 * The full ledger loop (M2 + M4 + M5) end-to-end against the committed MSW mock:
 *   login → add member → add expense (+ shares) → create event → assign the
 *   expense → close the event → verify the debt-balance sums to zero.
 *
 * Determinism (A3): the expense time is a fixed MIDDAY value on a fixed date, and
 * the event covers that same date, so the assign (in-range) never touches the
 * midnight-UTC boundary regardless of the host clock. Money assertions parse the
 * vi-VN-formatted balance cells back to integers (A4 — the row set sums to zero
 * by construction).
 *
 * Navigation is ALWAYS via the app's own client-side routing (nav links + in-app
 * buttons); there is no mid-flow full reload, so the in-page MSW store keeps the
 * deterministic seed for the whole flow (OQ3a).
 */

// --- Fixed, in-range test data (A3) ---------------------------------------
const MEMBER_NAME = "Chi Lê";
const EXPENSE_NAME = "Ăn tối nhóm";
const SHARE_MEMBER = "An Nguyễn"; // first free active member appended by the editor
const SHARE_AMOUNT = 120_000;
const EXPENSE_TIME_LOCAL = "2026-07-15T12:00"; // midday, Asia/Ho_Chi_Minh
const EVENT_DATE = "2026-07-15"; // event range = this whole day (covers the expense)
const EVENT_NAME = "Đợt Đà Lạt E2E";

/** Parse a vi-VN balance cell ("+120.000 ₫…", "−120.000 ₫…", "0 ₫…") to a signed integer. */
function parseSignedVnd(text: string): number {
  const negative = text.includes("−") || text.includes("-");
  const digits = text.replace(/\D+/g, ""); // strips sign glyph, grouping dots, ₫, and labels
  const magnitude = digits === "" ? 0 : Number.parseInt(digits, 10);
  return negative ? -magnitude : magnitude;
}

test("ledger loop: member → expense → event → assign → close → balance = 0", async ({
  appPage: page,
}) => {
  // 1. Logged in as `demo` and on the app shell (via the appPage fixture).
  await expect(page).toHaveURL(/\/dashboard$/);

  // 2. Add a member.
  await gotoNav(page, copy.common.nav.members);
  await page.waitForURL("**/members");
  await page
    .getByRole("button", { name: copy.members.add, exact: true })
    .click();

  const memberDialog = page.getByRole("dialog");
  await memberDialog
    .getByLabel(copy.members.form.nameLabel)
    .fill(MEMBER_NAME);
  await memberDialog
    .getByRole("button", { name: copy.members.form.submitCreate })
    .click();

  await expect(
    page.getByRole("rowheader", { name: MEMBER_NAME }),
  ).toBeVisible();

  // 3. Add an expense with shares (owner-rep pays; one non-zero member share).
  await gotoNav(page, copy.common.nav.expenses);
  await page.waitForURL("**/expenses");
  await page
    .getByRole("link", { name: copy.expenses.add, exact: true })
    .first()
    .click();
  await page.waitForURL("**/expenses/new");

  await page.getByLabel(copy.expenses.form.nameLabel).fill(EXPENSE_NAME);
  // Native datetime-local: fixed midday, in the event's day range (A3).
  await page
    .getByLabel(copy.expenses.form.timeLabel)
    .fill(EXPENSE_TIME_LOCAL);

  // Append a share row (defaults to the first free active member = An Nguyễn)
  // and give it a non-zero amount so at least one member ends non-zero.
  await page.getByRole("button", { name: copy.expenses.shares.add }).click();
  await page
    .getByLabel(
      interpolate(copy.expenses.shares.amountForRow, { name: SHARE_MEMBER }),
    )
    .fill(String(SHARE_AMOUNT));

  await page
    .getByRole("button", { name: copy.expenses.form.submitCreate })
    .click();

  // Atomic create → redirect to the expense detail.
  await page.waitForURL(/\/expenses\/(?!new$)[^/]+$/);
  await expect(page.getByText(EXPENSE_NAME).first()).toBeVisible();

  // 4. Create an event covering the expense's date.
  await gotoNav(page, copy.common.nav.events);
  await page.waitForURL("**/events");
  await page
    .getByRole("button", { name: copy.events.add, exact: true })
    .first()
    .click();

  const eventDialog = page.getByRole("dialog");
  await eventDialog.getByLabel(copy.events.form.nameLabel).fill(EVENT_NAME);
  await eventDialog.getByLabel(copy.events.form.startLabel).fill(EVENT_DATE);
  await eventDialog.getByLabel(copy.events.form.endLabel).fill(EVENT_DATE);
  await eventDialog
    .getByRole("button", { name: copy.events.form.submitCreate })
    .click();

  // On success → navigate to the event detail.
  await page.waitForURL(/\/events\/[^/]+$/);
  await expect(
    page.getByRole("heading", { name: EVENT_NAME }),
  ).toBeVisible();

  // 5. Assign the loose, in-range expense to the event.
  await page
    .getByRole("button", { name: copy.events.expensesSection.assign })
    .click();

  const assignDialog = page.getByRole("dialog");
  await assignDialog.getByText(EXPENSE_NAME).click(); // selects the radio row
  await assignDialog
    .getByRole("button", { name: copy.events.assign.confirm })
    .click();

  // The expense now appears in the event's expenses table.
  await expect(
    page.getByRole("link", { name: EXPENSE_NAME }),
  ).toBeVisible();

  // 6. Close the event (one-way) and verify write controls disappear.
  await page
    .getByRole("button", { name: copy.events.detail.close })
    .click();

  const closeDialog = page.getByRole("dialog");
  await closeDialog.getByRole("checkbox").check();
  await closeDialog
    .getByRole("button", { name: copy.events.close.confirmButton })
    .click();

  // Status flips to closed: the closed banner shows and every write control is gone.
  await expect(
    page.getByText(copy.events.detail.closedTitle, { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: copy.events.detail.close }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: copy.events.detail.edit }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: copy.events.expensesSection.assign }),
  ).toHaveCount(0);

  // 7. Verify the debt-balance sums to zero (A4).
  const balanceCells: Locator = page.getByTestId("balance-amount");
  await expect(balanceCells.first()).toBeVisible();
  const count = await balanceCells.count();
  expect(count).toBeGreaterThanOrEqual(1);

  const texts = await balanceCells.allInnerTexts();
  const sum = texts.reduce((acc, text) => acc + parseSignedVnd(text), 0);
  expect(sum).toBe(0);

  // The sum-to-zero total row renders a settled (zero) balance.
  const totalRow = page.getByTestId("event-balance-total");
  await expect(totalRow).toBeVisible();
  await expect(totalRow).toContainText(copy.events.balance.zeroLabel);
});
