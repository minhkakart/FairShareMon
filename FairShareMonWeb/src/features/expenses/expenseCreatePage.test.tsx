import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useParams } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ExpenseCreatePage } from "./pages/ExpenseCreatePage";

/**
 * ExpenseCreatePage integration — the REAL create page + share editor + general
 * form against MSW. Members/categories/tags load from the seeded per-user store
 * (owner-rep "Bạn (chủ sổ)"; default category "Ăn uống"). POST /expenses is
 * stubbed per-test to capture the atomic body or return an error code, and a
 * detail stub route observes the post-success navigation.
 *
 * Seeded active members (owner-rep first, then A→Z): "Bạn (chủ sổ)" (owner-rep) ·
 * "An Nguyễn" · "Bình Trần".
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

let userSeq = 0;
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

/** A canned created expense so the success path can navigate to the detail route. */
function createdExpense() {
  return {
    uuid: "e-new",
    name: "Thuê xe",
    description: null,
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 0,
    category: {
      uuid: "c-d",
      name: "Ăn uống",
      color: "#F97316",
      icon: "🍜",
      isDefault: true,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    payer: {
      uuid: "m-o",
      name: "Bạn (chủ sổ)",
      isOwnerRepresentative: true,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    shares: [],
    tags: [],
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-16T03:00:00+00:00",
  };
}

function DetailStub() {
  const { uuid } = useParams();
  return <div data-testid="detail-stub">DETAIL {uuid}</div>;
}

function renderCreate() {
  return renderWithProviders(
    <Routes>
      <Route path="/expenses/new" element={<ExpenseCreatePage />} />
      <Route path="/expenses/:uuid" element={<DetailStub />} />
      <Route path="/expenses" element={<div>LIST</div>} />
    </Routes>,
    { initialPath: "/expenses/new", queryClient },
  );
}

/** Wait for the pickers to load — the name field renders once the form is ready. */
async function waitForForm() {
  return screen.findByRole("textbox", { name: "Tên phiếu" });
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

// ─── Owner-rep row ────────────────────────────────────────────────────────────
describe("ExpenseCreatePage owner-rep share row", () => {
  it("ExpenseCreatePage_OwnerRepRow_IsPresentLockedAndNonRemovable", async () => {
    renderCreate();
    await waitForForm();

    // The owner-rep row is auto-present with the lock badge…
    expect(screen.getByText("Đại diện · khóa")).toBeInTheDocument();
    // …the owner-rep member is NOT a Select (it is a locked label)…
    expect(
      screen.queryByRole("combobox", { name: /Bạn \(chủ sổ\)/ }),
    ).not.toBeInTheDocument();
    // …and has no remove control.
    expect(
      screen.queryByRole("button", {
        name: "Xóa phần gánh của Bạn (chủ sổ)",
      }),
    ).not.toBeInTheDocument();
    // The explanatory note is present.
    expect(
      screen.getByText(/luôn có mặt ở mức 0đ và không thể xóa/),
    ).toBeInTheDocument();
  });
});

// ─── Add / remove rows + live sum ────────────────────────────────────────────
describe("ExpenseCreatePage share editor", () => {
  it("ExpenseCreatePage_AddRow_AppendsMemberRowWithRemoveControl", async () => {
    const user = userEvent.setup();
    renderCreate();
    await waitForForm();

    await user.click(screen.getByRole("button", { name: "Thêm phần gánh" }));
    // The first free member (An Nguyễn) is auto-selected; the row is removable.
    expect(
      await screen.findByRole("button", {
        name: "Xóa phần gánh của An Nguyễn",
      }),
    ).toBeInTheDocument();
  });

  it("ExpenseCreatePage_AddAllMembers_DisablesAddWhenNoneRemain", async () => {
    const user = userEvent.setup();
    renderCreate();
    await waitForForm();

    const addBtn = screen.getByRole("button", { name: "Thêm phần gánh" });
    await user.click(addBtn); // An Nguyễn
    await user.click(addBtn); // Bình Trần
    // Owner-rep + An + Bình are all chosen → the client blocks further duplicates.
    await waitFor(() => expect(addBtn).toBeDisabled());
  });

  it("ExpenseCreatePage_RemoveRow_RemovesTheAddedShare", async () => {
    const user = userEvent.setup();
    renderCreate();
    await waitForForm();

    await user.click(screen.getByRole("button", { name: "Thêm phần gánh" }));
    const remove = await screen.findByRole("button", {
      name: "Xóa phần gánh của An Nguyễn",
    });
    await user.click(remove);
    await waitFor(() =>
      expect(
        screen.queryByRole("button", {
          name: "Xóa phần gánh của An Nguyễn",
        }),
      ).not.toBeInTheDocument(),
    );
  });

  it("ExpenseCreatePage_LiveSum_ReflectsRowAmounts", async () => {
    const user = userEvent.setup();
    renderCreate();
    await waitForForm();

    await user.click(screen.getByRole("button", { name: "Thêm phần gánh" }));
    const amount = await screen.findByRole("textbox", {
      name: "Số tiền — An Nguyễn",
    });
    await user.type(amount, "120000");

    // The display-only "Tổng (tạm tính)" reflects the entered amount.
    expect(screen.getByText("Tổng (tạm tính)")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getAllByText(/120\.000/).length).toBeGreaterThanOrEqual(1),
    );
  });
});

// ─── Submit (defaults + navigation) ──────────────────────────────────────────
describe("ExpenseCreatePage submit", () => {
  it("ExpenseCreatePage_SubmitDefaults_SendsPayerCategoryDefaultsAndOwnerRepShareThenNavigates", async () => {
    let body: {
      payerMemberUuid?: string;
      categoryUuid?: string;
      shares?: { memberUuid: string; amount: number }[];
    } | null = null;
    server.use(
      http.post("*/api/v1/expenses", async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok(createdExpense());
      }),
    );
    const user = userEvent.setup();
    renderCreate();
    await waitForForm();

    await user.type(
      screen.getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(screen.getByRole("button", { name: "Thêm phiếu" }));

    // Success toast + navigation to the created expense's detail route.
    expect(
      await screen.findByText("Đã thêm phiếu chi tiêu."),
    ).toBeInTheDocument();
    expect(await screen.findByTestId("detail-stub")).toHaveTextContent(
      "DETAIL e-new",
    );

    // Defaults are applied: a payer + category are sent (the seeded defaults) and
    // the owner-rep 0đ share is auto-present in the atomic body.
    expect(body).not.toBeNull();
    expect(typeof body!.payerMemberUuid).toBe("string");
    expect(body!.payerMemberUuid).not.toBe("");
    expect(typeof body!.categoryUuid).toBe("string");
    expect(body!.categoryUuid).not.toBe("");
    expect(body!.shares).toHaveLength(1);
    expect(body!.shares![0].amount).toBe(0);
  });

  it("ExpenseCreatePage_EmptyName_BlocksClientSideWithNoRequest", async () => {
    let posts = 0;
    server.use(
      http.post("*/api/v1/expenses", () => {
        posts += 1;
        return ok(createdExpense());
      }),
    );
    const user = userEvent.setup();
    renderCreate();
    await waitForForm();

    await user.click(screen.getByRole("button", { name: "Thêm phiếu" }));
    expect(
      await screen.findByText("Tên phiếu không được để trống."),
    ).toBeInTheDocument();
    expect(posts).toBe(0);
  });
});

// ─── Error mapping ────────────────────────────────────────────────────────────
describe("ExpenseCreatePage error mapping", () => {
  async function submitNamed(user: ReturnType<typeof userEvent.setup>) {
    await waitForForm();
    await user.type(
      screen.getByRole("textbox", { name: "Tên phiếu" }),
      "Thuê xe",
    );
    await user.click(screen.getByRole("button", { name: "Thêm phiếu" }));
  }

  it("ExpenseCreatePage_13002_RendersLimitNotice", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(13002, "Đã đạt giới hạn phiếu tháng này.", 400),
      ),
    );
    const user = userEvent.setup();
    renderCreate();
    await submitNamed(user);

    expect(
      await screen.findByText("Đã đạt giới hạn phiếu chi tiêu trong tháng"),
    ).toBeInTheDocument();
  });

  it("ExpenseCreatePage_6001_MapsOntoPayerField", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(6001, "Người trả không hợp lệ.", 400),
      ),
    );
    const user = userEvent.setup();
    renderCreate();
    await submitNamed(user);

    expect(
      await screen.findByText("Người trả không hợp lệ."),
    ).toBeInTheDocument();
  });

  it("ExpenseCreatePage_6002_MapsOntoCategoryField", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(6002, "Danh mục không hợp lệ.", 400),
      ),
    );
    const user = userEvent.setup();
    renderCreate();
    await submitNamed(user);

    expect(
      await screen.findByText("Danh mục không hợp lệ."),
    ).toBeInTheDocument();
  });

  it("ExpenseCreatePage_6003_MapsOntoTagField", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(6003, "Nhãn không hợp lệ.", 400),
      ),
    );
    const user = userEvent.setup();
    renderCreate();
    await submitNamed(user);

    expect(await screen.findByText("Nhãn không hợp lệ.")).toBeInTheDocument();
  });

  it("ExpenseCreatePage_7003_SurfacesFormLevelError", async () => {
    // On the atomic create, 7001/7003 surface form-level (the offending row can't
    // be pinpointed from the flat code) — mirrors the implementation.
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(7003, "Trùng thành viên phần gánh.", 400),
      ),
    );
    const user = userEvent.setup();
    renderCreate();
    await submitNamed(user);

    expect(
      await screen.findByText("Trùng thành viên phần gánh."),
    ).toBeInTheDocument();
  });

  it("ExpenseCreatePage_1001_MapsServerFieldErrorOntoNameField", async () => {
    server.use(
      http.post("*/api/v1/expenses", () =>
        fail(1001, "Dữ liệu không hợp lệ.", 400, {
          name: ["Tên phiếu bị máy chủ từ chối."],
        }),
      ),
    );
    const user = userEvent.setup();
    renderCreate();
    await submitNamed(user);

    expect(
      await screen.findByText("Tên phiếu bị máy chủ từ chối."),
    ).toBeInTheDocument();
  });
});

// ─── Per-row member exclusion (client dedup) ─────────────────────────────────
describe("ExpenseCreatePage per-row member picker", () => {
  it("ExpenseCreatePage_RowMemberSelect_ExcludesAlreadyChosenMembers", async () => {
    const user = userEvent.setup();
    renderCreate();
    await waitForForm();

    // Add a row (auto-selects An Nguyễn). Its member Select should offer the
    // current member + the remaining unchosen one (Bình Trần), never the
    // already-chosen owner-rep (mirrors 7003 client-side).
    await user.click(screen.getByRole("button", { name: "Thêm phần gánh" }));
    const rowSelect = await screen.findByRole("combobox", {
      name: "Thành viên — An Nguyễn",
    });
    await user.click(rowSelect);

    expect(
      await screen.findByRole("option", { name: /An Nguyễn/ }),
    ).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /Bình Trần/ })).toBeInTheDocument();
    expect(
      screen.queryByRole("option", { name: /Bạn \(chủ sổ\)/ }),
    ).not.toBeInTheDocument();
  });
});
