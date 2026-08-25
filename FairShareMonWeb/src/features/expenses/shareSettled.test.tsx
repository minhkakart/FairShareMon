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
import { useEventBalanceQuery } from "@/features/events/hooks/useEvents";
import { SharesSection } from "./components/SharesSection";
import type { ExpenseResponse, ShareResponse } from "./api/types";
import type { MemberResponse } from "@/features/members/api/types";

/**
 * Layer A (§6) — the per-share settled column + rollup chip in `SharesSection`,
 * driven by `ShareSettledToggle`. The section is rendered directly with a canned
 * expense (its own state comes from the prop, so we assert the fired request +
 * toast, matching the shipped add/edit specs). Billable shares (not the payer's
 * own share, amount > 0) render a color-independent `role="switch"`; payer-own +
 * 0đ shares render a muted "Không nợ" and are excluded from the rollup. The
 * settled column is EXEMPT from the closed-event write gate (R6). Network mocked
 * at the client boundary (MSW).
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

const MEMBERS: MemberResponse[] = [
  {
    uuid: "m-o",
    name: "Bạn (chủ sổ)",
    isOwnerRepresentative: true,
    isDeleted: false,
    createdAt: "2026-01-01T00:00:00+00:00",
  },
  {
    uuid: "m-1",
    name: "An Nguyễn",
    isOwnerRepresentative: false,
    isDeleted: false,
    createdAt: "2026-01-01T00:00:00+00:00",
  },
  {
    uuid: "m-2",
    name: "Bình Trần",
    isOwnerRepresentative: false,
    isDeleted: false,
    createdAt: "2026-01-01T00:00:00+00:00",
  },
];

function share(
  uuid: string,
  member: MemberResponse,
  amount: number,
  isSettled = false,
): ShareResponse {
  return {
    uuid,
    member,
    amount,
    note: null,
    isSettled,
    settledAt: isSettled ? "2026-07-16T03:00:00+00:00" : null,
    createdAt: "2026-07-16T03:00:00+00:00",
  };
}

/** Payer = owner-rep. Two BILLABLE shares (An 300.000, Bình 200.000) + the
 *  owner-rep's own 0đ share (settled-by-definition). */
