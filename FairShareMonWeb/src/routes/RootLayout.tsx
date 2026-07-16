import { useEffect } from "react";
import { Outlet, useNavigate } from "react-router-dom";
import { registerSessionExpiredHandler } from "@/lib/api/runtime";

/**
 * Top layout. Registers the API client's "session expired" callback with the
 * router so a failed refresh (incl. reuse-detection) routes to /login. This is
 * the one place the framework-agnostic client meets React Router.
 */
export function RootLayout() {
  const navigate = useNavigate();

  useEffect(() => {
    registerSessionExpiredHandler(() => {
      void navigate("/login", { replace: true });
    });
    return () => registerSessionExpiredHandler(null);
  }, [navigate]);

  return <Outlet />;
}
