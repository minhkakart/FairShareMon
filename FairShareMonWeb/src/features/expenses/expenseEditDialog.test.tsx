import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { useState } from "react";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ExpenseEditDialog } from "./components/ExpenseEditDialog";
import type { ExpenseResponse } from "./api/types";
import type { MemberResponse } from "@/features/members/api/types";
import type { CategoryResponse } from "@/features/categories/api/types";
import type { TagResponse } from "@/features/tags/api/types";

/**
 * ExpenseEditDialog — general-info edit (B1) via a dialog. Rendered directly with
 * canned pickers + a controlled `open` so close-on-success/close-on-terminal are
 * observable. PUT /expenses/:uuid is stubbed per-test to capture the body (tag
 * full-replace) or return a code. Never touches shares.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string; fields?: Record<string, string[]> } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}
function fail(
  code: number,
  message: string,
  status: number,
  fields?: Record<string, string[]>,
) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message, fields } },
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
];
const CATEGORIES: CategoryResponse[] = [
  {
    uuid: "c-d",
    name: "Ăn uống",
    color: "#F97316",
    icon: "🍜",
    isDefault: true,
    isDeleted: false,
    createdAt: "2026-01-01T00:00:00+00:00",
  },
  {
    uuid: "c-2",
    name: "Đi lại",
    color: "#3B82F6",
    icon: "🚗",
    isDefault: false,
    isDeleted: false,
    createdAt: "2026-01-01T00:00:00+00:00",
  },
];
const TAGS: TagResponse[] = [
  { uuid: "t-du", name: "Du lịch", isDeleted: false, createdAt: "2026-01-01T00:00:00+00:00" },
  { uuid: "t-cong", name: "Công tác", isDeleted: false, createdAt: "2026-01-01T00:00:00+00:00" },
];

function makeExpense(overrides: Partial<ExpenseResponse> = {}): ExpenseResponse {
  return {
    uuid: "e-1",
    name: "Thuê xe",
    description: "Đi Đà Lạt",
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 300000,
    category: CATEGORIES[1],
    payer: MEMBERS[1],
    isSettled: false,
    settledAt: null,
    shares: [],
    tags: [TAGS[0]],
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
    accessToken: "access-edit-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-edit-t",
    refreshTokenExpiresAt: future,
    user: { username: "edit", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function Harness({ expense }: { expense: ExpenseResponse }) {
  const [open, setOpen] = useState(true);
  return (
    <ExpenseEditDialog
      expense={expense}
      members={MEMBERS}
      categories={CATEGORIES}
      tags={TAGS}
      open={open}
      onOpenChange={setOpen}
    />
  );
}

function renderEdit(expense = makeExpense()) {
  return renderWithProviders(<Harness expense={expense} />, { queryClient });
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

describe("ExpenseEditDialog", () => {
  it("ExpenseEditDialog_Open_PrefillsGeneralInfo", async () => {
    renderEdit();
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("textbox", { name: "Tên phiếu" })).toHaveValue(
      "Thuê xe",
    );
    // The pre-existing tag shows as a removable chip (tag set pre-filled).
    expect(
      within(dialog).getByRole("button", { name: "Bỏ nhãn Du lịch" }),
    ).toBeInTheDocument();
  });

  it("ExpenseEditDialog_SubmitEditedName_PutsAndClosesWithToast", async () => {
    let body: { name?: string } | null = null;
    server.use(
      http.put("*/api/v1/expenses/e-1", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok(makeExpense({ name: "Thuê ô tô" }));
      }),
    );
    const user = userEvent.setup();
    renderEdit();
    const dialog = await screen.findByRole("dialog");
    const name = within(dialog).getByRole("textbox", { name: "Tên phiếu" });
    await user.clear(name);
    await user.type(name, "Thuê ô tô");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Đã cập nhật phiếu chi tiêu."),
    ).toBeInTheDocument();
    expect(body!.name).toBe("Thuê ô tô");
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("ExpenseEditDialog_TagSet_IsFullyReplacedOnSubmit", async () => {
    let body: { tagUuids?: string[] } | null = null;
    server.use(
      http.put("*/api/v1/expenses/e-1", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok(makeExpense());
      }),
    );
    const user = userEvent.setup();
    renderEdit();
    const dialog = await screen.findByRole("dialog");

    // Remove the pre-filled "Du lịch" and add "Công tác" → the set is replaced.
    await user.click(
      within(dialog).getByRole("button", { name: "Bỏ nhãn Du lịch" }),
    );
    await user.click(within(dialog).getByRole("button", { expanded: false }));
    await user.click(
      await within(dialog).findByRole("checkbox", { name: "Công tác" }),
    );
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body!.tagUuids).toEqual(["t-cong"]);
  });

  it("ExpenseEditDialog_9001Closed_ToastsAndCloses", async () => {
    server.use(
      http.put("*/api/v1/expenses/e-1", () =>
        fail(9001, "Đợt đã chốt, không thể chỉnh sửa.", 400),
      ),
    );
    const user = userEvent.setup();
    renderEdit();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Đợt đã chốt, không thể chỉnh sửa."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("ExpenseEditDialog_6002Category_MapsOntoCategoryFieldAndKeepsOpen", async () => {
    server.use(
      http.put("*/api/v1/expenses/e-1", () =>
        fail(6002, "Danh mục không hợp lệ.", 400),
      ),
    );
    const user = userEvent.setup();
    renderEdit();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await within(dialog).findByText("Danh mục không hợp lệ."),
    ).toBeInTheDocument();
    // Field-level error keeps the form mounted for correction.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
