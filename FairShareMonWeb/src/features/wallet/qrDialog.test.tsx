import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import type { MemberQrResponse } from "./api/types";
import { QrDialog } from "./components/QrDialog";
import type { QrDialogKind } from "./components/QrDialog";

/**
 * QrDialog — the REAL shared QR modal against MSW, now on the PER-MEMBER JSON
 * endpoints (`…/qr/members` → `MemberQrResponse[]`, each `image` a
 * `data:image/png;base64,…` data URL). The composite PNG blob + the
 * `URL.createObjectURL`/`revokeObjectURL` lifecycle are GONE — the `<img src>` is
 * the data URL straight from the API. The dialog owns the query, the error-code →
 * state mapping, and shows the FIRST member with a caption (name + amount + "1/N"
 * + member count + enlarge hint); enlarging opens the multi-slide YARL lightbox.
 * Ownership 404 (6000/9000) → close + toast exactly once (the one-shot guard).
 *
 * jsdom limits: real swipe/pinch/zoom geometry and the actual OS share sheet are
 * E2E territory — these specs drive component state + the button/plugin wiring
 * only (Web Share is stubbed on `navigator`; YARL renders its chrome in jsdom).
 */

// The Download footer action calls the shared downloadBlob helper — mocked so we
// assert it receives the per-member BlobResult + fallback filename without
// touching the real DOM anchor/object-URL path.
const downloadSpy = vi.fn();
vi.mock("@/lib/download/downloadBlob", () => ({
  downloadBlob: (...args: unknown[]) => downloadSpy(...args),
}));

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

// A tiny 1×1 PNG, delivered as a data URL exactly like the backend does. `atob`
// works in jsdom, so dataUrlToBlob (in the download/share helpers) runs for real.
const DATA_URL =
  "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HBwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

const M1 = { memberUuid: "m-1", memberName: "An Nguyễn", amount: 120000, image: DATA_URL };
const M2 = { memberUuid: "m-2", memberName: "Bình Trần", amount: 80000, image: DATA_URL };

function memberQrs(list: MemberQrResponse[]) {
  return ok(list);
}

const ACCOUNTS = [
  {
    uuid: "ba-def",
    bankBin: "970436",
    bankName: "Vietcombank",
    accountNumber: "0071001234567",
    accountHolderName: "NGUYEN VAN MINH",
    isDefault: true,
    createdAt: "2026-01-01T00:00:00+00:00",
  },
  {
    uuid: "ba-alt",
    bankBin: "970407",
    bankName: "Techcombank",
    accountNumber: "19024681012345",
    accountHolderName: "NGUYEN VAN MINH",
    isDefault: false,
    createdAt: "2026-01-02T00:00:00+00:00",
  },
];

function seedSession(tier: "FREE" | "PREMIUM") {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-qrd-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-qrd-t",
    refreshTokenExpiresAt: future,
    user: { username: "qrd", tier, role: "USER" },
    profileStatus: "resolved",
  });
}

function renderQr(
  props: Partial<{
    kind: QrDialogKind;
    targetUuid: string;
    title: string;
    onOpenChange: (open: boolean) => void;
  }> = {},
) {
  const onOpenChange = props.onOpenChange ?? (() => {});
  return renderWithProviders(
    <QrDialog
      open
      onOpenChange={onOpenChange}
      kind={props.kind ?? "expense"}
      targetUuid={props.targetUuid ?? "e-1"}
      title={props.title ?? "Mã QR chuyển khoản"}
    />,
    { initialPath: "/expenses/e-1", queryClient },
  );
}

