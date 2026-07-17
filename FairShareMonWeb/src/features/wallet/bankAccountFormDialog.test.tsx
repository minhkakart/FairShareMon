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
import { BankAccountFormDialog } from "./components/BankAccountFormDialog";
import type { BankAccountResponse } from "./api/types";

/**
 * BankAccountFormDialog — the REAL RHF + Zod dialog against MSW. Client validation
 * mirrors the backend validators; `1001` maps onto BIN/account fields; a stale-tier
 * `13003` renders an inline UpgradePrompt (form stays open); a stale-`12000` edit
 * toasts + closes; success toasts + closes. Handlers are overridden per test so the
 * dialog's error-code handling is the subject (the Premium gate itself is covered
 * by walletPage + qrDialog).
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

const ACCOUNT: BankAccountResponse = {
  uuid: "ba-1",
  bankBin: "970436",
  bankName: "Vietcombank",
  accountNumber: "0071001234567",
  accountHolderName: "NGUYEN VAN MINH",
  isDefault: true,
  createdAt: "2026-01-01T00:00:00+00:00",
};

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-baform-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-baform-t",
    refreshTokenExpiresAt: future,
    user: { username: "baform", tier: "PREMIUM", role: "USER" },
    profileStatus: "resolved",
  });
}

function Harness({
  mode,
  account,
}: {
  mode: "create" | "edit";
  account?: BankAccountResponse;
}) {
  const [open, setOpen] = useState(true);
  return (
    <BankAccountFormDialog
      mode={mode}
      account={account}
      open={open}
      onOpenChange={setOpen}
    />
  );
}

async function fillValid(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
  await user.type(
    within(dialog).getByRole("textbox", { name: "Tên ngân hàng" }),
    "ACB",
  );
  await user.type(
    within(dialog).getByRole("textbox", { name: "Mã ngân hàng (BIN)" }),
    "970416",
  );
  await user.type(
    within(dialog).getByRole("textbox", { name: "Số tài khoản" }),
    "123456789",
  );
  await user.type(
    within(dialog).getByRole("textbox", { name: "Chủ tài khoản" }),
    "TRAN VAN B",
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

describe("BankAccountFormDialog client validation", () => {
  it("BankAccountFormDialog_EmptySubmit_BlocksClientSideWithNoRequest", async () => {
    let posts = 0;
    server.use(
      http.post("*/api/v1/bank-accounts", () => {
        posts += 1;
        return ok(ACCOUNT);
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="create" />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    // Zod blocks the empty submit — no request leaves the client.
    expect(
      await within(dialog).findByText("Tên ngân hàng không được để trống."),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });

  it("BankAccountFormDialog_BadBin_ShowsPatternErrorClientSide", async () => {
    let posts = 0;
    server.use(
      http.post("*/api/v1/bank-accounts", () => {
        posts += 1;
        return ok(ACCOUNT);
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="create" />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên ngân hàng" }),
      "ACB",
    );
    await user.type(
      within(dialog).getByRole("textbox", { name: "Mã ngân hàng (BIN)" }),
      "12ab",
    );
    await user.type(
      within(dialog).getByRole("textbox", { name: "Số tài khoản" }),
      "123456789",
    );
    await user.type(
      within(dialog).getByRole("textbox", { name: "Chủ tài khoản" }),
      "TRAN VAN B",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("BIN gồm đúng 6 chữ số."),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });
});

describe("BankAccountFormDialog server errors", () => {
  it("BankAccountFormDialog_Create1001_MapsFieldErrorsOntoBinAndAccount", async () => {
    server.use(
      http.post("*/api/v1/bank-accounts", () =>
        fail(1001, "Dữ liệu không hợp lệ.", 400, {
          bankBin: ["BIN không hợp lệ theo máy chủ."],
          accountNumber: ["Số tài khoản không hợp lệ theo máy chủ."],
        }),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="create" />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await fillValid(user, dialog);
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("BIN không hợp lệ theo máy chủ."),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText("Số tài khoản không hợp lệ theo máy chủ."),
    ).toBeInTheDocument();
    // Form stays mounted for correction.
    expect(
      within(dialog).getByRole("textbox", { name: "Mã ngân hàng (BIN)" }),
    ).toBeInTheDocument();
  });

  it("BankAccountFormDialog_Create13003_RendersInlineUpgradePromptAndKeepsFormOpen", async () => {
    server.use(
      http.post("*/api/v1/bank-accounts", () =>
        fail(
          13003,
          "Tính năng này chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng.",
          403,
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="create" />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await fillValid(user, dialog);
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    // Inline UpgradePrompt (status) with the localized gate message; the form is
    // NOT destroyed and no success toast fires.
    expect(
      await within(dialog).findByText(
        "Tính năng này chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng.",
      ),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByRole("textbox", { name: "Tên ngân hàng" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Đã thêm tài khoản ngân hàng."),
    ).not.toBeInTheDocument();
  });

  it("BankAccountFormDialog_Edit12000Stale_ToastsAndCloses", async () => {
    server.use(
      http.put("*/api/v1/bank-accounts/:uuid", () =>
        fail(12000, "Không tìm thấy tài khoản ngân hàng.", 404),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="edit" account={ACCOUNT} />, {
      queryClient,
    });
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Không tìm thấy tài khoản ngân hàng."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});

describe("BankAccountFormDialog success", () => {
  it("BankAccountFormDialog_CreateSuccess_ToastsAndCloses", async () => {
    server.use(
      http.post("*/api/v1/bank-accounts", () =>
        ok({ ...ACCOUNT, uuid: "ba-new", bankName: "ACB" }),
      ),
      http.get("*/api/v1/bank-accounts", () => ok([])),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="create" />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    await fillValid(user, dialog);
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await screen.findByText("Đã thêm tài khoản ngân hàng."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("BankAccountFormDialog_EditPrefills_ThenUpdatesAndToasts", async () => {
    server.use(
      http.put("*/api/v1/bank-accounts/:uuid", () =>
        ok({ ...ACCOUNT, accountHolderName: "NGUYEN VAN C" }),
      ),
      http.get("*/api/v1/bank-accounts", () => ok([])),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="edit" account={ACCOUNT} />, {
      queryClient,
    });
    const dialog = await screen.findByRole("dialog");

    // Edit mode pre-fills every field from the account.
    expect(
      within(dialog).getByRole("textbox", { name: "Tên ngân hàng" }),
    ).toHaveValue("Vietcombank");
    expect(
      within(dialog).getByRole("textbox", { name: "Số tài khoản" }),
    ).toHaveValue("0071001234567");

    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));
    expect(
      await screen.findByText("Đã cập nhật tài khoản ngân hàng."),
    ).toBeInTheDocument();
  });
});
