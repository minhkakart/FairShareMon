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
import { AddExpenseDialog } from "./components/AddExpenseDialog";
import { useEventsQuery } from "./hooks/useEvents";
import type { EventResponse } from "./api/types";

/**
 * AddExpenseDialog (F2) — the "Thêm phiếu" popup on an OPEN event's detail page.
 * Holds the shared create form with the current event LOCKED (read-only, no
 * editable event control) and always submits `eventUuid = event.uuid`. On success
 * the dialog closes + toasts; a since-closed/vanished event (9001/9000) toasts
 * danger + closes; 9002 stays open as an `expenseTime` field error; 13002 shows
 * the LimitNotice inside the dialog; a successful create with an eventUuid
 * invalidates the events caches so the detail refetches.
 *
 * Members/categories/tags load from the seeded per-user mock store (real
 * handlers); POST /expenses is stubbed per test. Uses the app singleton
 * queryClient so `useCreateExpense`'s invalidation reaches mounted event queries.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string; fields?: Record<string, string[]> } | null;
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

let userSeq = 0;
function seedSession(): void {
  userSeq += 1;
  const username = `adx${userSeq}`;
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: `access-${username}-t`,
    accessTokenExpiresAt: future,
    refreshToken: `refresh-${username}-t`,
    refreshTokenExpiresAt: future,
    user: { username, tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function makeEvent(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    uuid: "ev-locked",
    name: "Đà Lạt",
    description: null,
    startDate: "2026-01-01T00:00:00+07:00",
    endDate: "2026-12-31T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 0,
    createdAt: "2026-07-01T00:00:00+00:00",
    ...overrides,
  };
}

/** A created expense carrying the locked event's uuid (so events invalidate). */
function createdInEvent(eventUuid = "ev-locked") {
  return {
    uuid: "e-new",
    name: "Thuê xe",
    description: null,
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 0,
    category: {
      uuid: "c-d",
      name: "Ăn uống",
      color: "#F97316",
      icon: "🍜",
      isDefault: true,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    payer: {
      uuid: "m-o",
      name: "Bạn (chủ sổ)",
      isOwnerRepresentative: true,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    shares: [],
    tags: [],
    eventUuid,
    eventName: "Đà Lạt",
    eventIsClosed: false,
    createdAt: "2026-07-16T03:00:00+00:00",
  };
}

function Harness({ event = makeEvent() }: { event?: EventResponse }) {
  const [open, setOpen] = useState(true);
  return <AddExpenseDialog event={event} open={open} onOpenChange={setOpen} />;
}

/** Harness with a mounted events query so invalidation-driven refetch is observable. */
function ProbeHarness({ event = makeEvent() }: { event?: EventResponse }) {
  const [open, setOpen] = useState(true);
  useEventsQuery({});
  return <AddExpenseDialog event={event} open={open} onOpenChange={setOpen} />;
}

/** Wait for the dialog form to finish its members/categories/tags load. */
async function waitForDialogForm(): Promise<HTMLElement> {
  const dialog = await screen.findByRole("dialog");
  await within(dialog).findByRole("textbox", { name: "Tên phiếu" });
  return dialog;
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

describe("AddExpenseDialog locked event", () => {
  it("AddExpenseDialog_Open_ShowsEventLockedReadOnlyWithNoEditableEventControl", async () => {
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await waitForDialogForm();

    // The locked display: label + the event name + the lock badge, conveyed by
    // text AND icon (not colour alone).
    expect(within(dialog).getByText("Đợt")).toBeInTheDocument();
    expect(within(dialog).getByText("Đà Lạt")).toBeInTheDocument();
    expect(within(dialog).getByText("khóa")).toBeInTheDocument();
    expect(
      within(dialog).getByText("Phiếu sẽ được thêm vào đợt này."),
    ).toBeInTheDocument();

    // There is NO editable event control (no Combobox, no loose option).
    expect(
      within(dialog).queryByRole("combobox", { name: /Đợt/ }),
    ).not.toBeInTheDocument();
    expect(
      within(dialog).queryByText("Không thuộc đợt (phiếu lẻ)"),
    ).not.toBeInTheDocument();
  });
});

describe("AddExpenseDialog submit", () => {
  it("AddExpenseDialog_Submit_PostsLockedEventUuidThenToastsAndCloses", async () => {
    let body: { eventUuid?: string } | null = null;
    server.use(
      http.post("*/api/v1/expenses", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok(createdInEvent());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await waitForDialogForm();

    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm phiếu" }));

    // The body carries the locked event's uuid.
    await waitFor(() => expect(body).not.toBeNull());
    expect(body!.eventUuid).toBe("ev-locked");
    // Success toast shows and the dialog closes.
    expect(
      await screen.findByText("Đã thêm phiếu chi tiêu."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("AddExpenseDialog_SuccessWithEvent_InvalidatesEventsCachesSoDetailRefetches", async () => {
    let listCalls = 0;
    server.use(
      http.get("*/api/v1/events", () => {
        listCalls += 1;
        return ok([]);
      }),
      http.post("*/api/v1/expenses", () => ok(createdInEvent())),
    );
    const user = userEvent.setup();
    renderWithProviders(<ProbeHarness />, { queryClient });
    const dialog = await waitForDialogForm();

    await waitFor(() => expect(listCalls).toBe(1));

    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm phiếu" }));

    // The created expense joined an event → eventsKeys.all is invalidated → the
    // mounted events query refetches (a second GET /events), refreshing counts/
    // balance on the detail page.
    await waitFor(() => expect(listCalls).toBeGreaterThanOrEqual(2));
  });
});

describe("AddExpenseDialog error branches", () => {
  it("AddExpenseDialog_9001_ToastsDangerAndCloses", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(9001, "Đợt chi tiêu đã chốt.", 400),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await waitForDialogForm();

    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm phiếu" }));

    // A since-closed event → danger toast (the backend message) + dialog closes.
    expect(await screen.findByText("Đợt chi tiêu đã chốt.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("AddExpenseDialog_9000_ToastsDangerAndCloses", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(9000, "Không tìm thấy đợt chi tiêu.", 404),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await waitForDialogForm();

    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm phiếu" }));

    expect(
      await screen.findByText("Không tìm thấy đợt chi tiêu."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("AddExpenseDialog_9002_ShowsExpenseTimeFieldErrorAndStaysOpen", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(
          9002,
          "Thời điểm chi của phiếu nằm ngoài khoảng thời gian của đợt.",
          400,
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await waitForDialogForm();

    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm phiếu" }));

    // Out-of-range is recoverable in place → field error on the time, dialog stays.
    expect(
      await within(dialog).findByText(
        "Thời điểm chi của phiếu nằm ngoài khoảng thời gian của đợt.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("AddExpenseDialog_13002_ShowsLimitNoticeInsideDialog", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(13002, "Đã đạt giới hạn phiếu tháng này.", 400),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await waitForDialogForm();

    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm phiếu" }));

    expect(
      await within(dialog).findByText(
        "Đã đạt giới hạn phiếu chi tiêu trong tháng",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
