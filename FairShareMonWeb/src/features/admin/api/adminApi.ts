import { api } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import { assertNoLedgerKeys } from "./privacy";
import type {
  AdminMetricsRequest,
  AdminMetricsResponse,
  AdminUserDetailResponse,
  AdminUserListRequest,
  AdminUserRow,
  GrantTierRequest,
  PagedResult,
  ResetPasswordRequest,
  ResetPasswordResponse,
  RevenueRequest,
  RevenueResponse,
  RevokeTierRequest,
  SetRoleRequest,
  TierGrantRow,
} from "./types";

/**
 * Admin endpoints (`api/v1/admin/*`). ADMIN-only (the backend answers 403 `1004`
 * to non-admins; the SPA gates the whole area behind `AdminRoute`). The
 * centralized client unwraps the envelope, injects auth / `X-Time-Zone` /
 * `Accept-Language`, handles `401 → refresh`, and drops `undefined`/`null` query
 * keys (so an omitted bound = all-time and a cleared filter = no filter).
 *
 * Every read is wrapped in the DEV-only `assertNoLedgerKeys` tripwire (OQ2a) — a
 * belt over the backend's R10 guarantee that no ledger field ever reaches here.
 *
 * `reset-password` returns the temp password EXACTLY once; the caller holds it in
 * component state only — it is never cached, persisted, or logged.
 */
export const adminApi = {
  // --- Dashboards ---------------------------------------------------------
  metrics: (req: AdminMetricsRequest) =>
    api
      .get<AdminMetricsResponse>("/v1/admin/dashboard", {
        query: { from: req.from, to: req.to, bucket: req.bucket },
      })
      .then(assertNoLedgerKeys),

  revenue: (req: RevenueRequest) =>
    api
      .get<RevenueResponse>("/v1/admin/revenue", {
        query: { from: req.from, to: req.to, bucket: req.bucket },
      })
      .then(assertNoLedgerKeys),

  // --- Users --------------------------------------------------------------
  listUsers: (req: AdminUserListRequest) =>
    api
      .get<PagedResult<AdminUserRow>>("/v1/admin/users", {
        query: {
          tier: req.tier,
          status: req.status,
          role: req.role,
          search: req.search,
          page: req.page,
          pageSize: req.pageSize,
          sort: req.sort,
          direction: req.direction,
        },
      })
      .then(assertNoLedgerKeys),

  getUser: (uuid: string) =>
    api
      .get<AdminUserDetailResponse>(`/v1/admin/users/${uuid}`)
      .then(assertNoLedgerKeys),

  // --- Sensitive actions --------------------------------------------------
  grantTier: (uuid: string, body: GrantTierRequest) =>
    api
      .post<TierGrantRow>(`/v1/admin/users/${uuid}/tier/grant`, body)
      .then(assertNoLedgerKeys),

  revokeTier: (uuid: string, body: RevokeTierRequest) =>
    api
      .post<TierGrantRow>(`/v1/admin/users/${uuid}/tier/revoke`, body)
      .then(assertNoLedgerKeys),

  disableUser: (uuid: string) =>
    api.post<MessageResponse>(`/v1/admin/users/${uuid}/disable`),

  enableUser: (uuid: string) =>
    api.post<MessageResponse>(`/v1/admin/users/${uuid}/enable`),

  revokeTokens: (uuid: string) =>
    api.post<MessageResponse>(`/v1/admin/users/${uuid}/revoke-tokens`),

  /** One-time temp-password reveal. The response is NEVER cached (see hooks). */
  resetPassword: (uuid: string, body: ResetPasswordRequest) =>
    api.post<ResetPasswordResponse>(
      `/v1/admin/users/${uuid}/reset-password`,
      body,
    ),

  setRole: (uuid: string, body: SetRoleRequest) =>
    api.post<MessageResponse>(`/v1/admin/users/${uuid}/role`, body),
};
