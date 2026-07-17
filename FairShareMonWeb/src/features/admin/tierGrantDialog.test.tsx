import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { resetAdminStore } from "@/test/msw/handlers";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { TierGrantDialog } from "./components/users/TierGrantDialog";

/**
 * TierGrantDialog — grant Premium (records amount + reference/note). Proves: the
 * client schema blocks a missing amount (no request sent); a valid grant succeeds
 * with a success toast, closes the dialog, and invalidates the users cache; a
 * server `1001` with `fields.amount` maps onto the amount field. `MoneyInput`
 * strips non-digits so a negative can't be typed — the ≥0 rule + `1001 fields.amount`
 * mapping are the client + server halves of the negative-amount guard.
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

function Harness() {
  const [open, setOpen] = useState(true);
  return (
    <TierGrantDialog
      user={{ uuid: "uuid-le-b", username: "le.thi.b" }}
      open={open}
      onOpenChange={setOpen}
    />
  );
}

function render() {
  return renderWithProviders(<Harness />, { queryClient });
}

beforeEach(async () => {
  window.localStorage.clear();
  resetAdminStore();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedAdmin();
});
afterEach(() => {
  sessionStore.getState().clearSession();
  vi.restoreAllMocks();
});

describe("TierGrantDialog", () => {
  it("TierGrantDialog_MissingAmount_BlocksSubmitWithClientError", async () => {
    let requested = false;
    server.use(
      http.post("*/api/v1/admin/users/:uuid/tier/grant", () => {
        requested = true;
        return HttpResponse.json({ data: {}, isSuccess: true, error: null });
      }),
    );
    render();

    await userEvent.click(screen.getByRole("button", { name: "Cấp Premium" }));
    expect(await screen.findByText("Vui lòng nhập số tiền.")).toBeInTheDocument();
    expect(requested).toBe(false);
  });

  it("TierGrantDialog_ValidGrant_TogglesToastClosesAndInvalidates", async () => {
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    render();

    await userEvent.type(
      screen.getByRole("textbox", { name: "Số tiền (VND)" }),
      "200000",
    );
    await userEvent.click(screen.getByRole("button", { name: "Cấp Premium" }));

    // Success toast (verbatim vi-VN with the target name).
    expect(
      await screen.findByText("Đã cấp Premium cho le.thi.b."),
    ).toBeInTheDocument();
    // Dialog closes (the submit button unmounts).
    await waitFor(() =>
      expect(
        screen.queryByRole("button", { name: "Cấp Premium" }),
      ).not.toBeInTheDocument(),
    );
    // The user subtree was invalidated so list + detail refetch.
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["admin", "users"] });
  });

  it("TierGrantDialog_Server1001FieldsAmount_MapsOntoTheAmountField", async () => {
    server.use(
      http.post("*/api/v1/admin/users/:uuid/tier/grant", () =>
        HttpResponse.json(
          {
            data: null,
            isSuccess: false,
            error: {
              code: 1001,
              message: "Dữ liệu không hợp lệ.",
              fields: { amount: ["Số tiền không được âm."] },
            },
          },
          { status: 400 },
        ),
      ),
    );
    render();

    await userEvent.type(
      screen.getByRole("textbox", { name: "Số tiền (VND)" }),
      "100",
    );
    await userEvent.click(screen.getByRole("button", { name: "Cấp Premium" }));

    // The 1001 field error maps onto the amount field (verbatim message).
    expect(
      await screen.findByText("Số tiền không được âm."),
    ).toBeInTheDocument();
    // The dialog stays open on a validation failure.
    expect(screen.getByRole("button", { name: "Cấp Premium" })).toBeInTheDocument();
  });
});
