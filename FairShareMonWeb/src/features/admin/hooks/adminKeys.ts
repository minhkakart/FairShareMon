import type {
  AdminMetricsRequest,
  AdminUserListRequest,
  RevenueRequest,
} from "../api/types";

/**
 * Query-key factory for the admin feature. `all` (`["admin"]`) is the invalidation
 * root; each key embeds its exact request so a changed range/filter refetches and
 * an identical one is deduped. Prefixes are hierarchical so a mutation can
 * invalidate a whole subtree (`["admin","users"]` covers both the list and the
 * detail; `["admin","metrics"]` covers every range variant).
 */
export const adminKeys = {
  all: ["admin"] as const,
  metrics: (req: AdminMetricsRequest) => ["admin", "metrics", req] as const,
  revenue: (req: RevenueRequest) => ["admin", "revenue", req] as const,
  users: () => ["admin", "users"] as const,
  userList: (req: AdminUserListRequest) =>
    ["admin", "users", "list", req] as const,
  user: (uuid: string) => ["admin", "users", "detail", uuid] as const,
};
