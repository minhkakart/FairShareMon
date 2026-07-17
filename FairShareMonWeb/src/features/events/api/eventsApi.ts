import { api } from "@/lib/api/client";
import type { QueryValue } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import type {
  CreateEventRequest,
  EventBalanceResponse,
  EventFilter,
  EventResponse,
  EventSummaryResponse,
  UpdateEventRequest,
} from "./types";

/** Build the query object for `list`, sending only defined filter keys. */
function filterQuery(filter: EventFilter): Record<string, QueryValue> {
  return { closed: filter.closed };
}

/**
 * Event endpoints (`api/v1/events`) + the balance / export / close sub-routes.
 * All authenticated + resource-owned: an event that isn't the caller's yields
 * 404 (code 9000). The centralized client handles the envelope unwrap, auth,
 * refresh, and error typing. The backend returns the list in `startDate` DESC
 * then `createdAt` DESC order — rendered verbatim (no client re-sort).
 */
export const eventsApi = {
  list: (filter: EventFilter) =>
    api.get<EventSummaryResponse[]>("/v1/events", {
      query: filterQuery(filter),
    }),

  get: (uuid: string) => api.get<EventResponse>(`/v1/events/${uuid}`),

  create: (body: CreateEventRequest) =>
    api.post<EventResponse>("/v1/events", body),

  update: (uuid: string, body: UpdateEventRequest) =>
    api.put<EventResponse>(`/v1/events/${uuid}`, body),

  remove: (uuid: string) => api.delete<MessageResponse>(`/v1/events/${uuid}`),

  /** One-way close (no body). Never reopenable. */
  close: (uuid: string) =>
    api.put<MessageResponse>(`/v1/events/${uuid}/close`),

  balance: (uuid: string) =>
    api.get<EventBalanceResponse>(`/v1/events/${uuid}/balance`),

  /** Binary CSV export (blob path, not the JSON envelope). Open + closed events. */
  exportCsv: (uuid: string) =>
    api.blob("GET", `/v1/events/${uuid}/export`, {
      query: { format: "csv" },
    }),
};
