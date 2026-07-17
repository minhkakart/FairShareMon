import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { useState } from "react";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { AssignExpenseDialog } from "./components/AssignExpenseDialog";
import type { ExpenseSummaryResponse } from "@/features/expenses/api/types";

/**
 * AssignExpenseDialog (OQ4a) — the picker offers the caller's LOOSE, in-range
 * expenses as a single-select radio list; the eligible query is seeded from the
 * event range (looseOnly + from/to). Confirm → PUT /expenses/:uuid/event with
 * `{ eventUuid }`. A `9002` (out-of-range) surfaces inline; success closes.
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

const EVENT_UUID = "ev-1";

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-assign-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-assign-t",
    refreshTokenExpiresAt: future,
    user: { username: "assign", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function makeSummary(
  overrides: Partial<ExpenseSummaryResponse> = {},
): ExpenseSummaryResponse {
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
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-14T03:00:00+00:00",
    ...overrides,
  };
}

function Harness() {
  const [open, setOpen] = useState(true);
  return (
    <AssignExpenseDialog
      eventUuid={EVENT_UUID}
      eventName="Đà Lạt"
      startDate="2026-07-12T00:00:00+07:00"
      endDate="2026-07-18T23:59:59+07:00"
      open={open}
      onOpenChange={setOpen}
    />
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

describe("AssignExpenseDialog", () => {
  it("AssignExpenseDialog_EligibleExpenses_ShownAsSingleSelectRadioList", async () => {
    server.use(
      http.get("*/api/v1/expenses", () =>
        ok([
          makeSummary({ uuid: "e-1", name: "Thuê xe" }),
          makeSummary({ uuid: "e-2", name: "Ăn tối" }),
        ]),
      ),
    );
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    // The eligible loose in-range expenses are offered as radios (single-select).
    expect(
      await within(dialog).findByRole("radio", { name: /Thuê xe/ }),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByRole("radio", { name: /Ăn tối/ }),
    ).toBeInTheDocument();
    // The confirm is disabled until a selection is made.
    expect(
      within(dialog).getByRole("button", { name: "Gán phiếu" }),
    ).toBeDisabled();
  });

  it("AssignExpenseDialog_SeedsQueryWithLooseOnlyAndEventRange", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/expenses", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );
    renderWithProviders(<Harness />, { queryClient });
    await screen.findByRole("dialog");

    await waitFor(() => expect(seenUrl).not.toBe(""));
    const params = new URL(seenUrl).searchParams;
    expect(params.get("looseOnly")).toBe("true");
    expect(params.get("from")).toBe("2026-07-12T00:00:00+07:00");
    expect(params.get("to")).toBe("2026-07-18T23:59:59+07:00");
  });

  it("AssignExpenseDialog_ConfirmSuccess_PutsEventUuidAndCloses", async () => {
    let body: { eventUuid: string } | undefined;
    let assignedUuid = "";
    server.use(
      http.get("*/api/v1/expenses", () => ok([makeSummary()])),
      http.put("*/api/v1/expenses/:uuid/event", async ({ request, params }) => {
        assignedUuid = String(params.uuid);
        body = (await request.json()) as typeof body;
        return ok(makeSummary());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await user.click(await within(dialog).findByRole("radio", { name: /Thuê xe/ }));
    await user.click(within(dialog).getByRole("button", { name: "Gán phiếu" }));

    await waitFor(() => expect(body).toBeDefined());
    expect(assignedUuid).toBe("e-1");
    expect(body!.eventUuid).toBe(EVENT_UUID);
    expect(await screen.findByText("Đã gán phiếu vào đợt.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("AssignExpenseDialog_9002_ShowsInlineOutOfRangeAndStaysOpen", async () => {
    server.use(
      http.get("*/api/v1/expenses", () => ok([makeSummary()])),
      http.put("*/api/v1/expenses/:uuid/event", () =>
        fail(
          9002,
          "Thời điểm chi của phiếu nằm ngoài khoảng thời gian của đợt.",
          400,
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await user.click(await within(dialog).findByRole("radio", { name: /Thuê xe/ }));
    await user.click(within(dialog).getByRole("button", { name: "Gán phiếu" }));

    expect(
      await within(dialog).findByText(
        "Thời điểm chi của phiếu nằm ngoài khoảng thời gian của đợt.",
      ),
    ).toBeInTheDocument();
    // Out-of-range is recoverable in place — the dialog stays open.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("AssignExpenseDialog_NoEligibleExpenses_ShowsEmptyState", async () => {
    server.use(http.get("*/api/v1/expenses", () => ok([])));
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");
    expect(
      await within(dialog).findByText("Không có phiếu nào để gán"),
    ).toBeInTheDocument();
  });
});
