import { api } from "@/lib/api/client";
import type {
  ByCategoryStatsRequest,
  ByCategoryStatsResponse,
  OverviewStatsResponse,
  StatsRangeRequest,
} from "./types";

/**
 * Stats endpoints (`api/v1/stats`). Read-only, authenticated + resource-owned.
 * The centralized client unwraps the envelope, injects auth / `X-Time-Zone` /
 * `Accept-Language`, handles `401 → refresh`, and drops `undefined`/`null` query
 * keys (so an omitted bound = all-time). Error codes: `1001` bad range (both
 * bounds set with `from > to`, or range + event together); `9000` event miss
 * (event-scope mode only). Reads impose no tier gate.
 */
export const statsApi = {
  overview: (range: StatsRangeRequest) =>
    api.get<OverviewStatsResponse>("/v1/stats/overview", {
      query: { from: range.from, to: range.to },
    }),

  byCategory: (req: ByCategoryStatsRequest) =>
    api.get<ByCategoryStatsResponse>("/v1/stats/by-category", {
      query: { from: req.from, to: req.to, eventUuid: req.eventUuid },
    }),
};
