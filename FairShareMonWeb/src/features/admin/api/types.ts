/**
 * Admin DTOs — mirror `FairShareMonApi/Models/Admin/**` and `Models/PagedResult`.
 *
 * PRIVACY BOUNDARY (R10 — the milestone's defining constraint): these types
 * declare ONLY account metadata + tier-grant/revenue fields. NO ledger type
 * (members/expenses/events/shares/bank accounts) is importable or mappable in the
 * admin module. Money fields here are exclusively grant/revenue amounts
 * (`TierGrantRow.amount`, `RevenueBucketRow.total`, `RevenueResponse.totalRevenue`)
 * — the API computes them and the UI renders them verbatim via `<Money>` (R3).
 * Datetimes are ISO-8601 offset-aware.
 */

export type Tier = "FREE" | "PREMIUM";
export type Role = "USER" | "ADMIN";
export type Status = "ACTIVE" | "DISABLED";
export type Bucket = "month" | "day";

/** A key→count pair in a distribution (e.g. tier → user count). */
export interface MetricCount {
  key: string;
  count: number;
}

/** A signup count for one time bucket. */
export interface PeriodMetric {
  periodLabel: string;
  count: number;
}

/** `GET /admin/dashboard` request — omitted bounds = all-time. */
export interface AdminMetricsRequest {
  from?: string;
  to?: string;
  bucket: Bucket;
}

/** `GET /admin/dashboard` response — account-metadata metrics only (no ledger). */
export interface AdminMetricsResponse {
  from: string | null;
  to: string | null;
  totalUsers: number;
  tierDistribution: MetricCount[];
  roleDistribution: MetricCount[];
  statusDistribution: MetricCount[];
  signups: PeriodMetric[];
}

/** `GET /admin/revenue` request — omitted bounds = all-time. */
export interface RevenueRequest {
  from?: string;
  to?: string;
  bucket: Bucket;
}

/** Revenue for one time bucket. */
export interface RevenueBucketRow {
  periodLabel: string;
  total: number;
  grantCount: number;
}

/** `GET /admin/revenue` response — sourced ONLY from tier_grants (GRANT rows). */
export interface RevenueResponse {
  from: string | null;
  to: string | null;
  bucket: Bucket;
  buckets: RevenueBucketRow[];
  totalRevenue: number;
  grantCount: number;
  references: string[];
}

/** `GET /admin/users` request — all filters optional; paged + sorted. */
export interface AdminUserListRequest {
  tier?: Tier;
  status?: Status;
  role?: Role;
  search?: string;
  page: number;
  pageSize: number;
  sort: string;
  direction: "asc" | "desc";
}

/** One row in the admin user list — account metadata + grant summary only. */
export interface AdminUserRow {
  uuid: string;
  username: string;
  tier: Tier;
  role: Role;
  status: Status;
  createdAt: string;
  grantCount: number;
  lastGrantAt?: string | null;
}

/** Generic paged envelope (mirrors backend `PagedResult<T>`). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** One tier grant/revoke history row (from tier_grants). */
export interface TierGrantRow {
  uuid: string;
  tier: Tier;
  action: "GRANT" | "REVOKE";
  amount: number;
  currency: string;
  reference?: string | null;
  note?: string | null;
  grantedByUsername: string;
  createdAt: string;
}

/** `GET /admin/users/{uuid}` response — metadata + grant history (no ledger). */
export interface AdminUserDetailResponse {
  uuid: string;
  username: string;
  tier: Tier;
  role: Role;
  status: Status;
  createdAt: string;
  grants: TierGrantRow[];
}

/** `POST /admin/users/{uuid}/tier/grant` body. */
export interface GrantTierRequest {
  amount: number;
  currency?: string;
  reference?: string;
  note?: string;
}

/** `POST /admin/users/{uuid}/tier/revoke` body. */
export interface RevokeTierRequest {
  note?: string;
}

/** `POST /admin/users/{uuid}/reset-password` body (client-generated temp pw). */
export interface ResetPasswordRequest {
  newPassword: string;
}

/** `POST /admin/users/{uuid}/reset-password` response — the one-time temp pw. */
export interface ResetPasswordResponse {
  username: string;
  password: string;
}

/** `POST /admin/users/{uuid}/role` body. */
export interface SetRoleRequest {
  role: Role;
}
