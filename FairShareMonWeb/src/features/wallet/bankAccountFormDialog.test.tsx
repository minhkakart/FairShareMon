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

/** Open the bank picker, filter by `typed`, and select the option matching it. */
async function pickBank(
  user: ReturnType<typeof userEvent.setup>,
  dialog: HTMLElement,
  typed: string,
  optionLabel: string | RegExp,
) {
  await user.click(within(dialog).getByRole("button", { name: /Ngân hàng/ }));
  const search = await within(dialog).findByRole("combobox");
  await user.type(search, typed);
  await user.click(
    await within(dialog).findByRole("option", { name: optionLabel }),
  );
}

async function fillValid(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
  // Techcombank (970407) is in the MSW VietQR directory + the committed snapshot.
  await pickBank(user, dialog, "techcom", /Techcombank/);
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

    // Zod blocks the empty submit — the picker is required ("select a bank");
    // no request leaves the client.
    expect(
      await within(dialog).findByText("Vui lòng chọn ngân hàng."),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });

  it("BankAccountFormDialog_BadAccountNumber_ShowsPatternErrorClientSide", async () => {
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

    // A valid bank is picked (the picker only yields valid 6-digit BINs), but a
    // malformed account number is still blocked client-side.
    await pickBank(user, dialog, "techcom", /Techcombank/);
    await user.type(
      within(dialog).getByRole("textbox", { name: "Số tài khoản" }),
      "12ab",
    );
    await user.type(
      within(dialog).getByRole("textbox", { name: "Chủ tài khoản" }),
      "TRAN VAN B",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Số tài khoản gồm 6–19 chữ số."),
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
    // Form stays mounted for correction (the bank picker is still present).
    expect(
      within(dialog).getByRole("button", { name: /Ngân hàng/ }),
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
      within(dialog).getByRole("button", { name: /Ngân hàng/ }),
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

    // Edit mode pre-fills every field from the account — the picker trigger
    // shows the account's bank (Vietcombank 970436, matched in the directory).
    expect(
      within(dialog).getByRole("button", { name: /Vietcombank/ }),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByRole("textbox", { name: "Số tài khoản" }),
    ).toHaveValue("0071001234567");

    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));
    expect(
      await screen.findByText("Đã cập nhật tài khoản ngân hàng."),
    ).toBeInTheDocument();
  });
});

describe("BankAccountFormDialog bank picker → body", () => {
  /** A legacy/edited account whose BIN is NOT in the VietQR directory/snapshot. */
  const LEGACY: BankAccountResponse = {
    ...ACCOUNT,
    uuid: "ba-legacy",
    bankBin: "999999",
    bankName: "Ngân hàng Cũ",
  };

  it("BankAccountFormDialog_SelectBank_SubmitsBinAndShortNameInBody", async () => {
    let body: Record<string, unknown> | undefined;
    server.use(
      http.post("*/api/v1/bank-accounts", async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return ok({ ...ACCOUNT, uuid: "ba-new" });
      }),
      http.get("*/api/v1/bank-accounts", () => ok([])),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="create" />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    // Picking Techcombank sets bankBin (970407) AND bankName = its short name.
    await fillValid(user, dialog);
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    await screen.findByText("Đã thêm tài khoản ngân hàng.");
    // The submitted contract is unchanged and carries the derived values (D2/D3).
    expect(body).toEqual({
      bankBin: "970407",
      bankName: "Techcombank",
      accountNumber: "123456789",
      accountHolderName: "TRAN VAN B",
    });
  });

  it("BankAccountFormDialog_EditUnknownBin_ShowsSyntheticOptionAndSubmitsStoredValues", async () => {
    let body: Record<string, unknown> | undefined;
    server.use(
      http.put("*/api/v1/bank-accounts/:uuid", async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return ok(LEGACY);
      }),
      http.get("*/api/v1/bank-accounts", () => ok([])),
    );
    const user = userEvent.setup();
    renderWithProviders(<Harness mode="edit" account={LEGACY} />, { queryClient });
    const dialog = await screen.findByRole("dialog");

    // The unknown BIN pre-selects via a synthetic option carrying the stored name
    // (no logo) — nothing is lost; the trigger renders that synthetic option.
    expect(
      within(dialog).getByRole("button", { name: /Ngân hàng Cũ/ }),
    ).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));
    await screen.findByText("Đã cập nhật tài khoản ngân hàng.");
    // The stored BIN + name round-trip unchanged (picker-only, no data loss — R4).
    expect(body).toMatchObject({ bankBin: "999999", bankName: "Ngân hàng Cũ" });
  });
});
