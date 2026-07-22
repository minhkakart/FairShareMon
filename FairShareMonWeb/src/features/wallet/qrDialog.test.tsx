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
import { QrDialog } from "./components/QrDialog";
import type { QrDialogKind } from "./components/QrDialog";

/**
 * QrDialog — the REAL shared QR modal against MSW. It owns the query, the
 * error-code → state mapping, and the blob object-URL lifecycle (create on data,
 * revoke on unmount/refetch). The QR image is decorative; the human-readable
 * account block is the accessible + copy channel (OQ4a — holder + number, never a
 * raw TLV). Hybrid gate (OQ1a): Free → UpgradePrompt, query never fires; a
 * stale-tier 403 renders the same panel reactively. Ownership 404 (6000/9000) →
 * close + toast exactly once (the one-shot guard — regression for the fixed loop).
 */

// The QrDialog's <img> uses URL.createObjectURL; the Download footer action calls
// the shared downloadBlob helper — mocked so we assert it receives the BlobResult
// without touching the DOM download path.
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

const PNG_1x1_BASE64 =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HBwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
function pngResponse(name = "qr.png") {
  const binary = atob(PNG_1x1_BASE64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  return new HttpResponse(bytes, {
    headers: {
      "Content-Type": "image/png",
      "Content-Disposition": `attachment; filename="${name}"`,
    },
  });
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

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  downloadSpy.mockClear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  vi.restoreAllMocks();
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

// ─── Premium ready state ─────────────────────────────────────────────────────
describe("QrDialog premium ready", () => {
  it("QrDialog_PremiumExpense_RendersImageFromBlobObjectUrl", async () => {
    seedSession("PREMIUM");
    const createSpy = vi
      .spyOn(URL, "createObjectURL")
      .mockReturnValue("blob:qr-ready");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
    );
    renderQr();

    const img = await screen.findByRole("img", { name: /VietQR/ });
    // The <img> is sourced from the object URL created off the blob.
    expect(img.getAttribute("src")).toBe("blob:qr-ready");
    expect(createSpy).toHaveBeenCalled();
    // The human-readable account block (accessible + copy channel) is present.
    expect(screen.getByText("0071 0012 3456 7")).toBeInTheDocument();
  });

  it("QrDialog_DownloadAction_CallsDownloadBlobWithBlobResult", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse("expense-qr.png")),
    );
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(screen.getByRole("button", { name: "Tải ảnh QR" }));

    expect(downloadSpy).toHaveBeenCalledTimes(1);
    const [result] = downloadSpy.mock.calls[0];
    expect(result).toHaveProperty("blob");
    expect((result as { filename?: string }).filename).toBe("expense-qr.png");
  });

  it("QrDialog_CopyDetails_CopiesHolderAndNumberNotRawPayload", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
    );
    const user = userEvent.setup();
    const writeSpy = vi.spyOn(navigator.clipboard, "writeText");
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(screen.getByRole("button", { name: "Sao chép thông tin" }));

    // Copies the holder + account number + bank (the transfer-actionable details),
    // never a machine TLV payload string.
    expect(writeSpy).toHaveBeenCalledTimes(1);
    const copied = writeSpy.mock.calls[0][0];
    expect(copied).toContain("NGUYEN VAN MINH");
    expect(copied).toContain("0071001234567");
    expect(copied).toContain("Vietcombank");
    // The button confirms the copy.
    expect(await screen.findByText("Đã sao chép")).toBeInTheDocument();
  });

  it("QrDialog_CopyDetails_WriteRejects_DoesNotConfirmCopy", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
    );
    const user = userEvent.setup();
    // The clipboard write rejects (e.g. permission denied). Handled by the
    // component's `.catch`, so no unhandled rejection.
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
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
    );
    // Simulate an insecure origin / older browser where `navigator.clipboard`
    // is absent. Click via fireEvent so userEvent's setup doesn't re-stub it.
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

      // Clipboard API absent → the button never flips to the copied state.
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

  it("QrDialog_Unmount_RevokesTheObjectUrl", async () => {
    seedSession("PREMIUM");
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:qr-revoke");
    const revokeSpy = vi.spyOn(URL, "revokeObjectURL");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
    );
    const { unmount } = renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    unmount();
    // The object URL created for the image is revoked on unmount (no leak).
    expect(revokeSpy).toHaveBeenCalledWith("blob:qr-revoke");
  });
});