// --- Web Share API stubbing (feature-detected via navigator.share/canShare) ---
// jsdom exposes neither by default, so `canShareMemberQr` is false and the footer
// Share button is absent. Tests that need it present define both, and we always
// restore in cleanup so the "unsupported" specs stay honest.
const shareDescriptors: Record<string, PropertyDescriptor | undefined> = {};
function stubWebShare() {
  const shareSpy = vi.fn().mockResolvedValue(undefined);
  const canShareSpy = vi.fn().mockReturnValue(true);
  shareDescriptors.share = Object.getOwnPropertyDescriptor(navigator, "share");
  shareDescriptors.canShare = Object.getOwnPropertyDescriptor(
    navigator,
    "canShare",
  );
  Object.defineProperty(navigator, "share", {
    configurable: true,
    writable: true,
    value: shareSpy,
  });
  Object.defineProperty(navigator, "canShare", {
    configurable: true,
    writable: true,
    value: canShareSpy,
  });
  return { shareSpy, canShareSpy };
}
function restoreWebShare() {
  for (const key of ["share", "canShare"] as const) {
    const prev = shareDescriptors[key];
    if (prev) {
      Object.defineProperty(navigator, key, prev);
    } else {
      delete (navigator as unknown as Record<string, unknown>)[key];
    }
    shareDescriptors[key] = undefined;
  }
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  downloadSpy.mockClear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  restoreWebShare();
  vi.restoreAllMocks();
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

// ─── Premium ready state ─────────────────────────────────────────────────────
describe("QrDialog premium ready", () => {
  it("QrDialog_PremiumExpense_RendersFirstMemberFromDataUrl", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1, M2])),
    );
    renderQr();

    const img = await screen.findByRole("img", { name: /VietQR/ });
    // The <img> is the first member's data URL — no object URL in sight.
    expect(img.getAttribute("src")?.startsWith("data:image/png")).toBe(true);
    expect(img.getAttribute("src")).toBe(DATA_URL);
    // Caption: first member's name + amount + "1/N" indicator + member count.
    expect(screen.getByText("An Nguyễn")).toBeInTheDocument();
    // Intl separates the amount + "₫" with a narrow no-break space (U+202F); a
    // regex over the grouped digits sidesteps whitespace-normalization mismatch.
    expect(screen.getByText(/120\.000\s*₫/)).toBeInTheDocument();
    expect(screen.getByText(/1\/2/)).toBeInTheDocument();
    expect(screen.getByText(/2 thành viên/)).toBeInTheDocument();
    // The human-readable account block (accessible + copy channel) is present.
    expect(screen.getByText("0071 0012 3456 7")).toBeInTheDocument();
  });

  it("QrDialog_Ready_NeverCreatesAnObjectUrl", async () => {
    seedSession("PREMIUM");
    const createSpy = vi.spyOn(URL, "createObjectURL");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1])),
    );
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    // Data URLs go straight into <img src> — the blob object-URL lifecycle is gone.
    expect(createSpy).not.toHaveBeenCalled();
  });

  it("QrDialog_DownloadAction_CallsDownloadBlobWithMemberFilename", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1, M2])),
    );
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(screen.getByRole("button", { name: "Tải ảnh QR" }));

    // Downloads the SHOWN member (index 0) as qr-{memberName}.png (spaces +
    // diacritics kept). The real dataUrlToBlob runs → a Blob reaches downloadBlob.
    expect(downloadSpy).toHaveBeenCalledTimes(1);
    const [result, fallbackName] = downloadSpy.mock.calls[0];
    expect(result).toHaveProperty("blob");
    expect((result as { blob: unknown }).blob).toBeInstanceOf(Blob);
    expect(fallbackName).toBe("qr-An Nguyễn.png");
  });

  it("QrDialog_CopyDetails_CopiesHolderAndNumberNotRawPayload", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1])),
    );
    const user = userEvent.setup();
    const writeSpy = vi.spyOn(navigator.clipboard, "writeText");
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(screen.getByRole("button", { name: "Sao chép thông tin" }));

    // Copies the holder + account number + bank (the transfer-actionable details),
    // never a machine TLV payload string, and never a per-member amount.
    expect(writeSpy).toHaveBeenCalledTimes(1);
    const copied = writeSpy.mock.calls[0][0];
    expect(copied).toContain("NGUYEN VAN MINH");
    expect(copied).toContain("0071001234567");
    expect(copied).toContain("Vietcombank");
    expect(await screen.findByText("Đã sao chép")).toBeInTheDocument();
  });

  it("QrDialog_CopyDetails_WriteRejects_DoesNotConfirmCopy", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1])),
    );
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, "writeText").mockRejectedValue(
      new Error("clipboard denied"),
    );
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(screen.getByRole("button", { name: "Sao chép thông tin" }));

    // No false success: the copied state must NOT appear when the write failed.
    await new Promise((r) => setTimeout(r, 30));
    expect(screen.queryByText("Đã sao chép")).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Sao chép thông tin" }),
    ).toBeInTheDocument();
  });

  it("QrDialog_CopyDetails_ClipboardUnavailable_DoesNotConfirmCopy", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1])),
    );
    const prev = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: undefined,
    });
    try {
      renderQr();
      await screen.findByRole("img", { name: /VietQR/ });

      fireEvent.click(
        screen.getByRole("button", { name: "Sao chép thông tin" }),
      );

      await new Promise((r) => setTimeout(r, 30));
      expect(screen.queryByText("Đã sao chép")).not.toBeInTheDocument();
      expect(
        screen.getByRole("button", { name: "Sao chép thông tin" }),
      ).toBeInTheDocument();
    } finally {
      if (prev) {
        Object.defineProperty(navigator, "clipboard", prev);
      } else {
        delete (navigator as unknown as Record<string, unknown>).clipboard;
      }
    }
  });
});

