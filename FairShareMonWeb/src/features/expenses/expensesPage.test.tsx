import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useLocation } from "react-router-dom";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ExpensesPage } from "./pages/ExpensesPage";
import type { ExpenseSummaryResponse } from "./api/types";

/**
 * ExpensesPage integration — the REAL page/filter bar/table/hooks against MSW at
 * the client boundary. The per-user MSW expenses store starts empty, so tests that
 * need rows override GET /expenses with canned summaries; filter tests capture the
 * refetch request URL (proving both the URL-driven state and the server refetch).
 * A LocationProbe surfaces the router URL so filter persistence is observable.
 * Copy is the vi-VN default unless a spec flips the locale.
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
function seedSession(): string {
  userSeq += 1;
  const username = `xptest${userSeq}`;
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: `access-${username}-t`,
    accessTokenExpiresAt: future,
    refreshToken: `refresh-${username}-t`,
    refreshTokenExpiresAt: future,
    user: { username, tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
  return username;
}

function makeSummary(
  overrides: Partial<ExpenseSummaryResponse> = {},
): ExpenseSummaryResponse {
  return {
    uuid: "e-1",
    name: "Thuê xe",
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 300000,
    category: {
      uuid: "c-1",
      name: "Đi lại",
      color: "#3B82F6",
      icon: "🚗",
      isDefault: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    payer: {
      uuid: "m-1",
      name: "An Nguyễn",
      isOwnerRepresentative: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    tagNames: [],
    shareCount: 2,
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-16T03:00:00+00:00",
    ...overrides,
  };
}

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="loc-search">{location.search}</div>;
}

function renderExpenses(initialPath = "/expenses") {
  return renderWithProviders(
    <Routes>
      <Route
        path="/expenses"
        element={
          <>
            <ExpensesPage />
            <LocationProbe />
          </>
        }
      />
    </Routes>,
    { initialPath, queryClient },
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

// ─── States ───────────────────────────────────────────────────────────────────
describe("ExpensesPage states", () => {
  it("ExpensesPage_Loading_ShowsSkeletonRows", () => {
    server.use(
      http.get("*/api/v1/expenses", async () => {
        await delay(50);
        return ok([]);
      }),
    );
    renderExpenses();
    // While pending, the table shell renders 5 placeholder rows (empty rowheaders).
    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders).toHaveLength(5);
    expect(rowHeaders.every((c) => c.textContent === "")).toBe(true);
  });

  it("ExpensesPage_EmptyStore_ShowsNoExpensesEmptyState", async () => {
    server.use(http.get("*/api/v1/expenses", () => ok([])));
    renderExpenses();
    expect(
      await screen.findByText("Chưa có phiếu chi tiêu nào"),
    ).toBeInTheDocument();
  });

  it("ExpensesPage_ListError_ShowsErrorStateThenRetryRecovers", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/expenses", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok([makeSummary()]);
      }),
    );
    const user = userEvent.setup();
    renderExpenses();

    expect(
      await screen.findByText("Không tải được danh sách phiếu chi tiêu"),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Thử lại" }));
    expect(
      await screen.findByRole("rowheader", { name: "Thuê xe" }),
    ).toBeInTheDocument();
  });
});

// ─── Rows / columns ─────────────────────────────────────────────────────────
describe("ExpensesPage rows", () => {
  it("ExpensesPage_Row_RendersPayerCategoryMarkerTotalTimeSettledAndLooseBadge", async () => {
    server.use(http.get("*/api/v1/expenses", () => ok([makeSummary()])));
    renderExpenses();

    const row = (
      await screen.findByRole("rowheader", { name: "Thuê xe" })
    ).closest("tr") as HTMLElement;

    // Payer, category marker (name visible), Money total (vi-VN grouping),
    // loose badge, and the color-independent settled toggle.
    expect(within(row).getByText("An Nguyễn")).toBeInTheDocument();
    expect(within(row).getByText("Đi lại")).toBeInTheDocument();
    expect(within(row).getByText(/300\.000/)).toBeInTheDocument();
    expect(within(row).getByText("Phiếu lẻ")).toBeInTheDocument();
    const toggle = within(row).getByRole("switch", {
      name: "Trạng thái đã trả của Thuê xe",
    });
    expect(toggle).toHaveAttribute("aria-checked", "false");
  });

  it("ExpensesPage_EventExpense_ShowsEventBadge", async () => {
    server.use(
      http.get("*/api/v1/expenses", () =>
        ok([
          makeSummary({
            uuid: "e-2",
            name: "Ăn tối",
            eventUuid: "ev-1",
            eventName: "Đà Lạt",
          }),
        ]),
      ),
    );
    renderExpenses();
    const row = (
      await screen.findByRole("rowheader", { name: "Ăn tối" })
    ).closest("tr") as HTMLElement;
    expect(within(row).getByText("Đà Lạt")).toBeInTheDocument();
    expect(within(row).queryByText("Phiếu lẻ")).not.toBeInTheDocument();
  });

  it("ExpensesPage_DeletedPayerOrCategory_ShowsDeletedTag", async () => {
    server.use(
      http.get("*/api/v1/expenses", () =>
        ok([
          makeSummary({
            payer: {
              uuid: "m-x",
              name: "Cũ",
              isOwnerRepresentative: false,
              isDeleted: true,
              createdAt: "2026-01-01T00:00:00+00:00",
            },
          }),
        ]),
      ),
    );
    renderExpenses();
    const row = (
      await screen.findByRole("rowheader", { name: "Thuê xe" })
    ).closest("tr") as HTMLElement;
    expect(within(row).getByText("(đã xóa)")).toBeInTheDocument();
  });

  it("ExpensesPage_NameCell_LinksToDetailRoute", async () => {
    server.use(http.get("*/api/v1/expenses", () => ok([makeSummary()])));
    renderExpenses();
    const link = await screen.findByRole("link", { name: "Thuê xe" });
    expect(link).toHaveAttribute("href", "/expenses/e-1");
  });
});

