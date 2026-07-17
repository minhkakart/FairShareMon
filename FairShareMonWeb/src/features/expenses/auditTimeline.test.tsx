import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { AuditTimeline } from "./components/AuditTimeline";
import { ExpenseAuditSection } from "./components/ExpenseAuditSection";
import type { AuditLogResponse } from "./api/types";

/**
 * AuditTimeline — the resilient field-diff renderer (OQ11a): Create shows the new
 * snapshot; Update shows only changed fields (before → after); Delete shows the
 * removed snapshot; money renders via Money, tags as chips, and unknown fields
 * fall back to a raw key/value line. ExpenseAuditSection wraps it with loading /
 * error / empty states. Presentational — rendered directly against the providers.
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

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-audit-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-audit-t",
    refreshTokenExpiresAt: future,
    user: { username: "audit", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

const CREATE_EXPENSE: AuditLogResponse = {
  uuid: "al-1",
  entityType: "Expense",
  entityUuid: "e-1",
  action: "Create",
  before: null,
  after: {
    uuid: "e-1",
    name: "Thuê xe",
    description: null,
    expenseTime: "2026-07-16T03:00:00+00:00",
    payerMemberUuid: "m-1",
    payerMemberName: "An Nguyễn",
    categoryUuid: "c-1",
    categoryName: "Đi lại",
    tags: [{ uuid: "t-1", name: "Du lịch" }],
    isSettled: false,
  },
  createdAt: "2026-07-16T03:00:00+00:00",
};

const UPDATE_SHARE: AuditLogResponse = {
  uuid: "al-2",
  entityType: "Share",
  entityUuid: "s-1",
  action: "Update",
  before: { uuid: "s-1", memberUuid: "m-1", memberName: "An Nguyễn", amount: 100000, note: "x" },
  after: { uuid: "s-1", memberUuid: "m-1", memberName: "An Nguyễn", amount: 250000, note: "x" },
  createdAt: "2026-07-16T04:00:00+00:00",
};

const DELETE_EXPENSE: AuditLogResponse = {
  uuid: "al-3",
  entityType: "Expense",
  entityUuid: "e-1",
  action: "Delete",
  before: {
    uuid: "e-1",
    name: "Thuê xe",
    payerMemberName: "An Nguyễn",
    categoryName: "Đi lại",
    tags: [],
    isSettled: true,
  },
  after: null,
  createdAt: "2026-07-16T05:00:00+00:00",
};

const CREATE_SHARE_UNKNOWN: AuditLogResponse = {
  uuid: "al-4",
  entityType: "Share",
  entityUuid: "s-2",
  action: "Create",
  before: null,
  after: {
    uuid: "s-2",
    memberUuid: "m-2",
    memberName: "Bình Trần",
    amount: 5000,
    note: null,
    // A field the label map doesn't know → must fall back to a raw key/value line.
    mysteryField: "XYZ",
  },
  createdAt: "2026-07-16T06:00:00+00:00",
};

function liContaining(text: string): HTMLElement {
  const node = screen.getByText(text).closest("li");
  if (!node) throw new Error(`No <li> containing "${text}"`);
  return node as HTMLElement;
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

describe("AuditTimeline", () => {
  it("AuditTimeline_RendersAsOrderedList", () => {
    renderWithProviders(<AuditTimeline entries={[CREATE_EXPENSE]} />);
    // The timeline is an accessible ordered list of entries.
    const items = screen.getAllByRole("listitem");
    expect(items).toHaveLength(1);
    expect(items[0].closest("ol")).toBeInTheDocument();
  });

  it("AuditTimeline_CreateEntry_ShowsAfterSnapshotWithTagsChipsAndMoney", () => {
    renderWithProviders(<AuditTimeline entries={[CREATE_EXPENSE]} />);
    const item = liContaining("Thuê xe");
    // Create shows the new snapshot: action badge + entity + known fields.
    expect(within(item).getByText("Tạo")).toBeInTheDocument();
    expect(within(item).getByText("Phiếu chi tiêu")).toBeInTheDocument();
    expect(within(item).getByText("An Nguyễn")).toBeInTheDocument();
    expect(within(item).getByText("Đi lại")).toBeInTheDocument();
    // Tag rendered as a chip; settled state as color-independent text.
    expect(within(item).getByText("Du lịch")).toBeInTheDocument();
    expect(within(item).getByText("Chưa trả")).toBeInTheDocument();
  });

  it("AuditTimeline_UpdateEntry_ShowsOnlyChangedFieldsBeforeToAfter", () => {
    renderWithProviders(<AuditTimeline entries={[UPDATE_SHARE]} />);
    const item = liContaining("Cập nhật");
    // Only "Số tiền" changed → before 100.000 → after 250.000 (money via Money).
    expect(within(item).getByText("Số tiền")).toBeInTheDocument();
    expect(within(item).getByText(/100\.000/)).toBeInTheDocument();
    expect(within(item).getByText(/250\.000/)).toBeInTheDocument();
    // The "changed to" arrow carries an accessible label.
    expect(within(item).getByLabelText("đổi thành")).toBeInTheDocument();
    // Unchanged fields (member, note) are NOT shown.
    expect(within(item).queryByText("Thành viên")).not.toBeInTheDocument();
    expect(within(item).queryByText("Ghi chú")).not.toBeInTheDocument();
  });

  it("AuditTimeline_DeleteEntry_ShowsRemovedSnapshot", () => {
    renderWithProviders(<AuditTimeline entries={[DELETE_EXPENSE]} />);
    const item = liContaining("Xóa");
    expect(within(item).getByText("Phiếu chi tiêu")).toBeInTheDocument();
    // The removed snapshot's fields are shown (name, payer, category).
    expect(within(item).getByText("Thuê xe")).toBeInTheDocument();
    expect(within(item).getByText("An Nguyễn")).toBeInTheDocument();
    expect(within(item).getByText("Đi lại")).toBeInTheDocument();
  });

  it("AuditTimeline_UnknownField_FallsBackToRawKeyValue", () => {
    renderWithProviders(<AuditTimeline entries={[CREATE_SHARE_UNKNOWN]} />);
    const item = liContaining("Bình Trần");
    // The known amount still renders via Money…
    expect(within(item).getByText(/5\.000/)).toBeInTheDocument();
    // …and the unknown field degrades to a raw key/value line (never breaks).
    expect(within(item).getByText("mysteryField")).toBeInTheDocument();
    expect(within(item).getByText("XYZ")).toBeInTheDocument();
  });

  it("AuditTimeline_MultipleEntries_RenderInProvidedOrder", () => {
    renderWithProviders(
      <AuditTimeline entries={[CREATE_EXPENSE, UPDATE_SHARE, DELETE_EXPENSE]} />,
    );
    const items = screen.getAllByRole("listitem");
    expect(items).toHaveLength(3);
    // Rendered time-ascending, verbatim as returned.
    expect(within(items[0]).getByText("Tạo")).toBeInTheDocument();
    expect(within(items[1]).getByText("Cập nhật")).toBeInTheDocument();
    expect(within(items[2]).getByText("Xóa")).toBeInTheDocument();
  });
});

describe("ExpenseAuditSection", () => {
  it("ExpenseAuditSection_NoHistory_ShowsEmptyNote", async () => {
    server.use(http.get("*/api/v1/expenses/e-1/history", () => ok([])));
    renderWithProviders(<ExpenseAuditSection uuid="e-1" />, { queryClient });
    expect(
      await screen.findByText("Chưa có thay đổi nào."),
    ).toBeInTheDocument();
  });

  it("ExpenseAuditSection_LoadedHistory_RendersTimeline", async () => {
    server.use(
      http.get("*/api/v1/expenses/e-1/history", () => ok([CREATE_EXPENSE])),
    );
    renderWithProviders(<ExpenseAuditSection uuid="e-1" />, { queryClient });
    expect(await screen.findByText("Tạo")).toBeInTheDocument();
    // The timeline entry sits inside an ordered list.
    expect(screen.getByRole("listitem").closest("ol")).toBeInTheDocument();
  });

  it("ExpenseAuditSection_Error_ShowsInlineRetryThenRecovers", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/expenses/e-1/history", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok([CREATE_EXPENSE]);
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExpenseAuditSection uuid="e-1" />, { queryClient });

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Đã xảy ra lỗi máy chủ.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Thử lại" }));
    expect(await screen.findByText("Tạo")).toBeInTheDocument();
  });
});
