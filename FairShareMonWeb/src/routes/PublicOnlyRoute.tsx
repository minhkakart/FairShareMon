import { Navigate, Outlet } from "react-router-dom";
import { useSessionStatus } from "@/features/auth/hooks/useAuth";
import { BootSplash } from "./BootSplash";

/** Public auth routes: already-authenticated users skip straight to the app. */
export function PublicOnlyRoute() {
  const status = useSessionStatus();
  if (status === "idle") return <BootSplash />;
  if (status === "authenticated") return <Navigate to="/dashboard" replace />;
  return <Outlet />;
}
