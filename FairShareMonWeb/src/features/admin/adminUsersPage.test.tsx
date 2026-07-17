import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { useLocation } from "react-router-dom";
import { server } from "@/test/msw/server";
import { resetAdminStore } from "@/test/msw/handlers";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { AdminUsersPage } from "./pages/AdminUsersPage";

/**
 * AdminUsersPage integration — the REAL page + filters + sortable table +
 * pagination + hooks against the committed admin MSW fixtures (25 users → 2 pages).
 * Proves: the list renders account metadata only; the list state (page / filters /
 * sort) is URL-synced (OQ5a) and drives a refetch with the matching query params;
 * an empty result → EmptyState; loading → skeletons; error → ErrorState + retry.
 * The request URLs are captured to assert the query the UI sends.
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

/** Exposes the current router search string for URL-sync assertions. */
function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="loc">{loc.search}</div>;
}

function renderUsers() {
  return renderWithProviders(
    <>
      <AdminUsersPage />
      <LocationProbe />
    </>,
    { initialPath: "/admin/users" },
  );
}

function loc(): string {
  return screen.getByTestId("loc").textContent ?? "";
}

beforeEach(async () => {
  window.localStorage.clear();
  resetAdminStore();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedAdmin();
});
afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("AdminUsersPage — list + pagination", () => {
  it("AdminUsersPage_FirstPage_RendersMetadataRowsAndPagination", async () => {
    renderUsers();
    await screen.findByRole("table", { name: "Danh sách người dùng" });
    // Page size 20 → 20 metadata rows (each username is a row-header cell).
    await waitFor(() =>
      expect(screen.getAllByRole("rowheader")).toHaveLength(20),
    );
    // 25 users → 2 pages; the page summary announces it.
    expect(screen.getByRole("status")).toHaveTextContent("Trang 1 / 2");
    expect(screen.getByRole("button", { name: "Trang trước" })).toBeDisabled();
  });

  it("AdminUsersPage_NextPage_RefetchesAndSyncsPageToUrl", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/admin/users", ({ request }) => {
        urls.push(request.url);
        return passThroughList(request);
      }),
    );
    renderUsers();
    await screen.findByRole("table", { name: "Danh sách người dùng" });
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));

    await userEvent.click(screen.getByRole("button", { name: "Trang sau" }));

    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("page") === "2"),
      ).toBe(true),
    );
    expect(loc()).toContain("page=2");
    // Page 2 holds the remaining 5 rows.
    await waitFor(() =>
      expect(screen.getAllByRole("rowheader")).toHaveLength(5),
    );
  });
});

describe("AdminUsersPage — filters + sort are URL-synced", () => {
  it("AdminUsersPage_TierFilter_SyncsUrlAndSendsQueryAndResetsPage", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/admin/users", ({ request }) => {
        urls.push(request.url);
        return passThroughList(request);
      }),
    );
    renderUsers();
    await screen.findByRole("table", { name: "Danh sách người dùng" });

    await userEvent.click(screen.getByRole("combobox", { name: "Hạng" }));
    await userEvent.click(await screen.findByRole("option", { name: "Premium" }));

    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("tier") === "PREMIUM"),
      ).toBe(true),
    );
    expect(loc()).toContain("tier=PREMIUM");
    // A filter change returns to page 1 (no page param).
    expect(loc()).not.toContain("page=");
  });

  it("AdminUsersPage_SortByUsername_TogglesDirectionInUrlAndQuery", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/admin/users", ({ request }) => {
        urls.push(request.url);
        return passThroughList(request);
      }),
    );
    renderUsers();
    await screen.findByRole("table", { name: "Danh sách người dùng" });

    const sortBtn = screen.getByRole("button", { name: /Tên đăng nhập/ });
    await userEvent.click(sortBtn);
    await waitFor(() => expect(loc()).toContain("sort=username"));
    expect(loc()).toContain("dir=asc");

    await userEvent.click(screen.getByRole("button", { name: /Tên đăng nhập/ }));
    await waitFor(() => expect(loc()).toContain("dir=desc"));
    expect(
      urls.some((u) => {
        const p = new URL(u).searchParams;
        return p.get("sort") === "username" && p.get("direction") === "desc";
      }),
    ).toBe(true);
  });

  it("AdminUsersPage_Search_DebouncesThenSyncsUrlAndQuery", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/admin/users", ({ request }) => {
        urls.push(request.url);
        return passThroughList(request);
      }),
    );
    renderUsers();
    await screen.findByRole("table", { name: "Danh sách người dùng" });

    await userEvent.type(
      screen.getByLabelText("Tìm theo tên đăng nhập"),
      "nguyen",
    );
    await waitFor(
      () => expect(loc()).toContain("search=nguyen"),
      { timeout: 2000 },
    );
    expect(
      urls.some((u) => new URL(u).searchParams.get("search") === "nguyen"),
    ).toBe(true);
  });
});

