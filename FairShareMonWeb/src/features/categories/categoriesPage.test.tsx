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
import { CategoriesPage } from "./pages/CategoriesPage";

/**
 * CategoriesPage integration — the REAL page/hooks/dialogs/pickers against MSW at
 * the client boundary. Mutations invalidate via the singleton `queryClient`, so
 * every render uses it (and clears it per test). Each test seeds a UNIQUE username
 * so the MSW categories store (seeded lazily per user) is isolated + deterministic.
 * Copy is the vi-VN default unless a spec flips the locale.
 *
 * Default seed per fresh user (default-first then A→Z; one soft-deleted):
 *   "Ăn uống" 🍜 (default) · "Đi lại" 🚗 · "Khác" ⋯ · "Khách sạn" 🏨 ·
 *   "Mua sắm" 🛍️ · "Giải trí (cũ)" 🎬 (deleted)
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
function seedSession(): string {
  userSeq += 1;
  const username = `ctest${userSeq}`;
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

function renderCategories() {
  return renderWithProviders(<CategoriesPage />, {
    initialPath: "/categories",
    queryClient,
  });
}

/** The <tr> containing a category's row-header cell. */
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
describe("CategoriesPage list + order", () => {
  it("CategoriesPage_ListLoaded_RendersDefaultFirstThenAlphabetical", async () => {
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    const rowheaders = screen.getAllByRole("rowheader");
    // Backend order rendered verbatim: default first, then name A→Z (vi), deleted
    // hidden. Accessible name excludes the aria-hidden glyph → just the name.
    const expected = ["Ăn uống", "Đi lại", "Khác", "Khách sạn", "Mua sắm"];
    expected.forEach((name, i) =>
      expect(rowheaders[i]).toHaveAccessibleName(name),
    );
  });

  it("CategoriesPage_ListLoaded_RendersServerOrderVerbatimWithoutClientResort", async () => {
    // Server returns a deliberately non-alphabetical order; the client must NOT
    // re-sort it (R1). Icons omitted so rowheader text is exactly the name.
    server.use(
      http.get("*/api/v1/categories", () =>
        ok([
          {
            uuid: "c-a",
            name: "Ăn uống",
            color: "#F97316",
            icon: null,
            isDefault: true,
            isDeleted: false,
            createdAt: "2026-01-01T00:00:00+00:00",
          },
          {
            uuid: "c-z",
            name: "Zeta",
            color: "#3B82F6",
            icon: null,
            isDefault: false,
            isDeleted: false,
            createdAt: "2026-01-01T00:00:00+00:00",
          },
          {
            uuid: "c-alpha",
            name: "Alpha",
            color: "#8B5CF6",
            icon: null,
            isDefault: false,
            isDeleted: false,
            createdAt: "2026-01-01T00:00:00+00:00",
          },
        ]),
      ),
    );
    renderCategories();
    await screen.findByRole("rowheader", { name: "Zeta" });

    const names = screen
      .getAllByRole("rowheader")
      .map((cell) => cell.textContent);
    // Verbatim: "Zeta" stays before "Alpha" (a client A→Z re-sort would flip them).
    expect(names).toEqual(["Ăn uống", "Zeta", "Alpha"]);
  });

  it("CategoriesPage_EachRow_ShowsColorMarkerAndIconGlyph", async () => {
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    // The CategoryMarker renders the emoji glyph verbatim in the Name cell.
    expect(within(rowFor("Ăn uống")).getByText("🍜")).toBeInTheDocument();
    expect(within(rowFor("Đi lại")).getByText("🚗")).toBeInTheDocument();
  });

  it("CategoriesPage_DefaultList_HidesDeletedCategories", async () => {
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    expect(
      screen.queryByRole("rowheader", { name: "Giải trí (cũ)" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Đã xóa")).not.toBeInTheDocument();
  });

  it("CategoriesPage_Table_HasAccessibleNameAndColumnHeaders", async () => {
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    expect(
      screen.getByRole("table", { name: "Danh sách danh mục" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "Danh mục" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "Trạng thái" }),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_Loading_ShowsSkeletonRows", () => {
    server.use(
      http.get("*/api/v1/categories", async () => {
        await delay(50);
        return ok([]);
      }),
    );
    renderCategories();

    // While pending, the table shell renders 5 placeholder (skeleton) rows.
    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders).toHaveLength(5);
    expect(rowHeaders.every((c) => c.textContent === "")).toBe(true);
  });

  it("CategoriesPage_EmptyList_ShowsDefensiveEmptyState", async () => {
    server.use(http.get("*/api/v1/categories", () => ok([])));
    renderCategories();

    expect(
      await screen.findByText("Chưa có danh mục nào"),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_ListError_ShowsErrorStateThenRetryRecovers", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/categories", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok([
          {
            uuid: "c-ok",
            name: "Ăn uống",
            color: "#F97316",
            icon: "🍜",
            isDefault: true,
            isDeleted: false,
            createdAt: "2026-01-01T00:00:00+00:00",
          },
        ]);
      }),
    );
    const user = userEvent.setup();
    renderCategories();

    const errorRegion = await screen.findByRole("alert");
    expect(
      within(errorRegion).getByText("Không tải được danh sách danh mục"),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Thử lại" }));
    expect(
      await screen.findByRole("rowheader", { name: "Ăn uống" }),
    ).toBeInTheDocument();
  });
});

// ─── Show-deleted toggle ─────────────────────────────────────────────────────
describe("CategoriesPage show-deleted toggle", () => {
  it("CategoriesPage_ToggleShowDeleted_RevealsMutedReadOnlyRowWithBadge", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    await user.click(
      screen.getByRole("checkbox", { name: "Hiện danh mục đã xóa" }),
    );

    const deletedRow = (
      await screen.findByRole("rowheader", { name: "Giải trí (cũ)" })
    ).closest("tr") as HTMLElement;
    expect(within(deletedRow).getByText("Đã xóa")).toBeInTheDocument();
    expect(deletedRow).toHaveAttribute("data-deleted", "true");
    // Deleted rows are read-only (reactivation happens via create) — no actions.
    expect(within(deletedRow).queryByRole("button")).not.toBeInTheDocument();
  });

  it("CategoriesPage_ToggleShowDeletedOff_HidesDeletedAgain", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    const toggle = screen.getByRole("checkbox", {
      name: "Hiện danh mục đã xóa",
    });
    await user.click(toggle);
    await screen.findByRole("rowheader", { name: "Giải trí (cũ)" });

    await user.click(toggle);
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: "Giải trí (cũ)" }),
      ).not.toBeInTheDocument(),
    );
  });
});

