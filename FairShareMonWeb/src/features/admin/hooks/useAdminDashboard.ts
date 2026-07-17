import { useQuery } from "@tanstack/react-query";
import { adminApi } from "../api/adminApi";
import type { AdminMetricsRequest, RevenueRequest } from "../api/types";
import { adminKeys } from "./adminKeys";

/**
 * Read-only dashboard queries. The query keys embed the exact request (range +
 * bucket), so changing either refetches; the client drops omitted bounds so an
 * all-time range is a stable key. No metric is a ledger aggregate (R10).
 */
export function useMetricsQuery(req: AdminMetricsRequest, enabled = true) {
  return useQuery({
    queryKey: adminKeys.metrics(req),
    queryFn: () => adminApi.metrics(req),
    enabled,
  });
}

export function useRevenueQuery(req: RevenueRequest, enabled = true) {
  return useQuery({
    queryKey: adminKeys.revenue(req),
    queryFn: () => adminApi.revenue(req),
    enabled,
  });
}
