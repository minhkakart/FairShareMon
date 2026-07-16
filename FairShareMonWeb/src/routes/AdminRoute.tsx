import { Outlet } from "react-router-dom";
import {
  useCurrentUser,
  useProfileStatus,
  useSessionStatus,
} from "@/features/auth/hooks/useAuth";
import { BootSplash } from "./BootSplash";
import { Forbidden } from "./Forbidden";

/**
 * Admin-area guard. `role` now arrives from `GET /auth/me` (mounted at
 * `ProtectedRoute`) and is synced into the session store. While the profile is
 * still resolving we hold on the boot splash rather than flash `Forbidden` at an
 * admin who deep-links to `/admin` (OQ5a); once the profile has settled —
 * `resolved` OR `error` — we admit ADMIN and deny everyone else. A failed
 * `/auth/me` (OQ3a) settles to `error` with no `role`, so it fails safe into a
 * deny, never an infinite splash. An absent/unknown role never yields ADMIN.
 */
export function AdminRoute() {
  const status = useSessionStatus();
  const profileStatus = useProfileStatus();
  const user = useCurrentUser();

  if (
    status === "authenticated" &&
    (profileStatus === "idle" || profileStatus === "pending")
  ) {
    return <BootSplash />;
  }

  return user?.role === "ADMIN" ? <Outlet /> : <Forbidden />;
}