// ─── Share current (Web Share API, feature-detected) ─────────────────────────
describe("QrDialog share current", () => {
  it("QrDialog_ShareSupported_RendersShareAndSharesFilePayload", async () => {
    seedSession("PREMIUM");
    const { shareSpy } = stubWebShare();
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1, M2])),
    );
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    const shareBtn = await screen.findByRole("button", { name: "Chia sẻ" });
    await user.click(shareBtn);

    // Web Share API invoked with a files[] payload (the shown member's QR PNG).
    await waitFor(() => expect(shareSpy).toHaveBeenCalledTimes(1));
    const arg = shareSpy.mock.calls[0][0] as { files?: File[] };
    expect(Array.isArray(arg.files)).toBe(true);
    expect(arg.files?.[0]).toBeInstanceOf(File);
    expect(arg.files?.[0].name).toBe("qr-An Nguyễn.png");
  });

  it("QrDialog_ShareUnsupported_ShareButtonIsAbsent", async () => {
    seedSession("PREMIUM");
    // No navigator.share/canShare → the control is hidden (never a dead button).
    restoreWebShare();
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1, M2])),
    );
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    expect(screen.queryByRole("button", { name: "Chia sẻ" })).not.toBeInTheDocument();
    // The always-present Download control still renders.
    expect(screen.getByRole("button", { name: "Tải ảnh QR" })).toBeInTheDocument();
  });
});

// ─── Destination picker ──────────────────────────────────────────────────────
describe("QrDialog destination picker", () => {
  it("QrDialog_TwoAccounts_ShowsDestinationSelect", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1])),
    );
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    expect(
      screen.getByRole("combobox", { name: "Tài khoản nhận tiền" }),
    ).toBeInTheDocument();
  });

  it("QrDialog_SingleAccount_HidesDestinationSelect", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok([ACCOUNTS[0]])),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs([M1])),
    );
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    expect(
      screen.queryByRole("combobox", { name: "Tài khoản nhận tiền" }),
    ).not.toBeInTheDocument();
  });

  it("QrDialog_PickNonDefault_RefetchesWithBankAccountUuidQuery", async () => {
    seedSession("PREMIUM");
    const seenUrls: string[] = [];
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", ({ request }) => {
        seenUrls.push(request.url);
        return memberQrs([M1]);
      }),
    );
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });
    // First fetch uses the implicit default → no override param.
    expect(new URL(seenUrls[0]).searchParams.get("bankAccountUuid")).toBeNull();

    await user.click(
      screen.getByRole("combobox", { name: "Tài khoản nhận tiền" }),
    );
    await user.click(
      await screen.findByRole("option", { name: /Techcombank/ }),
    );

    // Picking the non-default account drives ?bankAccountUuid= on the refetch.
    await waitFor(() => {
      const last = seenUrls[seenUrls.length - 1];
      expect(new URL(last).searchParams.get("bankAccountUuid")).toBe("ba-alt");
    });
  });
});

