import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ExpenseDetailPage } from "./pages/ExpenseDetailPage";
import type { ExpenseResponse } from "./api/types";

/**
 * Layer A reconcile + whole-expense cascade — the REAL detail route/hooks against
 * a mutable MSW store. Because the per-share/whole-expense toggles are
 * refetch-based (OQ6a, no optimistic update), the UI must reconcile from the
 * expense-detail refetch after each mutation. The whole-expense toggle cascades
 * to every BILLABLE share on the server (OQ3a); the detail refetch surfaces the
 * cascaded share flags + the derived rollup. Network mocked at the client
 * boundary (MSW).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const UUID = "e-detail";

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-reconcile-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-reconcile-t",
    refreshTokenExpiresAt: future,
    user: { username: "reconcile", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

/** Payer = owner-rep (m-owner). One billable share (An, 300.000) + the owner-rep's
 *  own 0đ share (settled-by-definition, excluded from the rollup + cascade). */
function freshExpense(): ExpenseResponse {
  return {
    uuid: UUID,
    name: "Thuê xe",
    description: null,
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 300000,
    category: {
      uuid: "c-1",
      name: "Đi lại",
      color: "#3B82F6",
      icon: "🚗",
      isDefault: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    payer: {
      uuid: "m-owner",
      name: "Bạn (chủ sổ)",
      isOwnerRepresentative: true,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    shares: [
      {
        uuid: "s-owner",
        isSettled: false,
        settledAt: null,
        member: {
          uuid: "m-owner",
          name: "Bạn (chủ sổ)",
          isOwnerRepresentative: true,
          isDeleted: false,
          createdAt: "2026-01-01T00:00:00+00:00",
        },
        amount: 0,
        note: null,
        createdAt: "2026-07-16T03:00:00+00:00",
      },
      {
        uuid: "s-1",
        isSettled: false,
        settledAt: null,
        member: {
          uuid: "m-1",
          name: "An Nguyễn",
          isOwnerRepresentative: false,
          isDeleted: false,
          createdAt: "2026-01-01T00:00:00+00:00",
        },
        amount: 300000,
        note: null,
        createdAt: "2026-07-16T03:00:00+00:00",
      },
    ],
    tags: [],
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-16T03:00:00+00:00",
  };
}

let store: ExpenseResponse;

/** Mutable detail store + the two settled write routes that mutate it, so the
 *  refetch after each mutation returns the reconciled state. */
function installMutableStore() {
  store = freshExpense();
  server.use(
    http.get(`*/api/v1/expenses/${UUID}`, () =>
      // Return a fresh clone each read so React Query treats it as new data.
      ok(JSON.parse(JSON.stringify(store))),
    ),
    http.get(`*/api/v1/expenses/${UUID}/history`, () => ok([])),
    http.put(
      `*/api/v1/expenses/${UUID}/shares/:shareUuid/settled`,
      async ({ request, params }) => {
        const body = (await request.json()) as { isSettled?: boolean };
        const s = store.shares.find((x) => x.uuid === params.shareUuid);
        if (s) s.isSettled = Boolean(body.isSettled);
        return ok({ message: "OK" });
      },
    ),
    http.put(`*/api/v1/expenses/${UUID}/settled`, async ({ request }) => {
      const body = (await request.json()) as { isSettled?: boolean };
      const next = Boolean(body.isSettled);
      store.isSettled = next;
      // Cascade to billable shares only (not the payer's own / 0đ share).
      for (const s of store.shares) {
        if (s.member.uuid !== store.payer.uuid && s.amount > 0) {
          s.isSettled = next;
        }
      }
      return ok({ message: "OK" });
    }),
  );
}

function renderDetail() {
  return renderWithProviders(
    <Routes>
      <Route path="/expenses/:uuid" element={<ExpenseDetailPage />} />
    </Routes>,
    { initialPath: `/expenses/${UUID}`, queryClient },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
  installMutableStore();
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("ExpenseDetailPage Layer A reconcile", () => {
  it("ExpenseDetailPage_ToggleShareSettled_ReconcilesSwitchFromRefetch", async () => {
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    const toggle = screen.getByRole("switch", {
      name: "Trạng thái đã trả phần gánh của An Nguyễn",
    });
    expect(toggle).toHaveAttribute("aria-checked", "false");

    await user.click(toggle);

    // Refetch-based (OQ6a): the switch flips to checked ONLY after the detail
    // refetch returns the server-persisted flag.
    await waitFor(() =>
      expect(
        screen.getByRole("switch", {
          name: "Trạng thái đã trả phần gánh của An Nguyễn",
        }),
      ).toHaveAttribute("aria-checked", "true"),
    );
  });

  it("ExpenseDetailPage_WholeExpenseSettled_CascadesToShareTogglesAndRollup", async () => {
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    // Before: the billable share reads unsettled and the rollup is "Chưa trả".
    expect(
      screen.getByRole("switch", {
        name: "Trạng thái đã trả phần gánh của An Nguyễn",
      }),
    ).toHaveAttribute("aria-checked", "false");

    // Flip the WHOLE-expense header toggle on.
    await user.click(
      screen.getByRole("switch", { name: "Trạng thái đã trả của Thuê xe" }),
    );

    // The backend cascade marks every billable share settled; the detail refetch
    // reconciles the per-share switch…
    await waitFor(() =>
      expect(
        screen.getByRole("switch", {
          name: "Trạng thái đã trả phần gánh của An Nguyễn",
        }),
      ).toHaveAttribute("aria-checked", "true"),
    );
    // …and the derived rollup chip now reads "Đã trả toàn bộ".
    const header = screen.getByRole("heading", { name: "Phần gánh" })
      .parentElement as HTMLElement;
    expect(within(header).getByText("Đã trả toàn bộ")).toBeInTheDocument();
  });
});
