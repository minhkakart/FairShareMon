import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { EventBalanceTable } from "@/features/events/components/EventBalanceTable";
import { ExpenseDetailPage } from "./pages/ExpenseDetailPage";
import type { ExpenseResponse } from "./api/types";

/**
 * Layer A reconcile + whole-expense cascade — the REAL detail route/hooks against
 * a mutable MSW store. Because the per-share/whole-expense toggles are
 * refetch-based (OQ6a, no optimistic update), the UI must reconcile from the
 * expense-detail refetch after each mutation. The whole-expense toggle cascades
 * to every BILLABLE share on the server (OQ3a); the detail refetch surfaces the
 * cascaded share flags + the derived rollup. Network mocked at the client
 * boundary (MSW).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const UUID = "e-detail";

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-reconcile-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-reconcile-t",
    refreshTokenExpiresAt: future,
    user: { username: "reconcile", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

/** Payer = owner-rep (m-owner). One billable share (An, 300.000) + the owner-rep's
 *  own 0đ share (settled-by-definition, excluded from the rollup + cascade). */
function freshExpense(): ExpenseResponse {
  return {
    uuid: UUID,
    name: "Thuê xe",
    description: null,
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
      uuid: "m-owner",
      name: "Bạn (chủ sổ)",
      isOwnerRepresentative: true,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    shares: [
      {
        uuid: "s-owner",
        isSettled: false,
        settledAt: null,
        member: {
          uuid: "m-owner",
          name: "Bạn (chủ sổ)",
          isOwnerRepresentative: true,
          isDeleted: false,
          createdAt: "2026-01-01T00:00:00+00:00",
        },
        amount: 0,
        note: null,
        createdAt: "2026-07-16T03:00:00+00:00",
      },
      {
        uuid: "s-1",
        isSettled: false,
        settledAt: null,
        member: {
          uuid: "m-1",
          name: "An Nguyễn",
          isOwnerRepresentative: false,
          isDeleted: false,
          createdAt: "2026-01-01T00:00:00+00:00",
        },
        amount: 300000,
        note: null,
        createdAt: "2026-07-16T03:00:00+00:00",
      },
    ],
    tags: [],
    // event-expense-settlement-sync (M2.5): this expense now belongs to an
    // event (open, so no write is gated) so the cross-cache invalidation test
    // below can exercise the real `SettledToggle`/`ShareSettledToggle`
    // `eventUuid` wiring. None of the pre-existing Layer-A reconcile tests in
    // this file assert on the absence of an event badge, so this is additive.
    eventUuid: EVENT_UUID,
    eventName: "Đà Lạt",
    eventIsClosed: false,
    createdAt: "2026-07-16T03:00:00+00:00",
  };
}

const EVENT_UUID = "ev-detail";
/** An's SECOND billable debtor share, in a different (unrendered) expense in
 *  the same event — needed so a single share-settle on the rendered expense
 *  produces a genuine PARTIAL clearance (some, not all, of An's net debt),
 *  rather than jumping straight from Unsettled to Settled. */
const AN_SECOND_EXPENSE_SHARE = 200000;

/** username -> raw credited amount (Direction 2) for An in `EVENT_UUID`,
 *  mirroring `src/test/msw/handlers.ts`'s own `clearedCreditByUser` shape —
 *  kept LOCAL to this file (its own installed handlers), not the committed
 *  MSW store, matching this file's existing "own mutable store" pattern. */
let anCredited = 0;

function anNetOwed(): number {
  return freshExpense().shares.find((s) => s.member.uuid === "m-1")!.amount +
    AN_SECOND_EXPENSE_SHARE;
}