// ─── Premium gate (proactive + reactive) ─────────────────────────────────────
describe("QrDialog premium gate", () => {
  it("QrDialog_FreeUser_ShowsUpgradePromptAndNeverFiresQuery", async () => {
    seedSession("FREE");
    let qrCalls = 0;
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr/members", () => {
        qrCalls += 1;
        return memberQrs([M1]);
      }),
    );
    renderQr();

    const dialog = await screen.findByRole("dialog");
    // Proactive gate: the informational upgrade panel, and the QR query is never
    // enabled for a Free user.
    expect(within(dialog).getByText("Tính năng Premium")).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 30));
    expect(qrCalls).toBe(0);
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("QrDialog_StaleTier403_ReactivelyShowsUpgradePrompt", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () =>
        fail(13003, "Premium.", 403),
      ),
    );
    renderQr();

    const dialog = await screen.findByRole("dialog");
    // Reactive gate: the server is authoritative — a 403 renders the same panel.
    expect(
      await within(dialog).findByText("Tính năng Premium"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });
});

// ─── Friendly error states ───────────────────────────────────────────────────
describe("QrDialog error states", () => {
  it("QrDialog_NoBankAccount12001_ShowsEmptyStateWithWalletLink", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok([])),
      http.get("*/api/v1/expenses/:uuid/qr/members", () =>
        fail(12001, "Chưa có tài khoản ngân hàng nhận tiền.", 400),
      ),
    );
    renderQr();

    expect(
      await screen.findByText("Chưa có tài khoản nhận tiền"),
    ).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Thêm tài khoản" });
    expect(link).toHaveAttribute("href", "/wallet");
  });

  it("QrDialog_EventNoDebt12003_ShowsInformationalAlert", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/qr/members", () =>
        fail(12003, "Không còn ai nợ trong đợt này.", 400),
      ),
    );
    renderQr({ kind: "event", targetUuid: "ev-1", title: "Mã QR quyết toán" });

    expect(
      await screen.findByText("Không còn ai nợ trong đợt này"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("QrDialog_ExpenseNoDebt12003_ShowsExpenseSpecificAlert", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () =>
        fail(12003, "Không còn ai nợ trên phiếu này.", 400),
      ),
    );
    renderQr({ kind: "expense", targetUuid: "e-1", title: "Mã QR chuyển khoản" });

    expect(
      await screen.findByText("Không còn ai nợ trên phiếu này"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("QrDialog_EventNotClosed12002_ShowsDefensiveWarningAlert", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/qr/members", () =>
        fail(12002, "Đợt chưa được chốt.", 400),
      ),
    );
    renderQr({ kind: "event", targetUuid: "ev-1", title: "Mã QR quyết toán" });

    expect(await screen.findByText("Đợt chưa được chốt")).toBeInTheDocument();
  });

  it("QrDialog_GenericError_ShowsErrorStateWithRetry", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    renderQr();

    expect(await screen.findByText("Không tạo được mã QR")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Thử lại" })).toBeInTheDocument();
  });
});

