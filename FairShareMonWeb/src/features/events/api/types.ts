/**
 * Event DTOs — mirror `FairShareMonApi/Models/Events/**` and the balance DTOs
 * `Models/Stats/{EventBalanceResponse,MemberBalanceRow}.cs`. Feature-local per
 * the feature-first convention. Datetimes are offset-aware ISO-8601 strings;
 * money (`advanced`/`owed`/`balance`) is typed `number` — the API returns the
 * server-computed value and the UI renders it, never re-derives it (R3).
 */

/** Summary row for the events list (`EventSummaryResponse`). */
export interface EventSummaryResponse {
  uuid: string;
  name: string;
  /** Start day, 00:00:00 in the request timezone → UTC. */
  startDate: string;
  /** End day, 23:59:59.999999 in the request timezone → UTC. */
  endDate: string;
  isClosed: boolean;
  /** Present once the event has been closed. */
  closedAt?: string | null;
  /** Derived count of expenses in the event. */
  expenseCount: number;
  createdAt: string;
}

/** Full event detail (`EventResponse`) — adds `description`. Does not embed expenses (backend OQ15). */
export interface EventResponse {
  uuid: string;
  name: string;
  description?: string | null;
  startDate: string;
  endDate: string;
  isClosed: boolean;
  closedAt?: string | null;
  expenseCount: number;
  createdAt: string;
}

/** `CreateEventRequest` — new events are always open. */
export interface CreateEventRequest {
  name: string;
  description?: string | null;
  /** Offset-aware ISO pinning the intended start calendar day (noon-anchored, OQ5a). */
  startDate: string;
  /** Offset-aware ISO pinning the intended end calendar day (noon-anchored, OQ5a). */
  endDate: string;
}

/** `UpdateEventRequest` — same shape; open-only. */
export interface UpdateEventRequest {
  name: string;
  description?: string | null;
  startDate: string;
  endDate: string;
}

/** List filter (`EventFilter`). Only defined keys are sent. */
export interface EventFilter {
  /** true = closed, false = open, undefined = all. */
  closed?: boolean;
}

/**
 * One member's balance in an event (`MemberBalanceRow`). Denormalized — carries
 * its own `memberUuid`/`memberName` so soft-deleted members still render (§4.7).
 * `balance` = `advanced` − `owed`; positive = owed to this member, negative = this
 * member owes. Rendered verbatim, never re-computed.
 */
export interface MemberBalanceRow {
  memberUuid: string;
  memberName: string;
  isOwnerRepresentative: boolean;
  isDeleted: boolean;
  advanced: number;
  owed: number;
  balance: number;
}

/**
 * The event debt-balance (`EventBalanceResponse`, §3.7). Viewable for open AND
 * closed events; `settled` is ignored; the row set sums to zero on `balance`; an
 * event with no expenses returns an empty `rows`.
 */
export interface EventBalanceResponse {
  eventUuid: string;
  eventName: string;
  isClosed: boolean;
  rows: MemberBalanceRow[];
}
