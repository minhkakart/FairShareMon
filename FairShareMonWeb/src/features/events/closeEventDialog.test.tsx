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
import { CloseEventDialog } from "./components/CloseEventDialog";

/**
 * CloseEventDialog — the one-way close confirm. The irreversible copy is present;
 * a MANDATORY acknowledgment checkbox gates the danger button; success toasts +
 * closes; a terminal `9001` (already closed) toasts + closes; a transient 500
 * keeps the dialog open with an inline error for in-place retry (OQ7a).
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
    accessToken: "access-close-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-close-t",
    refreshTokenExpiresAt: future,
    user: { username: "close", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function Harness() {
  const [open, setOpen] = useState(true);
  return (
    <CloseEventDialog
      uuid="ev-1"
      name="Đà Lạt"
      open={open}
      onOpenChange={setOpen}
    />
  );
}

const CONFIRM = "Chốt đợt — không thể hoàn tác";

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

describe("CloseEventDialog", () => {
  it("CloseEventDialog_Open_ShowsIrreversibleCopyAndGatingCheckbox", async () => {
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    // Emphatic irreversible copy.
    expect(
      within(dialog).getByText(/hành động MỘT CHIỀU/),
    ).toBeInTheDocument();
    expect(within(dialog).getByText("Sau khi chốt, đợt bị khóa")).toBeInTheDocument();
    // The confirm button is disabled until the ack checkbox is ticked.
    expect(
      within(dialog).getByRole("button", { name: CONFIRM }),
    ).toBeDisabled();
  });

  it("CloseEventDialog_AckCheckbox_EnablesConfirm", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    const confirm = within(dialog).getByRole("button", { name: CONFIRM });
    expect(confirm).toBeDisabled();
    await user.click(within(dialog).getByRole("checkbox"));
    expect(confirm).toBeEnabled();
  });

  it("CloseEventDialog_ConfirmSuccess_ToastsAndCloses", async () => {
    let closed = 0;
    server.use(
      http.put("*/api/v1/events/ev-1/close", () => {
        closed += 1;
        return ok({ message: "Đã chốt đợt chi tiêu." });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("checkbox"));
    await user.click(within(dialog).getByRole("button", { name: CONFIRM }));

    expect(await screen.findByText("Đã chốt đợt chi tiêu.")).toBeInTheDocument();
    expect(closed).toBe(1);
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("CloseEventDialog_Terminal9001_ToastsAndCloses", async () => {
    server.use(
      http.put("*/api/v1/events/ev-1/close", () =>
        fail(9001, "Đợt chi tiêu đã chốt.", 400),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("checkbox"));
    await user.click(within(dialog).getByRole("button", { name: CONFIRM }));

    // Already-closed is treated as done → toast + close (nothing to retry).
    expect(await screen.findByText("Đợt chi tiêu đã chốt.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("CloseEventDialog_TransientServerError_StaysOpenWithInlineError", async () => {
    server.use(
      http.put("*/api/v1/events/ev-1/close", () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("checkbox"));
    await user.click(within(dialog).getByRole("button", { name: CONFIRM }));

    // OQ7a: a transient failure keeps the dialog open with an inline error.
    expect(
      await within(dialog).findByText("Đã xảy ra lỗi máy chủ."),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