// ─── Ownership 404 → close + toast (one-shot regression guard) ────────────────
describe("QrDialog ownership 404 one-shot", () => {
  it("QrDialog_Expense6000_ClosesAndToastsExactlyOnce_NoRenderLoop", async () => {
    seedSession("PREMIUM");
    const onOpenChange = vi.fn();
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () =>
        fail(6000, "Không tìm thấy phiếu chi tiêu.", 404),
      ),
    );
    // `open` stays true (we ignore onOpenChange) so a missing guard WOULD re-fire
    // the toast/close every render — the count proves the one-shot ref holds.
    renderQr({ onOpenChange });

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    await new Promise((r) => setTimeout(r, 60));
    expect(onOpenChange).toHaveBeenCalledTimes(1);
    // The ownership miss is toasted (no existence leak; never a dialog state).
    expect(
      screen.getByText("Không tìm thấy phiếu chi tiêu."),
    ).toBeInTheDocument();
  });

  it("QrDialog_Event9000_ClosesAndToastsExactlyOnce", async () => {
    seedSession("PREMIUM");
    const onOpenChange = vi.fn();
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/qr/members", () =>
        fail(9000, "Không tìm thấy đợt chi tiêu.", 404),
      ),
    );
    renderQr({
      kind: "event",
      targetUuid: "ev-1",
      title: "Mã QR quyết toán",
      onOpenChange,
    });

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    await new Promise((r) => setTimeout(r, 60));
    expect(onOpenChange).toHaveBeenCalledTimes(1);
  });
});

// ─── QR preview (YARL multi-slide lightbox) ──────────────────────────────────
describe("QrDialog QR preview", () => {
  function seedReadyExpense(list: MemberQrResponse[] = [M1, M2]) {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr/members", () => memberQrs(list)),
    );
  }

  // The preview is a `yet-another-react-lightbox` (YARL) portal: a role="dialog"
  // whose accessible name is YARL's `Lightbox` label, localized via
  // `wallet:qr.previewTitle` (vi-VN "Xem mã QR phóng to"). That name uniquely
  // distinguishes the lightbox layer from the base QR dialog (named from `title`).
  //
  // NOTE: real wheel/pinch/drag zoom + native swipe are E2E territory — jsdom has
  // no layout or PointerEvent geometry. These specs assert the lightbox chrome +
  // slide image + captions/counter/nav + the custom Escape interceptor only.
  const previewName = "Xem mã QR phóng to"; // wallet:qr.previewTitle (vi-VN pinned)
  const findLightbox = () => screen.findByRole("dialog", { name: previewName });
  const queryLightbox = () => screen.queryByRole("dialog", { name: previewName });

  it("QrPreview_ReadyImage_ExposesTwoEnlargeTriggers", async () => {
    seedReadyExpense();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    // D1 — the transparent full-image surface AND the top-right badge, both named
    // wallet:qr.enlarge ("Phóng to mã QR"). Unchanged by the per-member swap.
    const triggers = screen.getAllByRole("button", { name: /Phóng to mã QR/ });
    expect(triggers).toHaveLength(2);
  });

  it("QrPreview_ClickImageSurface_OpensMultiSlideLightbox", async () => {
    seedReadyExpense([M1, M2]);
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    // First trigger (DOM order) is the transparent .enlargeSurface over the image.
    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );

    const lightbox = await findLightbox();
    // The lightbox renders at least one QR slide image (with our per-member alt).
    expect(
      within(lightbox).getAllByRole("img", { name: /VietQR/ }).length,
    ).toBeGreaterThanOrEqual(1);
    // Captions plugin shows the ACTIVE (first) member's name + amount.
    expect(within(lightbox).getByText("An Nguyễn")).toBeInTheDocument();
    expect(within(lightbox).getByText(/120\.000\s*₫/)).toBeInTheDocument();
    // Counter plugin reflects the multi-member set (index / total).
    expect(within(lightbox).getByText(/1\s*\/\s*2/)).toBeInTheDocument();
    // Multi-member → prev/next navigation present (YARL default English labels).
    expect(
      within(lightbox).getByRole("button", { name: "Next" }),
    ).toBeInTheDocument();
    expect(
      within(lightbox).getByRole("button", { name: "Previous" }),
    ).toBeInTheDocument();
  });

  it("QrPreview_ClickExpandBadge_OpensLightbox", async () => {
    seedReadyExpense([M1, M2]);
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    // Second trigger (DOM order) is the top-right .enlargeBadge icon button —
    // opens the SAME lightbox.
    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[1],
    );

    const lightbox = await findLightbox();
    expect(
      within(lightbox).getAllByRole("img", { name: /VietQR/ }).length,
    ).toBeGreaterThanOrEqual(1);
  });

  it("QrPreview_SingleMember_HidesPrevNextNavigation", async () => {
    seedReadyExpense([M1]);
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    const lightbox = await findLightbox();

    // Single member → the carousel nav is suppressed (render buttonPrev/Next → null).
    expect(
      within(lightbox).queryByRole("button", { name: "Next" }),
    ).not.toBeInTheDocument();
    expect(
      within(lightbox).queryByRole("button", { name: "Previous" }),
    ).not.toBeInTheDocument();
  });

  it("QrPreview_Escape_ClosesPreviewOnly_BaseDialogStaysOpen", async () => {
    seedReadyExpense([M1, M2]);
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    await findLightbox();

    // The key regression guard. YARL portals to <body> and is NOT part of Radix's
    // dismissable-layer stack, and Radix's base Dialog listens for Escape in the
    // DOCUMENT-capture phase, so a bare Escape would close BOTH. A custom
    // WINDOW-capture interceptor swallows the first Escape so Radix never sees it
    // and closes ONLY the preview. (A second Escape then closes the base dialog.)
    await user.keyboard("{Escape}");

    // The lightbox is gone…
    await waitFor(() => expect(queryLightbox()).not.toBeInTheDocument());
    // …but the base QR dialog's (first member) image survives.
    expect(screen.getByRole("img", { name: /VietQR/ })).toBeInTheDocument();
  });

  it("QrPreview_CloseButton_ClosesPreviewOnly", async () => {
    seedReadyExpense([M1, M2]);
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    const lightbox = await findLightbox();

    // YARL's close button carries our localized label (wallet:qr.close → "Đóng").
    // Scope to the lightbox so we never match the base dialog's own "Đóng" controls.
    await user.click(within(lightbox).getByRole("button", { name: "Đóng" }));

    await waitFor(() => expect(queryLightbox()).not.toBeInTheDocument());
    expect(screen.getByRole("img", { name: /VietQR/ })).toBeInTheDocument();
  });

  it("QrPreview_EventKind_UsesEventImageAlt", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/qr/members", () => memberQrs([M1])),
    );
    const user = userEvent.setup();
    renderQr({ kind: "event", targetUuid: "ev-1", title: "Mã QR quyết toán" });
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    const lightbox = await findLightbox();

    // The event-specific alt copy ("còn nợ của đợt") is on the lightbox image.
    const img = within(lightbox).getAllByRole("img", { name: /VietQR/ })[0];
    expect(img.getAttribute("alt")).toMatch(/còn nợ của đợt/);
  });
});

