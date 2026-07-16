import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { MembersPage } from "./pages/MembersPage";
import type { MemberResponse } from "./api/types";

/**
 * MembersPage integration — the REAL page/hooks/dialogs against MSW at the client
 * boundary. Mutations invalidate via the singleton `queryClient`, so every render
 * uses it (and clears it per test). Each test seeds a UNIQUE username so the MSW
 * members store (seeded lazily per user) is isolated + deterministic. Copy is the
 * vi-VN default unless a spec flips the locale.
 *
 * Default seed per fresh user (owner-rep first, then A→Z; one soft-deleted):
 *   "Bạn (chủ sổ)" (owner-rep) · "An Nguyễn" · "Bình Trần" · "Cũ (đã xóa)" (deleted)
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: {
    code: number;
    message: string;
    fields?: Record<string, string[]>;
  } | null;
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

let userSeq = 0;

/** Seed an authenticated session with a fresh username → isolated MSW store. */
function seedSession(tier = "FREE"): string {
  userSeq += 1;
  const username = `mtest${userSeq}`;
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: `access-${username}-t`,
    accessTokenExpiresAt: future,
    refreshToken: `refresh-${username}-t`,
    refreshTokenExpiresAt: future,
    user: { username, tier, role: "USER" },
    profileStatus: "resolved",
  });
  return username;
}

function renderMembers() {
  return renderWithProviders(<MembersPage />, {
    initialPath: "/members",
    queryClient,
  });
}

/** The <tr> containing a member's row-header cell. */
function rowFor(name: string): HTMLElement {
  const cell = screen.getByRole("rowheader", { name });
  const row = cell.closest("tr");
  if (!row) throw new Error(`No row for ${name}`);
  return row as HTMLElement;
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

// ─── List / order / states ───────────────────────────────────────────────────
describe("MembersPage list + order", () => {
  it("MembersPage_ListLoaded_RendersOwnerRepFirstThenAlphabetical", async () => {
    renderMembers();

    await screen.findByRole("rowheader", { name: "An Nguyễn" });
    const names = screen
      .getAllByRole("rowheader")
      .map((cell) => cell.textContent);
    // Backend order rendered verbatim: owner-rep first, then A→Z (deleted hidden).
    expect(names).toEqual(["Bạn (chủ sổ)", "An Nguyễn", "Bình Trần"]);
  });

  it("MembersPage_DefaultList_HidesDeletedMembers", async () => {
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    expect(
      screen.queryByRole("rowheader", { name: "Cũ (đã xóa)" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Đã xóa")).not.toBeInTheDocument();
  });

  it("MembersPage_Table_HasAccessibleNameAndColumnHeaders", async () => {
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    expect(
      screen.getByRole("table", { name: "Danh sách thành viên" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "Tên" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "Trạng thái" }),
    ).toBeInTheDocument();
  });

  it("MembersPage_FreeTier_ShowsActiveMemberCount", async () => {
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });
    // 3 active members (owner-rep + 2), deleted excluded from the count.
    expect(
      screen.getByText("3 thành viên đang hoạt động"),
    ).toBeInTheDocument();
  });

  it("MembersPage_Loading_ShowsSkeletonRows", async () => {
    server.use(
      http.get("*/api/v1/members", async () => {
        await delay(50);
        return ok([]);
      }),
    );
    renderMembers();

    // While pending, the table shell renders 4 placeholder (skeleton) rows and
    // no real member data yet.
    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders).toHaveLength(4);
    expect(rowHeaders.every((c) => c.textContent === "")).toBe(true);
  });

  it("MembersPage_EmptyList_ShowsEmptyState", async () => {
    server.use(http.get("*/api/v1/members", () => ok([])));
    renderMembers();

    expect(
      await screen.findByText("Chưa có thành viên nào"),
    ).toBeInTheDocument();
  });

  it("MembersPage_ListError_ShowsErrorStateThenRetryRecovers", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/members", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok([
          {
            uuid: "m-ok",
            name: "An Nguyễn",
            isOwnerRepresentative: false,
            isDeleted: false,
            createdAt: "2026-01-01T00:00:00+00:00",
          } satisfies MemberResponse,
        ]);
      }),
    );

    const user = userEvent.setup();
    renderMembers();

    const errorRegion = await screen.findByRole("alert");
    expect(
      within(errorRegion).getByText("Không tải được danh sách thành viên"),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Thử lại" }));
    // Retry refetches and the list recovers.
    expect(
      await screen.findByRole("rowheader", { name: "An Nguyễn" }),
    ).toBeInTheDocument();
  });
});

