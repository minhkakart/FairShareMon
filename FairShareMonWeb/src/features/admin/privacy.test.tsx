import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { resetAdminStore } from "@/test/msw/handlers";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { assertNoLedgerKeys, LEDGER_KEYS } from "./api/privacy";
import { adminApi } from "./api/adminApi";
import { AdminDashboardPage } from "./pages/AdminDashboardPage";
import { AdminRevenuePage } from "./pages/AdminRevenuePage";
import { AdminUsersPage } from "./pages/AdminUsersPage";
import { AdminUserDetailPage } from "./pages/AdminUserDetailPage";

/**
 * PRIVACY BOUNDARY (R10 — the milestone's defining constraint). Two layers proven
 * here: (1) the DEV-only `assertNoLedgerKeys` tripwire throws on any ledger
 * structural key and is a no-op for legitimate grant/revenue fields; (2) every
 * admin READ (against the committed MSW fixtures, which deliberately carry NO
 * ledger fields) returns account/grant data only — deep-scanned for ledger keys —
 * and no ledger key ever reaches the rendered DOM of any admin surface. A final
 * case proves the tripwire actually TRIPS when a (mock) backend leaks a ledger key.
 */

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

/** Deep-collect every object key in a payload for the response-level scan. */
function collectKeys(value: unknown, acc: Set<string> = new Set()): Set<string> {
  if (value === null || typeof value !== "object") return acc;
  if (Array.isArray(value)) {
    for (const v of value) collectKeys(v, acc);
    return acc;
  }
  for (const [k, v] of Object.entries(value)) {
    acc.add(k);
    collectKeys(v, acc);
  }
  return acc;
}

/** Unambiguous ledger structural keys that must never appear in admin DOM markup
 *  (excludes bare words like "member"/"event" that Vietnamese UI copy contains). */
const DOM_FORBIDDEN = [
  "payerMemberUuid",
  "payerMemberId",
  "payerMemberName",
  "shareUuid",
  "bankAccounts",
  "bankAccount",
  "expenseTime",
  "isSettled",
  "settledAt",
  "categoryUuid",
  "categoryName",
  "eventUuid",
  "eventName",
];

beforeEach(async () => {
  window.localStorage.clear();
  resetAdminStore();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedAdmin();
});
afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("assertNoLedgerKeys — DEV tripwire", () => {
  it("AssertNoLedgerKeys_CleanGrantPayload_ReturnsUnchanged", () => {
    const payload = {
      totalRevenue: 500000,
      grantCount: 2,
      buckets: [{ periodLabel: "2026-07", total: 500000, grantCount: 2 }],
      references: ["VCB-1"],
    };
    expect(assertNoLedgerKeys(payload)).toBe(payload);
  });

  it("AssertNoLedgerKeys_GrantRow_AmountCurrencyTotal_DoNotTrip", () => {
    // amount / currency / total are legitimate tier-grant/revenue fields.
    expect(() =>
      assertNoLedgerKeys({ amount: 200000, currency: "VND", total: 200000 }),
    ).not.toThrow();
  });

  it("AssertNoLedgerKeys_EachLedgerKey_ThrowsInDev", () => {
    for (const key of LEDGER_KEYS) {
      expect(() => assertNoLedgerKeys({ [key]: "x" }), key).toThrow(
        /privacy boundary/,
      );
    }
  });

  it("AssertNoLedgerKeys_NestedAndArrayLedgerKey_Throws", () => {
    expect(() =>
      assertNoLedgerKeys({ items: [{ ok: 1, shares: [] }] }),
    ).toThrow(/forbidden ledger key "shares"/);
    expect(() =>
      assertNoLedgerKeys({ user: { payerMemberUuid: "m-1" } }),
    ).toThrow(/payerMemberUuid/);
  });
});

describe("admin responses carry no ledger data", () => {
  it("AdminReads_AllEndpoints_ReturnNoLedgerKey", async () => {
    const responses = await Promise.all([
      adminApi.metrics({ bucket: "month" }),
      adminApi.revenue({ bucket: "month" }),
      adminApi.listUsers({ page: 1, pageSize: 20, sort: "createdAt", direction: "desc" }),
      adminApi.getUser("uuid-nguyen-a"),
    ]);
    const ledgerSet = new Set(LEDGER_KEYS);
    for (const res of responses) {
      const keys = [...collectKeys(res)];
      const leaked = keys.filter((k) => ledgerSet.has(k));
      expect(leaked, `leaked ${leaked.join(",")}`).toHaveLength(0);
    }
  });
});

describe("no ledger key reaches the admin DOM", () => {
  async function expectNoLedgerKeyInDom(html: string) {
    for (const key of DOM_FORBIDDEN) {
      expect(html.includes(key), `DOM contains "${key}"`).toBe(false);
    }
  }

  it("AdminDashboardPage_Rendered_HasNoLedgerKeyInDom", async () => {
    const { container } = renderWithProviders(<AdminDashboardPage />);
    await screen.findByText("Tổng người dùng");
    await expectNoLedgerKeyInDom(container.innerHTML);
  });

  it("AdminRevenuePage_Rendered_HasNoLedgerKeyInDom", async () => {
    const { container } = renderWithProviders(<AdminRevenuePage />);
    await screen.findByText("Tổng doanh thu");
    await expectNoLedgerKeyInDom(container.innerHTML);
  });

  it("AdminUsersPage_Rendered_HasNoLedgerKeyInDom", async () => {
    const { container } = renderWithProviders(<AdminUsersPage />, {
      initialPath: "/admin/users",
    });
    await screen.findByRole("table", { name: "Danh sách người dùng" });
    await expectNoLedgerKeyInDom(container.innerHTML);
  });

  it("AdminUserDetailPage_Rendered_HasNoLedgerKeyInDom", async () => {
    const { container } = renderWithProviders(
      <Routes>
        <Route path="/admin/users/:uuid" element={<AdminUserDetailPage />} />
      </Routes>,
      { initialPath: "/admin/users/uuid-nguyen-a" },
    );
    await screen.findByText("Thông tin tài khoản");
    await expectNoLedgerKeyInDom(container.innerHTML);
  });
});

describe("tripwire trips on a leaked ledger key", () => {
  it("AdminMetrics_BackendLeaksLedgerKey_TripwireRejects", async () => {
    server.use(
      http.get("*/api/v1/admin/dashboard", () =>
        HttpResponse.json({
          data: {
            from: null,
            to: null,
            totalUsers: 1,
            tierDistribution: [],
            roleDistribution: [],
            statusDistribution: [],
            signups: [],
            // A leaked ledger field the tripwire must catch.
            expenses: [{ amount: 1 }],
          },
          isSuccess: true,
          error: null,
        }),
      ),
    );
    await expect(adminApi.metrics({ bucket: "month" })).rejects.toThrow(
      /forbidden ledger key "expenses"/,
    );
  });
});