function balancePayload() {
  const netOwed = anNetOwed();
  const clearedAmount = Math.min(anCredited, netOwed);
  const outstanding = Math.max(0, netOwed - clearedAmount);
  const settlementStatus: "Unsettled" | "PartiallySettled" | "Settled" =
    clearedAmount <= 0
      ? "Unsettled"
      : clearedAmount >= netOwed
        ? "Settled"
        : "PartiallySettled";
  return {
    eventUuid: EVENT_UUID,
    eventName: "Đà Lạt",
    isClosed: false,
    rows: [
      {
        memberUuid: "m-owner",
        memberName: "Bạn (chủ sổ)",
        isOwnerRepresentative: true,
        isDeleted: false,
        advanced: netOwed,
        owed: 0,
        balance: netOwed,
        outstanding: 0,
        isSettled: false,
        settledAt: null,
        isEligibleForAutoCascade: false,
        clearedAmount: 0,
        settlementStatus: "Unsettled",
      },
      {
        memberUuid: "m-1",
        memberName: "An Nguyễn",
        isOwnerRepresentative: false,
        isDeleted: false,
        advanced: 0,
        owed: netOwed,
        balance: -netOwed,
        outstanding,
        isSettled: false,
        settledAt: null,
        isEligibleForAutoCascade: true,
        clearedAmount,
        settlementStatus,
      },
    ],
    totalOutstanding: outstanding,
    owingMemberCount: outstanding > 0 ? 1 : 0,
    settledMemberCount: 0,
    partiallySettledMemberCount: settlementStatus === "PartiallySettled" ? 1 : 0,
  };
}

let store: ExpenseResponse;

/** Mutable detail store + the two settled write routes that mutate it, so the
 *  refetch after each mutation returns the reconciled state. */
/** Shared credit/claw-back helper for this file's own local mutable store —
 *  mirrors `src/test/msw/handlers.ts`'s `applyShareCredit` (one code path for
 *  both the whole-expense and per-share handlers below), applied only on an
 *  ACTUAL isSettled transition so a flip is credited/clawed back exactly once. */
function applyLocalCredit(memberUuid: string, amount: number, next: boolean) {
  if (memberUuid !== "m-1") return; // only An participates in this fixture.
  anCredited = Math.max(0, anCredited + (next ? amount : -amount));
}

function installMutableStore() {
  store = freshExpense();
  anCredited = 0;
  server.use(
    http.get(`*/api/v1/expenses/${UUID}`, () =>
      // Return a fresh clone each read so React Query treats it as new data.
      ok(JSON.parse(JSON.stringify(store))),
    ),
    http.get(`*/api/v1/expenses/${UUID}/history`, () => ok([])),
    http.put(
      `*/api/v1/expenses/${UUID}/shares/:shareUuid/settled`,
      async ({ request, params }) => {
        const body = (await request.json()) as { isSettled?: boolean };
        const next = Boolean(body.isSettled);
        const s = store.shares.find((x) => x.uuid === params.shareUuid);
        if (s) {
          const billable = s.member.uuid !== store.payer.uuid && s.amount > 0;
          if (billable && s.isSettled !== next) {
            applyLocalCredit(s.member.uuid, s.amount, next);
          }
          s.isSettled = next;
        }
        return ok({ message: "OK" });
      },
    ),
    http.put(`*/api/v1/expenses/${UUID}/settled`, async ({ request }) => {
      const body = (await request.json()) as { isSettled?: boolean };
      const next = Boolean(body.isSettled);
      store.isSettled = next;
      // Cascade to billable shares only (not the payer's own / 0đ share).
      for (const s of store.shares) {
        if (s.member.uuid !== store.payer.uuid && s.amount > 0) {
          if (s.isSettled !== next) applyLocalCredit(s.member.uuid, s.amount, next);
          s.isSettled = next;
        }
      }
      return ok({ message: "OK" });
    }),
    http.get(`*/api/v1/events/${EVENT_UUID}/balance`, () => ok(balancePayload())),
  );
}

