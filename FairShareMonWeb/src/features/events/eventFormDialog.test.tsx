import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { EventFormDialog } from "./components/EventFormDialog";
import { isoToDateInput } from "./dateRange";
import type { EventResponse } from "./api/types";

/**
 * EventFormDialog (shared create/edit, OQ2a) against MSW. Create submits
 * noon-anchored ISO dates (OQ5a) and closes on success; `13001` shows the inline
 * LimitNotice (form stays mounted, OQ9a); edit pre-fills + PUTs; `9003`
 * (range-excludes-assigned) shows a form-level message and stays open; `9001`
 * (edit a closed event) toasts + closes; `1001` maps onto fields; a client-side
 * `endDate < startDate` is blocked before any request.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: {
    code: number;
    message: string;
    fields?: Record<string, string[]>;
  } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}
function fail(
  code: number,
  message: string,
  status: number,
  fields?: Record<string, string[]>,
) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message, fields } },
    { status },
  );
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-evform-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-evform-t",
    refreshTokenExpiresAt: future,
    user: { username: "evform", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function madeEvent(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    uuid: "ev-1",
    name: "Đà Lạt",
    description: "Chuyến đi",
    startDate: "2026-07-12T00:00:00+07:00",
    endDate: "2026-07-18T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 0,
    createdAt: "2026-07-01T00:00:00+00:00",
    ...overrides,
  };
}

function Harness({
  event,
  onCreated,
}: {
  event?: EventResponse;
  onCreated?: (uuid: string) => void;
}) {
  const [open, setOpen] = useState(true);
  return (
    <EventFormDialog
      event={event}
      open={open}
      onOpenChange={setOpen}
      onCreated={onCreated}
    />
  );
}

async function fillForm(
  dialog: HTMLElement,
  values: { name?: string; description?: string; start?: string; end?: string },
) {
  // The labels carry a required-asterisk span, so match by substring (regex).
  if (values.name !== undefined) {
    fireEvent.change(within(dialog).getByLabelText(/Tên đợt/), {
      target: { value: values.name },
    });
  }
  if (values.description !== undefined) {
    fireEvent.change(within(dialog).getByLabelText(/Mô tả/), {
      target: { value: values.description },
    });
  }
  if (values.start !== undefined) {
    fireEvent.change(within(dialog).getByLabelText(/Ngày bắt đầu/), {
      target: { value: values.start },
    });
  }
  if (values.end !== undefined) {
    fireEvent.change(within(dialog).getByLabelText(/Ngày kết thúc/), {
      target: { value: values.end },
    });
  }
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

describe("EventFormDialog create", () => {
  it("EventFormDialog_CreateSuccess_PostsNoonAnchoredDatesToastsAndCloses", async () => {
    let body: { name: string; startDate: string; endDate: string } | undefined;
    const onCreated = vi.fn();
    server.use(
      http.post("*/api/v1/events", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok(madeEvent({ uuid: "ev-new", name: "Nha Trang" }));
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness onCreated={onCreated} />, { queryClient });

    const dialog = await screen.findByRole("dialog");
    await fillForm(dialog, {
      name: "  Nha Trang  ",
      start: "2026-07-12",
      end: "2026-07-18",
    });
    await user.click(within(dialog).getByRole("button", { name: "Thêm đợt" }));

    await waitFor(() => expect(body).toBeDefined());
    // Name is trimmed; dates are noon-anchored ISO whose calendar day matches input.
    expect(body!.name).toBe("Nha Trang");
    expect(body!.startDate).toBe("2026-07-12T05:00:00.000Z");
    expect(body!.endDate).toBe("2026-07-18T05:00:00.000Z");
    expect(isoToDateInput(body!.startDate)).toBe("2026-07-12");
    expect(isoToDateInput(body!.endDate)).toBe("2026-07-18");

    expect(await screen.findByText("Đã thêm đợt chi tiêu.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(onCreated).toHaveBeenCalledWith("ev-new");
  });

  it("EventFormDialog_Create13001_ShowsLimitNoticeAndStaysOpen", async () => {
    server.use(
      http.post("*/api/v1/events", () =>
        fail(13001, "Đã đạt giới hạn số đợt đang mở.", 400),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });

    const dialog = await screen.findByRole("dialog");
    await fillForm(dialog, {
      name: "Đợt thứ tư",
      start: "2026-07-12",
      end: "2026-07-18",
    });
    await user.click(within(dialog).getByRole("button", { name: "Thêm đợt" }));

    // The informational LimitNotice appears in place; the form stays mounted (R9).
    expect(
      await screen.findByText("Đã đạt giới hạn số đợt đang mở"),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("EventFormDialog_EndBeforeStart_BlockedClientSideNoRequest", async () => {
    let posts = 0;
    server.use(
      http.post("*/api/v1/events", () => {
        posts += 1;
        return ok(madeEvent());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });

    const dialog = await screen.findByRole("dialog");
    await fillForm(dialog, {
      name: "Đảo ngày",
      start: "2026-07-18",
      end: "2026-07-12",
    });
    await user.click(within(dialog).getByRole("button", { name: "Thêm đợt" }));

    // The Zod refinement blocks submit — the endDate error shows, no POST fires.
    expect(
      await within(dialog).findByText(
        "Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.",
      ),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });

  it("EventFormDialog_Create1001_MapsServerFieldErrorOntoName", async () => {
    server.use(
      http.post("*/api/v1/events", () =>
        fail(1001, "Dữ liệu không hợp lệ.", 400, {
          name: ["Tên đợt không được để trống."],
        }),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });

    const dialog = await screen.findByRole("dialog");
    await fillForm(dialog, {
      name: "x",
      start: "2026-07-12",
      end: "2026-07-18",
    });
    await user.click(within(dialog).getByRole("button", { name: "Thêm đợt" }));

    expect(
      await within(dialog).findByText("Tên đợt không được để trống."),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

describe("EventFormDialog edit", () => {
  it("EventFormDialog_Edit_PreFillsAndPuts", async () => {
    let putBody: { name: string } | undefined;
    server.use(
      http.put("*/api/v1/events/ev-1", async ({ request }) => {
        putBody = (await request.json()) as typeof putBody;
        return ok(madeEvent({ name: "Đà Lạt (sửa)" }));
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness event={madeEvent()} />, { queryClient });

    const dialog = await screen.findByRole("dialog");
    // Pre-filled from the event DTO.
    expect(within(dialog).getByLabelText(/Tên đợt/)).toHaveValue("Đà Lạt");
    expect(within(dialog).getByLabelText(/Ngày bắt đầu/)).toHaveValue(
      isoToDateInput("2026-07-12T00:00:00+07:00"),
    );

    fireEvent.change(within(dialog).getByLabelText(/Tên đợt/), {
      target: { value: "Đà Lạt (sửa)" },
    });
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    await waitFor(() => expect(putBody).toBeDefined());
    expect(putBody!.name).toBe("Đà Lạt (sửa)");
    expect(await screen.findByText("Đã cập nhật đợt chi tiêu.")).toBeInTheDocument();
  });

  it("EventFormDialog_Edit9003_ShowsFormLevelMessageAndStaysOpen", async () => {
    server.use(
      http.put("*/api/v1/events/ev-1", () =>
        fail(9003, "Khoảng thời gian mới loại một phiếu đã gán ra ngoài đợt.", 400),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness event={madeEvent()} />, { queryClient });

    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/Ngày kết thúc/), {
      target: { value: "2026-07-13" },
    });
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await within(dialog).findByText(
        "Khoảng thời gian mới loại một phiếu đã gán ra ngoài đợt.",
      ),
    ).toBeInTheDocument();
    // Range conflict is recoverable in place — the dialog stays open.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("EventFormDialog_Edit9001_ToastsAndCloses", async () => {
    server.use(
      http.put("*/api/v1/events/ev-1", () =>
        fail(9001, "Đợt chi tiêu đã chốt.", 400),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness event={madeEvent()} />, { queryClient });

    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/Tên đợt/), {
      target: { value: "Đà Lạt khác" },
    });
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    // A defensive close-guard (9001) is terminal → toast + close.
    expect(await screen.findByText("Đợt chi tiêu đã chốt.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});
