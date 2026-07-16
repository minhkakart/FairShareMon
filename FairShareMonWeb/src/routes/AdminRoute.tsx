import { Outlet } from "react-router-dom";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import { Forbidden } from "./Forbidden";

/**
 * Admin-area guard SEAM. The backend `UserResponse` does not currently expose a
 * `role` field (see planning/frontend-foundation.md — Assumptions + OQ), and
 * login returns no user payload, so `user.role` is always undefined today and
 * this guard denies (fail-safe: never assume ADMIN). When the admin cycle wires
 * a real role source, this comparison starts admitting ADMINs unchanged.
 */
export function AdminRoute() {
  const user = useCurrentUser();
  const isAdmin = user?.role === "ADMIN";
  return isAdmin ? <Outlet /> : <Forbidden />;
}
