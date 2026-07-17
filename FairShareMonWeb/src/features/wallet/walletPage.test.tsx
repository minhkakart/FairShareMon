import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { registerTestProfile } from "@/test/msw/handlers";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { WalletPage } from "./pages/WalletPage";

/**
 * WalletPage integration — the REAL page/hooks/dialogs against MSW at the client
 * boundary. Each test seeds a UNIQUE username so the per-user MSW bank-account
 * store (lazily seeded: Vietcombank default + Techcombank) is isolated. Premium
 * tests register the username as PREMIUM (so the committed mutation handlers run —
 * atomic default-swap, delete-promotion); Free tests leave it Free. Copy is vi-VN
 * default unless a spec flips the locale.
 *
 * Hybrid gate (OQ1a): Free = read-only table + informational UpgradePrompt (no
 * action controls); Premium = create / edit / set-default / delete. Reads are Free.
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

let userSeq = 0;
/** Seed an authenticated session with a fresh username → isolated MSW store. */
function seedSession(tier: "FREE" | "PREMIUM"): string {
  userSeq += 1;
  const username = `wtest${userSeq}`;
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: `access-${username}-t`,
    accessTokenExpiresAt: future,
    refreshToken: `refresh-${username}-t`,
    refreshTokenExpiresAt: future,
    user: { username, tier, role: "USER" },
    profileStatus: "resolved",
  });
  return username;
}

/** Premium on both sides: the session (proactive UI gate) + MSW (server gate). */
function seedPremium(): string {
  const username = seedSession("PREMIUM");
  registerTestProfile(username, "PREMIUM");
  return username;
}

function renderWallet() {
  return renderWithProviders(<WalletPage />, {
    initialPath: "/wallet",
    queryClient,
  });
}

/** The <tr> containing an account's bank row-header cell. */
function rowFor(bankName: string | RegExp): HTMLElement {
  const cell = screen.getByRole("rowheader", { name: bankName });
  const row = cell.closest("tr");
  if (!row) throw new Error(`No row for ${String(bankName)}`);
  return row as HTMLElement;
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

// ─── Premium: list + controls ────────────────────────────────────────────────
describe("WalletPage premium list", () => {
  it("WalletPage_PremiumList_RendersDefaultAccountFirst", async () => {
    seedPremium();
    renderWallet();

    await screen.findByRole("rowheader", { name: /Vietcombank/ });
    const banks = screen
      .getAllByRole("rowheader")
      .map((c) => c.textContent ?? "");
    // Backend order rendered verbatim: default (Vietcombank) first, then the rest.
    expect(banks[0]).toContain("Vietcombank");
    expect(banks[1]).toContain("Techcombank");
    // Exactly one default marker, on the default row.
    const defaultRow = rowFor(/Vietcombank/);
    expect(within(defaultRow).getByText("Mặc định")).toBeInTheDocument();
    expect(screen.getAllByText("Mặc định")).toHaveLength(1);
  });

  it("WalletPage_PremiumRow_HasSetDefaultEditDeleteControls", async () => {
    seedPremium();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Techcombank/ });

    const techRow = rowFor(/Techcombank/);
    // Non-default row exposes all three actions…
    expect(
      within(techRow).getByRole("button", {
        name: "Đặt Techcombank làm mặc định",
      }),
    ).toBeInTheDocument();
    expect(
      within(techRow).getByRole("button", { name: "Sửa Techcombank" }),
    ).toBeInTheDocument();
    expect(
      within(techRow).getByRole("button", { name: "Xóa Techcombank" }),
    ).toBeInTheDocument();
    // …the default row hides "set default" (it already is).
    const defaultRow = rowFor(/Vietcombank/);
    expect(
      within(defaultRow).queryByRole("button", {
        name: "Đặt Vietcombank làm mặc định",
      }),
    ).not.toBeInTheDocument();
  });

  it("WalletPage_MaskedNumberRevealToggle_TogglesAriaPressedAndFullNumber", async () => {
    seedPremium();
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Vietcombank/ });

    const defaultRow = rowFor(/Vietcombank/);
    // Masked by default (last 4 behind a dot run).
    expect(within(defaultRow).getByText("•••• 4567")).toBeInTheDocument();
    const toggle = within(defaultRow).getByRole("button", {
      name: "Hiện số tài khoản Vietcombank",
    });
    expect(toggle).toHaveAttribute("aria-pressed", "false");

    await user.click(toggle);
    // Revealed → grouped full number + aria-pressed flips + label becomes "hide".
    expect(within(defaultRow).getByText("0071 0012 3456 7")).toBeInTheDocument();
    expect(
      within(defaultRow).getByRole("button", {
        name: "Ẩn số tài khoản Vietcombank",
      }),
    ).toHaveAttribute("aria-pressed", "true");
  });
});

