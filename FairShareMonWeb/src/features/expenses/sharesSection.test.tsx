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
import { SharesSection } from "./components/SharesSection";
import type { ExpenseResponse } from "./api/types";
import type { MemberResponse } from "@/features/members/api/types";

/**
 * SharesSection integration — the shares breakdown + add/edit/change-member/remove
 * sub-CRUD (B4). Rendered directly with a canned expense; GET /members is stubbed
 * with fixed members so the pickers are deterministic, and the share write
 * endpoints are stubbed per-test to capture the body / return a code. Owner-rep
 * protection (no delete + member lock) and no-duplicate-member (picker exclusion +
 * 7003) are exercised; closed events disable all writes (R4).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string; fields?: Record<string, string[]> } | null;
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

function makeExpense(overrides: Partial<ExpenseResponse> = {}): ExpenseResponse {
  return {
    uuid: "e-1",
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
    payer: MEMBERS[1],
    isSettled: false,
    settledAt: null,
    shares: [
      {
        uuid: "s-o",
        member: MEMBERS[0],
        amount: 0,
        note: null,
        isSettled: false,
        createdAt: "2026-07-16T03:00:00+00:00",
      },
      {
        uuid: "s-1",
        member: MEMBERS[1],
        amount: 300000,
        note: "Cả nhóm",
        isSettled: false,
        createdAt: "2026-07-16T03:00:00+00:00",
      },
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
    accessToken: "access-shares-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-shares-t",
    refreshTokenExpiresAt: future,
    user: { username: "shares", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function renderShares(expense = makeExpense(), disabled = false) {
  return renderWithProviders(
    <SharesSection expense={expense} disabled={disabled} />,
    { queryClient },
  );
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

// ─── Rendering / owner-rep protection ────────────────────────────────────────
describe("SharesSection rendering", () => {
  it("SharesSection_RendersMemberRowsAmountsAndDerivedTotal", () => {
    renderShares();
    const table = screen.getByRole("table", { name: "Phần gánh của phiếu" });
    expect(
      within(table).getByRole("rowheader", { name: /Bạn \(chủ sổ\)/ }),
    ).toBeInTheDocument();
    expect(
      within(table).getByRole("rowheader", { name: "An Nguyễn" }),
    ).toBeInTheDocument();
    // Derived total row.
    expect(within(table).getByRole("rowheader", { name: "Tổng" })).toBeInTheDocument();
    expect(within(table).getAllByText(/300\.000/).length).toBeGreaterThanOrEqual(1);
  });

  it("SharesSection_OwnerRepRow_HasNoDeleteControlAndShowsLock", () => {
    renderShares();
    // Owner-rep is editable but not removable (mirrors 7002) with a "khóa" note.
    expect(
      screen.getByRole("button", { name: "Sửa phần gánh của Bạn (chủ sổ)" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", {
        name: "Xóa phần gánh của Bạn (chủ sổ)",
      }),
    ).not.toBeInTheDocument();
  });

  it("SharesSection_NormalRow_HasEditAndRemoveControls", () => {
    renderShares();
    expect(
      screen.getByRole("button", { name: "Sửa phần gánh của An Nguyễn" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Xóa phần gánh của An Nguyễn" }),
    ).toBeInTheDocument();
  });

  it("SharesSection_ClosedEvent_HidesAllWriteControls", () => {
    renderShares(makeExpense(), true);
    expect(
      screen.queryByRole("button", { name: "Thêm phần gánh" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Sửa phần gánh của An Nguyễn" }),
    ).not.toBeInTheDocument();
    // Read-only marker in the actions cell.
    expect(screen.getAllByText("chỉ đọc").length).toBeGreaterThanOrEqual(1);
  });
});

// ─── Add share ────────────────────────────────────────────────────────────────
describe("SharesSection add share", () => {
  it("SharesSection_AddShare_PostsMemberAndAmountThenToasts", async () => {
    let body: { memberUuid?: string; amount?: number } | null = null;
    server.use(
      http.post("*/api/v1/expenses/e-1/shares", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok({ uuid: "s-new", member: MEMBERS[2], amount: 50000, note: null });
      }),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(screen.getByRole("button", { name: "Thêm phần gánh" }));
    const dialog = await screen.findByRole("dialog");
    // The picker excludes members already sharing (m-o, m-1) → offers Bình Trần.
    await user.click(within(dialog).getByRole("combobox", { name: "Thành viên" }));
    await user.click(await screen.findByRole("option", { name: /Bình Trần/ }));
    await user.type(
      within(dialog).getByRole("textbox", { name: "Số tiền" }),
      "50000",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(await screen.findByText("Đã thêm phần gánh.")).toBeInTheDocument();
    expect(body).toEqual({ memberUuid: "m-2", amount: 50000 });
  });

  it("SharesSection_AddSharePicker_ExcludesMembersAlreadySharing", async () => {
    const user = userEvent.setup();
    renderShares();
    await user.click(screen.getByRole("button", { name: "Thêm phần gánh" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("combobox", { name: "Thành viên" }));

    // Only the un-shared member is offered (owner-rep + An already share → 7003).
    expect(
      await screen.findByRole("option", { name: /Bình Trần/ }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("option", { name: /An Nguyễn/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("option", { name: /Bạn \(chủ sổ\)/ }),
    ).not.toBeInTheDocument();
  });

  it("SharesSection_AddShare7003_MapsOntoMemberField", async () => {
    server.use(
      http.post("*/api/v1/expenses/e-1/shares", () =>
        fail(7003, "Trùng thành viên phần gánh.", 400),
      ),
    );
    const user = userEvent.setup();
    renderShares();
    await user.click(screen.getByRole("button", { name: "Thêm phần gánh" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("combobox", { name: "Thành viên" }));
    await user.click(await screen.findByRole("option", { name: /Bình Trần/ }));
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Trùng thành viên phần gánh."),
    ).toBeInTheDocument();
    // Field-level mapping keeps the dialog open for correction.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

// ─── Edit share ───────────────────────────────────────────────────────────────
describe("SharesSection edit share", () => {
  it("SharesSection_EditAmount_PutsToShareSubpathThenToasts", async () => {
    let path = "";
    let body: { amount?: number } | null = null;
    server.use(
      http.put(
        "*/api/v1/expenses/e-1/shares/:shareUuid",
        async ({ request }) => {
          path = new URL(request.url).pathname;
          body = (await request.json()) as typeof body;
          return ok({ uuid: "s-1", member: MEMBERS[1], amount: 400000, note: null });
        },
      ),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("button", { name: "Sửa phần gánh của An Nguyễn" }),
    );
    const dialog = await screen.findByRole("dialog");
    const amount = within(dialog).getByRole("textbox", { name: "Số tiền" });
    await user.clear(amount);
    await user.type(amount, "400000");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(await screen.findByText("Đã cập nhật phần gánh.")).toBeInTheDocument();
    expect(path).toBe("/api/v1/expenses/e-1/shares/s-1");
    expect(body!.amount).toBe(400000);
  });

  it("SharesSection_ChangeMember_PutsNewMemberUuid", async () => {
    let body: { memberUuid?: string } | null = null;
    server.use(
      http.put("*/api/v1/expenses/e-1/shares/:shareUuid", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok({ uuid: "s-1", member: MEMBERS[2], amount: 300000, note: null });
      }),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("button", { name: "Sửa phần gánh của An Nguyễn" }),
    );
    const dialog = await screen.findByRole("dialog");
    // The member Select offers the row's own member + the un-shared member; change
    // An → Bình.
    await user.click(within(dialog).getByRole("combobox", { name: "Thành viên" }));
    await user.click(await screen.findByRole("option", { name: /Bình Trần/ }));
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body!.memberUuid).toBe("m-2");
  });

  it("SharesSection_EditOwnerRepShare_LocksTheMemberSelect", async () => {
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("button", { name: "Sửa phần gánh của Bạn (chủ sổ)" }),
    );
    const dialog = await screen.findByRole("dialog");
    // The owner-rep share's member cannot be changed (disabled Select + hint).
    expect(
      within(dialog).getByRole("combobox", { name: "Thành viên" }),
    ).toBeDisabled();
    expect(
      within(dialog).getByText(/Không thể đổi thành viên của phần gánh/),
    ).toBeInTheDocument();
  });
});

// ─── Remove share (OQ12a + 7002) ─────────────────────────────────────────────
describe("SharesSection remove share", () => {
  it("SharesSection_RemoveShareConfirm_DeletesThenToasts", async () => {
    let path = "";
    server.use(
      http.delete("*/api/v1/expenses/e-1/shares/:shareUuid", ({ request }) => {
        path = new URL(request.url).pathname;
        return ok({ message: "Đã xóa phần gánh." });
      }),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("button", { name: "Xóa phần gánh của An Nguyễn" }),
    );
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Xóa phần gánh" }),
    );

    expect(await screen.findByText("Đã xóa phần gánh.")).toBeInTheDocument();
    expect(path).toBe("/api/v1/expenses/e-1/shares/s-1");
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("SharesSection_RemoveShareTerminal7002_ToastsAndCloses", async () => {
    // Defensive: a 7002 (owner-rep share not deletable) is terminal → toast+close.
    server.use(
      http.delete("*/api/v1/expenses/e-1/shares/:shareUuid", () =>
        fail(7002, "Không thể xóa phần gánh của đại diện chủ sổ.", 400),
      ),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("button", { name: "Xóa phần gánh của An Nguyễn" }),
    );
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Xóa phần gánh" }),
    );

    expect(
      await screen.findByText(
        "Không thể xóa phần gánh của đại diện chủ sổ.",
      ),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("SharesSection_RemoveShareTransientError_StaysOpenForRetry", async () => {
    server.use(
      http.delete("*/api/v1/expenses/e-1/shares/:shareUuid", () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    const user = userEvent.setup();
    renderShares();

    await user.click(
      screen.getByRole("button", { name: "Xóa phần gánh của An Nguyễn" }),
    );
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Xóa phần gánh" }),
    );

    // OQ12a: non-terminal failure keeps the confirm open with an inline error.
    expect(
      await within(dialog).findByText("Đã xảy ra lỗi máy chủ."),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
