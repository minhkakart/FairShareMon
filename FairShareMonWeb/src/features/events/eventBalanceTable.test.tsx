import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { EventBalanceTable } from "./components/EventBalanceTable";
import type { MemberBalanceRow } from "./api/types";

/**
 * EventBalanceTable — the §3.7 debt-balance table against MSW. advanced / owed /
 * balance render via `Money` (vi-VN grouping, verbatim); the balance is
 * sign-labelled with a color-independent polarity WORD; the owner-rep + deleted
 * markers show; the `TableFoot` total row proves sum-to-zero (advanced total ==
 * owed total, balance total 0); an empty `rows` set shows the calm empty note.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}
function fail(code: number, message: string, status: number) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message } },
    { status },
  );
}

const UUID = "ev-bal";

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-bal-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-bal-t",
    refreshTokenExpiresAt: future,
    user: { username: "bal", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

const ROWS: MemberBalanceRow[] = [
  {
    memberUuid: "m-owner",
    memberName: "Bạn (chủ sổ)",
    isOwnerRepresentative: true,
    isDeleted: false,
    advanced: 0,
    owed: 0,
    balance: 0,
    outstanding: 0,
    isSettled: false,
    isEligibleForAutoCascade: false,
    clearedAmount: 0,
    settlementStatus: "Unsettled",
  },
  {
    memberUuid: "m-1",
    memberName: "An Nguyễn",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 300000,
    owed: 100000,
    balance: 200000,
    outstanding: 0,
    isSettled: false,
    isEligibleForAutoCascade: true,
    clearedAmount: 0,
    settlementStatus: "Unsettled",
  },
  {
    memberUuid: "m-2",
    memberName: "Cũ",
    isOwnerRepresentative: false,
    isDeleted: true,
    advanced: 0,
    owed: 200000,
    balance: -200000,
    outstanding: 200000,
    isSettled: false,
    isEligibleForAutoCascade: true,
    clearedAmount: 0,
    settlementStatus: "Unsettled",
  },
];

function stubBalance(rows: MemberBalanceRow[]) {
  const totalOutstanding = rows.reduce((s, r) => s + r.outstanding, 0);
  const owingMemberCount = rows.filter((r) => r.outstanding > 0).length;
  const settledMemberCount = rows.filter(
    (r) => r.balance < 0 && r.isSettled,
  ).length;
  const partiallySettledMemberCount = rows.filter(
    (r) => r.settlementStatus === "PartiallySettled",
  ).length;
  server.use(
    http.get(`*/api/v1/events/${UUID}/balance`, () =>
      ok({
        eventUuid: UUID,
        eventName: "Đà Lạt",
        isClosed: false,
        rows,
        totalOutstanding,
        owingMemberCount,
        settledMemberCount,
        partiallySettledMemberCount,
      }),
    ),
  );
}

/**
 * A 3-state fixture (event-expense-settlement-sync M2.3/M2.5): "An Nguyễn" is
 * `PartiallySettled` (300.000 of a 500.000 net debt cleared via Direction 2),
 * "Bình Trần" is plain `Unsettled` (nothing cleared yet), "Cũ" is fully
 * `Settled` (Layer-B net-clearance override, outstanding floored at 0). The
 * owner-rep row is an unaffected net creditor (unchanged M1 shape).
 */
const PARTIAL_ROWS: MemberBalanceRow[] = [
  {
    memberUuid: "m-owner",
    memberName: "Bạn (chủ sổ)",
    isOwnerRepresentative: true,
    isDeleted: false,
    advanced: 850000,
    owed: 0,
    balance: 850000,
    outstanding: 0,
    isSettled: false,
    isEligibleForAutoCascade: false,
    clearedAmount: 0,
    settlementStatus: "Unsettled",
  },
  {
    memberUuid: "m-1",
    memberName: "An Nguyễn",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 0,
    owed: 500000,
    balance: -500000,
    outstanding: 200000,
    isSettled: false,
    isEligibleForAutoCascade: true,
    clearedAmount: 300000,
    settlementStatus: "PartiallySettled",
  },
  {
    memberUuid: "m-2",
    memberName: "Bình Trần",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 0,
    owed: 150000,
    balance: -150000,
    outstanding: 150000,
    isSettled: false,
    isEligibleForAutoCascade: true,
    clearedAmount: 0,
    settlementStatus: "Unsettled",
  },
  {
    memberUuid: "m-3",
    memberName: "Cũ",
    isOwnerRepresentative: false,
    isDeleted: true,
    advanced: 0,
    owed: 200000,
    balance: -200000,
    outstanding: 0,
    isSettled: true,
    isEligibleForAutoCascade: true,
    clearedAmount: 200000,
    settlementStatus: "Settled",
  },
];

function renderTable() {
  return renderWithProviders(<EventBalanceTable uuid={UUID} />, { queryClient });
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("EventBalanceTable", () => {
  it("EventBalanceTable_Rows_RenderAdvancedOwedBalanceAsVndMoney", async () => {
    stubBalance(ROWS);
    renderTable();

    const row = (
      await screen.findByRole("rowheader", { name: /An Nguyễn/ })
    ).closest("tr") as HTMLElement;
    // Money with vi-VN grouping (dots), verbatim from the API.
    expect(within(row).getByText(/300\.000/)).toBeInTheDocument();
    expect(within(row).getByText(/100\.000/)).toBeInTheDocument();
    // The positive balance carries the "+" glyph magnitude (200.000).
    expect(within(row).getByText(/200\.000/)).toBeInTheDocument();
  });

  it("EventBalanceTable_OwnerRepAndDeleted_ShowColorIndependentMarkers", async () => {
    stubBalance(ROWS);
    renderTable();

    const ownerRow = (
      await screen.findByRole("rowheader", { name: /Bạn \(chủ sổ\)/ })
    ).closest("tr") as HTMLElement;
    expect(within(ownerRow).getByText("đại diện")).toBeInTheDocument();

    const deletedRow = screen
      .getByRole("rowheader", { name: /Cũ/ })
      .closest("tr") as HTMLElement;
    expect(within(deletedRow).getByText("(đã xóa)")).toBeInTheDocument();
  });

  it("EventBalanceTable_Balance_IsSignLabelledColorIndependent", async () => {
    stubBalance(ROWS);
    renderTable();

    const positive = (
      await screen.findByRole("rowheader", { name: /An Nguyễn/ })
    ).closest("tr") as HTMLElement;
    // A polarity WORD backs the sign glyph (not color alone).
    expect(within(positive).getByText("được nhận lại")).toBeInTheDocument();

    const negative = screen
      .getByRole("rowheader", { name: /Cũ/ })
      .closest("tr") as HTMLElement;
    expect(within(negative).getByText("phải trả")).toBeInTheDocument();
  });

  it("EventBalanceTable_Footer_ProvesSumToZero", async () => {
    stubBalance(ROWS);
    renderTable();

    // The footer total row (label "Tổng").
    const totalRow = (
      await screen.findByText("Tổng")
    ).closest("tr") as HTMLElement;
    // advanced total == owed total (300.000 each) — the sum-to-zero invariant.
    expect(within(totalRow).getAllByText(/300\.000/)).toHaveLength(2);
    // The balance total is the documented invariant: 0, labelled "đã cân bằng".
    expect(within(totalRow).getByText("đã cân bằng")).toBeInTheDocument();
    expect(within(totalRow).getByText("Cân bằng luôn bằng 0")).toBeInTheDocument();
  });

  it("EventBalanceTable_NoExpenses_ShowsEmptyNoteAndNoFooter", async () => {
    stubBalance([]);
    renderTable();

    expect(
      await screen.findByText("Chưa có phiếu nào trong đợt"),
    ).toBeInTheDocument();
    // No total/footer row when there are no rows.
    expect(screen.queryByText("Tổng")).not.toBeInTheDocument();
  });

  it("EventBalanceTable_EligibleCreditorRow_RendersSameStatusCellShapeAsDebtorRow", async () => {
    // Regression for M1-R2/OQ2: the eligible-creditor branch (`balance > 0 &&
    // isEligibleForAutoCascade`) must render the same Badge + `MemberSettledToggle`
    // shape a debtor row (`balance < 0`) gets — not silently regress to the old
    // "muted dash, no control" rendering every `balance >= 0` row used to get.
    stubBalance(ROWS);
    renderTable();

    // "An Nguyễn" (ROWS): balance 200.000 (creditor), isEligibleForAutoCascade: true.
    const creditorRow = (
      await screen.findByRole("rowheader", { name: /An Nguyễn/ })
    ).closest("tr") as HTMLElement;
    // "Chưa trả" (renamed from "Còn nợ", OQ6) appears twice (the Badge text +
    // the switch's own labelOff span) — getAllByText, mirroring the existing
    // color-independent-status assertion pattern in `memberSettled.test.tsx`.
    expect(
      within(creditorRow).getAllByText("Chưa trả").length,
    ).toBeGreaterThanOrEqual(1);
    expect(
      within(creditorRow).getByRole("switch", {
        name: "Trạng thái đã trả của An Nguyễn",
      }),
    ).toBeInTheDocument();
    // Plus the M1-R2 eligibility HelpHint distinguishing it from a debtor row
    // (short accessible name, distinct from the longer bubble body).
    expect(
      within(creditorRow).getByRole("button", {
        name: "Đánh dấu đã trả sẽ làm gì?",
      }),
    ).toBeInTheDocument();

    // "Cũ" (ROWS): balance -200.000 (debtor) — the pre-existing branch, same shape.
    const debtorRow = screen
      .getByRole("rowheader", { name: /Cũ/ })
      .closest("tr") as HTMLElement;
    expect(
      within(debtorRow).getAllByText("Chưa trả").length,
    ).toBeGreaterThanOrEqual(1);
    expect(
      within(debtorRow).getByRole("switch", {
        name: "Trạng thái đã trả của Cũ",
      }),
    ).toBeInTheDocument();
  });

  it("EventBalanceTable_PartiallySettledRow_RendersMeterWithComposedNetOwedAndStatusBadge", async () => {
    // Regression for M2.3/M2.5 (Step M2.5 named test): a `PartiallySettled` row
    // renders `SettlementStatusBadge` (`partial` tone, locked copy) AND a
    // `SettlementMeter` (fraction text + `role="progressbar"`) in the "Còn nợ"
    // cell IN PLACE of the plain `<Money>` figure — with `netOwed` composed as
    // `clearedAmount + outstanding` (the OQ-L composition rule), never a
    // client-side re-derivation. `Unsettled`/`Settled` rows keep the existing
    // plain-cell behavior (a positive `<Money>` figure or the muted "—").
    stubBalance(PARTIAL_ROWS);
    renderTable();

    const rows = await screen.findAllByTestId("event-balance-row");
    const anRow = rows.find((r) =>
      within(r).queryByText(/An Nguyễn/),
    ) as HTMLElement;
    const binhRow = rows.find((r) =>
      within(r).queryByText(/Bình Trần/),
    ) as HTMLElement;
    const cuRow = rows.find((r) => within(r).queryByText(/Cũ/)) as HTMLElement;

    // "An Nguyễn" — PartiallySettled: the 3-state badge (distinct from both
    // "Chưa trả" and "Đã trả") plus the meter, not the plain Money cell.
    expect(within(anRow).getByText("Đã trả một phần")).toBeInTheDocument();
    const anCell = within(anRow).getByTestId("outstanding-amount");
    const anMeter = within(anCell).getByRole("progressbar");
    // netOwed composition: clearedAmount (300.000) + outstanding (200.000) = 500.000.
    expect(anMeter).toHaveAttribute("aria-valuemax", "500000");
    expect(anMeter).toHaveAttribute("aria-valuenow", "300000");
    expect(anCell.textContent).toMatch(/300\.000/);
    expect(anCell.textContent).toMatch(/500\.000/);

    // "Bình Trần" — plain Unsettled debtor: plain positive Money cell, no meter.
    expect(
      within(binhRow).getAllByText("Chưa trả").length,
    ).toBeGreaterThanOrEqual(1);
    const binhCell = within(binhRow).getByTestId("outstanding-amount");
    expect(within(binhCell).queryByRole("progressbar")).not.toBeInTheDocument();
    expect(binhCell.textContent).toMatch(/150\.000/);

    // "Cũ" — fully Settled: the badge reads "Đã trả" (also echoed by the
    // switch's own labelOn — getAllByText, mirroring the existing
    // color-independent-status assertion pattern), outstanding floored to 0
    // (muted dash, unchanged pre-M2 shape), no meter.
    expect(within(cuRow).getAllByText("Đã trả").length).toBeGreaterThanOrEqual(
      1,
    );
    const cuCell = within(cuRow).getByTestId("outstanding-amount");
    expect(within(cuCell).queryByRole("progressbar")).not.toBeInTheDocument();
    expect(cuCell.textContent).toBe("—");

    // The column-header HelpHint is present exactly once, not per-row (four
    // rows rendered above, one hint expected). Short accessible name, distinct
    // from the longer bubble body.
    expect(
      screen.getAllByRole("button", {
        name: "Vì sao số tiền này có thể khác tổng các phần gánh?",
      }),
    ).toHaveLength(1);

    // The footer surfaces partiallySettledMemberCount verbatim (one row above).
    expect(
      await screen.findByText("1 thành viên đã trả một phần"),
    ).toBeInTheDocument();
  });

  it("EventBalanceTable_LoadError_ShowsRetry", async () => {
    server.use(
      http.get(`*/api/v1/events/${UUID}/balance`, () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    renderTable();
    expect(
      await screen.findByText("Không tải được cân đối công nợ"),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Thử lại" })).toBeInTheDocument();
  });
});