// ─── Premium: create / edit / set-default / delete ──────────────────────────
describe("WalletPage premium mutations", () => {
  it("WalletPage_CreateValid_AddsRowToastsAndClosesDialog", async () => {
    seedPremium();
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Vietcombank/ });

    await user.click(screen.getByRole("button", { name: "Thêm tài khoản" }));
    const dialog = await screen.findByRole("dialog");
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
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await screen.findByText("Đã thêm tài khoản ngân hàng."),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: /ACB/ }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("WalletPage_EditAccount_PrefillsAndUpdatesHolder", async () => {
    seedPremium();
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Techcombank/ });

    await user.click(screen.getByRole("button", { name: "Sửa Techcombank" }));
    const dialog = await screen.findByRole("dialog");
    const holder = within(dialog).getByRole("textbox", { name: "Chủ tài khoản" });
    expect(holder).toHaveValue("NGUYEN VAN MINH");
    await user.clear(holder);
    await user.type(holder, "NGUYEN VAN CAP NHAT");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Đã cập nhật tài khoản ngân hàng."),
    ).toBeInTheDocument();
    expect(
      await screen.findByText("NGUYEN VAN CAP NHAT"),
    ).toBeInTheDocument();
  });

  it("WalletPage_SetDefault_AtomicallySwapsToExactlyOneDefault", async () => {
    seedPremium();
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Techcombank/ });

    await user.click(
      screen.getByRole("button", { name: "Đặt Techcombank làm mặc định" }),
    );

    expect(
      await screen.findByText("Đã đặt tài khoản mặc định."),
    ).toBeInTheDocument();
    // After the server swap + cache invalidation: Techcombank is the sole default.
    await waitFor(() => {
      const techRow = rowFor(/Techcombank/);
      expect(within(techRow).getByText("Mặc định")).toBeInTheDocument();
    });
    expect(screen.getAllByText("Mặc định")).toHaveLength(1);
    const vcbRow = rowFor(/Vietcombank/);
    expect(within(vcbRow).queryByText("Mặc định")).not.toBeInTheDocument();
  });

  it("WalletPage_DeleteDefault_ShowsPromotionCopyThenPromotesRemaining", async () => {
    seedPremium();
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Vietcombank/ });

    await user.click(screen.getByRole("button", { name: "Xóa Vietcombank" }));
    const dialog = await screen.findByRole("dialog");
    // Deleting the default surfaces the default-promotion explanation.
    expect(
      within(dialog).getByText(/tài khoản được thêm gần nhất còn lại sẽ trở thành mặc định/),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Xóa tài khoản" }));

    expect(
      await screen.findByText("Đã xóa tài khoản ngân hàng."),
    ).toBeInTheDocument();
    // Vietcombank is gone; the remaining Techcombank was promoted to default.
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: /Vietcombank/ }),
      ).not.toBeInTheDocument(),
    );
    const techRow = rowFor(/Techcombank/);
    expect(within(techRow).getByText("Mặc định")).toBeInTheDocument();
  });

  it("WalletPage_DeleteNonDefault_ShowsPlainBodyCopy", async () => {
    seedPremium();
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Techcombank/ });

    await user.click(screen.getByRole("button", { name: "Xóa Techcombank" }));
    const dialog = await screen.findByRole("dialog");
    // Non-default delete uses the plain body (no promotion wording).
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

