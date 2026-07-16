import { useMutation } from "@tanstack/react-query";
import { authApi } from "../api/authApi";
import { useSession } from "@/lib/auth/session";
import type {
  ProfileStatus,
  SessionStatus,
  SessionUser,
} from "@/lib/auth/session";

export {
  useCurrentUserQuery,
  invalidateCurrentUser,
  currentUserQueryKey,
} from "./useCurrentUserQuery";

/** TanStack Query mutations over the auth endpoints. Session/navigation
 *  side-effects are orchestrated by the calling page. */
export function useLogin() {
  return useMutation({ mutationFn: authApi.login });
}

export function useRegister() {
  return useMutation({ mutationFn: authApi.register });
}

export function useLogout() {
  return useMutation({ mutationFn: authApi.logout });
}

export function useChangePassword() {
  return useMutation({ mutationFn: authApi.changePassword });
}

/** Current-user selector over the session store. */
export function useCurrentUser(): SessionUser | null {
  return useSession((state) => state.user);
}

export function useSessionStatus(): SessionStatus {
  return useSession((state) => state.status);
}

/** Lifecycle of the `/auth/me` profile fetch (drives the admin-guard splash). */
export function useProfileStatus(): ProfileStatus {
  return useSession((state) => state.profileStatus);
}

export function useIsAuthenticated(): boolean {
  return useSession((state) => state.status === "authenticated");
}
