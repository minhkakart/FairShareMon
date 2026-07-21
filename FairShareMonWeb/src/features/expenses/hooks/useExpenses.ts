import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { downloadBlob } from "@/lib/download/downloadBlob";
import { eventsKeys } from "@/features/events/hooks/useEvents";
import { expensesApi } from "../api/expensesApi";
import type {
  AssignEventRequest,
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

/**
 * Create an expense (+ its shares). Invalidates the expenses caches; when the
 * created expense joined an event, also invalidates `eventsKeys.all` so the
 * event detail's `expenseCount` (`eventsKeys.detail`) and balance
 * (`eventsKeys.balance`) refresh — mirrors `useAssignExpenseEvent`. Loose
 * creates skip the events refetch.
 */
export function useCreateExpense() {
  return useMutation({
    mutationFn: (body: CreateExpenseRequest) => expensesApi.create(body),
    onSuccess: (expense) => {
      invalidateExpense(expense.uuid);
      if (expense.eventUuid) {
        void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
      }
    },
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

/**
 * Per-share settled toggle (Layer A). Invalidates the expenses caches ONLY
 * (OQ7a): the event overlay `outstanding` is Layer-B (net) driven, so a
 * per-share (gross) flip does not change the balance overlay. The expense-detail
 * refetch surfaces any backend recompute of the whole-expense `isSettled`.
 */
export function useSetShareSettled() {
  return useMutation({
    mutationFn: ({
      expenseUuid,
      shareUuid,
      body,
    }: {
      expenseUuid: string;
      shareUuid: string;
      body: SetSettledRequest;
    }) => expensesApi.setShareSettled(expenseUuid, shareUuid, body),
    onSuccess: (_data, { expenseUuid }) => invalidateExpense(expenseUuid),
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
 * Assign / move an expense to an event, or remove it from its event (M5). Both
 * invalidate the expenses caches (linkage + detail) AND the events caches
 * (`eventsKeys.all` for counts + the balance/detail reflect the change) so the
 * event detail's expense list, counts, and balance refresh.
 */
export function useAssignExpenseEvent() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: AssignEventRequest }) =>
      expensesApi.assignEvent(uuid, body),
    onSuccess: (_data, { uuid }) => {
      invalidateExpense(uuid);
      void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
    },
  });
}

export function useRemoveExpenseEvent() {
  return useMutation({
    mutationFn: (uuid: string) => expensesApi.removeEvent(uuid),
    onSuccess: (_data, uuid) => {
      invalidateExpense(uuid);
      void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
    },
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
