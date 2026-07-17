import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { EventExpensesSection } from "./components/EventExpensesSection";
import type { EventResponse } from "./api/types";

/**
 * EventExpensesSection — the event's expenses (GET /expenses?eventUuid=) with a
 * per-row remove-from-event (DELETE /expenses/:uuid/event) and a "Gán phiếu"
 * picker trigger. All write controls are open-only: a closed event hides them and
 * shows a read-only note. Assign is exercised end-to-end in assignExpenseDialog.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const EVENT_UUID = "ev-1";

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-evexp-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-evexp-t",
    refreshTokenExpiresAt: future,
    user: { username: "evexp", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function makeEvent(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    uuid: EVENT_UUID,
    name: "Đà Lạt",
    description: null,
    startDate: "2026-07-12T00:00:00+07:00",
    endDate: "2026-07-18T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 1,
    createdAt: "2026-07-01T00:00:00+00:00",
    ...overrides,
  };
}

function inEventExpense(overrides: Record<string, unknown> = {}) {
  return {
    uuid: "e-1",
    name: "Thuê xe",
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
    tagNames: [],
    shareCount: 1,
    eventUuid: EVENT_UUID,
    eventName: "Đà Lạt",
    eventIsClosed: false,
    createdAt: "2026-07-14T03:00:00+00:00",
    ...overrides,
  };
}

/** Route GET /expenses by filter: in-event list vs the assign-picker loose list. */
function stubExpenses(inEvent: unknown[], loose: unknown[] = []) {
  server.use(
    http.get("*/api/v1/expenses", ({ request }) => {
      const params = new URL(request.url).searchParams;
      if (params.get("looseOnly") === "true") return ok(loose);
      return ok(inEvent);
    }),
  );
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

describe("EventExpensesSection open event", () => {
  it("EventExpensesSection_OpenEvent_ListsExpensesWithRemoveAndAssign", async () => {
    stubExpenses([inEventExpense()]);
    renderWithProviders(<EventExpensesSection event={makeEvent()} />, {
      queryClient,
    });

    const row = (
      await screen.findByRole("rowheader", { name: "Thuê xe" })
    ).closest("tr") as HTMLElement;
    expect(within(row).getByText("An Nguyễn")).toBeInTheDocument();
    expect(within(row).getByText(/300\.000/)).toBeInTheDocument();
    // Per-row remove + the header assign trigger are both present when open.
    expect(
      within(row).getByRole("button", { name: "Gỡ phiếu Thuê xe khỏi đợt" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Gán phiếu" }),
    ).toBeInTheDocument();
  });

  it("EventExpensesSection_Remove_CallsDeleteAndToasts", async () => {
    let removedUuid = "";
    stubExpenses([inEventExpense()]);
    server.use(
      http.delete("*/api/v1/expenses/:uuid/event", ({ params }) => {
        removedUuid = String(params.uuid);
        return ok({ message: "Đã gỡ phiếu khỏi đợt." });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<EventExpensesSection event={makeEvent()} />, {
      queryClient,
    });

    await user.click(
      await screen.findByRole("button", { name: "Gỡ phiếu Thuê xe khỏi đợt" }),
    );

    expect(await screen.findByText("Đã gỡ phiếu khỏi đợt.")).toBeInTheDocument();
    expect(removedUuid).toBe("e-1");
  });

  it("EventExpensesSection_AssignTrigger_OpensThePicker", async () => {
    stubExpenses([inEventExpense()], [
      {
        ...inEventExpense({ uuid: "e-loose", name: "Ăn tối", eventUuid: null, eventName: null }),
      },
    ]);
    const user = userEvent.setup();
    renderWithProviders(<EventExpensesSection event={makeEvent()} />, {
      queryClient,
    });

    await screen.findByRole("rowheader", { name: "Thuê xe" });
    await user.click(screen.getByRole("button", { name: "Gán phiếu" }));

    // The picker dialog opens with its loose in-range candidate.
    const dialog = await screen.findByRole("dialog");
    expect(
      await within(dialog).findByRole("radio", { name: /Ăn tối/ }),
    ).toBeInTheDocument();
  });
});

describe("EventExpensesSection closed event", () => {
  it("EventExpensesSection_ClosedEvent_HidesAssignAndRemoveShowsReadOnly", async () => {
    stubExpenses([inEventExpense({ eventIsClosed: true })]);
    renderWithProviders(
      <EventExpensesSection event={makeEvent({ isClosed: true })} />,
      { queryClient },
    );

    await screen.findByRole("rowheader", { name: "Thuê xe" });
    // No assign trigger, no per-row remove — the section is read-only.
    expect(
      screen.queryByRole("button", { name: "Gán phiếu" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Gỡ phiếu/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByText("Đợt đã chốt — không thể thêm hoặc gỡ phiếu."),
    ).toBeInTheDocument();
    // The row shows the read-only marker instead of a remove control.
    await waitFor(() =>
      expect(screen.getByText("Chỉ đọc")).toBeInTheDocument(),
    );
  });
});
