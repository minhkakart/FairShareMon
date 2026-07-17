import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ExpenseEventControl } from "./components/ExpenseEventControl";
import type { ExpenseResponse } from "./api/types";

/**
 * ExpenseEventControl (OQ3a, expense side) against MSW. It lists the caller's OPEN
 * events (GET /events?closed=false) to assign/move, and removes via the expense
 * event routes. Error mapping: 9002 (out of range) → inline on the Select; 9001
 * (closed source/target) → toast; a closed OWNING event renders read-only.
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

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-eec-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-eec-t",
    refreshTokenExpiresAt: future,
    user: { username: "eec", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function openEvents() {
  return [
    {
      uuid: "ev-1",
      name: "Đà Lạt",
      startDate: "2026-07-12T00:00:00+07:00",
      endDate: "2026-07-18T23:59:59+07:00",
      isClosed: false,
      closedAt: null,
      expenseCount: 0,
      createdAt: "2026-07-01T00:00:00+00:00",
    },
  ];
}

function makeExpense(overrides: Partial<ExpenseResponse> = {}): ExpenseResponse {
  return {
    uuid: "e-1",
    name: "Thuê xe",
    description: null,
    expenseTime: "2026-07-14T03:00:00+00:00",
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
      uuid: "m-1",
      name: "An Nguyễn",
      isOwnerRepresentative: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    shares: [],
    tags: [],
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-14T03:00:00+00:00",
    ...overrides,
  };
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
  server.use(http.get("*/api/v1/events", () => ok(openEvents())));
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("ExpenseEventControl loose expense", () => {
  it("ExpenseEventControl_Loose_ShowsLooseStateAndAssignSelectOfOpenEvents", async () => {
    renderWithProviders(<ExpenseEventControl expense={makeExpense()} />, {
      queryClient,
    });
    expect(
      screen.getByText("Phiếu lẻ (không thuộc đợt nào)"),
    ).toBeInTheDocument();
    // The assign Select is present (open events only).
    expect(
      await screen.findByRole("combobox", { name: "Gán vào đợt" }),
    ).toBeInTheDocument();
  });

  it("ExpenseEventControl_AssignOpenEvent_PutsAndToasts", async () => {
    let body: { eventUuid: string } | undefined;
    server.use(
      http.put("*/api/v1/expenses/e-1/event", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok(makeExpense({ eventUuid: "ev-1", eventName: "Đà Lạt" }));
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExpenseEventControl expense={makeExpense()} />, {
      queryClient,
    });

    await user.click(
      await screen.findByRole("combobox", { name: "Gán vào đợt" }),
    );
    await user.click(await screen.findByRole("option", { name: "Đà Lạt" }));

    await waitFor(() => expect(body).toBeDefined());
    expect(body!.eventUuid).toBe("ev-1");
    expect(await screen.findByText("Đã gán phiếu vào đợt.")).toBeInTheDocument();
  });

  it("ExpenseEventControl_Assign9002_ShowsInlineOutOfRange", async () => {
    server.use(
      http.put("*/api/v1/expenses/e-1/event", () =>
        fail(
          9002,
          "Thời điểm chi của phiếu nằm ngoài khoảng thời gian của đợt.",
          400,
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExpenseEventControl expense={makeExpense()} />, {
      queryClient,
    });

    await user.click(
      await screen.findByRole("combobox", { name: "Gán vào đợt" }),
    );
    await user.click(await screen.findByRole("option", { name: "Đà Lạt" }));

    // 9002 surfaces inline (on the Select), not as a toast.
    expect(
      await screen.findByText(
        "Thời điểm chi của phiếu nằm ngoài khoảng thời gian của đợt.",
      ),
    ).toBeInTheDocument();
  });

  it("ExpenseEventControl_Assign9001_ClosedTarget_Toasts", async () => {
    server.use(
      http.put("*/api/v1/expenses/e-1/event", () =>
        fail(9001, "Đợt chi tiêu đã chốt.", 400),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExpenseEventControl expense={makeExpense()} />, {
      queryClient,
    });

    await user.click(
      await screen.findByRole("combobox", { name: "Gán vào đợt" }),
    );
    await user.click(await screen.findByRole("option", { name: "Đà Lạt" }));

    // 9001 (closed target) is a toast.
    expect(await screen.findByText("Đợt chi tiêu đã chốt.")).toBeInTheDocument();
  });
});

describe("ExpenseEventControl assigned / closed expense", () => {
  it("ExpenseEventControl_AssignedOpen_ShowsCurrentEventAndRemove", async () => {
    let removed = false;
    server.use(
      http.delete("*/api/v1/expenses/e-1/event", () => {
        removed = true;
        return ok({ message: "Đã gỡ phiếu khỏi đợt." });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(
      <ExpenseEventControl
        expense={makeExpense({
          eventUuid: "ev-1",
          eventName: "Đà Lạt",
          eventIsClosed: false,
        })}
      />,
      { queryClient },
    );

    // The current event links to its detail; a move Select + remove are offered.
    expect(screen.getByRole("link", { name: "Đà Lạt" })).toHaveAttribute(
      "href",
      "/events/ev-1",
    );
    expect(
      await screen.findByRole("combobox", { name: "Chuyển sang đợt khác" }),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Gỡ khỏi đợt" }));
    await waitFor(() => expect(removed).toBe(true));
    expect(await screen.findByText("Đã gỡ phiếu khỏi đợt.")).toBeInTheDocument();
  });

  it("ExpenseEventControl_ClosedOwningEvent_IsReadOnly", () => {
    renderWithProviders(
      <ExpenseEventControl
        expense={makeExpense({
          eventUuid: "ev-1",
          eventName: "Đà Lạt",
          eventIsClosed: true,
        })}
      />,
      { queryClient },
    );

    // A closed owning event is immutable — read-only note, no assign/move/remove.
    expect(
      screen.getByText("Phiếu thuộc một đợt đã chốt — không thể chuyển hay gỡ."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Gỡ khỏi đợt" }),
    ).not.toBeInTheDocument();
  });
});
