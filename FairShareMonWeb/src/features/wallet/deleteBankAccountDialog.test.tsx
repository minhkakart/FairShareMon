import { useState } from "react";
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
import { DeleteBankAccountDialog } from "./components/DeleteBankAccountDialog";
import type { BankAccountResponse } from "./api/types";

/**
 * DeleteBankAccountDialog — the confirm dialog against MSW. The body explains the
 * server's default-promotion when deleting the default (plain copy otherwise);
 * success toasts + closes; a `13003`/`12000` surfaces the localized server text +
 * closes. Delete is a Premium mutation; handlers are overridden per test.
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

const DEFAULT_ACCOUNT: BankAccountResponse = {
  uuid: "ba-1",
  bankBin: "970436",
  bankName: "Vietcombank",
  accountNumber: "0071001234567",
  accountHolderName: "NGUYEN VAN MINH",
  isDefault: true,
  createdAt: "2026-01-01T00:00:00+00:00",
};
const NON_DEFAULT_ACCOUNT: BankAccountResponse = {
  ...DEFAULT_ACCOUNT,
  uuid: "ba-2",
  bankName: "Techcombank",
  isDefault: false,
};

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-badel-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-badel-t",
    refreshTokenExpiresAt: future,
    user: { username: "badel", tier: "PREMIUM", role: "USER" },
    profileStatus: "resolved",
  });
}

function Harness({ account }: { account: BankAccountResponse }) {
  const [open, setOpen] = useState(true);
  return (
    <DeleteBankAccountDialog
      account={account}
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

describe("DeleteBankAccountDialog body copy", () => {
  it("DeleteBankAccountDialog_DefaultAccount_ShowsPromotionBody", async () => {
    renderWithProviders(<Harness account={DEFAULT_ACCOUNT} />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    expect(
      within(dialog).getByRole("heading", { name: "Xóa tài khoản Vietcombank?" }),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        /tài khoản được thêm gần nhất còn lại sẽ trở thành mặc định/,
      ),
    ).toBeInTheDocument();
  });

  it("DeleteBankAccountDialog_NonDefaultAccount_ShowsPlainBody", async () => {
    renderWithProviders(<Harness account={NON_DEFAULT_ACCOUNT} />, {
      queryClient,
    });
    const dialog = await screen.findByRole("dialog");

    expect(
      within(dialog).getByText(
        "Tài khoản ngân hàng này sẽ bị xóa khỏi ví của bạn.",
      ),
    ).toBeInTheDocument();
    expect(
      within(dialog).queryByText(/trở thành mặc định/),
    ).not.toBeInTheDocument();
  });
});

describe("DeleteBankAccountDialog outcomes", () => {
  it("DeleteBankAccountDialog_Confirm_SuccessToastsAndCloses", async () => {
    server.use(
      http.delete("*/api/v1/bank-accounts/:uuid", () =>
        ok({ message: "Đã xóa tài khoản ngân hàng." }),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness account={NON_DEFAULT_ACCOUNT} />, {
      queryClient,
    });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Xóa tài khoản" }));

    expect(
      await screen.findByText("Đã xóa tài khoản ngân hàng."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("DeleteBankAccountDialog_Cancel_ClosesWithNoRequest", async () => {
    let deletes = 0;
    server.use(
      http.delete("*/api/v1/bank-accounts/:uuid", () => {
        deletes += 1;
        return ok({ message: "x" });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness account={NON_DEFAULT_ACCOUNT} />, {
      queryClient,
    });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getAllByRole("button", { name: "Hủy" })[0]);

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(deletes).toBe(0);
  });

  it("DeleteBankAccountDialog_13003_ToastsLocalizedGateAndCloses", async () => {
    server.use(
      http.delete("*/api/v1/bank-accounts/:uuid", () =>
        fail(
          13003,
          "Tính năng này chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng.",
          403,
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness account={NON_DEFAULT_ACCOUNT} />, {
      queryClient,
    });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Xóa tài khoản" }));

    expect(
      await screen.findByText(
        "Tính năng này chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng.",
      ),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("DeleteBankAccountDialog_12000Stale_ToastsLocalizedTextAndCloses", async () => {
    server.use(
      http.delete("*/api/v1/bank-accounts/:uuid", () =>
        fail(12000, "Không tìm thấy tài khoản ngân hàng.", 404),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness account={NON_DEFAULT_ACCOUNT} />, {
      queryClient,
    });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Xóa tài khoản" }));

    expect(
      await screen.findByText("Không tìm thấy tài khoản ngân hàng."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});
