import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { MemberExpenseBreakdown } from "./components/MemberExpenseBreakdown";
import type { PublicExpense } from "./api/types";

/**
 * MemberExpenseBreakdown — the public, read-only per-member drill-in. Purely
 * presentational: it groups the payload's `expenses[].shares` by `memberUuid`,
 * shows the member's own per-expense amount + time + payer, annotates the
 * expenses the member advanced as payer, and shows a calm empty note when the
 * member has no share in any expense. Money via `formatMoneyVnd` (vi-VN), no
 * float math. Pinned vi-VN + Asia/Ho_Chi_Minh (setup.ts).
 */

const EXPENSES: PublicExpense[] = [
  {
    uuid: "x-1",
    name: "Khách sạn",
    payerMemberUuid: "m-an",
    payerName: "An Nguyễn",
    expenseTime: "2026-07-02T12:00:00.000Z",
    total: 900000,
    shares: [
      { memberUuid: "m-an", memberName: "An Nguyễn", amount: 300000, isSettled: false, note: null },
      { memberUuid: "m-binh", memberName: "Bình Trần", amount: 300000, isSettled: false, note: null },
      { memberUuid: "m-rep", memberName: "Chủ đợt", amount: 300000, isSettled: false, note: null },
    ],
  },
  {
    uuid: "x-2",
    name: "Ăn tối",
    payerMemberUuid: "m-rep",
    payerName: "Chủ đợt",
    expenseTime: "2026-07-03T19:00:00.000Z",
    total: 400000,
    shares: [
      { memberUuid: "m-binh", memberName: "Bình Trần", amount: 200000, isSettled: false, note: null },
      { memberUuid: "m-chi", memberName: "Chi Lê", amount: 200000, isSettled: true, note: null },
    ],
  },
];

function renderBreakdown(memberUuid: string, memberName: string) {
  return renderWithProviders(
    <MemberExpenseBreakdown
      memberUuid={memberUuid}
      memberName={memberName}
      expenses={EXPENSES}
    />,
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("MemberExpenseBreakdown grouping", () => {
  it("MemberExpenseBreakdown_PayerMember_ShowsOnlyTheirExpensesWithShareAmount", () => {
    // m-an has a share only in Khách sạn (300.000đ); Ăn tối must NOT appear.
    renderBreakdown("m-an", "An Nguyễn");

    expect(screen.getByText("Khách sạn")).toBeInTheDocument();
    expect(screen.queryByText("Ăn tối")).not.toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    // The member's OWN share amount (not the expense total) is shown.
    expect(screen.getByText(/300\.000\s*₫/)).toBeInTheDocument();
  });

  it("MemberExpenseBreakdown_Payer_AnnotatesWhatTheyAdvanced", () => {
    // m-an is the payer of Khách sạn → the advanced-as-payer line shows the
    // expense TOTAL (900.000đ), distinct from their 300.000đ share.
    renderBreakdown("m-an", "An Nguyễn");

    expect(screen.getByText(/Đã ứng \(người trả\)/)).toBeInTheDocument();
    expect(screen.getByText(/900\.000\s*₫/)).toBeInTheDocument();
  });

  it("MemberExpenseBreakdown_NonPayerInMultipleExpenses_ShowsAllShares_NoAdvancedLine", () => {
    // m-binh has a share in BOTH expenses but pays neither → both rows, no
    // advanced-as-payer annotation anywhere.
    renderBreakdown("m-binh", "Bình Trần");

    expect(screen.getByText("Khách sạn")).toBeInTheDocument();
    expect(screen.getByText("Ăn tối")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
    expect(screen.queryByText(/Đã ứng \(người trả\)/)).not.toBeInTheDocument();
  });

  it("MemberExpenseBreakdown_SettledShare_ShowsSettledTag", () => {
    // m-chi's Ăn tối share is settled → the đã-trả tag renders on that row.
    renderBreakdown("m-chi", "Chi Lê");

    expect(screen.getByText("đã trả")).toBeInTheDocument();
  });

  it("MemberExpenseBreakdown_MemberWithNoShares_ShowsEmptyNote", () => {
    renderBreakdown("m-nobody", "Người lạ");

    expect(
      screen.getByText("Thành viên này không có phần gánh nào trong đợt."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
  });
});