// ─── Filters (URL + refetch) ─────────────────────────────────────────────────
describe("ExpensesPage filters", () => {
  it("ExpensesPage_LooseOnlyToggle_UpdatesUrlAndRefetchesWithLooseOnly", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/expenses", ({ request }) => {
        urls.push(request.url);
        return ok([]);
      }),
    );
    const user = userEvent.setup();
    renderExpenses();
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));

    await user.click(screen.getByRole("checkbox", { name: "Chỉ phiếu lẻ" }));

    // The refetch carries looseOnly=true…
    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("looseOnly") === "true"),
      ).toBe(true),
    );
    // …and the state is reflected in the URL (shareable / back-friendly).
    expect(screen.getByTestId("loc-search").textContent).toContain("loose=1");
  });

  it("ExpensesPage_CategoryFilter_UpdatesUrlAndRefetchesWithCategoryUuid", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/expenses", ({ request }) => {
        urls.push(request.url);
        return ok([]);
      }),
    );
    const user = userEvent.setup();
    renderExpenses();
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));

    // Open the category filter Select and pick a seeded category ("Đi lại").
    await user.click(screen.getByRole("combobox", { name: "Danh mục" }));
    await user.click(await screen.findByRole("option", { name: /Đi lại/ }));

    await waitFor(() =>
      expect(
        urls.some((u) => (new URL(u).searchParams.get("categoryUuid") ?? "") !== ""),
      ).toBe(true),
    );
    expect(screen.getByTestId("loc-search").textContent).toContain("category=");
  });

  it("ExpensesPage_SettledFilter_UpdatesUrlAndRefetchesWithSettledFlag", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/expenses", ({ request }) => {
        urls.push(request.url);
        return ok([]);
      }),
    );
    const user = userEvent.setup();
    renderExpenses();
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));

    await user.click(screen.getByRole("combobox", { name: "Trạng thái" }));
    await user.click(await screen.findByRole("option", { name: "Đã trả" }));

    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("settled") === "true"),
      ).toBe(true),
    );
    expect(screen.getByTestId("loc-search").textContent).toContain(
      "settled=yes",
    );
  });

  it("ExpensesPage_ClearFilters_RemovesUrlState", async () => {
    server.use(http.get("*/api/v1/expenses", () => ok([])));
    const user = userEvent.setup();
    renderExpenses("/expenses?loose=1&settled=no");

    // The clear button is enabled because filters are active…
    const clear = await screen.findByRole("button", { name: "Xóa lọc" });
    expect(clear).toBeEnabled();
    await user.click(clear);
    await waitFor(() =>
      expect(screen.getByTestId("loc-search").textContent).toBe(""),
    );
  });

  it("ExpensesPage_ActiveFilterNoMatches_ShowsNoMatchesEmptyState", async () => {
    server.use(http.get("*/api/v1/expenses", () => ok([])));
    renderExpenses("/expenses?loose=1");
    expect(
      await screen.findByText("Không có phiếu nào khớp bộ lọc"),
    ).toBeInTheDocument();
  });
});

// ─── Client-side name search ─────────────────────────────────────────────────
describe("ExpensesPage name search", () => {
  it("ExpensesPage_NameSearch_FiltersLoadedRowsClientSide", async () => {
    let getCount = 0;
    server.use(
      http.get("*/api/v1/expenses", () => {
        getCount += 1;
        return ok([
          makeSummary({ uuid: "e-1", name: "Thuê xe" }),
          makeSummary({ uuid: "e-2", name: "Ăn tối" }),
        ]);
      }),
    );
    const user = userEvent.setup();
    renderExpenses();
    await screen.findByRole("rowheader", { name: "Thuê xe" });
    const getsAfterLoad = getCount;

    await user.type(screen.getByRole("searchbox", { name: "Tìm theo tên" }), "Ăn");

    // "Ăn tối" stays; "Thuê xe" is filtered out — no extra server round-trip.
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: "Thuê xe" }),
      ).not.toBeInTheDocument(),
    );
    expect(
      screen.getByRole("rowheader", { name: "Ăn tối" }),
    ).toBeInTheDocument();
    expect(getCount).toBe(getsAfterLoad);
  });
});