function renderDetail() {
  return renderWithProviders(
    <Routes>
      <Route path="/expenses/:uuid" element={<ExpenseDetailPage />} />
    </Routes>,
    { initialPath: `/expenses/${UUID}`, queryClient },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
  installMutableStore();
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("ExpenseDetailPage Layer A reconcile", () => {
  it("ExpenseDetailPage_ToggleShareSettled_ReconcilesSwitchFromRefetch", async () => {
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    const toggle = screen.getByRole("switch", {
      name: "Trạng thái đã trả phần gánh của An Nguyễn",
    });
    expect(toggle).toHaveAttribute("aria-checked", "false");

    await user.click(toggle);

    // Refetch-based (OQ6a): the switch flips to checked ONLY after the detail
    // refetch returns the server-persisted flag.
    await waitFor(() =>
      expect(
        screen.getByRole("switch", {
          name: "Trạng thái đã trả phần gánh của An Nguyễn",
        }),
      ).toHaveAttribute("aria-checked", "true"),
    );
  });

  it("ExpenseDetailPage_WholeExpenseSettled_CascadesToShareTogglesAndRollup", async () => {
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    // Before: the billable share reads unsettled and the rollup is "Chưa trả".
    expect(
      screen.getByRole("switch", {
        name: "Trạng thái đã trả phần gánh của An Nguyễn",
      }),
    ).toHaveAttribute("aria-checked", "false");

    // Flip the WHOLE-expense header toggle on.
    await user.click(
      screen.getByRole("switch", { name: "Trạng thái đã trả của Thuê xe" }),
    );

    // The backend cascade marks every billable share settled; the detail refetch
    // reconciles the per-share switch…
    await waitFor(() =>
      expect(
        screen.getByRole("switch", {
          name: "Trạng thái đã trả phần gánh của An Nguyễn",
        }),
      ).toHaveAttribute("aria-checked", "true"),
    );
    // …and the derived rollup chip now reads "Đã trả toàn bộ".
    const header = screen.getByRole("heading", { name: "Phần gánh" })
      .parentElement as HTMLElement;
    expect(within(header).getByText("Đã trả toàn bộ")).toBeInTheDocument();
  });
});

describe("ExpenseDetailPage + EventBalanceTable cross-invalidation (event-expense-settlement-sync M2.2/M2.3/M2.5)", () => {
  /** Scopes to the `EventBalanceTable`'s own row for a given member name — the
   *  table's `data-testid="event-balance-row"` is unique to it (unlike
   *  `rowheader`/plain text, which `SharesSection`'s own table also renders for
   *  the same member names, causing ambiguous unscoped queries). */
  function balanceRowFor(name: string): HTMLElement {
    return screen
      .getAllByTestId("event-balance-row")
      .find((r) => within(r).queryByText(new RegExp(name))) as HTMLElement;
  }

  it("ShareSettledToggle_TogglingShare_UpdatesClearedAmountOnCoRenderedEventBalanceTableWithoutManualRerender", async () => {
    // The end-to-end proof of the M2.2 cross-cache invalidation wiring: mount
    // BOTH `ExpenseDetailPage` (real `useSetShareSettled` hook) AND
    // `EventBalanceTable` (real `useEventBalanceQuery`) against the SAME
    // queryClient + the same event. Toggling a share must update
    // `clearedAmount`/`settlementStatus` on the co-rendered balance table with
    // NO manual refetch call from the test — only the mutation's own
    // `eventsKeys.balance` invalidation.
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <Routes>
          <Route path="/expenses/:uuid" element={<ExpenseDetailPage />} />
        </Routes>
        <EventBalanceTable uuid={EVENT_UUID} />
      </>,
      { initialPath: `/expenses/${UUID}`, queryClient },
    );
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    // Before: An owes 500.000 net (300.000 on the rendered expense's share +
    // 200.000 elsewhere in the event, per `anNetOwed()`), nothing credited yet
    // → Unsettled status, plain (non-meter) cell, no progressbar.
    let anRow = balanceRowFor("An Nguyễn");
    expect(within(anRow).getAllByText("Chưa trả").length).toBeGreaterThanOrEqual(
      1,
    );
    expect(within(anRow).queryByRole("progressbar")).not.toBeInTheDocument();

    // Toggle the ONE rendered share (300.000 of An's 500.000 net debt)
    // settled — no manual refetch call anywhere in this test.
    await user.click(
      screen.getByRole("switch", {
        name: "Trạng thái đã trả phần gánh của An Nguyễn",
      }),
    );

    // The co-rendered EventBalanceTable reconciles ON ITS OWN: the badge flips
    // to "Đã trả một phần" and the "Còn nợ" cell now renders the
    // `SettlementMeter` fraction for the new `clearedAmount`.
    await waitFor(() => {
      expect(
        within(balanceRowFor("An Nguyễn")).getByText("Đã trả một phần"),
      ).toBeInTheDocument();
    });
    anRow = balanceRowFor("An Nguyễn");
    const outstandingCell = within(anRow).getByTestId("outstanding-amount");
    expect(within(outstandingCell).getByRole("progressbar")).toBeInTheDocument();
    // clearedAmount (300.000) / netOwed (500.000) — the composed fraction.
    expect(outstandingCell.textContent).toMatch(/300\.000/);
    expect(outstandingCell.textContent).toMatch(/500\.000/);
  });
});
