import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import { useExpensesQuery } from "@/features/expenses/hooks/useExpenses";
import i18n from "@/i18n";
import { EventBalanceTable } from "./components/EventBalanceTable";

/**
 * Layer B (§6) — the outstanding overlay + per-member settled toggle in
 * `EventBalanceTable`. The balance columns (advanced / owed / balance) and the
 * sum-to-zero footer stay PURE and unchanged (D2); the additive overlay renders
 * `outstanding` (còn nợ), a color-independent đã-trả/còn-nợ status, and a per-
 * member toggle ONLY for owing members (`balance < 0`, OQ5a). The toggle is
 * refetch-based (OQ6a): marking a member reconciles `outstanding` → 0 + the badge
 * from the balance refetch, and it stays enabled on OPEN and CLOSED events (R6).
 * Network mocked at the client boundary (MSW).
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

const UUID = "ev-bal";

interface BaseRow {
  memberUuid: string;
  memberName: string;
  isOwnerRepresentative: boolean;
  isDeleted: boolean;
  advanced: number;
  owed: number;
  balance: number;
  /**
   * Direction-1 auto-cascade eligibility (event-expense-settlement-sync).
   * Deliberately `false` on every BASE row — none of these three members are
   * used to exercise the cascade-aware branches (that's the dedicated
   * `CREDITOR_ROWS` fixture below); keeping BASE unchanged here preserves every
   * pre-existing assertion in this file (the plain, non-cascade toast copy; the
   * owner's row falling into the ineligible-creditor branch, which still shows
   * no `switch`, matching "RendersOnlyForOwingMembers" below).
   */
  isEligibleForAutoCascade: boolean;
}

/** Owner-rep is owed 500.000; An owes 300.000; Cũ (deleted) owes 200.000. Two
 *  owing members; the row set sums to zero on balance. */
const BASE: BaseRow[] = [
  {
    memberUuid: "m-owner",
    memberName: "Bạn (chủ sổ)",
    isOwnerRepresentative: true,
    isDeleted: false,
    advanced: 500000,
    owed: 0,
    balance: 500000,
    isEligibleForAutoCascade: false,
  },
  {
    // Advanced 100.000 so `owed` (400.000) is distinct from the balance/outstanding
    // magnitude (300.000) — lets the D2 assertions target `owed` unambiguously.
    memberUuid: "m-1",
    memberName: "An Nguyễn",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 100000,
    owed: 400000,
    balance: -300000,
    isEligibleForAutoCascade: false,
  },
  {
    memberUuid: "m-2",
    memberName: "Cũ",
    isOwnerRepresentative: false,
    isDeleted: true,
    advanced: 0,
    owed: 200000,
    balance: -200000,
    isEligibleForAutoCascade: false,
  },
];

let settled: Set<string>;
let isClosed: boolean;

/** Build the overlay verbatim (outstanding net-driven; totals summed) exactly as
 *  the backend does, so the refetch reflects the mutated settled set. */
function balancePayload() {
  const rows = BASE.map((r) => {
    const marked = settled.has(r.memberUuid);
    const outstanding = r.balance < 0 && !marked ? -r.balance : 0;
    // event-expense-settlement-sync M2: this fixture only ever exercises the
    // fully-settled/fully-unsettled cases (no partial-credit scenario), so
    // `clearedAmount`/`settlementStatus` mirror `outstanding`'s own derivation
    // exactly — mechanical fixture completion, not new test authorship.
    const clearedAmount = r.balance < 0 && marked ? -r.balance : 0;
    const settlementStatus = r.balance < 0 && marked ? "Settled" : "Unsettled";
    return {
      ...r,
      outstanding,
      isSettled: marked,
      settledAt: null,
      clearedAmount,
      settlementStatus,
    };
  });
  return {
    eventUuid: UUID,
    eventName: "Đà Lạt",
    isClosed,
    rows,
    totalOutstanding: rows.reduce((s, r) => s + r.outstanding, 0),
    owingMemberCount: rows.filter((r) => r.outstanding > 0).length,
    settledMemberCount: rows.filter((r) => r.balance < 0 && r.isSettled).length,
    partiallySettledMemberCount: 0,
  };
}

