/**
 * Stats DTOs — mirror `FairShareMonApi/Models/Stats/**`. Feature-local per the
 * feature-first convention. Money fields (`totalSpending`, `total`) are typed
 * `number`: the API returns the computed value and the UI renders it verbatim
 * (via `<Money>`) — it is NEVER derived or float-mathed on the client (R3).
 * Bar-length ratios and any "% share" are display-only ratios off these integer
 * totals, never a displayed money figure. Datetimes are ISO-8601 offset-aware.
 */

/** `GET /stats/overview` — the two scalars over an optional inclusive range. */
export interface OverviewStatsResponse {
  /** The `from` bound actually used (null = unbounded). */
  from: string | null;
  /** The `to` bound actually used (null = unbounded). */
  to: string | null;
  /** Total spending across the whole ledger (loose + event) in the range. */
  totalSpending: number;
  /** Number of expenses in the range. */
  expenseCount: number;
}

/** Query for `GET /stats/overview` — both bounds optional (omit = all-time). */
export interface StatsRangeRequest {
  from?: string;
  to?: string;
}

/** One category's aggregate (`CategoryStatRow`). */
export interface CategoryStatRow {
  categoryUuid: string;
  categoryName: string;
  /** Category color as `#RRGGBB` (identity swatch; NOT the chart bar fill). */
  color: string;
  /** Optional emoji glyph. */
  icon?: string | null;
  /** Soft-deleted category still shown in historical stats (§4.7). */
  isDeleted: boolean;
  /** Category total = SUM(shares.amount), authoritative from the API. */
  total: number;
  expenseCount: number;
}

/** `GET /stats/by-category` — rows in total-DESC order, rendered verbatim. */
export interface ByCategoryStatsResponse {
  /** Set in event-scope mode; null in time-range mode. */
  eventUuid: string | null;
  from: string | null;
  to: string | null;
  rows: CategoryStatRow[];
}

/**
 * Query for `GET /stats/by-category`. Time-range XOR event: sending `from`/`to`
 * together with `eventUuid` is a `1001`. M6's Stats page uses the time-range
 * lens only (OQ3a) — `eventUuid` is part of the contract but unused here.
 */
export interface ByCategoryStatsRequest {
  from?: string;
  to?: string;
  eventUuid?: string;
}
