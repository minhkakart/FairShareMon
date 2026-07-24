import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { PublicBalanceTable } from "./components/PublicBalanceTable";
import type { MemberBalanceRow, PublicEventShareResponse } from "./api/types";

/**
 * PublicBalanceTable — the read-only public balance table + lazy per-member QR.
 * MSW at the client boundary for the anonymous QR endpoint. Asserts: one row per
 * member; the QR button appears ONLY for `hasQr && outstanding > 0`; the QR click
 * lazily fetches the per-member QRs exactly once and opens the shared
 * QrPreviewDialog carousel at the CLICKED member's slide; the expand toggle
 * reveals MemberExpenseBreakdown with correct `aria-expanded`/`aria-controls`;
 * and there is NO write control (no toggle/switch) in the DOM (read-only report).
 * Pinned vi-VN + Asia/Ho_Chi_Minh.
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

const DATA_URL =
  "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HBwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

function mkRow(over: Partial<MemberBalanceRow> & { memberUuid: string; memberName: string }): MemberBalanceRow {
  return {
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 0,
    owed: 0,
    balance: 0,
    outstanding: 0,
    isSettled: false,
    settledAt: null,
    ...over,
  };
}

function makeData(over: Partial<PublicEventShareResponse> = {}): PublicEventShareResponse {
  return {
    eventName: "Chuyến Đà Lạt",
    closedAt: "2026-07-20T10:00:00.000Z",
    rows: [
      mkRow({ memberUuid: "m-an", memberName: "An Nguyễn", advanced: 0, owed: 300000, balance: -300000, outstanding: 300000 }),
      mkRow({ memberUuid: "m-binh", memberName: "Bình Trần", advanced: 0, owed: 500000, balance: -500000, outstanding: 500000 }),
      mkRow({ memberUuid: "m-chi", memberName: "Chi Lê", advanced: 0, owed: 200000, balance: -200000, outstanding: 0, isSettled: true }),
      mkRow({ memberUuid: "m-rep", memberName: "Chủ đợt", isOwnerRepresentative: true, advanced: 900000, owed: 300000, balance: 600000, outstanding: 0 }),
    ],
    expenses: [
      {
        uuid: "x-1",
        name: "Khách sạn",
        payerMemberUuid: "m-an",
        payerName: "An Nguyễn",
        expenseTime: "2026-07-02T12:00:00.000Z",
        total: 300000,
        shares: [
          { memberUuid: "m-an", memberName: "An Nguyễn", amount: 300000, isSettled: false, note: null },
        ],
      },
    ],
    totalOutstanding: 800000,
    owingMemberCount: 2,
    settledMemberCount: 1,
    hasQr: true,
    ...over,
  };
}

/** The two owing members, mapped to the anonymous QR endpoint's shape. */
function owingQrs() {
  return [
    { memberUuid: "m-an", memberName: "An Nguyễn", amount: 300000, image: DATA_URL },
    { memberUuid: "m-binh", memberName: "Bình Trần", amount: 500000, image: DATA_URL },
  ];
}

function renderTable(data = makeData()) {
  return renderWithProviders(<PublicBalanceTable token="tok" data={data} />);
}

const PREVIEW_NAME = "Xem mã QR phóng to"; // wallet:qr.previewTitle (vi-VN)

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("PublicBalanceTable rows + status", () => {
  it("PublicBalanceTable_Data_RendersOneRowHeaderPerMember", () => {
    renderTable();
    expect(screen.getByRole("rowheader", { name: /An Nguyễn/ })).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: /Bình Trần/ })).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: /Chi Lê/ })).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: /Chủ đợt/ })).toBeInTheDocument();
  });

  it("PublicBalanceTable_IsReadOnly_HasNoToggleOrWriteControl", () => {
    // The public report strips every write control — no settled toggle lives here.
    renderTable();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    // But the read-only status badges still render (color-independent text).
    expect(screen.getByText("Đã trả")).toBeInTheDocument(); // m-chi (settled)
    expect(screen.getAllByText("Còn nợ").length).toBeGreaterThanOrEqual(1);
  });
});