// ─── Show-deleted toggle ─────────────────────────────────────────────────────
describe("MembersPage show-deleted toggle", () => {
  it("MembersPage_ToggleShowDeleted_RevealsDeletedRowWithBadgeAndNoActions", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(
      screen.getByRole("checkbox", { name: "Hiện thành viên đã xóa" }),
    );

    const deletedRow = (
      await screen.findByRole("rowheader", { name: "Cũ (đã xóa)" })
    ).closest("tr") as HTMLElement;
    // Distinguished by badge text + a muted data-deleted row (never color alone).
    expect(within(deletedRow).getByText("Đã xóa")).toBeInTheDocument();
    expect(deletedRow).toHaveAttribute("data-deleted", "true");
    // Deleted rows are read-only (OQ4a) — no rename/delete controls.
    expect(within(deletedRow).queryByRole("button")).not.toBeInTheDocument();
  });

  it("MembersPage_ToggleShowDeletedOff_HidesDeletedAgain", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    const toggle = screen.getByRole("checkbox", {
      name: "Hiện thành viên đã xóa",
    });
    await user.click(toggle);
    await screen.findByRole("rowheader", { name: "Cũ (đã xóa)" });

    await user.click(toggle);
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: "Cũ (đã xóa)" }),
      ).not.toBeInTheDocument(),
    );
  });
});

// ─── Owner-rep protection ────────────────────────────────────────────────────
describe("MembersPage owner-rep protection", () => {
  it("MembersPage_OwnerRepRow_HasRenameNoDeleteAndExplanation", async () => {
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    const ownerRow = rowFor("Bạn (chủ sổ)");
    expect(within(ownerRow).getByText("Đại diện chủ sổ")).toBeInTheDocument();
    // Renamable…
    expect(
      within(ownerRow).getByRole("button", {
        name: "Đổi tên Bạn (chủ sổ)",
      }),
    ).toBeInTheDocument();
    // …but never deletable, with a short explanation.
    expect(
      within(ownerRow).queryByRole("button", { name: "Xóa Bạn (chủ sổ)" }),
    ).not.toBeInTheDocument();
    expect(
      within(ownerRow).getByText(
        "Thành viên đại diện chủ sổ không thể xóa.",
      ),
    ).toBeInTheDocument();
  });

  it("MembersPage_NormalMemberRow_HasRenameAndDeleteControls", async () => {
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    const row = rowFor("An Nguyễn");
    expect(
      within(row).getByRole("button", { name: "Đổi tên An Nguyễn" }),
    ).toBeInTheDocument();
    expect(
      within(row).getByRole("button", { name: "Xóa An Nguyễn" }),
    ).toBeInTheDocument();
  });
});

