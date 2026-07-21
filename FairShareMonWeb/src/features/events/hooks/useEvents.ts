import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { downloadBlob } from "@/lib/download/downloadBlob";
import { expensesKeys } from "@/features/expenses/hooks/useExpenses";
import { eventsApi } from "../api/eventsApi";
import type {
  CreateEventRequest,
  EventFilter,
  SetSettledRequest,
  UpdateEventRequest,
} from "../api/types";

/**
 * Query-key factory for events. `all` is the invalidation root; `detail(uuid)`
 * and `balance(uuid)` are the per-event sub-caches. Every event write invalidates
 * `all` + the specific `detail`/`balance`; close/delete/update also reach into
 * `expenses` (close flips `eventIsClosed`, delete makes the event's expenses
 * loose) so the M4 write-guard + counts refresh.
 */
export const eventsKeys = {
  all: ["events"] as const,
  lists: () => ["events", "list"] as const,
  list: (filter: EventFilter) => ["events", "list", filter] as const,
  detail: (uuid: string) => ["events", "detail", uuid] as const,
  balance: (uuid: string) => ["events", "balance", uuid] as const,
};

/** The caller's events (startDate DESC then createdAt DESC — backend order, verbatim). */
export function useEventsQuery(filter: EventFilter) {
  return useQuery({
    queryKey: eventsKeys.list(filter),
    queryFn: () => eventsApi.list(filter),
  });
}

/** A single event's full detail. */
export function useEventQuery(uuid: string) {
  return useQuery({
    queryKey: eventsKeys.detail(uuid),
    queryFn: () => eventsApi.get(uuid),
  });
}

/** The event's debt-balance (open + closed). */
export function useEventBalanceQuery(uuid: string) {
  return useQuery({
    queryKey: eventsKeys.balance(uuid),
    queryFn: () => eventsApi.balance(uuid),
  });
}

/** Invalidate the events root + the specific detail/balance. */
function invalidateEvent(uuid?: string) {
  void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
  if (uuid) {
    void queryClient.invalidateQueries({ queryKey: eventsKeys.detail(uuid) });
    void queryClient.invalidateQueries({ queryKey: eventsKeys.balance(uuid) });
  }
}

/** Also invalidate the expenses caches (linkage / closed-guard / counts change). */
function invalidateEventAndExpenses(uuid?: string) {
  invalidateEvent(uuid);
  void queryClient.invalidateQueries({ queryKey: expensesKeys.all });
}

export function useCreateEvent() {
  return useMutation({
    mutationFn: (body: CreateEventRequest) => eventsApi.create(body),
    onSuccess: (event) => invalidateEvent(event.uuid),
  });
}

export function useUpdateEvent() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: UpdateEventRequest }) =>
      eventsApi.update(uuid, body),
    onSuccess: (event) => invalidateEventAndExpenses(event.uuid),
  });
}

export function useDeleteEvent() {
  return useMutation({
    mutationFn: (uuid: string) => eventsApi.remove(uuid),
    onSuccess: (_data, uuid) => invalidateEventAndExpenses(uuid),
  });
}

export function useCloseEvent() {
  return useMutation({
    mutationFn: (uuid: string) => eventsApi.close(uuid),
    onSuccess: (_data, uuid) => invalidateEventAndExpenses(uuid),
  });
}

/**
 * Per-member net-clearance settled toggle (Layer B, OQ7a). Invalidates the event
 * balance overlay + `eventsKeys.all` (so the summary counts refresh); it does NOT
 * reach the expenses caches — Layer B does not change expense/share data.
 */
export function useSetMemberSettled() {
  return useMutation({
    mutationFn: ({
      eventUuid,
      memberUuid,
      body,
    }: {
      eventUuid: string;
      memberUuid: string;
      body: SetSettledRequest;
    }) => eventsApi.setMemberSettled(eventUuid, memberUuid, body),
    onSuccess: (_data, { eventUuid }) => {
      void queryClient.invalidateQueries({
        queryKey: eventsKeys.balance(eventUuid),
      });
      void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
    },
  });
}

/**
 * Per-event CSV export (open + closed). Resolves the blob then triggers a
 * browser download using the server-provided filename (fallback supplied by the
 * caller). Toast / error side-effects stay in the component.
 */
export function useExportEvent() {
  return useMutation({
    mutationFn: async ({
      uuid,
      fallbackName,
    }: {
      uuid: string;
      fallbackName: string;
    }) => {
      const result = await eventsApi.exportCsv(uuid);
      downloadBlob(result, fallbackName);
    },
  });
}