describe("PublicBalanceTable QR gating", () => {
  it("PublicBalanceTable_QrButton_OnlyForOwingRows_WhenHasQr", () => {
    renderTable();
    // Owing rows (outstanding > 0) get a QR button…
    expect(
      screen.getByRole("button", { name: "Xem mã QR của An Nguyễn" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Xem mã QR của Bình Trần" }),
    ).toBeInTheDocument();
    // …settled / non-owing rows do not.
    expect(
      screen.queryByRole("button", { name: "Xem mã QR của Chi Lê" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Xem mã QR của Chủ đợt" }),
    ).not.toBeInTheDocument();
  });

  it("PublicBalanceTable_HasQrFalse_HidesQrColumnEntirely", () => {
    renderTable(makeData({ hasQr: false }));
    expect(
      screen.queryByRole("columnheader", { name: "Mã QR" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Xem mã QR của/ }),
    ).not.toBeInTheDocument();
  });
});

describe("PublicBalanceTable lazy QR carousel", () => {
  it("PublicBalanceTable_ClickQr_FetchesOnceAndOpensPreviewAtClickedMember", async () => {
    let qrCalls = 0;
    server.use(
      http.get("*/api/v1/public/shares/:token/qr/members", () => {
        qrCalls += 1;
        return ok(owingQrs());
      }),
    );
    const user = userEvent.setup();
    renderTable();

    // Lazy: no fetch until the first QR click.
    expect(qrCalls).toBe(0);

    await user.click(
      screen.getByRole("button", { name: "Xem mã QR của Bình Trần" }),
    );

    const lightbox = await screen.findByRole("dialog", { name: PREVIEW_NAME });
    // Opened at Bình Trần's slide (index 1) — Counter shows 2 / 2, caption shows
    // that member's name + amount.
    expect(within(lightbox).getByText(/2\s*\/\s*2/)).toBeInTheDocument();
    expect(within(lightbox).getByText("Bình Trần")).toBeInTheDocument();
    expect(within(lightbox).getByText(/500\.000\s*₫/)).toBeInTheDocument();
    // Exactly one network fetch for the whole interaction.
    expect(qrCalls).toBe(1);
  });

  it("PublicBalanceTable_ClickFirstMemberQr_OpensAtIndexZero", async () => {
    server.use(
      http.get("*/api/v1/public/shares/:token/qr/members", () => ok(owingQrs())),
    );
    const user = userEvent.setup();
    renderTable();

    await user.click(
      screen.getByRole("button", { name: "Xem mã QR của An Nguyễn" }),
    );

    const lightbox = await screen.findByRole("dialog", { name: PREVIEW_NAME });
    expect(within(lightbox).getByText(/1\s*\/\s*2/)).toBeInTheDocument();
    expect(within(lightbox).getByText("An Nguyễn")).toBeInTheDocument();
  });

  it("PublicBalanceTable_QrFetchFails_ShowsToast_PreviewStaysClosed", async () => {
    server.use(
      http.get("*/api/v1/public/shares/:token/qr/members", () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    const user = userEvent.setup();
    renderTable();

    await user.click(
      screen.getByRole("button", { name: "Xem mã QR của An Nguyễn" }),
    );

    // A friendly danger toast; the lightbox never opens.
    expect(await screen.findByText("Không tải được mã QR")).toBeInTheDocument();
    expect(
      screen.queryByRole("dialog", { name: PREVIEW_NAME }),
    ).not.toBeInTheDocument();
  });
});

describe("PublicBalanceTable expand toggle", () => {
  it("PublicBalanceTable_ExpandToggle_RevealsBreakdownAndSetsAria", async () => {
    const user = userEvent.setup();
    renderTable();

    const toggle = screen.getByRole("button", { name: "Xem chi tiết của An Nguyễn" });
    // Collapsed initially: aria-expanded=false, aria-controls points at a region.
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const regionId = toggle.getAttribute("aria-controls");
    expect(regionId).toBeTruthy();
    expect(
      screen.queryByRole("heading", { name: "Chi tiết phần gánh của An Nguyễn" }),
    ).not.toBeInTheDocument();

    await user.click(toggle);

    // Expanded: the toggle flips + the breakdown region appears with that id.
    const expandedToggle = screen.getByRole("button", {
      name: "Thu gọn chi tiết của An Nguyễn",
    });
    expect(expandedToggle).toHaveAttribute("aria-expanded", "true");
    const heading = screen.getByRole("heading", {
      name: "Chi tiết phần gánh của An Nguyễn",
    });
    expect(heading).toBeInTheDocument();
    expect(document.getElementById(regionId as string)).toContainElement(heading);
  });
});
