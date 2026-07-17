import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { useState } from "react";
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
import { DeleteEventDialog } from "./components/DeleteEventDialog";

/**
 * DeleteEventDialog — hard-delete confirm for an OPEN event. The body states the
 * event's expenses become loose (not deleted). Close-on-error per OQ7a: close on
 * success + terminal codes (9000 / 9001), stay open with an inline error on
 * network/transient for in-place retry. Rendered in a Router so the success
 * navigation to /events is observable.
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
    accessToken: "access-evdel-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-evdel-t",
    refreshTokenExpiresAt: future,
    user: { username: "evdel", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function Harness() {
  const [open, setOpen] = useState(true);
  return (
    <DeleteEventDialog
      uuid="ev-1"
      name="Đà Lạt"
      open={open}
      onOpenChange={setOpen}
    />
  );
}

function renderDelete() {
  return renderWithProviders(
    <Routes>
      <Route path="/events/:uuid" element={<Harness />} />
      <Route path="/events" element={<div>LIST</div>} />
    </Routes>,
    { initialPath: "/events/ev-1", queryClient },
  );
}

const CONFIRM = "Xóa đợt";

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

describe("DeleteEventDialog", () => {
  it("DeleteEventDialog_Open_ExplainsExpensesBecomeLoose", async () => {
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Xóa đợt Đà Lạt?")).toBeInTheDocument();
    // The body states expenses are NOT deleted — they become loose.
    expect(
      within(dialog).getByText(/trở thành phiếu lẻ/),
    ).toBeInTheDocument();
  });

  it("DeleteEventDialog_ConfirmSuccess_ToastsClosesAndNavigatesToList", async () => {
    server.use(
      http.delete("*/api/v1/events/ev-1", () =>
        ok({ message: "Đã xóa đợt chi tiêu." }),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: CONFIRM }));

    expect(await screen.findByText("Đã xóa đợt chi tiêu.")).toBeInTheDocument();
    expect(await screen.findByText("LIST")).toBeInTheDocument();
  });

  it("DeleteEventDialog_Terminal9000_ToastsAndCloses", async () => {
    server.use(
      http.delete("*/api/v1/events/ev-1", () =>
        fail(9000, "Không tìm thấy đợt chi tiêu.", 404),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: CONFIRM }));

    expect(
      await screen.findByText("Không tìm thấy đợt chi tiêu."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("DeleteEventDialog_Terminal9001_ToastsAndCloses", async () => {
    server.use(
      http.delete("*/api/v1/events/ev-1", () =>
        fail(9001, "Đợt chi tiêu đã chốt.", 400),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: CONFIRM }));

    expect(await screen.findByText("Đợt chi tiêu đã chốt.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("DeleteEventDialog_TransientServerError_StaysOpenWithInlineErrorForRetry", async () => {
    server.use(
      http.delete("*/api/v1/events/ev-1", () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: CONFIRM }));

    expect(
      await within(dialog).findByText("Đã xảy ra lỗi máy chủ."),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.queryByText("LIST")).not.toBeInTheDocument();
  });
});