// ─── QR preview i18n keys ─────────────────────────────────────────────────────
describe("QrDialog QR preview i18n keys", () => {
  it("QrPreviewKeys_ExistInBothLocales_NonEmpty", async () => {
    const viLocale = await import("@/i18n/locales/vi-VN/wallet.json");
    const enLocale = await import("@/i18n/locales/en-US/wallet.json");
    // The lightbox + per-member action labels consumed by the YARL swap: enlarge
    // triggers, the localized lightbox name (`previewTitle` → YARL `Lightbox`),
    // zoom/close chrome, per-member download/share, and the dialog caption keys.
    const keys = [
      "enlarge",
      "previewTitle",
      "zoomIn",
      "zoomOut",
      "close",
      "download",
      "share",
      "shareTitle",
      "shareText",
      "slideCounter",
      "memberCount",
      "enlargeHint",
    ] as const;
    for (const k of keys) {
      expect(viLocale.default.qr[k]?.trim()).toBeTruthy();
      expect(enLocale.default.qr[k]?.trim()).toBeTruthy();
    }
  });
});

// ─── i18n ────────────────────────────────────────────────────────────────────
describe("QrDialog i18n", () => {
  it("QrDialog_EnUsLocale_RendersEnglishGateCopy", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    seedSession("FREE");
    renderQr({ title: "Transfer QR code" });

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Premium feature")).toBeInTheDocument();
  });
});