// ─── Settled toggle from the list ────────────────────────────────────────────
describe("ExpensesPage settled toggle", () => {
  it("ExpensesPage_SettledToggle_MutatesAndToasts", async () => {
    let settled = false;
    let settledBody: unknown;
    server.use(
      http.get("*/api/v1/expenses", () =>
        ok([makeSummary({ isSettled: settled })]),
      ),
      http.put("*/api/v1/expenses/:uuid/settled", async ({ request }) => {
        settledBody = await request.json();
        settled = (settledBody as { isSettled: boolean }).isSettled;
        return ok({ message: "Đã cập nhật trạng thái đã trả." });
      }),
    );
    const user = userEvent.setup();
    renderExpenses();

    const toggle = await screen.findByRole("switch", {
      name: "Trạng thái đã trả của Thuê xe",
    });
    expect(toggle).toHaveAttribute("aria-checked", "false");
    await user.click(toggle);

    expect(
      await screen.findByText("Đã đánh dấu là đã trả."),
    ).toBeInTheDocument();
    expect(settledBody).toEqual({ isSettled: true });
    // After the invalidate → refetch, the row reflects the new settled state.
    await waitFor(() =>
      expect(
        screen.getByRole("switch", {
          name: "Trạng thái đã trả của Thuê xe",
        }),
      ).toHaveAttribute("aria-checked", "true"),
    );
  });
});

// ─── Event filter seam (M5 — completes the M4 OQ7 deferral) ──────────────────
describe("ExpensesPage event filter", () => {
  function eventSummary() {
    return [
      {
        uuid: "ev-1",
        name: "Đà Lạt",
        startDate: "2026-07-12T00:00:00+07:00",
        endDate: "2026-07-18T23:59:59+07:00",
        isClosed: false,
        closedAt: null,
        expenseCount: 0,
        createdAt: "2026-07-01T00:00:00+00:00",
      },
    ];
  }

  it("ExpensesPage_EventFilter_UpdatesUrlAndRefetchesWithEventUuid", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/events", () => ok(eventSummary())),
      http.get("*/api/v1/expenses", ({ request }) => {
        urls.push(request.url);
        return ok([]);
      }),
    );
    const user = userEvent.setup();
    renderExpenses();
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));

    // The event Select (labelled "Đợt") lists the caller's events.
    await user.click(screen.getByRole("combobox", { name: "Đợt" }));
    await user.click(await screen.findByRole("option", { name: "Đà Lạt" }));

    // The refetch carries eventUuid=ev-1…
    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("eventUuid") === "ev-1"),
      ).toBe(true),
    );
    // …and the URL reflects ?event=ev-1.
    expect(screen.getByTestId("loc-search").textContent).toContain("event=ev-1");
  });

  it("ExpensesPage_SelectEvent_ClearsLooseOnlyMutualExclusivity", async () => {
    server.use(
      http.get("*/api/v1/events", () => ok(eventSummary())),
      http.get("*/api/v1/expenses", () => ok([])),
    );
    const user = userEvent.setup();
    // Start with loose-only active.
    renderExpenses("/expenses?loose=1");
    expect(
      await screen.findByRole("checkbox", { name: "Chỉ phiếu lẻ" }),
    ).toBeChecked();

    await user.click(screen.getByRole("combobox", { name: "Đợt" }));
    await user.click(await screen.findByRole("option", { name: "Đà Lạt" }));

    // Selecting an event clears loose-only (mutually exclusive) — URL has no loose.
    await waitFor(() =>
      expect(screen.getByTestId("loc-search").textContent).toContain(
        "event=ev-1",
      ),
    );
    expect(screen.getByTestId("loc-search").textContent).not.toContain("loose=1");
    expect(
      screen.getByRole("checkbox", { name: "Chỉ phiếu lẻ" }),
    ).not.toBeChecked();
  });

  it("ExpensesPage_LooseOnly_ClearsSelectedEventMutualExclusivity", async () => {
    server.use(
      http.get("*/api/v1/events", () => ok(eventSummary())),
      http.get("*/api/v1/expenses", () => ok([])),
    );
    const user = userEvent.setup();
    // Start with an event selected.
    renderExpenses("/expenses?event=ev-1");
    await waitFor(() =>
      expect(screen.getByTestId("loc-search").textContent).toContain(
        "event=ev-1",
      ),
    );

    await user.click(screen.getByRole("checkbox", { name: "Chỉ phiếu lẻ" }));

    // Enabling loose-only clears the event selection.
    await waitFor(() =>
      expect(screen.getByTestId("loc-search").textContent).toContain("loose=1"),
    );
    expect(screen.getByTestId("loc-search").textContent).not.toContain(
      "event=ev-1",
    );
  });
});

// ─── i18n ────────────────────────────────────────────────────────────────────
describe("ExpensesPage i18n", () => {
  it("ExpensesPage_EnUsLocale_RendersEnglishChrome", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    server.use(http.get("*/api/v1/expenses", () => ok([])));
    renderExpenses();
    expect(
      await screen.findByRole("heading", { level: 1, name: "Expenses" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Add expense" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("No expenses yet")).toBeInTheDocument();
  });
});