// ─── Create ──────────────────────────────────────────────────────────────────
describe("MembersPage create", () => {
  it("MembersPage_CreateValid_AddsRowToastsAndClosesDialog", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Thêm thành viên" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên thành viên" }),
      "Chi Lê",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(await screen.findByText("Đã thêm thành viên.")).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "Chi Lê" }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("MembersPage_CreateEmptyName_BlocksClientSideWithNoRequest", async () => {
    let posts = 0;
    server.use(
      http.post("*/api/v1/members", () => {
        posts += 1;
        return ok({});
      }),
    );
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Thêm thành viên" }));
    const dialog = await screen.findByRole("dialog");
    // Submit with an empty name → Zod blocks it, no request leaves the client.
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Tên thành viên không được để trống."),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });

  it("MembersPage_CreateNameField_CapsInputAtHundredChars", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Thêm thành viên" }));
    const dialog = await screen.findByRole("dialog");
    // The 1–100 rule is enforced client-side; the field guards the max via
    // maxLength (the >100 rule itself is unit-tested in schemas.test.ts).
    expect(within(dialog).getByRole("textbox", { name: "Tên thành viên" })).toHaveAttribute(
      "maxLength",
      "100",
    );
  });

  it("MembersPage_Create1001_MapsServerErrorOntoNameField", async () => {
    server.use(
      http.post("*/api/v1/members", () =>
        fail(1001, "Dữ liệu không hợp lệ.", 400, {
          name: ["Tên không hợp lệ theo máy chủ."],
        }),
      ),
    );
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Thêm thành viên" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByRole("textbox", { name: "Tên thành viên" }), "Hợp lệ");
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Tên không hợp lệ theo máy chủ."),
    ).toBeInTheDocument();
    // Form stays mounted for correction.
    expect(within(dialog).getByRole("textbox", { name: "Tên thành viên" })).toBeInTheDocument();
  });

  it("MembersPage_Create13000_RendersInDialogLimitNoticeAndKeepsFormMounted", async () => {
    const limitMessage =
      "Tài khoản Free chỉ có thể có tối đa 5 thành viên đang hoạt động. Nâng cấp Premium để bỏ giới hạn.";
    server.use(
      http.post("*/api/v1/members", () => fail(13000, limitMessage, 400)),
    );
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Thêm thành viên" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByRole("textbox", { name: "Tên thành viên" }), "Người 6");
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    // Friendly in-dialog LimitNotice rendering the server's localized message…
    expect(
      await within(dialog).findByText("Đã đạt giới hạn số thành viên"),
    ).toBeInTheDocument();
    expect(within(dialog).getByText(limitMessage)).toBeInTheDocument();
    // …the form is NOT destroyed, and no success toast / navigation happens.
    expect(within(dialog).getByRole("textbox", { name: "Tên thành viên" })).toBeInTheDocument();
    expect(screen.queryByText("Đã thêm thành viên.")).not.toBeInTheDocument();
  });
});

