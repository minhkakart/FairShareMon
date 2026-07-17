import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import {
  expensesKeys,
  useAddShare,
  useCreateExpense,
  useDeleteExpense,
  useDeleteShare,
  useExpenseHistoryQuery,
  useExpenseQuery,
  useExpensesQuery,
  useExportExpense,
  useSetSettled,
  useUpdateExpense,
  useUpdateShare,
} from "./hooks/useExpenses";
import type { ExpenseFilter } from "./api/types";

/**
 * Expense hooks over MSW at the network boundary — exercises the REAL centralized
 * client + TanStack Query (never mocks the hook). Every expense/share write
 * invalidates the ["expenses"] root, which prefixes the list, detail(uuid), and
 * history(uuid) keys — so mounting all three queries and asserting each refetches
 * after a mutation proves the OQ14a cross-cache reach. `downloadBlob` is mocked to
 * assert `useExportExpense` triggers the browser download.
 */

const downloadSpy = vi.fn();
vi.mock("@/lib/download/downloadBlob", () => ({
  downloadBlob: (...args: unknown[]) => downloadSpy(...args),
}));

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const EXPENSE_UUID = "e-1";
const SHARE_UUID = "s-1";

function minimalExpense() {
  return { uuid: EXPENSE_UUID, name: "Phiếu", shares: [] };
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-hooktest-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-hooktest-t",
    refreshTokenExpiresAt: future,
    user: { username: "hooktest", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

beforeEach(() => {
  window.localStorage.clear();
  queryClient.clear();
  downloadSpy.mockClear();
  seedSession();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("expensesKeys", () => {
  it("ExpensesKeys_ListDetailHistory_AreScopedUnderTheInvalidationRoot", () => {
    // The root ["expenses"] must prefix every sub-key so one invalidate reaches
    // the list, the detail, and the history caches (OQ14a).
    expect(expensesKeys.all).toEqual(["expenses"]);
    const filter: ExpenseFilter = { looseOnly: true };
    expect(expensesKeys.list(filter)).toEqual(["expenses", "list", filter]);
    expect(expensesKeys.detail("e-9")).toEqual(["expenses", "detail", "e-9"]);
    expect(expensesKeys.history("e-9")).toEqual(["expenses", "history", "e-9"]);
    expect(expensesKeys.list(filter)[0]).toBe(expensesKeys.all[0]);
  });
});

describe("useExpensesQuery filter wiring", () => {
  it("UseExpensesQuery_DefinedFilters_SentAsQueryParams", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/expenses", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );
    const filter: ExpenseFilter = {
      from: "2026-07-01T00:00:00.000Z",
      to: "2026-07-31T23:59:59.999Z",
      categoryUuid: "c-1",
      tagUuid: "t-1",
      settled: true,
      looseOnly: true,
    };
    function Probe() {
      useExpensesQuery(filter);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    const params = new URL(seenUrl).searchParams;
    expect(params.get("from")).toBe(filter.from);
    expect(params.get("to")).toBe(filter.to);
    expect(params.get("categoryUuid")).toBe("c-1");
    expect(params.get("tagUuid")).toBe("t-1");
    expect(params.get("settled")).toBe("true");
    expect(params.get("looseOnly")).toBe("true");
  });

  it("UseExpensesQuery_UndefinedFilters_AreDropped", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/expenses", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );
    function Probe() {
      useExpensesQuery({ categoryUuid: "c-1" });
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    const params = new URL(seenUrl).searchParams;
    expect(params.get("categoryUuid")).toBe("c-1");
    // The client drops undefined/null keys — no empty filters are sent.
    expect(params.has("from")).toBe(false);
    expect(params.has("settled")).toBe(false);
    expect(params.has("looseOnly")).toBe(false);
  });
});

// ─── Mutation invalidation reach (list + detail + history) ────────────────────
describe("expense mutation invalidation", () => {
  type Captured = {
    create?: ReturnType<typeof useCreateExpense>;
    update?: ReturnType<typeof useUpdateExpense>;
    remove?: ReturnType<typeof useDeleteExpense>;
    settled?: ReturnType<typeof useSetSettled>;
    addShare?: ReturnType<typeof useAddShare>;
    updateShare?: ReturnType<typeof useUpdateShare>;
    deleteShare?: ReturnType<typeof useDeleteShare>;
  };

  /** Mount the 3 queries + all mutations, and count each GET. */
  function setup() {
    const counters = { list: 0, detail: 0, history: 0 };
    server.use(
      http.get("*/api/v1/expenses", () => {
        counters.list += 1;
        return ok([]);
      }),
      http.get(`*/api/v1/expenses/${EXPENSE_UUID}`, () => {
        counters.detail += 1;
        return ok(minimalExpense());
      }),
      http.get(`*/api/v1/expenses/${EXPENSE_UUID}/history`, () => {
        counters.history += 1;
        return ok([]);
      }),
      http.post("*/api/v1/expenses", () => ok(minimalExpense())),
      http.put(`*/api/v1/expenses/${EXPENSE_UUID}`, () => ok(minimalExpense())),
      http.delete(`*/api/v1/expenses/${EXPENSE_UUID}`, () =>
        ok({ message: "ok" }),
      ),
      http.put(`*/api/v1/expenses/${EXPENSE_UUID}/settled`, () =>
        ok({ message: "ok" }),
      ),
      http.post(`*/api/v1/expenses/${EXPENSE_UUID}/shares`, () =>
        ok({ uuid: SHARE_UUID }),
      ),
      http.put(`*/api/v1/expenses/${EXPENSE_UUID}/shares/${SHARE_UUID}`, () =>
        ok({ uuid: SHARE_UUID }),
      ),
      http.delete(`*/api/v1/expenses/${EXPENSE_UUID}/shares/${SHARE_UUID}`, () =>
        ok({ message: "ok" }),
      ),
    );

    const captured: Captured = {};
    function Probe() {
      useExpensesQuery({});
      useExpenseQuery(EXPENSE_UUID);
      useExpenseHistoryQuery(EXPENSE_UUID);
      captured.create = useCreateExpense();
      captured.update = useUpdateExpense();
      captured.remove = useDeleteExpense();
      captured.settled = useSetSettled();
      captured.addShare = useAddShare();
      captured.updateShare = useUpdateShare();
      captured.deleteShare = useDeleteShare();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });
    return { counters, captured };
  }

  async function expectAllRefetched(
    counters: { list: number; detail: number; history: number },
    mutate: () => Promise<unknown>,
  ) {
    await waitFor(() => {
      expect(counters.list).toBe(1);
      expect(counters.detail).toBe(1);
      expect(counters.history).toBe(1);
    });
    await act(async () => {
      await mutate();
    });
    // The ["expenses"] root invalidation reaches all three caches → each refetches
    // at least once more (the explicit detail/history invalidations may add a
    // redundant refetch; the reach — not the exact count — is what matters).
    await waitFor(() => {
      expect(counters.list).toBeGreaterThanOrEqual(2);
      expect(counters.detail).toBeGreaterThanOrEqual(2);
      expect(counters.history).toBeGreaterThanOrEqual(2);
    });
  }

  it("UseCreateExpense_OnSuccess_InvalidatesListDetailHistory", async () => {
    const { counters, captured } = setup();
    await expectAllRefetched(counters, () =>
      captured.create!.mutateAsync({
        name: "X",
        expenseTime: "2026-07-16T10:00:00.000Z",
      }),
    );
  });

  it("UseUpdateExpense_OnSuccess_InvalidatesListDetailHistory", async () => {
    const { counters, captured } = setup();
    await expectAllRefetched(counters, () =>
      captured.update!.mutateAsync({
        uuid: EXPENSE_UUID,
        body: { name: "Y", expenseTime: "2026-07-16T10:00:00.000Z" },
      }),
    );
  });

  it("UseDeleteExpense_OnSuccess_InvalidatesListDetailHistory", async () => {
    const { counters, captured } = setup();
    await expectAllRefetched(counters, () =>
      captured.remove!.mutateAsync(EXPENSE_UUID),
    );
  });

  it("UseSetSettled_OnSuccess_InvalidatesListDetailHistory", async () => {
    const { counters, captured } = setup();
    await expectAllRefetched(counters, () =>
      captured.settled!.mutateAsync({
        uuid: EXPENSE_UUID,
        body: { isSettled: true },
      }),
    );
  });

  it("UseAddShare_OnSuccess_InvalidatesListDetailHistory", async () => {
    const { counters, captured } = setup();
    await expectAllRefetched(counters, () =>
      captured.addShare!.mutateAsync({
        uuid: EXPENSE_UUID,
        body: { memberUuid: "m-1", amount: 100 },
      }),
    );
  });

  it("UseUpdateShare_OnSuccess_InvalidatesListDetailHistory", async () => {
    const { counters, captured } = setup();
    await expectAllRefetched(counters, () =>
      captured.updateShare!.mutateAsync({
        uuid: EXPENSE_UUID,
        shareUuid: SHARE_UUID,
        body: { memberUuid: "m-1", amount: 100 },
      }),
    );
  });

  it("UseDeleteShare_OnSuccess_InvalidatesListDetailHistory", async () => {
    const { counters, captured } = setup();
    await expectAllRefetched(counters, () =>
      captured.deleteShare!.mutateAsync({
        uuid: EXPENSE_UUID,
        shareUuid: SHARE_UUID,
      }),
    );
  });
});

// ─── CSV export → downloadBlob ────────────────────────────────────────────────
describe("useExportExpense", () => {
  it("UseExportExpense_OnSuccess_TriggersDownloadBlobWithFallback", async () => {
    server.use(
      http.get(`*/api/v1/expenses/${EXPENSE_UUID}/export`, () =>
        new HttpResponse("col1,col2\r\n", {
          headers: {
            "Content-Type": "text/csv; charset=utf-8",
            "Content-Disposition": 'attachment; filename="expense-e-1.csv"',
          },
        }),
      ),
    );
    const captured: { exp?: ReturnType<typeof useExportExpense> } = {};
    function Probe() {
      captured.exp = useExportExpense();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.exp).toBeDefined());
    await act(async () => {
      await captured.exp!.mutateAsync({
        uuid: EXPENSE_UUID,
        fallbackName: "phieu.csv",
      });
    });

    // downloadBlob receives the resolved blob result + the caller's fallback name.
    expect(downloadSpy).toHaveBeenCalledTimes(1);
    const [result, fallback] = downloadSpy.mock.calls[0];
    expect(fallback).toBe("phieu.csv");
    expect(result).toHaveProperty("blob");
    expect((result as { filename?: string }).filename).toBe("expense-e-1.csv");
  });
});
