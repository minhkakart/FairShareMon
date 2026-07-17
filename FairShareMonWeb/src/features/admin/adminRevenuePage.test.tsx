import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatMoneyVnd } from "@/i18n/format";
import i18n from "@/i18n";
import { AdminRevenuePage } from "./pages/AdminRevenuePage";
import type { RevenueResponse } from "./api/types";

/**
 * AdminRevenuePage integration — the REAL page/hooks/client against MSW. Proves:
 * total revenue renders via `<Money>` VERBATIM (the API SUM, never a client re-sum
 * of the buckets — R3); grant count + the over-time chart + its paired money table
 * + the references list render; a REVOKE-only / no-grant range shows 0 revenue with
 * the empty affordances (not an error); a range change refetches; error → ErrorState.
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

/** VND renders with a non-breaking space the DOM normalizer collapses. */
const vnd = (n: number) => formatMoneyVnd(n).replace(/\s+/g, " ");

function revenue(overrides: Partial<RevenueResponse> = {}): RevenueResponse {
  return {
    from: null,
    to: null,
    bucket: "month",
    buckets: [
      { periodLabel: "2026-01", total: 100000, grantCount: 1 },
      { periodLabel: "2026-02", total: 100000, grantCount: 1 },
    ],
    // Deliberately inconsistent with the bucket sum (200000) so a verbatim render
    // is distinguishable from a client re-sum.
    totalRevenue: 999999,
    grantCount: 5,
    references: ["VCB-20260716-8842", "MB-20260115-1001"],
    ...overrides,
  };
}

function seedAdmin() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-admin-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-admin-t",
    refreshTokenExpiresAt: future,
    user: { username: "admin", role: "ADMIN", uuid: "uuid-admin", tier: "PREMIUM" },
    profileStatus: "resolved",
  });
}

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedAdmin();
});
afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("AdminRevenuePage — success", () => {
  it("AdminRevenuePage_Success_RendersTotalVerbatimNotClientSummed", async () => {
    server.use(http.get("*/api/v1/admin/revenue", () => ok(revenue())));
    renderWithProviders(<AdminRevenuePage />);

    // The API total (999.999 ₫) appears (KPI + table footer) — VERBATIM.
    await waitFor(() =>
      expect(screen.getAllByText(vnd(999999)).length).toBeGreaterThanOrEqual(1),
    );
    // The client sum of the buckets (200.000 ₫) is NEVER shown as the total.
    // Both are present per-bucket, but the footer/KPI total must be the API value.
    const footer = screen.getByRole("table", { name: "Doanh thu theo kỳ" });
    expect(footer).toHaveTextContent(vnd(999999));

    // Grant count KPI ("Số lượt cấp" is also the chart-table column header).
    expect(screen.getAllByText("Số lượt cấp").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("5").length).toBeGreaterThanOrEqual(1);

    // Over-time chart (role=img) + its paired money table.
    expect(
      screen.getByRole("img", { name: /doanh thu premium theo từng kỳ/i }),
    ).toBeInTheDocument();

    // References list, verbatim.
    expect(screen.getByText("VCB-20260716-8842")).toBeInTheDocument();
    expect(screen.getByText("MB-20260115-1001")).toBeInTheDocument();
  });

  it("AdminRevenuePage_RevokeOnlyRange_ShowsZeroRevenueNotError", async () => {
    server.use(
      http.get("*/api/v1/admin/revenue", () =>
        ok(
          revenue({
            buckets: [],
            totalRevenue: 0,
            grantCount: 0,
            references: [],
          }),
        ),
      ),
    );
    renderWithProviders(<AdminRevenuePage />);

    // Zero revenue renders as valid Money(0), not an error.
    expect(await screen.findByText(vnd(0))).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    // Empty references affordance.
    expect(
      screen.getAllByText("Chưa có mã tham chiếu nào trong khoảng này.").length,
    ).toBeGreaterThanOrEqual(1);
  });
});

describe("AdminRevenuePage — range + error", () => {
  it("AdminRevenuePage_ChangePresetToAllTime_RefetchesWithoutBounds", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/admin/revenue", ({ request }) => {
        urls.push(request.url);
        return ok(revenue());
      }),
    );
    renderWithProviders(<AdminRevenuePage />);
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));
    expect(new URL(urls[0]).searchParams.has("from")).toBe(true);

    await userEvent.click(screen.getByRole("button", { name: "Tất cả" }));
    await waitFor(() => {
      const latest = new URL(urls[urls.length - 1]).searchParams;
      expect(latest.has("from")).toBe(false);
      expect(latest.has("to")).toBe(false);
    });
  });

  it("AdminRevenuePage_GenericError_ShowsErrorStateWithRetry", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/admin/revenue", () => {
        calls += 1;
        return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
      }),
    );
    renderWithProviders(<AdminRevenuePage />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Không tải được bảng doanh thu");
    const before = calls;
    await userEvent.click(screen.getByRole("button", { name: "Thử lại" }));
    await waitFor(() => expect(calls).toBeGreaterThan(before));
  });
});
