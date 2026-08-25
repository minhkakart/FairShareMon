import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { PublicSharePage } from "./pages/PublicSharePage";
import type { PublicEventShareResponse } from "./api/types";

/**
 * PublicSharePage — the anonymous `/share/:token` report, driven through its route
 * with the REAL public query + MSW. Covers: loading skeleton → success report;
 * one row per member + summary; the 16000 path rendering a single friendly
 * "link unavailable" screen with NO member data leaked (identical copy for
 * expired/revoked/missing); the locale toggle switching copy; a generic failure
 * offering retry; and the `noindex,nofollow` robots meta present while mounted +
 * removed on unmount. Pinned vi-VN + Asia/Ho_Chi_Minh.
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

const PAYLOAD: PublicEventShareResponse = {
  eventName: "Chuyến Đà Lạt",
  closedAt: "2026-07-20T10:00:00.000Z",
  rows: [
    {
      memberUuid: "m-an",
      memberName: "An Nguyễn",
      isOwnerRepresentative: false,
      isDeleted: false,
      advanced: 0,
      owed: 300000,
      balance: -300000,
      outstanding: 300000,
      isSettled: false,
      settledAt: null,
      isEligibleForAutoCascade: true,
    },
    {
      memberUuid: "m-binh",
      memberName: "Bình Trần",
      isOwnerRepresentative: false,
      isDeleted: false,
      advanced: 500000,
      owed: 200000,
      balance: 300000,
      outstanding: 0,
      isSettled: false,
      settledAt: null,
      isEligibleForAutoCascade: false,
    },
  ],
  expenses: [],
  totalOutstanding: 800000,
  owingMemberCount: 2,
  settledMemberCount: 1,
  hasQr: false,
};

function renderPage(path = "/share/tok") {
  return renderWithProviders(
    <Routes>
      <Route path="/share/:token" element={<PublicSharePage />} />
    </Routes>,
    { initialPath: path },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("PublicSharePage loading → success", () => {
  it("PublicSharePage_Pending_RendersLoadingSkeletonThenReport", async () => {
    server.use(
      http.get("*/api/v1/public/shares/:token", async () => {
        await delay(40);
        return ok(PAYLOAD);
      }),
    );
    const { container } = renderPage();

    // Skeleton visible first (aria-busy region, no report heading yet).
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(
      screen.queryByRole("heading", { level: 1, name: "Chuyến Đà Lạt" }),
    ).not.toBeInTheDocument();

    // Then the success report resolves.
    expect(
      await screen.findByRole("heading", { level: 1, name: "Chuyến Đà Lạt" }),
    ).toBeInTheDocument();
    expect(container.querySelector('[aria-busy="true"]')).toBeNull();
  });

  it("PublicSharePage_Success_RendersHeaderSummaryAndOneRowPerMember", async () => {
    server.use(http.get("*/api/v1/public/shares/:token", () => ok(PAYLOAD)));
    renderPage();

    await screen.findByRole("heading", { level: 1, name: "Chuyến Đà Lạt" });

    // Summary block: total outstanding (VND, vi-VN grouping) + member counts.
    expect(screen.getByText("Tổng còn nợ")).toBeInTheDocument();
    expect(screen.getByText(/800\.000\s*₫/)).toBeInTheDocument();
    expect(screen.getByText(/2\s*thành viên/)).toBeInTheDocument();

    // One rowheader per member.
    expect(screen.getByRole("rowheader", { name: /An Nguyễn/ })).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: /Bình Trần/ })).toBeInTheDocument();
  });
});

describe("PublicSharePage no-leak expired screen (16000)", () => {
  it("PublicSharePage_16000_RendersFriendlyScreenWithNoMemberDataLeaked", async () => {
    server.use(
      http.get("*/api/v1/public/shares/:token", () =>
        fail(16000, "Liên kết không tồn tại hoặc đã hết hạn.", 404),
      ),
    );
    renderPage("/share/whatever");

    expect(
      await screen.findByText("Liên kết không khả dụng"),
    ).toBeInTheDocument();
    // No existence leak: no report heading, no member names, no summary.
    expect(
      screen.queryByRole("heading", { level: 1, name: "Chuyến Đà Lạt" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("An Nguyễn")).not.toBeInTheDocument();
    expect(screen.queryByText("Tổng còn nợ")).not.toBeInTheDocument();
    // The friendly screen never echoes the token back either.
    expect(screen.queryByText(/whatever/)).not.toBeInTheDocument();
  });

  it("PublicSharePage_16000_IdenticalCopyForExpiredRevokedMissing", async () => {
    // The backend collapses expired/revoked/missing to a single 16000 code, and
    // the page must render ONE identical screen regardless of the token — so the
    // visitor can never distinguish "never existed" from "expired".
    const titles: string[] = [];
    const bodies: string[] = [];
    for (const token of ["expired-tok", "revoked-tok", "never-existed"]) {
      server.use(
        http.get("*/api/v1/public/shares/:token", () =>
          fail(16000, "Liên kết không tồn tại hoặc đã hết hạn.", 404),
        ),
      );
      const view = renderPage(`/share/${token}`);
      titles.push((await screen.findByText("Liên kết không khả dụng")).textContent ?? "");
      bodies.push(
        screen.getByText(/Hãy liên hệ người chia sẻ/).textContent ?? "",
      );
      view.unmount();
    }
    expect(new Set(titles).size).toBe(1);
    expect(new Set(bodies).size).toBe(1);
  });
});

describe("PublicSharePage robots meta (OQ6)", () => {
  it("PublicSharePage_Mounted_InjectsNoindexMeta_RemovedOnUnmount", async () => {
    server.use(http.get("*/api/v1/public/shares/:token", () => ok(PAYLOAD)));
    const view = renderPage();
    await screen.findByRole("heading", { level: 1, name: "Chuyến Đà Lạt" });

    const meta = document.querySelector('meta[name="robots"]');
    expect(meta).not.toBeNull();
    expect(meta?.getAttribute("content")).toBe("noindex,nofollow");

    view.unmount();
    expect(document.querySelector('meta[name="robots"]')).toBeNull();
  });
});

describe("PublicSharePage locale toggle (OQ5)", () => {
  it("PublicSharePage_ClickEnglish_SwitchesCopyToEnUs", async () => {
    server.use(http.get("*/api/v1/public/shares/:token", () => ok(PAYLOAD)));
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("heading", { level: 1, name: "Chuyến Đà Lạt" });

    // vi-VN default copy present…
    expect(screen.getByText("Tổng còn nợ")).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "English" }));

    // …switches to en-US on toggle.
    expect(await screen.findByText("Total outstanding")).toBeInTheDocument();
    expect(screen.queryByText("Tổng còn nợ")).not.toBeInTheDocument();
  });
});

describe("PublicSharePage generic failure", () => {
  it("PublicSharePage_ServerError_ShowsRetry_ThenRecoversOnRetry", async () => {
    let firstCall = true;
    server.use(
      http.get("*/api/v1/public/shares/:token", () => {
        if (firstCall) {
          firstCall = false;
          return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        }
        return ok(PAYLOAD);
      }),
    );
    const user = userEvent.setup();
    renderPage();

    // Generic (non-16000) failure → error state with a retry (not the expired
    // no-leak screen).
    expect(await screen.findByText("Không tải được liên kết")).toBeInTheDocument();
    expect(
      screen.queryByText("Liên kết không khả dụng"),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Thử lại" }));

    // Retry re-runs the query and the report loads.
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { level: 1, name: "Chuyến Đà Lạt" }),
      ).toBeInTheDocument(),
    );
  });
});