function installBalanceStore() {
  settled = new Set();
  isClosed = false;
  server.use(
    http.get(`*/api/v1/events/${UUID}/balance`, () => ok(balancePayload())),
    http.put(
      `*/api/v1/events/${UUID}/members/:memberUuid/settled`,
      async ({ request, params }) => {
        const body = (await request.json()) as { isSettled?: boolean };
        if (body.isSettled) settled.add(String(params.memberUuid));
        else settled.delete(String(params.memberUuid));
        return ok({ message: "OK" });
      },
    ),
  );
}

// --- Creditor-row affordance fixture (event-expense-settlement-sync, M1-R2) ---
// A dedicated event/store, separate from BASE/UUID above, so these dedicated
// eligibility scenarios never perturb the pre-existing BASE assertions (switch
// counts, footer X-of-Y counts, plain-toast copy) that hardcode BASE's shape.
const CREDITOR_UUID = "ev-creditor";

interface CreditorRow {
  memberUuid: string;
  memberName: string;
  isOwnerRepresentative: boolean;
  isDeleted: boolean;
  advanced: number;
  owed: number;
  balance: number;
  isEligibleForAutoCascade: boolean;
}

/** One row per M1-R2 branch: eligible creditor, ineligible (gross-mixed)
 *  creditor, true net-zero, and an eligible DEBTOR (cascade isn't creditor-only). */
const CREDITOR_ROWS: CreditorRow[] = [
  {
    memberUuid: "cm-eligible-creditor",
    memberName: "Đông Vũ",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 150000,
    owed: 0,
    balance: 150000,
    isEligibleForAutoCascade: true,
  },
  {
    memberUuid: "cm-ineligible-creditor",
    memberName: "Giang Phạm",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 150000,
    owed: 50000,
    balance: 100000,
    isEligibleForAutoCascade: false,
  },
  {
    memberUuid: "cm-net-zero",
    memberName: "Hà Đặng",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 100000,
    owed: 100000,
    balance: 0,
    isEligibleForAutoCascade: false,
  },
  {
    memberUuid: "cm-eligible-debtor",
    memberName: "Khang Bùi",
    isOwnerRepresentative: false,
    isDeleted: false,
    advanced: 0,
    owed: 200000,
    balance: -200000,
    isEligibleForAutoCascade: true,
  },
];

let creditorSettled: Set<string>;

function creditorBalancePayload() {
  const rows = CREDITOR_ROWS.map((r) => {
    const marked = creditorSettled.has(r.memberUuid);
    const outstanding = r.balance < 0 && !marked ? -r.balance : 0;
    const clearedAmount = r.balance < 0 && marked ? -r.balance : 0;
    const settlementStatus = r.balance < 0 && marked ? "Settled" : "Unsettled";
    return {
      ...r,
      outstanding,
      isSettled: marked,
      settledAt: null,
      clearedAmount,
      settlementStatus,
    };
  });
  return {
    eventUuid: CREDITOR_UUID,
    eventName: "Nha Trang",
    isClosed: false,
    rows,
    totalOutstanding: rows.reduce((s, r) => s + r.outstanding, 0),
    owingMemberCount: rows.filter((r) => r.outstanding > 0).length,
    settledMemberCount: rows.filter((r) => r.balance < 0 && r.isSettled).length,
    partiallySettledMemberCount: 0,
  };
}

