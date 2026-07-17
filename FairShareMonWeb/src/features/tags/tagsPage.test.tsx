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
import { TagsPage } from "./pages/TagsPage";

/**
 * TagsPage integration — the REAL page/hooks/dialogs against MSW at the client
 * boundary. Mutations invalidate via the singleton `queryClient`, so every render
 * uses it (and clears it per test). Each test seeds a UNIQUE username so the MSW
 * tags store (seeded lazily per user) is isolated + deterministic. Copy is the
 * vi-VN default unless a spec flips the locale.
 *
 * Default seed per fresh user (name A→Z; one soft-deleted):
 *   "Công tác" · "Du lịch" · "Sinh nhật (cũ)" (deleted)
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

function seedSession(): string {
  userSeq += 1;
  const username = `tgtest${userSeq}`;
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

function renderTags() {
  return renderWithProviders(<TagsPage />, {
    initialPath: "/tags",
    queryClient,
  });
}

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
describe("TagsPage list + order", () => {
  it("TagsPage_ListLoaded_RendersNamesAlphabetical", async () => {
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    const names = screen
      .getAllByRole("rowheader")
      .map((cell) => cell.textContent);
    // Backend order rendered verbatim: name A→Z (vi), deleted hidden.
    expect(names).toEqual(["Công tác", "Du lịch"]);
  });

  it("TagsPage_DefaultList_HidesDeletedTags", async () => {
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    expect(
      screen.queryByRole("rowheader", { name: "Sinh nhật (cũ)" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Đã xóa")).not.toBeInTheDocument();
  });

  it("TagsPage_Table_HasAccessibleNameAndColumnHeaders", async () => {
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    expect(
      screen.getByRole("table", { name: "Danh sách nhãn" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Nhãn" })).toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "Trạng thái" }),
    ).toBeInTheDocument();
  });

  it("TagsPage_Loading_ShowsSkeletonRows", () => {
    server.use(
      http.get("*/api/v1/tags", async () => {
        await delay(50);
        return ok([]);
      }),
    );
    renderTags();

    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders).toHaveLength(4);
    expect(rowHeaders.every((c) => c.textContent === "")).toBe(true);
  });

  it("TagsPage_EmptyList_ShowsEmptyStateWithAddAffordance", async () => {
    server.use(http.get("*/api/v1/tags", () => ok([])));
    renderTags();

    expect(await screen.findByText("Chưa có nhãn nào")).toBeInTheDocument();
    // The tag list can be genuinely empty → an "add tag" affordance is offered.
    expect(
      screen.getAllByRole("button", { name: "Thêm nhãn" }).length,
    ).toBeGreaterThan(0);
  });

  it("TagsPage_ListError_ShowsErrorStateThenRetryRecovers", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/tags", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok([
          {
            uuid: "t-ok",
            name: "Công tác",
            isDeleted: false,
            createdAt: "2026-01-01T00:00:00+00:00",
          },
        ]);
      }),
    );
    const user = userEvent.setup();
    renderTags();

    const errorRegion = await screen.findByRole("alert");
    expect(
      within(errorRegion).getByText("Không tải được danh sách nhãn"),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Thử lại" }));
    expect(
      await screen.findByRole("rowheader", { name: "Công tác" }),
    ).toBeInTheDocument();
  });
});

// ─── Show-deleted toggle ─────────────────────────────────────────────────────
describe("TagsPage show-deleted toggle", () => {
  it("TagsPage_ToggleShowDeleted_RevealsMutedReadOnlyRowWithBadge", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(
      screen.getByRole("checkbox", { name: "Hiện nhãn đã xóa" }),
    );

    const deletedRow = (
      await screen.findByRole("rowheader", { name: "Sinh nhật (cũ)" })
    ).closest("tr") as HTMLElement;
    expect(within(deletedRow).getByText("Đã xóa")).toBeInTheDocument();
    expect(deletedRow).toHaveAttribute("data-deleted", "true");
    expect(within(deletedRow).queryByRole("button")).not.toBeInTheDocument();
  });

  it("TagsPage_ToggleShowDeletedOff_HidesDeletedAgain", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    const toggle = screen.getByRole("checkbox", { name: "Hiện nhãn đã xóa" });
    await user.click(toggle);
    await screen.findByRole("rowheader", { name: "Sinh nhật (cũ)" });

    await user.click(toggle);
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: "Sinh nhật (cũ)" }),
      ).not.toBeInTheDocument(),
    );
  });
});

// ─── Row actions ─────────────────────────────────────────────────────────────
describe("TagsPage row actions", () => {
  it("TagsPage_NormalRow_HasRenameAndDeleteControls", async () => {
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    const row = rowFor("Công tác");
    expect(
      within(row).getByRole("button", { name: "Đổi tên Công tác" }),
    ).toBeInTheDocument();
    expect(
      within(row).getByRole("button", { name: "Xóa Công tác" }),
    ).toBeInTheDocument();
  });
});