// ─── Rename ──────────────────────────────────────────────────────────────────
describe("MembersPage rename", () => {
  it("MembersPage_RenameDialog_PrefillsCurrentNameThenUpdates", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(
      screen.getByRole("button", { name: "Đổi tên An Nguyễn" }),
    );
    const dialog = await screen.findByRole("dialog");
    const field = within(dialog).getByRole("textbox", { name: "Tên thành viên" });
    expect(field).toHaveValue("An Nguyễn");

    await user.clear(field);
    await user.type(field, "An Cập Nhật");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Đã cập nhật tên thành viên."),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "An Cập Nhật" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("rowheader", { name: "An Nguyễn" }),
    ).not.toBeInTheDocument();
  });

  it("MembersPage_RenameOwnerRep_Succeeds", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(
      screen.getByRole("button", { name: "Đổi tên Bạn (chủ sổ)" }),
    );
    const dialog = await screen.findByRole("dialog");
    const field = within(dialog).getByRole("textbox", { name: "Tên thành viên" });
    await user.clear(field);
    await user.type(field, "Chủ Sổ Mới");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Đã cập nhật tên thành viên."),
    ).toBeInTheDocument();
    // Owner-rep rename is allowed; it stays first in backend order.
    await screen.findByRole("rowheader", { name: "Chủ Sổ Mới" });
    expect(screen.getAllByRole("rowheader")[0].textContent).toBe("Chủ Sổ Mới");
  });

  it("MembersPage_Rename3000Stale_ToastsAndClosesWithoutCrash", async () => {
    server.use(
      http.put("*/api/v1/members/:uuid", () =>
        fail(3000, "Không tìm thấy thành viên.", 404),
      ),
    );
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(
      screen.getByRole("button", { name: "Đổi tên An Nguyễn" }),
    );
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Không tìm thấy thành viên."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});

// ─── Delete ──────────────────────────────────────────────────────────────────
describe("MembersPage delete", () => {
  it("MembersPage_DeleteConfirmDialog_ShowsMemberNamedTitleAndHistoryPreservedCopy", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Xóa An Nguyễn" }));
    const dialog = await screen.findByRole("dialog");

    expect(
      within(dialog).getByRole("heading", { name: "Xóa An Nguyễn?" }),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(/toàn bộ dữ liệu lịch sử vẫn được giữ nguyên/),
    ).toBeInTheDocument();
  });

  it("MembersPage_DeleteCancel_ClosesWithNoRequest", async () => {
    let deletes = 0;
    server.use(
      http.delete("*/api/v1/members/:uuid", () => {
        deletes += 1;
        return ok({ message: "x" });
      }),
    );
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Xóa An Nguyễn" }));
    const dialog = await screen.findByRole("dialog");
    // Two "Hủy" affordances (the header ✕ and the footer button) both cancel.
    await user.click(within(dialog).getAllByRole("button", { name: "Hủy" })[0]);

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(deletes).toBe(0);
  });

  it("MembersPage_DeleteConfirm_SoftDeletesToastsAndRowLeavesDefaultList", async () => {
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Xóa An Nguyễn" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Xóa thành viên" }),
    );

    expect(await screen.findByText("Đã xóa thành viên.")).toBeInTheDocument();
    // The row leaves the default (active-only) list after the soft-delete.
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: "An Nguyễn" }),
      ).not.toBeInTheDocument(),
    );
    // …and reappears under the show-deleted toggle.
    await user.click(
      screen.getByRole("checkbox", { name: "Hiện thành viên đã xóa" }),
    );
    const deletedRow = (
      await screen.findByRole("rowheader", { name: "An Nguyễn" })
    ).closest("tr") as HTMLElement;
    expect(deletedRow).toHaveAttribute("data-deleted", "true");
  });

  it("MembersPage_Delete3001Defensive_ToastsServerMessage", async () => {
    server.use(
      http.delete("*/api/v1/members/:uuid", () =>
        fail(3001, "Không thể xóa thành viên đại diện chủ sổ.", 400),
      ),
    );
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("rowheader", { name: "An Nguyễn" });

    await user.click(screen.getByRole("button", { name: "Xóa An Nguyễn" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Xóa thành viên" }),
    );

    expect(
      await screen.findByText("Không thể xóa thành viên đại diện chủ sổ."),
    ).toBeInTheDocument();
  });
});

// ─── i18n parity ─────────────────────────────────────────────────────────────
describe("MembersPage i18n", () => {
  it("MembersPage_EnUsLocale_RendersEnglishChromeAndCopy", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    renderMembers();

    // LocaleProvider syncs i18n on mount; the members namespace resolves en-US.
    expect(
      await screen.findByRole("heading", { level: 1, name: "Members" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Add member" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Show deleted members" }),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "An Nguyễn" }),
    ).toBeInTheDocument();
  });

  it("MembersPage_EnUsValidation_ShowsEnglishRequiredMessage", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    const user = userEvent.setup();
    renderMembers();
    await screen.findByRole("heading", { level: 1, name: "Members" });

    await user.click(screen.getByRole("button", { name: "Add member" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Add" }));

    // The new validation:member.* keys resolve in en-US (parity with vi-VN).
    expect(
      await within(dialog).findByText("Member name is required."),
    ).toBeInTheDocument();
  });
});
