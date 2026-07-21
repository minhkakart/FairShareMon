import { api } from "@/lib/api/client";
import type { QueryValue } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import type {
  AssignEventRequest,
  AuditLogResponse,
  CreateExpenseRequest,
  CreateShareRequest,
  ExpenseFilter,
  ExpenseResponse,
  ExpenseSummaryResponse,
  SetSettledRequest,
  ShareResponse,
  UpdateExpenseRequest,
  UpdateShareRequest,
} from "./types";

/** Build the query object for `list`, sending only defined filter keys. */
function filterQuery(filter: ExpenseFilter): Record<string, QueryValue> {
  return {
    from: filter.from,
    to: filter.to,
    categoryUuid: filter.categoryUuid,
    tagUuid: filter.tagUuid,
    settled: filter.settled,
    looseOnly: filter.looseOnly,
    eventUuid: filter.eventUuid,
  };
}

/**
 * Expense endpoints (`api/v1/expenses`) + the shares / settled / history / export
 * sub-routes. All authenticated + resource-owned: an expense/share that isn't the
 * caller's yields 404 (codes 6000 / 7000). The centralized client handles the
 * envelope unwrap, auth, refresh, and error typing. The backend returns the list
 * in `expenseTime` DESC order — rendered verbatim (no client re-sort).
 */
export const expensesApi = {
  list: (filter: ExpenseFilter) =>
    api.get<ExpenseSummaryResponse[]>("/v1/expenses", {
      query: filterQuery(filter),
    }),

  get: (uuid: string) => api.get<ExpenseResponse>(`/v1/expenses/${uuid}`),

  create: (body: CreateExpenseRequest) =>
    api.post<ExpenseResponse>("/v1/expenses", body),

  update: (uuid: string, body: UpdateExpenseRequest) =>
    api.put<ExpenseResponse>(`/v1/expenses/${uuid}`, body),

  remove: (uuid: string) =>
    api.delete<MessageResponse>(`/v1/expenses/${uuid}`),

  setSettled: (uuid: string, body: SetSettledRequest) =>
    api.put<MessageResponse>(`/v1/expenses/${uuid}/settled`, body),

  /** Per-share settled toggle (Layer A). Allowed on a closed event's expense. */
  setShareSettled: (
    expenseUuid: string,
    shareUuid: string,
    body: SetSettledRequest,
  ) =>
    api.put<MessageResponse>(
      `/v1/expenses/${expenseUuid}/shares/${shareUuid}/settled`,
      body,
    ),

  addShare: (uuid: string, body: CreateShareRequest) =>
    api.post<ShareResponse>(`/v1/expenses/${uuid}/shares`, body),

  updateShare: (uuid: string, shareUuid: string, body: UpdateShareRequest) =>
    api.put<ShareResponse>(`/v1/expenses/${uuid}/shares/${shareUuid}`, body),

  removeShare: (uuid: string, shareUuid: string) =>
    api.delete<MessageResponse>(`/v1/expenses/${uuid}/shares/${shareUuid}`),

  history: (uuid: string) =>
    api.get<AuditLogResponse[]>(`/v1/expenses/${uuid}/history`),

  /** Assign / move the expense to an event (M5). Target must be owned + OPEN. */
  assignEvent: (uuid: string, body: AssignEventRequest) =>
    api.put<ExpenseResponse>(`/v1/expenses/${uuid}/event`, body),

  /** Remove the expense from its event (M5). No-op if already loose. */
  removeEvent: (uuid: string) =>
    api.delete<MessageResponse>(`/v1/expenses/${uuid}/event`),

  /** Binary CSV export (blob path, not the JSON envelope). */
  exportCsv: (uuid: string) =>
    api.blob("GET", `/v1/expenses/${uuid}/export`, {
      query: { format: "csv" },
    }),
};
