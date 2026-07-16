import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { authApi } from "../api/authApi";
import { getSession } from "@/lib/auth/session";
import { useSessionStatus } from "./useAuth";
import { isApiError } from "@/lib/api/errors";
import { queryClient } from "@/lib/query/queryClient";

/**
 * Query key for the current-user profile. Exported so future flows (e.g. a
 * successful upgrade-to-Premium) can invalidate it to force a fresh read.
 */
export const currentUserQueryKey = ["auth", "me"] as const;

/**
 * Manual invalidation seam (OQ4a): re-fetch `/auth/me` on demand. No flow needs
 * this yet — it exists so a future tier/role-changing action can refresh the
 * session profile without waiting for the next boot.
 */
export function invalidateCurrentUser(): Promise<void> {
  return queryClient.invalidateQueries({ queryKey: currentUserQueryKey });
}

/**
 * Fetches the signed-in user's profile (`GET /auth/me`) once the session is
 * authenticated and syncs it into the Zustand session store — the single source
 * of truth the guards and shell read. One code path covers both entry points:
 * login and boot-refresh rehydrate both flip `status` to `authenticated`, which
 * enables this query (OQ1a).
 *
 * Freshness (OQ4a): `staleTime: Infinity` + no window-focus refetch → one fetch
 * per login/boot; `queryClient.clear()` on logout drops the cache.
 *
 * Failure handling:
 *  - `401`/revoked rides the client's `401 → refresh-once → retry → else clear`
 *    flow (handled in `lib/api/client.ts`); nothing extra here.
 *  - a non-401 failure (network/500) while tokens are valid stays authenticated
 *    but degraded (OQ3a): the store settles to `profileStatus: "error"` with the
 *    `user` untouched, so non-admin surfaces work and the admin guard fail-safe
 *    denies. Genuine network errors auto-retry once; `refetch()` offers a manual
 *    retry.
 *
 * Mount ONCE at the authenticated boundary (`ProtectedRoute`) so the store sync
 * runs in exactly one place; guards read the store, not this query.
 */
export function useCurrentUserQuery() {
  const status = useSessionStatus();

  const query = useQuery({
    queryKey: currentUserQueryKey,
    queryFn: () => authApi.me(),
    enabled: status === "authenticated",
    staleTime: Infinity,
    retry: (failureCount, error) =>
      isApiError(error) && error.isNetwork && failureCount < 1,
  });

  const { data, isSuccess, isError } = query;

  useEffect(() => {
    if (isSuccess && data) {
      getSession().setUser({
        uuid: data.uuid,
        username: data.username,
        tier: data.tier,
        role: data.role,
        createdAt: data.createdAt,
      });
    } else if (isError) {
      getSession().markProfileUnavailable();
    }
  }, [isSuccess, isError, data]);

  return query;
}
