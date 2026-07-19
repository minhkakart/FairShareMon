import { useEffect, useRef } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import { registerSessionExpiredHandler } from "@/lib/api/runtime";

/**
 * Top layout. Registers the API client's "session expired" callback with the
 * router so a failed refresh (incl. reuse-detection) routes to /login. This is
 * the one place the framework-agnostic client meets React Router.
 *
 * The redirect carries the location where the session expired as `state.from`
 * (same shape `ProtectedRoute` uses), so `LoginPage` returns the user there
 * after re-login instead of dumping them on the default page. A ref holds the
 * live location so the long-lived handler reads the current path at expiry time
 * without re-registering on every navigation.
 */
export function RootLayout() {
  const navigate = useNavigate();
  const location = useLocation();
  const locationRef = useRef(location);
  locationRef.current = location;

  useEffect(() => {
    registerSessionExpiredHandler(() => {
      const current = locationRef.current;
      void navigate("/login", {
        replace: true,
        state: { from: current.pathname + current.search },
      });
    });
    return () => registerSessionExpiredHandler(null);
  }, [navigate]);

  return <Outlet />;
}