// ─── Destination picker (OQ2a) ───────────────────────────────────────────────
describe("QrDialog destination picker", () => {
  it("QrDialog_TwoAccounts_ShowsDestinationSelect", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
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
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
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
      http.get("*/api/v1/expenses/:uuid/qr", ({ request }) => {
        seenUrls.push(request.url);
        return pngResponse();
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
      http.get("*/api/v1/expenses/:uuid/qr", () => {
        qrCalls += 1;
        return pngResponse();
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
      http.get("*/api/v1/expenses/:uuid/qr", () =>
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
      http.get("*/api/v1/expenses/:uuid/qr", () =>
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
      http.get("*/api/v1/events/:uuid/qr", () =>
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
      http.get("*/api/v1/expenses/:uuid/qr", () =>
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
      http.get("*/api/v1/events/:uuid/qr", () =>
        fail(12002, "Đợt chưa được chốt.", 400),
      ),
    );
    renderQr({ kind: "event", targetUuid: "ev-1", title: "Mã QR quyết toán" });

    expect(
      await screen.findByText("Đợt chưa được chốt"),
    ).toBeInTheDocument();
  });

  it("QrDialog_GenericError_ShowsErrorStateWithRetry", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    renderQr();

    expect(
      await screen.findByText("Không tạo được mã QR"),
    ).toBeInTheDocument();
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
      http.get("*/api/v1/expenses/:uuid/qr", () =>
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
      http.get("*/api/v1/events/:uuid/qr", () =>
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

// ─── QR preview (YARL lightbox) ──────────────────────────────────────────────
describe("QrDialog QR preview", () => {
  // Seed a ready PREMIUM expense QR against MSW, with createObjectURL stubbed to a
  // stable blob URL so the preview can be asserted to REUSE it (never a 2nd one).
  function seedReadyExpense(blobUrl = "blob:qr-preview") {
    seedSession("PREMIUM");
    const createSpy = vi
      .spyOn(URL, "createObjectURL")
      .mockReturnValue(blobUrl);
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
    );
    return createSpy;
  }

  // The preview is now a `yet-another-react-lightbox` (YARL) portal (D7): a
  // role="dialog" whose accessible name is YARL's `Lightbox` label, localized via
  // `wallet:qr.previewTitle` (vi-VN "Xem mã QR phóng to"). That name uniquely
  // distinguishes the lightbox layer from the base QR dialog (named from `title`).
  //
  // NOTE: real wheel / pinch / drag-to-pan zoom is E2E territory — jsdom has no
  // layout or PointerEvent geometry. The committed pngResponse() fixture is a 1×1
  // PNG, which YARL treats as non-zoomable (it renders the zoom-in button
  // DISABLED), so we never drive an actual zoom here — the zoom state machine is
  // no longer observable in jsdom. These specs assert the lightbox chrome + image
  // + the custom Escape/close interceptors only.
  const previewName = "Xem mã QR phóng to"; // wallet:qr.previewTitle (vi-VN pinned)
  const findLightbox = () => screen.findByRole("dialog", { name: previewName });
  const queryLightbox = () => screen.queryByRole("dialog", { name: previewName });
  // The QR raster inside the lightbox (scoped so it never matches the base <img>).
  const lightboxImg = (lightbox: HTMLElement) =>
    within(lightbox).getByRole("img", { name: /VietQR/ });

  it("QrPreview_ReadyImage_ExposesTwoEnlargeTriggers", async () => {
    seedReadyExpense();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    // D1 — the transparent full-image surface AND the top-right badge, both named
    // wallet:qr.enlarge ("Phóng to mã QR"). Unchanged by the YARL swap.
    const triggers = screen.getAllByRole("button", {
      name: /Phóng to mã QR/,
    });
    expect(triggers).toHaveLength(2);
  });

  it("QrPreview_ClickImageSurface_OpensPreview", async () => {
    seedReadyExpense();
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    // First trigger (DOM order) is the transparent .enlargeSurface over the image.
    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );

    // The YARL lightbox is open and renders the QR image (with our alt) inside it.
    const lightbox = await findLightbox();
    expect(lightboxImg(lightbox)).toBeInTheDocument();
  });

  it("QrPreview_ClickExpandBadge_OpensPreview", async () => {
    seedReadyExpense();
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    // Second trigger (DOM order) is the top-right .enlargeBadge icon button —
    // opens the SAME lightbox.
    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[1],
    );

    const lightbox = await findLightbox();
    expect(lightboxImg(lightbox)).toBeInTheDocument();
  });

  it("QrPreview_Escape_ClosesPreviewOnly_BaseDialogStaysOpen", async () => {
    seedReadyExpense();
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    await findLightbox();

    // R5 / D7 — the key regression guard. YARL portals to <body> and is NOT part
    // of Radix's dismissable-layer stack, and Radix's base Dialog listens for
    // Escape in the DOCUMENT-capture phase, so a bare Escape would close BOTH.
    // A custom WINDOW-capture interceptor in QrPreviewDialog swallows the first
    // Escape (stopImmediatePropagation + preventDefault) so Radix never sees it
    // and closes ONLY the preview. (A second Escape then closes the base dialog.)
    await user.keyboard("{Escape}");

    // The lightbox is gone…
    await waitFor(() => expect(queryLightbox()).not.toBeInTheDocument());
    // …but the base QR dialog's image survives (base dialog did NOT close).
    expect(screen.getByRole("img", { name: /VietQR/ })).toBeInTheDocument();
  });

  it("QrPreview_CloseButton_ClosesPreviewOnly", async () => {
    seedReadyExpense();
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    const lightbox = await findLightbox();

    // YARL's close button carries our localized label (wallet:qr.close → "Đóng").
    // Scope to the lightbox so we never match the base dialog's own "Đóng"
    // controls (the DialogContent close + the footer button).
    await user.click(within(lightbox).getByRole("button", { name: "Đóng" }));

    await waitFor(() => expect(queryLightbox()).not.toBeInTheDocument());
    // Base dialog + its QR image survive (the close affected the preview only).
    expect(screen.getByRole("img", { name: /VietQR/ })).toBeInTheDocument();
  });

  it("QrPreview_Open_ReusesSameBlobUrl_NoSecondCreateObjectUrl", async () => {
    const createSpy = seedReadyExpense("blob:qr-shared");
    const user = userEvent.setup();
    renderQr();
    const baseImg = await screen.findByRole("img", { name: /VietQR/ });
    expect(baseImg.getAttribute("src")).toBe("blob:qr-shared");

    // Count createObjectURL calls made up to the ready state.
    const callsBeforePreview = createSpy.mock.calls.length;

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    const lightbox = await findLightbox();

    // R4 — opening the preview creates NO new object URL (the preview only reads
    // the string QrDialog owns) and the lightbox raster points at the same blob.
    expect(createSpy.mock.calls.length).toBe(callsBeforePreview);
    expect(lightboxImg(lightbox).getAttribute("src")).toBe("blob:qr-shared");
  });

  it("QrPreview_OpenThenClose_NeverRevokesTheObjectUrl", async () => {
    // R4/R6 — the preview never owns the blob lifecycle; opening + closing it must
    // not revoke the URL (only base-dialog unmount / blob change revokes).
    seedSession("PREMIUM");
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:qr-norev");
    const revokeSpy = vi.spyOn(URL, "revokeObjectURL");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()),
    );
    const user = userEvent.setup();
    renderQr();
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    await findLightbox();
    await user.keyboard("{Escape}");
    await waitFor(() => expect(queryLightbox()).not.toBeInTheDocument());

    // The still-mounted base dialog keeps the blob alive — no revoke yet.
    expect(revokeSpy).not.toHaveBeenCalledWith("blob:qr-norev");
  });

  it("QrPreview_EventKind_UsesEventImageAlt", async () => {
    // R3 — the preview reuses the event alt (wallet:qr.imageAltEvent) for kind=event.
    seedSession("PREMIUM");
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:qr-event");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/qr", () => pngResponse()),
    );
    const user = userEvent.setup();
    renderQr({ kind: "event", targetUuid: "ev-1", title: "Mã QR quyết toán" });
    await screen.findByRole("img", { name: /VietQR/ });

    await user.click(
      screen.getAllByRole("button", { name: /Phóng to mã QR/ })[0],
    );
    const lightbox = await findLightbox();

    // The event-specific alt copy ("quyết toán công nợ của đợt") is on the
    // lightbox image.
    expect(lightboxImg(lightbox).getAttribute("alt")).toMatch(
      /quyết toán công nợ/,
    );
  });
});

// ─── QR preview i18n keys ─────────────────────────────────────────────────────
describe("QrDialog QR preview i18n keys", () => {
  it("QrPreviewKeys_ExistInBothLocales_NonEmpty", async () => {
    const vi = await import("@/i18n/locales/vi-VN/wallet.json");
    const en = await import("@/i18n/locales/en-US/wallet.json");
    // The preview labels still consumed after the YARL swap (D7): the enlarge
    // triggers, the localized lightbox accessible name (`previewTitle` → YARL's
    // `Lightbox` label), and YARL's zoom/close chrome. `zoomControls`/`resetZoom`
    // were retired with the hand-rolled viewer and removed from both locales.
    const keys = ["enlarge", "previewTitle", "zoomIn", "zoomOut", "close"] as const;
    for (const k of keys) {
      expect(vi.default.qr[k]?.trim()).toBeTruthy();
      expect(en.default.qr[k]?.trim()).toBeTruthy();
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