// ─── Create ──────────────────────────────────────────────────────────────────
describe("TagsPage create", () => {
  it("TagsPage_CreateValid_AddsRowToastsAndClosesDialog", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Thêm nhãn" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên nhãn" }),
      "Ăn trưa",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(await screen.findByText("Đã thêm nhãn.")).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "Ăn trưa" }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("TagsPage_CreateForm_ShowsStaticReactivateHint", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Thêm nhãn" }));
    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByText(
        "Nếu trùng tên một nhãn đã xóa, nhãn đó sẽ được khôi phục.",
      ),
    ).toBeInTheDocument();
  });

  it("TagsPage_CreateEmptyName_BlocksClientSideWithNoRequest", async () => {
    let posts = 0;
    server.use(
      http.post("*/api/v1/tags", () => {
        posts += 1;
        return ok({});
      }),
    );
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Thêm nhãn" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Tên nhãn không được để trống."),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });

  it("TagsPage_Create5001Duplicate_MapsOntoNameFieldAndKeepsFormMounted", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Thêm nhãn" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên nhãn" }),
      "Du lịch", // an active seed
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Tên nhãn đã tồn tại."),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByRole("textbox", { name: "Tên nhãn" }),
    ).toBeInTheDocument();
  });

  it("TagsPage_Create1001_MapsServerFieldErrorOntoNameField", async () => {
    server.use(
      http.post("*/api/v1/tags", () =>
        fail(1001, "Dữ liệu không hợp lệ.", 400, {
          name: ["Tên nhãn không hợp lệ theo máy chủ."],
        }),
      ),
    );
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Thêm nhãn" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên nhãn" }),
      "Hợp lệ",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Tên nhãn không hợp lệ theo máy chủ."),
    ).toBeInTheDocument();
  });

  it("TagsPage_CreateReactivatesSoftDeletedName_RevivesRowNoDuplicate", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    // "Sinh nhật (cũ)" is a soft-deleted seed → creating that name revives it
    // (same uuid, 200).
    await user.click(screen.getByRole("button", { name: "Thêm nhãn" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên nhãn" }),
      "Sinh nhật (cũ)",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(await screen.findByText("Đã thêm nhãn.")).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.getAllByRole("rowheader", { name: "Sinh nhật (cũ)" }),
      ).toHaveLength(1),
    );
    expect(rowFor("Sinh nhật (cũ)")).not.toHaveAttribute("data-deleted");
  });
});

// ─── Rename ──────────────────────────────────────────────────────────────────
describe("TagsPage rename", () => {
  it("TagsPage_RenameDialog_PrefillsCurrentNameThenUpdates", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Đổi tên Công tác" }));
    const dialog = await screen.findByRole("dialog");
    const field = within(dialog).getByRole("textbox", { name: "Tên nhãn" });
    expect(field).toHaveValue("Công tác");

    await user.clear(field);
    await user.type(field, "Công việc");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Đã cập nhật tên nhãn."),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "Công việc" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("rowheader", { name: "Công tác" }),
    ).not.toBeInTheDocument();
  });

  it("TagsPage_Rename5001Duplicate_MapsOntoNameField", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Đổi tên Công tác" }));
    const dialog = await screen.findByRole("dialog");
    const field = within(dialog).getByRole("textbox", { name: "Tên nhãn" });
    await user.clear(field);
    await user.type(field, "Du lịch"); // another active tag
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await within(dialog).findByText("Tên nhãn đã tồn tại."),
    ).toBeInTheDocument();
  });

  it("TagsPage_Rename5000Stale_ToastsAndCloses", async () => {
    server.use(
      http.put("*/api/v1/tags/:uuid", () =>
        fail(5000, "Không tìm thấy nhãn.", 404),
      ),
    );
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Đổi tên Công tác" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(await screen.findByText("Không tìm thấy nhãn.")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});

// ─── Delete ──────────────────────────────────────────────────────────────────
describe("TagsPage delete", () => {
  it("TagsPage_DeleteConfirmDialog_ShowsNamedTitleAndHistoryPreservedCopy", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Xóa Công tác" }));
    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByRole("heading", { name: "Xóa Công tác?" }),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(/toàn bộ dữ liệu lịch sử vẫn được giữ nguyên/),
    ).toBeInTheDocument();
  });

  it("TagsPage_DeleteConfirm_SoftDeletesToastsAndRowMovesUnderToggle", async () => {
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("rowheader", { name: "Công tác" });

    await user.click(screen.getByRole("button", { name: "Xóa Công tác" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Xóa nhãn" }));

    expect(await screen.findByText("Đã xóa nhãn.")).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: "Công tác" }),
      ).not.toBeInTheDocument(),
    );
    await user.click(
      screen.getByRole("checkbox", { name: "Hiện nhãn đã xóa" }),
    );
    const deletedRow = (
      await screen.findByRole("rowheader", { name: "Công tác" })
    ).closest("tr") as HTMLElement;
    expect(deletedRow).toHaveAttribute("data-deleted", "true");
  });
});

// ─── i18n parity ─────────────────────────────────────────────────────────────
describe("TagsPage i18n", () => {
  it("TagsPage_EnUsLocale_RendersEnglishChromeAndCopy", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    renderTags();

    expect(
      await screen.findByRole("heading", { level: 1, name: "Tags" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add tag" })).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Show deleted tags" }),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "Công tác" }),
    ).toBeInTheDocument();
  });

  it("TagsPage_EnUsValidation_ShowsEnglishRequiredMessage", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    const user = userEvent.setup();
    renderTags();
    await screen.findByRole("heading", { level: 1, name: "Tags" });

    await user.click(screen.getByRole("button", { name: "Add tag" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Add" }));

    expect(
      await within(dialog).findByText("Tag name is required."),
    ).toBeInTheDocument();
  });
});
