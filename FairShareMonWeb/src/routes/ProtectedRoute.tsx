import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useSessionStatus } from "@/features/auth/hooks/useAuth";
import { BootSplash } from "./BootSplash";

/**
 * Gates the authenticated app. While the session is still rehydrating (`idle`)
 * we hold on a boot splash so we neither flash the login screen nor render
 * protected content prematurely. Unauthenticated users are redirected to
 * /login with the intended path preserved for post-login return.
 */
export function ProtectedRoute() {
  const status = useSessionStatus();
  const location = useLocation();

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