describe("AdminUsersPage — states", () => {
  it("AdminUsersPage_Empty_ShowsEmptyState", async () => {
    server.use(
      http.get("*/api/v1/admin/users", () =>
        HttpResponse.json({
          data: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 },
          isSuccess: true,
          error: null,
        }),
      ),
    );
    renderUsers();
    expect(
      await screen.findByText("Không tìm thấy người dùng nào"),
    ).toBeInTheDocument();
  });

  it("AdminUsersPage_Loading_ShowsSkeletonBeforeRows", async () => {
    server.use(
      http.get("*/api/v1/admin/users", async ({ request }) => {
        await delay(40);
        return passThroughList(request);
      }),
    );
    const { container } = renderUsers();
    // Before data: skeleton cells (aria-hidden), no username row-headers yet.
    expect(screen.queryAllByRole("rowheader")).toHaveLength(0);
    expect(
      container.querySelectorAll('[aria-hidden="true"]').length,
    ).toBeGreaterThan(0);
    await waitFor(() =>
      expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0),
    );
  });

  it("AdminUsersPage_Error_ShowsErrorStateWithRetry", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/admin/users", () => {
        calls += 1;
        return HttpResponse.json(
          { data: null, isSuccess: false, error: { code: 1000, message: "Đã xảy ra lỗi máy chủ." } },
          { status: 500 },
        );
      }),
    );
    renderUsers();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Không tải được danh sách người dùng");
    const before = calls;
    await userEvent.click(screen.getByRole("button", { name: "Thử lại" }));
    await waitFor(() => expect(calls).toBeGreaterThan(before));
  });
});

/**
 * A local re-implementation of the committed list handler so capture wrappers can
 * both observe the request AND serve deterministic paged/filtered data (the
 * committed handler is not re-entrant from a `server.use` override).
 */
function passThroughList(request: Request) {
  // Delegate to the same fixture the committed handler uses by mirroring its
  // filter/sort/page logic against a fresh deterministic seed.
  const url = new URL(request.url);
  const page = Math.max(1, Number(url.searchParams.get("page")) || 1);
  const pageSize = Math.min(
    100,
    Math.max(1, Number(url.searchParams.get("pageSize")) || 20),
  );
  const tier = url.searchParams.get("tier");
  const total = tier === "PREMIUM" ? 9 : 25;
  const items = Array.from(
    { length: Math.min(pageSize, Math.max(0, total - (page - 1) * pageSize)) },
    (_, i) => {
      const n = (page - 1) * pageSize + i + 1;
      return {
        uuid: `uuid-user-${n}`,
        username: `user.${String(n).padStart(3, "0")}`,
        tier: tier ?? (n % 3 === 0 ? "PREMIUM" : "FREE"),
        role: "USER",
        status: "ACTIVE",
        createdAt: "2026-03-01T09:00:00.000Z",
        grantCount: 0,
        lastGrantAt: null,
      };
    },
  );
  return HttpResponse.json({
    data: { items, page, pageSize, totalCount: total, totalPages: Math.ceil(total / pageSize) },
    isSuccess: true,
    error: null,
  });
}