// ─── Premium: empty state ────────────────────────────────────────────────────
describe("WalletPage premium empty", () => {
  it("WalletPage_PremiumEmpty_OffersAddFirstAccount", async () => {
    seedPremium();
    server.use(http.get("*/api/v1/bank-accounts", () => ok([])));
    renderWallet();

    expect(
      await screen.findByText("Chưa có tài khoản nào"),
    ).toBeInTheDocument();
    // The empty state invites the first add (there are two "Thêm tài khoản"
    // buttons: the header + the empty-state CTA).
    expect(
      screen.getAllByRole("button", { name: "Thêm tài khoản" }).length,
    ).toBeGreaterThanOrEqual(1);
  });
});

// ─── Free: hybrid gate (read-only) ───────────────────────────────────────────
describe("WalletPage free hybrid gate", () => {
  it("WalletPage_FreeWithAccounts_ShowsReadOnlyTableAndUpgradePromptNoActions", async () => {
    seedSession("FREE");
    renderWallet();
    await screen.findByRole("rowheader", { name: /Vietcombank/ });

    // Informational upgrade panel (no self-serve) is shown…
    expect(
      screen.getByText("Quản lý phương thức nhận tiền là tính năng Premium"),
    ).toBeInTheDocument();
    // …no "add" and no per-row mutation controls (read-only)…
    expect(
      screen.queryByRole("button", { name: "Thêm tài khoản" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Sửa Techcombank" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Xóa Techcombank" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", {
        name: "Đặt Techcombank làm mặc định",
      }),
    ).not.toBeInTheDocument();
  });

  it("WalletPage_FreeReveal_StillWorks_ReadIsFreeSafe", async () => {
    seedSession("FREE");
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Vietcombank/ });

    const row = rowFor(/Vietcombank/);
    await user.click(
      within(row).getByRole("button", { name: "Hiện số tài khoản Vietcombank" }),
    );
    // Reveal is a read → works for Free too.
    expect(within(row).getByText("0071 0012 3456 7")).toBeInTheDocument();
  });

  it("WalletPage_FreeEmpty_ShowsPremiumFeatureExplainer", async () => {
    seedSession("FREE");
    server.use(http.get("*/api/v1/bank-accounts", () => ok([])));
    renderWallet();

    expect(await screen.findByText("Ví trống")).toBeInTheDocument();
    // The empty state doubles as the "wallet is Premium" explainer — no add CTA.
    expect(
      screen.queryByRole("button", { name: "Thêm tài khoản" }),
    ).not.toBeInTheDocument();
  });

  it("WalletPage_StalePremiumSetDefault403_ReactivelyToastsGateMessage", async () => {
    // Session says PREMIUM (controls render) but the server tier drifted: MSW is
    // NOT told this user is premium → the mutation returns 403 13003, caught
    // reactively and surfaced as the localized gate message.
    seedSession("PREMIUM");
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole("rowheader", { name: /Techcombank/ });

    await user.click(
      screen.getByRole("button", { name: "Đặt Techcombank làm mặc định" }),
    );

    expect(
      await screen.findByText(
        "Tính năng này chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng.",
      ),
    ).toBeInTheDocument();
  });
});

// ─── Loading / error states ──────────────────────────────────────────────────
describe("WalletPage states", () => {
  it("WalletPage_Loading_ShowsSkeletonRows", async () => {
    seedPremium();
    server.use(
      http.get("*/api/v1/bank-accounts", async () => {
        await delay(60);
        return ok([]);
      }),
    );
    renderWallet();

    // While pending: 3 placeholder rows with empty row-headers, no real data.
    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders).toHaveLength(3);
    expect(rowHeaders.every((c) => c.textContent === "")).toBe(true);
  });

  it("WalletPage_ListError_ShowsErrorStateThenRetryRecovers", async () => {
    seedPremium();
    let calls = 0;
    server.use(
      http.get("*/api/v1/bank-accounts", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok([]);
      }),
    );
    const user = userEvent.setup();
    renderWallet();

    const errorRegion = await screen.findByRole("alert");
    expect(
      within(errorRegion).getByText("Không tải được ví"),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Thử lại" }));
    // Retry refetches and the empty state renders (recovered).
    expect(
      await screen.findByText("Chưa có tài khoản nào"),
    ).toBeInTheDocument();
  });
});
