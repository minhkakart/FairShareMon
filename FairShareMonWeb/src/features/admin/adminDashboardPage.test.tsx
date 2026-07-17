import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { AdminDashboardPage } from "./pages/AdminDashboardPage";
import type { AdminMetricsResponse } from "./api/types";

/**
 * AdminDashboardPage integration — the REAL page + range control + hooks +
 * centralized client against MSW at the boundary. Every test overrides the metrics
 * endpoint with canned account-metadata fixtures (and captures request URLs).
 * Proves: KPIs + tier/role/status distributions (each a `role="img"` chart PAIRED
 * with an accessible table) + signups render; zeros are valid data (not an error /
 * empty-state confusion); the bucket toggle + a range preset drive a refetch; a
 * generic error → ErrorState + retry. Every figure is account metadata — no ledger.
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

function metrics(overrides: Partial<AdminMetricsResponse> = {}): AdminMetricsResponse {
  return {
    from: null,
    to: null,
    totalUsers: 25,
    tierDistribution: [
      { key: "FREE", count: 15 },
      { key: "PREMIUM", count: 10 },
    ],
    roleDistribution: [
      { key: "USER", count: 23 },
      { key: "ADMIN", count: 2 },
    ],
    statusDistribution: [
      { key: "ACTIVE", count: 20 },
      { key: "DISABLED", count: 5 },
    ],
    signups: [
      { periodLabel: "2026-01", count: 8 },
      { periodLabel: "2026-02", count: 17 },
    ],
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

describe("AdminDashboardPage — success composition", () => {
  it("AdminDashboardPage_Success_RendersKpisDistributionsAndSignups", async () => {
    server.use(http.get("*/api/v1/admin/dashboard", () => ok(metrics())));
    renderWithProviders(<AdminDashboardPage />);

    // Wait for data-only content (the paired table renders only with data; the KPI
    // labels also show during the loading skeleton, so they aren't a data signal).
    await screen.findByRole("table", { name: "Theo hạng tài khoản" });

    // KPIs: total users + active users (both counts, no ledger money).
    expect(screen.getByText("Tổng người dùng")).toBeInTheDocument();
    expect(screen.getAllByText("25").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("Đang hoạt động")).toBeInTheDocument();
    expect(screen.getAllByText("20").length).toBeGreaterThanOrEqual(1);

    // Three distribution charts, each role="img" with its summarizing label…
    expect(
      screen.getByRole("img", { name: /theo hạng tài khoản/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole("img", { name: /theo vai trò/i })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: /theo trạng thái/i })).toBeInTheDocument();

    // …each PAIRED with an accessible data table (color-independent channel).
    expect(
      screen.getByRole("table", { name: "Theo hạng tài khoản" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Theo vai trò" })).toBeInTheDocument();
    expect(
      screen.getByRole("table", { name: "Theo trạng thái" }),
    ).toBeInTheDocument();

    // Signups: chart + its paired table.
    expect(
      screen.getByRole("img", { name: /số lượt đăng ký theo từng kỳ/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Đăng ký theo kỳ" })).toBeInTheDocument();
  });

  it("AdminDashboardPage_ZeroState_RendersZeroKpiAndSignupsEmptyNotError", async () => {
    server.use(
      http.get("*/api/v1/admin/dashboard", () =>
        ok(
          metrics({
            totalUsers: 0,
            tierDistribution: [],
            roleDistribution: [],
            statusDistribution: [],
            signups: [],
          }),
        ),
      ),
    );
    renderWithProviders(<AdminDashboardPage />);

    // Signups empty state renders only with data — wait for it, then assert.
    await screen.findByText("Chưa có lượt đăng ký nào trong khoảng này.");
    // Total users KPI shows a valid 0…
    expect(screen.getByText("Tổng người dùng")).toBeInTheDocument();
    expect(screen.getAllByText("0").length).toBeGreaterThanOrEqual(1);
    // …and NOT an error.
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("AdminDashboardPage — range drives refetch", () => {
  it("AdminDashboardPage_ChangeBucketToDay_RefetchesWithBucketDay", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/admin/dashboard", ({ request }) => {
        urls.push(request.url);
        return ok(metrics());
      }),
    );
    renderWithProviders(<AdminDashboardPage />);
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));
    expect(new URL(urls[0]).searchParams.get("bucket")).toBe("month");

    await userEvent.click(screen.getByRole("button", { name: "Ngày" }));
    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("bucket") === "day"),
      ).toBe(true),
    );
  });

  it("AdminDashboardPage_ChangePresetToAllTime_RefetchesWithoutBounds", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/admin/dashboard", ({ request }) => {
        urls.push(request.url);
        return ok(metrics());
      }),
    );
    renderWithProviders(<AdminDashboardPage />);
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));
    // Default "This year" carries a from bound.
    expect(new URL(urls[0]).searchParams.has("from")).toBe(true);

    await userEvent.click(screen.getByRole("button", { name: "Tất cả" }));
    await waitFor(() => {
      const latest = new URL(urls[urls.length - 1]).searchParams;
      expect(latest.has("from")).toBe(false);
      expect(latest.has("to")).toBe(false);
    });
  });
});

describe("AdminDashboardPage — error branch", () => {
  it("AdminDashboardPage_GenericError_ShowsErrorStateWithRetry", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/admin/dashboard", () => {
        calls += 1;
        return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
      }),
    );
    renderWithProviders(<AdminDashboardPage />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Không tải được bảng chỉ số");
    expect(alert).toHaveTextContent("Đã xảy ra lỗi máy chủ.");

    const before = calls;
    await userEvent.click(screen.getByRole("button", { name: "Thử lại" }));
    await waitFor(() => expect(calls).toBeGreaterThan(before));
  });
});
