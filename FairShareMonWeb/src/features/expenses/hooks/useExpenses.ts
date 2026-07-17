import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { downloadBlob } from "@/lib/download/downloadBlob";
import { expensesApi } from "../api/expensesApi";
import type {
  CreateExpenseRequest,
  CreateShareRequest,
  ExpenseFilter,
  SetSettledRequest,
  UpdateExpenseRequest,
  UpdateShareRequest,
} from "../api/types";

/**
 * Query-key factory for expenses. `all` is the invalidation root; `detail(uuid)`
 * and `history(uuid)` are the per-expense sub-caches. Every expense/share write
 * invalidates `all` + the specific `detail` + `history` (OQ14a) — no optimistic
 * updates in M4.
 */
export const expensesKeys = {
  all: ["expenses"] as const,
  lists: () => ["expenses", "list"] as const,
  list: (filter: ExpenseFilter) => ["expenses", "list", filter] as const,
  detail: (uuid: string) => ["expenses", "detail", uuid] as const,
  history: (uuid: string) => ["expenses", "history", uuid] as const,
};

/** The caller's expenses (expenseTime DESC — backend order, rendered verbatim). */
export function useExpensesQuery(filter: ExpenseFilter) {
  return useQuery({
    queryKey: expensesKeys.list(filter),
    queryFn: () => expensesApi.list(filter),
  });
}

/** A single expense's full detail. */
export function useExpenseQuery(uuid: string) {
  return useQuery({
    queryKey: expensesKeys.detail(uuid),
    queryFn: () => expensesApi.get(uuid),
  });
}

/** The immutable per-expense change history (time-ascending). */
export function useExpenseHistoryQuery(uuid: string, enabled = true) {
  return useQuery({
    queryKey: expensesKeys.history(uuid),
    queryFn: () => expensesApi.history(uuid),
    enabled,
  });
}

/** Invalidate the list root + the specific detail + history for a mutation. */
function invalidateExpense(uuid?: string) {
  void queryClient.invalidateQueries({ queryKey: expensesKeys.all });
  if (uuid) {
    void queryClient.invalidateQueries({ queryKey: expensesKeys.detail(uuid) });
    void queryClient.invalidateQueries({ queryKey: expensesKeys.history(uuid) });
  }
}

export function useCreateExpense() {
  return useMutation({
    mutationFn: (body: CreateExpenseRequest) => expensesApi.create(body),
    onSuccess: (expense) => invalidateExpense(expense.uuid),
  });
}

export function useUpdateExpense() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: UpdateExpenseRequest }) =>
      expensesApi.update(uuid, body),
    onSuccess: (expense) => invalidateExpense(expense.uuid),
  });
}

export function useDeleteExpense() {
  return useMutation({
    mutationFn: (uuid: string) => expensesApi.remove(uuid),
    onSuccess: (_data, uuid) => invalidateExpense(uuid),
  });
}

export function useSetSettled() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: SetSettledRequest }) =>
      expensesApi.setSettled(uuid, body),
    onSuccess: (_data, { uuid }) => invalidateExpense(uuid),
  });
}

export function useAddShare() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: CreateShareRequest }) =>
      expensesApi.addShare(uuid, body),
    onSuccess: (_data, { uuid }) => invalidateExpense(uuid),
  });
}

export function useUpdateShare() {
  return useMutation({
    mutationFn: ({
      uuid,
      shareUuid,
      body,
    }: {
      uuid: string;
      shareUuid: string;
      body: UpdateShareRequest;
    }) => expensesApi.updateShare(uuid, shareUuid, body),
    onSuccess: (_data, { uuid }) => invalidateExpense(uuid),
  });
}

export function useDeleteShare() {
  return useMutation({
    mutationFn: ({ uuid, shareUuid }: { uuid: string; shareUuid: string }) =>
      expensesApi.removeShare(uuid, shareUuid),
    onSuccess: (_data, { uuid }) => invalidateExpense(uuid),
  });
}

/**
 * Per-expense CSV export. Resolves the blob then triggers a browser download
 * using the server-provided filename (fallback supplied by the caller). Toast /
 * error side-effects stay in the component.
 */
export function useExportExpense() {
  return useMutation({
    mutationFn: async ({
      uuid,
      fallbackName,
    }: {
      uuid: string;
      fallbackName: string;
    }) => {
      const result = await expensesApi.exportCsv(uuid);
      downloadBlob(result, fallbackName);
    },
  });
}
