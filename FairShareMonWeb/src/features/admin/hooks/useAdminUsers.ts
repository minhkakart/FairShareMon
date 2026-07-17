import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { adminApi } from "../api/adminApi";
import type {
  AdminUserListRequest,
  GrantTierRequest,
  RevokeTierRequest,
  SetRoleRequest,
} from "../api/types";
import { adminKeys } from "./adminKeys";

/** The paged/filtered/sorted user list (account metadata only — R10). */
export function useAdminUsersQuery(req: AdminUserListRequest) {
  return useQuery({
    queryKey: adminKeys.userList(req),
    queryFn: () => adminApi.listUsers(req),
    // Keep the previous page visible while the next one loads (smoother paging).
    placeholderData: (prev) => prev,
  });
}

/** A single user's metadata + grant history. */
export function useAdminUserQuery(uuid: string, enabled = true) {
  return useQuery({
    queryKey: adminKeys.user(uuid),
    queryFn: () => adminApi.getUser(uuid),
    enabled,
    retry: false,
  });
}

/** Invalidate the whole user subtree (list + every detail). */
function invalidateUsers() {
  return queryClient.invalidateQueries({ queryKey: adminKeys.users() });
}
/** Invalidate the dashboards affected by tier grant/revoke + role changes. */
function invalidateDashboards() {
  void queryClient.invalidateQueries({ queryKey: ["admin", "metrics"] });
  void queryClient.invalidateQueries({ queryKey: ["admin", "revenue"] });
}

/**
 * Sensitive-action mutations. Each `onSuccess` invalidates the user subtree so the
 * list + detail refetch; tier grant/revoke + role changes also invalidate the
 * dashboards (they move the tier/role distributions + revenue). Toast/close/cache
 * side-effects beyond invalidation live in the calling dialog (the repo convention).
 *
 * `useResetPassword` deliberately does NOT cache its response — the temp password
 * is returned to the caller (via `mutateAsync`) and held in component state only.
 */
export function useGrantTier(uuid: string) {
  return useMutation({
    mutationFn: (body: GrantTierRequest) => adminApi.grantTier(uuid, body),
    onSuccess: () => {
      void invalidateUsers();
      invalidateDashboards();
    },
  });
}

export function useRevokeTier(uuid: string) {
  return useMutation({
    mutationFn: (body: RevokeTierRequest) => adminApi.revokeTier(uuid, body),
    onSuccess: () => {
      void invalidateUsers();
      invalidateDashboards();
    },
  });
}

export function useDisableUser(uuid: string) {
  return useMutation({
    mutationFn: () => adminApi.disableUser(uuid),
    onSuccess: invalidateUsers,
  });
}

export function useEnableUser(uuid: string) {
  return useMutation({
    mutationFn: () => adminApi.enableUser(uuid),
    onSuccess: invalidateUsers,
  });
}

export function useRevokeTokens(uuid: string) {
  return useMutation({
    mutationFn: () => adminApi.revokeTokens(uuid),
    // Token revocation changes no displayed metadata — nothing to invalidate.
  });
}

export function useResetPassword(uuid: string) {
  return useMutation({
    mutationFn: (newPassword: string) =>
      adminApi.resetPassword(uuid, { newPassword }),
    // No cache write: the caller consumes the result from `mutateAsync` and holds
    // the temp password in component state only (cleared on dialog close).
  });
}

export function useSetRole(uuid: string) {
  return useMutation({
    mutationFn: (body: SetRoleRequest) => adminApi.setRole(uuid, body),
    onSuccess: () => {
      void invalidateUsers();
      // Role changes move the role distribution.
      void queryClient.invalidateQueries({ queryKey: ["admin", "metrics"] });
    },
  });
}
