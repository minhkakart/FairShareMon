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
import { DeleteExpenseDialog } from "./components/DeleteExpenseDialog";

/**
 * DeleteExpenseDialog — hard-delete confirm (B2) with the OQ12a close-on-error
 * behavior: close on success + terminal codes (6000 / 9001), stay open with an
 * inline error on network/transient failures for in-place retry. Rendered inside
 * a Router so the success navigation to the list is observable.
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
    accessToken: "access-del-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-del-t",
    refreshTokenExpiresAt: future,
    user: { username: "del", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function DeleteHarness() {
  const [open, setOpen] = useState(true);
  return (
    <DeleteExpenseDialog
      uuid="e-1"
      name="Thuê xe"
      open={open}
      onOpenChange={setOpen}
    />
  );
}

function renderDelete() {
  return renderWithProviders(
    <Routes>
      <Route path="/expenses/:uuid" element={<DeleteHarness />} />
      <Route path="/expenses" element={<div>LIST</div>} />
    </Routes>,
    { initialPath: "/expenses/e-1", queryClient },
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

describe("DeleteExpenseDialog", () => {
  it("DeleteExpenseDialog_Open_ShowsNamedTitleCascadeAndSurvivingAuditCopy", async () => {
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByText("Xóa phiếu Thuê xe?"),
    ).toBeInTheDocument();
    // Body explains the hard-delete cascade AND the surviving audit.
    expect(
      within(dialog).getByText(/toàn bộ phần gánh của nó sẽ bị xóa vĩnh viễn/),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(/Nhật ký thay đổi vẫn được giữ lại/),
    ).toBeInTheDocument();
  });

  it("DeleteExpenseDialog_ConfirmSuccess_ToastsClosesAndNavigatesToList", async () => {
    server.use(
      http.delete("*/api/v1/expenses/e-1", () =>
        ok({ message: "Đã xóa phiếu chi tiêu." }),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Xóa phiếu" }));

    expect(
      await screen.findByText("Đã xóa phiếu chi tiêu."),
    ).toBeInTheDocument();
    // Navigated back to the list.
    expect(await screen.findByText("LIST")).toBeInTheDocument();
  });

  it("DeleteExpenseDialog_Terminal6000_ToastsAndCloses", async () => {
    server.use(
      http.delete("*/api/v1/expenses/e-1", () =>
        fail(6000, "Không tìm thấy phiếu chi tiêu.", 404),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Xóa phiếu" }));

    expect(
      await screen.findByText("Không tìm thấy phiếu chi tiêu."),
    ).toBeInTheDocument();
    // Terminal code → the dialog closes (nothing to retry against).
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("DeleteExpenseDialog_Terminal9001_ToastsAndCloses", async () => {
    server.use(
      http.delete("*/api/v1/expenses/e-1", () =>
        fail(9001, "Đợt đã chốt.", 400),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Xóa phiếu" }));

    expect(await screen.findByText("Đợt đã chốt.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("DeleteExpenseDialog_TransientServerError_StaysOpenWithInlineErrorForRetry", async () => {
    // OQ12a: a non-terminal (transient) failure keeps the dialog OPEN with an
    // inline error, so the user can retry in place — the fix carried from M3.
    server.use(
      http.delete("*/api/v1/expenses/e-1", () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Xóa phiếu" }));

    expect(
      await within(dialog).findByText("Đã xảy ra lỗi máy chủ."),
    ).toBeInTheDocument();
    // The dialog remains mounted (not closed) + no list navigation happened.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.queryByText("LIST")).not.toBeInTheDocument();
  });

  it("DeleteExpenseDialog_NetworkError_StaysOpenWithInlineError", async () => {
    server.use(http.delete("*/api/v1/expenses/e-1", () => HttpResponse.error()));
    const user = userEvent.setup();
    renderDelete();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Xóa phiếu" }));

    // A failed fetch (network) is non-terminal → dialog stays open for retry.
    await waitFor(() =>
      expect(within(dialog).queryByRole("alert")).toBeInTheDocument(),
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