function makeExpense(overrides: Partial<ExpenseResponse> = {}): ExpenseResponse {
  return {
    uuid: "e-1",
    name: "Thuê xe",
    description: null,
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 500000,
    category: {
      uuid: "c-1",
      name: "Đi lại",
      color: "#3B82F6",
      icon: "🚗",
      isDefault: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    payer: MEMBERS[0],
    isSettled: false,
    settledAt: null,
    shares: [
      share("s-o", MEMBERS[0], 0),
      share("s-1", MEMBERS[1], 300000),
      share("s-2", MEMBERS[2], 200000),
    ],
    tags: [],
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-16T03:00:00+00:00",
    ...overrides,
  };
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-shsettled-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-shsettled-t",
    refreshTokenExpiresAt: future,
    user: { username: "shsettled", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function renderShares(expense = makeExpense(), disabled = false) {
  return renderWithProviders(
    <SharesSection expense={expense} disabled={disabled} />,
    { queryClient },
  );
}

/** The rollup chip lives in the card header (h3 "Phần gánh") — scope to it so its
 *  text never collides with the in-table switch labels. */
function rollupScope(): HTMLElement {
  return screen.getByRole("heading", { name: "Phần gánh" })
    .parentElement as HTMLElement;
}

const EVENT_UUID = "ev-share";

/** Subscribes to the event balance query so its invalidation (or lack thereof)
 *  is observable via the mocked GET's request count — mirrors the
 *  `settledToggle.test.tsx`/`memberSettled.test.tsx` counters+Probe pattern. */
function BalanceProbe({ uuid }: { uuid: string }) {
  useEventBalanceQuery(uuid);
  return null;
}

function emptyBalance() {
  return {
    eventUuid: EVENT_UUID,
    eventName: "Đà Lạt",
    isClosed: false,
    rows: [],
    totalOutstanding: 0,
    owingMemberCount: 0,
    settledMemberCount: 0,
    partiallySettledMemberCount: 0,
  };
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
  server.use(http.get("*/api/v1/members", () => ok(MEMBERS)));
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("SharesSection settled column (Layer A)", () => {
  it("SharesSection_SettledColumn_RendersHeader", () => {
    renderShares();
    expect(
      screen.getByRole("columnheader", { name: "Đã trả" }),
    ).toBeInTheDocument();
  });

  it("ShareSettledToggle_BillableShare_RendersEnabledColorIndependentSwitch", () => {
    renderShares();
    const toggle = screen.getByRole("switch", {
      name: "Trạng thái đã trả phần gánh của An Nguyễn",
    });
    expect(toggle).toBeEnabled();
    expect(toggle).toHaveAttribute("aria-checked", "false");
    // State conveyed by text (not color alone).
    expect(toggle).toHaveTextContent("Chưa trả");
  });

  it("ShareSettledToggle_SettledShare_ReflectsCheckedFromData", () => {
    renderShares(
      makeExpense({
        shares: [
          share("s-o", MEMBERS[0], 0),
          share("s-1", MEMBERS[1], 300000, true),
          share("s-2", MEMBERS[2], 200000),
        ],
      }),
    );
    const toggle = screen.getByRole("switch", {
      name: "Trạng thái đã trả phần gánh của An Nguyễn",
    });
    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(toggle).toHaveTextContent("Đã trả");
  });

  it("ShareSettledToggle_Click_PutsToPerShareSettledRouteThenToasts", async () => {
    let path = "";
    let body: { isSettled?: boolean } | null = null;
    server.use(
      http.put(
        "*/api/v1/expenses/e-1/shares/:shareUuid/settled",
        async ({ request }) => {
          path = new URL(request.url).pathname;
          body = (await request.json()) as typeof body;
          return ok({ message: "OK" });
        },
      ),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("switch", {
        name: "Trạng thái đã trả phần gánh của An Nguyễn",
      }),
    );

    expect(
      await screen.findByText("Đã đánh dấu phần gánh là đã trả."),
    ).toBeInTheDocument();
    // The per-share sub-route + the boolean body.
    expect(path).toBe("/api/v1/expenses/e-1/shares/s-1/settled");
    expect(body).toEqual({ isSettled: true });
  });

  it("ShareSettledToggle_Error7000_ToastsVerbatimStaleMiss", async () => {
    server.use(
      http.put("*/api/v1/expenses/e-1/shares/:shareUuid/settled", () =>
        fail(7000, "Không tìm thấy phần gánh.", 404),
      ),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("switch", {
        name: "Trạng thái đã trả phần gánh của An Nguyễn",
      }),
    );

    expect(
      await screen.findByText("Không tìm thấy phần gánh."),
    ).toBeInTheDocument();
  });
});

describe("SharesSection payer-own & 0đ shares (R3/OQ3a)", () => {
  it("SharesSection_PayerOwnAndZeroShares_ShowNotOwedWithNoToggle", () => {
    // Payer = An (m-1); An's own 100.000 share is payer-own; the owner-rep's is 0đ.
    // Only Bình (200.000) is billable → exactly one switch.
    renderShares(
      makeExpense({
        payer: MEMBERS[1],
        total: 300000,
        shares: [
          share("s-o", MEMBERS[0], 0),
          share("s-1", MEMBERS[1], 100000),
          share("s-2", MEMBERS[2], 200000),
        ],
      }),
    );

    // Payer-own (>0) and 0đ shares are settled-by-definition → muted "Không nợ".
    expect(screen.getAllByText("Không nợ")).toHaveLength(2);
    // Only the one billable share has an interactive toggle.
    const switches = screen.getAllByRole("switch");
    expect(switches).toHaveLength(1);
    expect(switches[0]).toHaveAccessibleName(
      "Trạng thái đã trả phần gánh của Bình Trần",
    );
  });

  it("SharesSection_Rollup_ExcludesPayerOwnAndZeroFromCount", () => {
    // One billable (Bình) settled, payer-own + 0đ excluded → all billable settled.
    renderShares(
      makeExpense({
        payer: MEMBERS[1],
        total: 300000,
        shares: [
          share("s-o", MEMBERS[0], 0),
          share("s-1", MEMBERS[1], 100000),
          share("s-2", MEMBERS[2], 200000, true),
        ],
      }),
    );
    expect(within(rollupScope()).getByText("Đã trả toàn bộ")).toBeInTheDocument();
  });
});

describe("SharesSection rollup chip (R2/OQ2a)", () => {
  it("SharesSection_RollupNone_WhenNoBillableShareSettled", () => {
    renderShares();
    expect(within(rollupScope()).getByText("Chưa trả")).toBeInTheDocument();
    expect(screen.queryByText("Đã trả toàn bộ")).not.toBeInTheDocument();
  });

  it("SharesSection_RollupPartial_ShowsXofYBillableShares", () => {
    renderShares(
      makeExpense({
        shares: [
          share("s-o", MEMBERS[0], 0),
          share("s-1", MEMBERS[1], 300000, true),
          share("s-2", MEMBERS[2], 200000),
        ],
      }),
    );
    // 1 of the 2 billable shares settled (the 0đ owner-rep share is excluded).
    expect(
      within(rollupScope()).getByText("Đã trả một phần (1/2 phần)"),
    ).toBeInTheDocument();
  });

  it("SharesSection_RollupAll_WhenAllBillableSharesSettled", () => {
    renderShares(
      makeExpense({
        shares: [
          share("s-o", MEMBERS[0], 0),
          share("s-1", MEMBERS[1], 300000, true),
          share("s-2", MEMBERS[2], 200000, true),
        ],
      }),
    );
    expect(within(rollupScope()).getByText("Đã trả toàn bộ")).toBeInTheDocument();
  });
});

describe("SharesSection closed-event settled exception (R6)", () => {
  it("SharesSection_ClosedEvent_KeepsShareTogglesButHidesAddEditRemove", async () => {
    renderShares(makeExpense(), true);

    // The add/edit/remove write controls are gone (closed-event gate)…
    expect(
      screen.queryByRole("button", { name: "Thêm phần gánh" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Sửa phần gánh của An Nguyễn" }),
    ).not.toBeInTheDocument();
    // …but the per-share settled toggle stays present + ENABLED (the sole write).
    const toggle = screen.getByRole("switch", {
      name: "Trạng thái đã trả phần gánh của An Nguyễn",
    });
    expect(toggle).toBeEnabled();

    // And it still fires the PUT on a closed event's expense.
    let called = false;
    server.use(
      http.put("*/api/v1/expenses/e-1/shares/:shareUuid/settled", () => {
        called = true;
        return ok({ message: "OK" });
      }),
    );
    const user = userEvent.setup();
    await user.click(toggle);
    await waitFor(() => expect(called).toBe(true));
  });
});

describe("ShareSettledToggle event cross-invalidation (event-expense-settlement-sync M2.2/M2.5)", () => {
  it("ShareSettledToggle_ShareOnEventExpense_AlsoInvalidatesEventBalance", async () => {
    // The M2.2 regression, share-level trigger: with `eventUuid` set, a
    // successful per-share settle now also credits/claws back the event
    // balance overlay's `clearedAmount`, so the mutation's onSuccess must
    // additionally invalidate `eventsKeys.balance(eventUuid)`. Mount a live
    // `useEventBalanceQuery` subscriber (counters+Probe pattern) and count the
    // GETs it fires.
    let balanceRequests = 0;
    server.use(
      http.put("*/api/v1/expenses/e-1/shares/:shareUuid/settled", () =>
        ok({ message: "OK" }),
      ),
      http.get(`*/api/v1/events/${EVENT_UUID}/balance`, () => {
        balanceRequests += 1;
        return ok(emptyBalance());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <SharesSection
          expense={makeExpense({ eventUuid: EVENT_UUID })}
          disabled={false}
        />
        <BalanceProbe uuid={EVENT_UUID} />
      </>,
      { queryClient },
    );

    await waitFor(() => expect(balanceRequests).toBe(1));

    await user.click(
      screen.getByRole("switch", {
        name: "Trạng thái đã trả phần gánh của An Nguyễn",
      }),
    );

    // The "synced" toast copy (distinct from the plain on/off pair) confirms
    // the `eventUuid`-gated toast branch fired.
    expect(
      await screen.findByText(
        "Đã cập nhật đã trả — số dư còn nợ của đợt đã được đồng bộ tương ứng.",
      ),
    ).toBeInTheDocument();
    // Before the M2.2 fix, `eventsKeys.balance` was never reached from a
    // per-share flip (the doc's own comment literally asserted it never would)
    // — the fix's whole point is that a second GET now fires.
    await waitFor(() => expect(balanceRequests).toBeGreaterThanOrEqual(2));
  });

  it("ShareSettledToggle_LooseExpense_NoEventUuid_NeverRequestsEventBalance", async () => {
    // Regression companion: a loose expense (`eventUuid` undefined/null, the
    // default `makeExpense()` fixture) must not touch `eventsKeys.balance` at
    // all — the plain settledToastOn copy stays, and the probe's balance query
    // is never refetched by this mutation.
    let balanceRequests = 0;
    server.use(
      http.put("*/api/v1/expenses/e-1/shares/:shareUuid/settled", () =>
        ok({ message: "OK" }),
      ),
      http.get(`*/api/v1/events/${EVENT_UUID}/balance`, () => {
        balanceRequests += 1;
        return ok(emptyBalance());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <SharesSection expense={makeExpense()} disabled={false} />
        <BalanceProbe uuid={EVENT_UUID} />
      </>,
      { queryClient },
    );

    await waitFor(() => expect(balanceRequests).toBe(1));

    await user.click(
      screen.getByRole("switch", {
        name: "Trạng thái đã trả phần gánh của An Nguyễn",
      }),
    );

    expect(
      await screen.findByText("Đã đánh dấu phần gánh là đã trả."),
    ).toBeInTheDocument();
    expect(balanceRequests).toBe(1);
  });
});
