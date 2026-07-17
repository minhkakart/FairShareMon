import { useQuery } from "@tanstack/react-query";
import { statsApi } from "../api/statsApi";
import type { ByCategoryStatsRequest, StatsRangeRequest } from "../api/types";

/**
 * Query-key factory for the read-only stats feature. `all` is the invalidation
 * root; the cache keys embed the exact request so a changed range refetches and
 * an identical range is deduped (the home + Stats page share the this-month
 * caches). No mutations — nothing invalidates these beyond React Query defaults.
 */
export const statsKeys = {
  all: ["stats"] as const,
  overview: (range: StatsRangeRequest) => ["stats", "overview", range] as const,
  byCategory: (req: ByCategoryStatsRequest) =>
    ["stats", "by-category", req] as const,
};

/** Overview KPIs (total spending + expense count) for a range. */
export function useOverviewQuery(range: StatsRangeRequest, enabled = true) {
  return useQuery({
    queryKey: statsKeys.overview(range),
    queryFn: () => statsApi.overview(range),
    enabled,
  });
}

/** Per-category breakdown (total DESC, rendered verbatim) for a range. */
export function useByCategoryQuery(req: ByCategoryStatsRequest, enabled = true) {
  return useQuery({
    queryKey: statsKeys.byCategory(req),
    queryFn: () => statsApi.byCategory(req),
    enabled,
  });
}