// ─── Default invariant (R3/R6) ───────────────────────────────────────────────
describe("CategoriesPage default invariant", () => {
  it("CategoriesPage_DefaultRow_HasBadgeEditButNoSetDefaultNoDeleteAndNote", async () => {
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    const defaultRow = rowFor("Ăn uống");
    // Default marked with a text badge (not color alone)…
    expect(within(defaultRow).getByText("Mặc định")).toBeInTheDocument();
    // …editable…
    expect(
      within(defaultRow).getByRole("button", { name: "Sửa Ăn uống" }),
    ).toBeInTheDocument();
    // …but never re-settable or deletable, with a short explanation.
    expect(
      within(defaultRow).queryByRole("button", {
        name: "Đặt Ăn uống làm mặc định",
      }),
    ).not.toBeInTheDocument();
    expect(
      within(defaultRow).queryByRole("button", { name: "Xóa Ăn uống" }),
    ).not.toBeInTheDocument();
    expect(
      within(defaultRow).getByText("Danh mục mặc định không thể xóa."),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_NormalRow_HasEditSetDefaultAndDeleteControls", async () => {
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    const row = rowFor("Đi lại");
    expect(
      within(row).getByRole("button", { name: "Sửa Đi lại" }),
    ).toBeInTheDocument();
    expect(
      within(row).getByRole("button", { name: "Đặt Đi lại làm mặc định" }),
    ).toBeInTheDocument();
    expect(
      within(row).getByRole("button", { name: "Xóa Đi lại" }),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_SetDefault_SwapsDefaultMarkerAtomically", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(
      screen.getByRole("button", { name: "Đặt Đi lại làm mặc định" }),
    );
    expect(
      await screen.findByText("Đã đặt danh mục mặc định."),
    ).toBeInTheDocument();

    // After the atomic swap + refetch, "Đi lại" is the new default…
    await waitFor(() =>
      expect(
        within(rowFor("Đi lại")).getByText("Mặc định"),
      ).toBeInTheDocument(),
    );
    expect(
      within(rowFor("Đi lại")).queryByRole("button", {
        name: "Đặt Đi lại làm mặc định",
      }),
    ).not.toBeInTheDocument();
    // …and the old default "Ăn uống" lost the badge + regained set-default/delete.
    expect(within(rowFor("Ăn uống")).queryByText("Mặc định")).not.toBeInTheDocument();
    expect(
      within(rowFor("Ăn uống")).getByRole("button", { name: "Xóa Ăn uống" }),
    ).toBeInTheDocument();
    // Exactly one default after the swap.
    expect(screen.getAllByText("Mặc định")).toHaveLength(1);
  });

  it("CategoriesPage_SetDefault4000Stale_ToastsServerMessage", async () => {
    server.use(
      http.put("*/api/v1/categories/:uuid/default", () =>
        fail(4000, "Không tìm thấy danh mục.", 404),
      ),
    );
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(
      screen.getByRole("button", { name: "Đặt Đi lại làm mặc định" }),
    );
    expect(
      await screen.findByText("Không tìm thấy danh mục."),
    ).toBeInTheDocument();
  });
});

// ─── Create ──────────────────────────────────────────────────────────────────
describe("CategoriesPage create", () => {
  it("CategoriesPage_CreateValid_AddsRowWithPickedColorIconToastsAndCloses", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    await user.click(screen.getByRole("button", { name: "Thêm danh mục" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên danh mục" }),
      "Cà phê",
    );
    // Pick a color swatch and an emoji — both feed the RHF fields.
    await user.click(
      within(within(dialog).getByRole("radiogroup", { name: "Màu" })).getByRole(
        "radio",
        { name: "#0EA5E9" },
      ),
    );
    await user.click(
      within(
        within(dialog).getByRole("radiogroup", { name: "Biểu tượng" }),
      ).getByRole("radio", { name: "☕" }),
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(await screen.findByText("Đã thêm danh mục.")).toBeInTheDocument();
    const newRow = (
      await screen.findByRole("rowheader", { name: "Cà phê" })
    ).closest("tr") as HTMLElement;
    // The chosen emoji is stored + rendered verbatim.
    expect(within(newRow).getByText("☕")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });

  it("CategoriesPage_CreateForm_ShowsStaticReactivateHint", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    await user.click(screen.getByRole("button", { name: "Thêm danh mục" }));
    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByText(
        "Nếu trùng tên một danh mục đã xóa, danh mục đó sẽ được khôi phục.",
      ),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_CreateEmptyName_BlocksClientSideWithNoRequest", async () => {
    let posts = 0;
    server.use(
      http.post("*/api/v1/categories", () => {
        posts += 1;
        return ok({});
      }),
    );
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    await user.click(screen.getByRole("button", { name: "Thêm danh mục" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Tên danh mục không được để trống."),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });

  it("CategoriesPage_Create4001Duplicate_MapsOntoNameFieldAndKeepsFormMounted", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    await user.click(screen.getByRole("button", { name: "Thêm danh mục" }));
    const dialog = await screen.findByRole("dialog");
    // "Đi lại" is an active seed → the server rejects the duplicate with 4001.
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên danh mục" }),
      "Đi lại",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    expect(
      await within(dialog).findByText("Tên danh mục đã tồn tại."),
    ).toBeInTheDocument();
    // Form stays mounted for correction (not a toast, not form-level).
    expect(
      within(dialog).getByRole("textbox", { name: "Tên danh mục" }),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_Create1001_MapsServerFieldErrorOntoColorField", async () => {
    server.use(
      http.post("*/api/v1/categories", () =>
        fail(1001, "Dữ liệu không hợp lệ.", 400, {
          color: ["Màu không hợp lệ theo máy chủ."],
        }),
      ),
    );
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    await user.click(screen.getByRole("button", { name: "Thêm danh mục" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên danh mục" }),
      "Hợp lệ",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    // 1001 error.fields.color is applied onto the ColorPicker field.
    expect(
      await within(dialog).findByText("Màu không hợp lệ theo máy chủ."),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_CreateReactivatesSoftDeletedName_RevivesRowNoDuplicate", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Ăn uống" });

    // "Giải trí (cũ)" is a soft-deleted seed → creating that name revives it
    // (same uuid, 200), returning the now-active row.
    await user.click(screen.getByRole("button", { name: "Thêm danh mục" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Tên danh mục" }),
      "Giải trí (cũ)",
    );
    await user.click(within(dialog).getByRole("button", { name: "Thêm" }));

    // Generic success toast (transparent reactivation, OQ3a).
    expect(await screen.findByText("Đã thêm danh mục.")).toBeInTheDocument();
    // The revived row now appears in the ACTIVE list, exactly once (no duplicate).
    await waitFor(() =>
      expect(
        screen.getAllByRole("rowheader", { name: "Giải trí (cũ)" }),
      ).toHaveLength(1),
    );
    expect(rowFor("Giải trí (cũ)")).not.toHaveAttribute("data-deleted");
  });
});

// ─── Edit ────────────────────────────────────────────────────────────────────
describe("CategoriesPage edit", () => {
  it("CategoriesPage_EditDialog_PrefillsNameColorIconThenUpdates", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(screen.getByRole("button", { name: "Sửa Đi lại" }));
    const dialog = await screen.findByRole("dialog");
    // Pre-filled name…
    const nameField = within(dialog).getByRole("textbox", {
      name: "Tên danh mục",
    });
    expect(nameField).toHaveValue("Đi lại");
    // …pre-filled color swatch (#3B82F6) + icon (🚗) are the checked radios.
    expect(
      within(within(dialog).getByRole("radiogroup", { name: "Màu" })).getByRole(
        "radio",
        { name: "#3B82F6" },
      ),
    ).toHaveAttribute("aria-checked", "true");
    expect(
      within(
        within(dialog).getByRole("radiogroup", { name: "Biểu tượng" }),
      ).getByRole("radio", { name: "🚗" }),
    ).toHaveAttribute("aria-checked", "true");

    await user.clear(nameField);
    await user.type(nameField, "Đi chuyển");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(await screen.findByText("Đã cập nhật danh mục.")).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "Đi chuyển" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("rowheader", { name: "Đi lại" }),
    ).not.toBeInTheDocument();
  });

  it("CategoriesPage_Edit4001Duplicate_MapsOntoNameField", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(screen.getByRole("button", { name: "Sửa Đi lại" }));
    const dialog = await screen.findByRole("dialog");
    const nameField = within(dialog).getByRole("textbox", {
      name: "Tên danh mục",
    });
    await user.clear(nameField);
    await user.type(nameField, "Khác"); // another active category
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await within(dialog).findByText("Tên danh mục đã tồn tại."),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_Edit4000Stale_ToastsAndCloses", async () => {
    server.use(
      http.put("*/api/v1/categories/:uuid", () =>
        fail(4000, "Không tìm thấy danh mục.", 404),
      ),
    );
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(screen.getByRole("button", { name: "Sửa Đi lại" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Lưu" }));

    expect(
      await screen.findByText("Không tìm thấy danh mục."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});

// ─── Delete ──────────────────────────────────────────────────────────────────
describe("CategoriesPage delete", () => {
  it("CategoriesPage_DeleteConfirmDialog_ShowsNamedTitleAndHistoryPreservedCopy", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(screen.getByRole("button", { name: "Xóa Đi lại" }));
    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByRole("heading", { name: "Xóa Đi lại?" }),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(/toàn bộ dữ liệu lịch sử vẫn được giữ nguyên/),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_DeleteConfirm_SoftDeletesToastsAndRowMovesUnderToggle", async () => {
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(screen.getByRole("button", { name: "Xóa Đi lại" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Xóa danh mục" }),
    );

    expect(await screen.findByText("Đã xóa danh mục.")).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.queryByRole("rowheader", { name: "Đi lại" }),
      ).not.toBeInTheDocument(),
    );
    // Reappears under the show-deleted toggle as a muted, read-only row.
    await user.click(
      screen.getByRole("checkbox", { name: "Hiện danh mục đã xóa" }),
    );
    const deletedRow = (
      await screen.findByRole("rowheader", { name: "Đi lại" })
    ).closest("tr") as HTMLElement;
    expect(deletedRow).toHaveAttribute("data-deleted", "true");
  });

  it("CategoriesPage_Delete4002Defensive_ToastsServerMessage", async () => {
    server.use(
      http.delete("*/api/v1/categories/:uuid", () =>
        fail(4002, "Không thể xóa danh mục mặc định.", 400),
      ),
    );
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("rowheader", { name: "Đi lại" });

    await user.click(screen.getByRole("button", { name: "Xóa Đi lại" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Xóa danh mục" }),
    );

    expect(
      await screen.findByText("Không thể xóa danh mục mặc định."),
    ).toBeInTheDocument();
  });
});

// ─── i18n parity ─────────────────────────────────────────────────────────────
describe("CategoriesPage i18n", () => {
  it("CategoriesPage_EnUsLocale_RendersEnglishChromeAndCopy", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    renderCategories();

    expect(
      await screen.findByRole("heading", { level: 1, name: "Categories" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Add category" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Show deleted categories" }),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("rowheader", { name: "Ăn uống" }),
    ).toBeInTheDocument();
  });

  it("CategoriesPage_EnUsValidation_ShowsEnglishRequiredMessage", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    const user = userEvent.setup();
    renderCategories();
    await screen.findByRole("heading", { level: 1, name: "Categories" });

    await user.click(screen.getByRole("button", { name: "Add category" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Add" }));

    expect(
      await within(dialog).findByText("Category name is required."),
    ).toBeInTheDocument();
  });
});