function installCreditorStore() {
  creditorSettled = new Set();
  server.use(
    http.get(`*/api/v1/events/${CREDITOR_UUID}/balance`, () =>
      ok(creditorBalancePayload()),
    ),
    http.put(
      `*/api/v1/events/${CREDITOR_UUID}/members/:memberUuid/settled`,
      async ({ request, params }) => {
        const body = (await request.json()) as { isSettled?: boolean };
        if (body.isSettled) creditorSettled.add(String(params.memberUuid));
        else creditorSettled.delete(String(params.memberUuid));
        return ok({ message: "OK" });
      },
    ),
  );
}

function renderCreditorTable() {
  return renderWithProviders(<EventBalanceTable uuid={CREDITOR_UUID} />, {
    queryClient,
  });
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-membersettled-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-membersettled-t",
    refreshTokenExpiresAt: future,
    user: { username: "membersettled", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function renderTable() {
  return renderWithProviders(<EventBalanceTable uuid={UUID} />, { queryClient });
}

function rowFor(name: RegExp): HTMLElement {
  return screen.getByRole("rowheader", { name }).closest("tr") as HTMLElement;
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
  installBalanceStore();
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("EventBalanceTable overlay (Layer B)", () => {
  it("EventBalanceTable_Overlay_RendersOutstandingStatusAndSummary", async () => {
    renderTable();
    await screen.findByRole("rowheader", { name: /An Nguyễn/ });

    // Còn nợ column: each owing member's outstanding via vi-VN money grouping
    // (targeted by the outstanding cell's testid — owed/balance also carry digits).
    const an = rowFor(/An Nguyễn/);
    expect(within(an).getByTestId("outstanding-amount")).toHaveTextContent(
      /300\.000/,
    );
    const cu = rowFor(/Cũ/);
    expect(within(cu).getByTestId("outstanding-amount")).toHaveTextContent(
      /200\.000/,
    );

    // Color-independent status: the owing rows carry the "Chưa trả" WORD
    // (renamed from "Còn nợ", OQ6 — the amount-column header keeps that wording).
    expect(within(an).getAllByText("Chưa trả").length).toBeGreaterThanOrEqual(1);

    // The footer summary reads the API totals verbatim (X-of-Y + total còn nợ).
    const totalRow = screen.getByText("Tổng").closest("tr") as HTMLElement;
    expect(
      within(totalRow).getByText(/Đã trả 0\/2 thành viên/),
    ).toBeInTheDocument();
  });

  it("EventBalanceTable_Toggle_RendersOnlyForOwingMembers", async () => {
    renderTable();
    await screen.findByRole("rowheader", { name: /An Nguyễn/ });

    // Exactly the two owing members (An, Cũ) get a toggle; the owed owner-rep does not.
    const switches = screen.getAllByRole("switch");
    expect(switches).toHaveLength(2);

    // The owed member's status cell shows a muted "—", not a control.
    const owner = rowFor(/Bạn \(chủ sổ\)/);
    expect(
      within(owner).queryByRole("switch"),
    ).not.toBeInTheDocument();
  });

  it("EventBalanceTable_SoftDeletedOwingMember_RendersOverlayAndToggle", async () => {
    renderTable();
    await screen.findByRole("rowheader", { name: /Cũ/ });
    const cu = rowFor(/Cũ/);
    // The soft-deleted owing member still renders its overlay + an enabled toggle.
    expect(within(cu).getByText("(đã xóa)")).toBeInTheDocument();
    expect(within(cu).getByTestId("outstanding-amount")).toHaveTextContent(
      /200\.000/,
    );
    expect(
      within(cu).getByRole("switch", { name: "Trạng thái đã trả của Cũ" }),
    ).toBeEnabled();
  });
});

describe("MemberSettledToggle write + reconcile", () => {
  it("MemberSettledToggle_Click_PutsToPerMemberRouteThenToasts", async () => {
    let path = "";
    let body: { isSettled?: boolean } | null = null;
    server.use(
      http.put(
        `*/api/v1/events/${UUID}/members/:memberUuid/settled`,
        async ({ request }) => {
          path = new URL(request.url).pathname;
          body = (await request.json()) as typeof body;
          return ok({ message: "OK" });
        },
      ),
    );
    const user = userEvent.setup();
    renderTable();

    await user.click(
      await screen.findByRole("switch", {
        name: "Trạng thái đã trả của An Nguyễn",
      }),
    );

    expect(
      await screen.findByText("Đã đánh dấu thành viên là đã trả."),
    ).toBeInTheDocument();
    expect(path).toBe(`/api/v1/events/${UUID}/members/m-1/settled`);
    expect(body).toEqual({ isSettled: true });
  });

  it("MemberSettledToggle_MarkSettled_ReconcilesOutstandingToZeroAndFlipsStatus", async () => {
    const user = userEvent.setup();
    renderTable();
    await screen.findByRole("rowheader", { name: /An Nguyễn/ });

    const an = rowFor(/An Nguyễn/);
    expect(within(an).getByTestId("outstanding-amount")).toHaveTextContent(
      /300\.000/,
    );

    await user.click(
      within(an).getByRole("switch", { name: "Trạng thái đã trả của An Nguyễn" }),
    );

    // Refetch-based: after the balance refetch, An's outstanding drops to the
    // muted "—", the toggle reads checked, and the status flips to "Đã trả".
    await waitFor(() => {
      const row = rowFor(/An Nguyễn/);
      expect(
        within(row).getByRole("switch", {
          name: "Trạng thái đã trả của An Nguyễn",
        }),
      ).toHaveAttribute("aria-checked", "true");
    });
    const settledRow = rowFor(/An Nguyễn/);
    expect(
      within(settledRow).getByTestId("outstanding-amount"),
    ).toHaveTextContent("—");
    expect(within(settledRow).getAllByText("Đã trả").length).toBeGreaterThanOrEqual(1);

    // The footer summary count reconciles: 1 of 2 members settled.
    const totalRow = screen.getByText("Tổng").closest("tr") as HTMLElement;
    expect(
      within(totalRow).getByText(/Đã trả 1\/2 thành viên/),
    ).toBeInTheDocument();
  });

  it("EventBalanceTable_SettledFlip_LeavesBalanceColumnsAndSumToZeroUnchanged", async () => {
    const user = userEvent.setup();
    renderTable();
    await screen.findByRole("rowheader", { name: /An Nguyễn/ });

    // Capture the PURE balance surfaces before the settled flip (D2).
    const totalRowBefore = screen.getByText("Tổng").closest("tr") as HTMLElement;
    expect(within(totalRowBefore).getByText("đã cân bằng")).toBeInTheDocument();
    expect(
      within(totalRowBefore).getByText("Cân bằng luôn bằng 0"),
    ).toBeInTheDocument();

    await user.click(
      screen.getByRole("switch", { name: "Trạng thái đã trả của An Nguyễn" }),
    );
    await waitFor(() =>
      expect(
        screen.getByRole("switch", { name: "Trạng thái đã trả của An Nguyễn" }),
      ).toHaveAttribute("aria-checked", "true"),
    );

    // The advanced/owed/balance columns + the sum-to-zero footer are untouched by
    // the overlay flip: An's owed (400.000, distinct from balance/outstanding).
    const an = rowFor(/An Nguyễn/);
    expect(within(an).getByText(/400\.000/)).toBeInTheDocument(); // owed, unchanged
    const totalRowAfter = screen.getByText("Tổng").closest("tr") as HTMLElement;
    expect(within(totalRowAfter).getByText("đã cân bằng")).toBeInTheDocument();
    expect(
      within(totalRowAfter).getByText("Cân bằng luôn bằng 0"),
    ).toBeInTheDocument();
    // advanced total == owed total (600.000 each) — the invariant is intact.
    expect(within(totalRowAfter).getAllByText(/600\.000/).length).toBe(2);
  });

  it("MemberSettledToggle_Error3000_ToastsVerbatimStaleMiss", async () => {
    server.use(
      http.put(`*/api/v1/events/${UUID}/members/:memberUuid/settled`, () =>
        fail(3000, "Không tìm thấy thành viên.", 404),
      ),
    );
    const user = userEvent.setup();
    renderTable();

    await user.click(
      await screen.findByRole("switch", {
        name: "Trạng thái đã trả của An Nguyễn",
      }),
    );

    expect(
      await screen.findByText("Không tìm thấy thành viên."),
    ).toBeInTheDocument();
  });

  it("MemberSettledToggle_MarkSettled_AlsoInvalidatesExpensesCache", async () => {
    // The M1.2 regression: useSetMemberSettled's onSuccess used to reach only
    // `eventsKeys` — a member-level settle can now cascade Share.isSettled
    // across N expenses (Direction 1), so the expenses cache must refetch too.
    // Mount a live subscriber on the expenses list (mirrors the
    // `useEvents.test.tsx` counters+Probe pattern) so `expensesKeys.all`'s
    // invalidation has an ACTIVE query to refetch, and count the GETs it fires.
    let expensesRequests = 0;
    server.use(
      http.get("*/api/v1/expenses", () => {
        expensesRequests += 1;
        return ok([]);
      }),
    );
    function ExpensesProbe() {
      useExpensesQuery({});
      return null;
    }
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <EventBalanceTable uuid={UUID} />
        <ExpensesProbe />
      </>,
      { queryClient },
    );

    await screen.findByRole("rowheader", { name: /An Nguyễn/ });
    await waitFor(() => expect(expensesRequests).toBe(1));

    await user.click(
      screen.getByRole("switch", { name: "Trạng thái đã trả của An Nguyễn" }),
    );

    // Before the M1.2 fix this never fired a second GET — the fix's whole point
    // is that expensesKeys.all is now reached by the mutation's onSuccess.
    await waitFor(() => expect(expensesRequests).toBeGreaterThanOrEqual(2));
  });
});

describe("MemberSettledToggle creditor-row affordance (event-expense-settlement-sync M1-R2)", () => {
  beforeEach(() => {
    installCreditorStore();
  });

  /**
   * The status column is the LAST `cell` in the row (member name is a
   * `rowheader`, not a `cell`) — scoped lookup because the "Còn nợ" (outstanding)
   * column ALSO renders a muted "—" for any `balance >= 0` row, so an unscoped
   * `getByText("—")` on the whole row is ambiguous (multiple matches) or
   * misleading (always finds one, regardless of the status column's own shape).
   */
  function statusCellFor(row: HTMLElement): HTMLElement {
    const cells = within(row).getAllByRole("cell");
    return cells[cells.length - 1];
  }

  it("MemberSettledToggle_CreditorRow_RendersAffordanceWhenEligible", async () => {
    renderCreditorTable();
    await screen.findByRole("rowheader", { name: /Đông Vũ/ });

    const status = statusCellFor(rowFor(/Đông Vũ/));
    // Not the muted "—" — the same toggle an owing row gets.
    expect(
      within(status).getByRole("switch", {
        name: "Trạng thái đã trả của Đông Vũ",
      }),
    ).toBeInTheDocument();
    expect(within(status).queryByText("—")).not.toBeInTheDocument();
    // Plus the eligibility HelpHint (short accessible name, distinct from the
    // longer bubble body — the two are no longer identical, see HelpHint's
    // own doc comment on `label` vs `children`).
    expect(
      within(status).getByRole("button", {
        name: "Đánh dấu đã trả sẽ làm gì?",
      }),
    ).toBeInTheDocument();
  });

  it("MemberSettledToggle_CreditorRow_IneligibleGrossMixed_HidesToggleShowsHint", async () => {
    renderCreditorTable();
    await screen.findByRole("rowheader", { name: /Giang Phạm/ });

    const status = statusCellFor(rowFor(/Giang Phạm/));
    // No toggle at all — OQ2 locked "hide", not "disable".
    expect(within(status).queryByRole("switch")).not.toBeInTheDocument();
    // The muted "—" is still shown…
    expect(within(status).getByText("—")).toBeInTheDocument();
    // …replaced-in-spirit by a HelpHint explaining why (not folded silently),
    // its short accessible name distinct from the longer bubble body.
    expect(
      within(status).getByRole("button", {
        name: "Vì sao không có nút đánh dấu đã trả?",
      }),
    ).toBeInTheDocument();
  });

  it("MemberSettledToggle_CreditorRow_NetZero_UnchangedMutedDash", async () => {
    renderCreditorTable();
    await screen.findByRole("rowheader", { name: /Hà Đặng/ });

    const status = statusCellFor(rowFor(/Hà Đặng/));
    // A true net-zero balance never gets a toggle…
    expect(within(status).queryByRole("switch")).not.toBeInTheDocument();
    // …stays the plain muted "—"…
    expect(within(status).getByText("—")).toBeInTheDocument();
    // …and — the regression this test locks in — is NOT folded into the
    // ineligible-creditor branch: no HelpHint button renders for it either.
    expect(within(status).queryByRole("button")).not.toBeInTheDocument();
  });

  it("MemberSettledToggle_EligibleCascade_ToastCommunicatesCascade", async () => {
    const user = userEvent.setup();
    renderCreditorTable();
    await screen.findByRole("rowheader", { name: /Đông Vũ/ });

    // Eligible creditor → cascade-aware toast (Step M1.5/OQ5).
    await user.click(
      screen.getByRole("switch", { name: "Trạng thái đã trả của Đông Vũ" }),
    );
    expect(
      await screen.findByText(
        "Đã đánh dấu Đông Vũ đã trả — các phần gánh liên quan của họ trong đợt cũng đã được tự động đánh dấu đã trả.",
      ),
    ).toBeInTheDocument();

    // Eligible DEBTOR → the same cascade-aware toast (cascade communication
    // isn't creditor-only — it's gated on eligibility, not polarity).
    await user.click(
      screen.getByRole("switch", { name: "Trạng thái đã trả của Khang Bùi" }),
    );
    expect(
      await screen.findByText(
        "Đã đánh dấu Khang Bùi đã trả — các phần gánh liên quan của họ trong đợt cũng đã được tự động đánh dấu đã trả.",
      ),
    ).toBeInTheDocument();
  });

  it("MemberSettledToggle_IneligibleOwingRow_KeepsPlainToast", async () => {
    // Regression companion to the test above: an ineligible row (BASE's An
    // Nguyễn, isEligibleForAutoCascade: false) keeps today's plain, non-cascade
    // toast — nothing extra happened, so nothing extra is communicated.
    const user = userEvent.setup();
    renderTable();

    await user.click(
      await screen.findByRole("switch", {
        name: "Trạng thái đã trả của An Nguyễn",
      }),
    );

    expect(
      await screen.findByText("Đã đánh dấu thành viên là đã trả."),
    ).toBeInTheDocument();
  });
});

describe("EventBalanceTable closed-event settled exception (R6)", () => {
  it("EventBalanceTable_ClosedEvent_PerMemberToggleStaysEnabled", async () => {
    isClosed = true; // the balance store now reports a closed event
    const user = userEvent.setup();
    renderTable();

    const toggle = await screen.findByRole("switch", {
      name: "Trạng thái đã trả của An Nguyễn",
    });
    // The sole write allowed on a closed event: the toggle is enabled + works.
    expect(toggle).toBeEnabled();
    await user.click(toggle);
    await waitFor(() =>
      expect(
        screen.getByRole("switch", {
          name: "Trạng thái đã trả của An Nguyễn",
        }),
      ).toHaveAttribute("aria-checked", "true"),
    );
  });
});
