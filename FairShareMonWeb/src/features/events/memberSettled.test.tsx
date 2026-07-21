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
  },
  {
    memberUuid: "m-2",
    memberName: "Cũ",
    isOwnerRepresentative: false,
    isDeleted: true,
    advanced: 0,
    owed: 200000,
    balance: -200000,
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
    return { ...r, outstanding, isSettled: marked, settledAt: null };
  });
  return {
    eventUuid: UUID,
    eventName: "Đà Lạt",
    isClosed,
    rows,
    totalOutstanding: rows.reduce((s, r) => s + r.outstanding, 0),
    owingMemberCount: rows.filter((r) => r.outstanding > 0).length,
    settledMemberCount: rows.filter((r) => r.balance < 0 && r.isSettled).length,
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

    // Color-independent status: the owing rows carry the "Còn nợ" WORD.
    expect(within(an).getAllByText("Còn nợ").length).toBeGreaterThanOrEqual(1);

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
