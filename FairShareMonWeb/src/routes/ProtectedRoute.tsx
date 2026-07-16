import { Navigate, Outlet, useLocation } from "react-router-dom";
import {
  useCurrentUserQuery,
  useSessionStatus,
} from "@/features/auth/hooks/useAuth";
import { BootSplash } from "./BootSplash";

/**
 * Gates the authenticated app. While the session is still rehydrating (`idle`)
 * we hold on a boot splash so we neither flash the login screen nor render
 * protected content prematurely. Unauthenticated users are redirected to
 * /login with the intended path preserved for post-login return.
 *
 * Mounts the current-user query here (the single authenticated boundary above
 * both the shell and the admin area) so `/auth/me` runs exactly once as soon as
 * `status` flips to `authenticated` — covering login-navigate and boot-rehydrate
 * uniformly (OQ1a). The `<Outlet />` is NOT gated on it: the shell renders
 * immediately and the profile reconciles the account label + admin guard as it
 * resolves (OQ2a).
 */
export function ProtectedRoute() {
  const status = useSessionStatus();
  const location = useLocation();
  // Fires only while authenticated (the hook's query is `enabled` on status);
  // harmless no-op during `idle`/`unauthenticated`.
  useCurrentUserQuery();

  if (status === "idle") return <BootSplash />;
  if (status === "unauthenticated") {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: location.pathname + location.search }}
      />
    );
  }
  return <Outlet />;
}
