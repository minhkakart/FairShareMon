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
  },
  {
    memberUuid: "m-1",
    memberName: "An Nguyễn",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 300000,
    owed: 100000,
    balance: 200000,
  },
  {
    memberUuid: "m-2",
    memberName: "Cũ",
    isOwnerRepresentative: false,
    isDeleted: true,
    advanced: 0,
    owed: 200000,
    balance: -200000,
  },
];

function stubBalance(rows: MemberBalanceRow[]) {
  server.use(
    http.get(`*/api/v1/events/${UUID}/balance`, () =>
      ok({ eventUuid: UUID, eventName: "Đà Lạt", isClosed: false, rows }),
    ),
  );
}

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
