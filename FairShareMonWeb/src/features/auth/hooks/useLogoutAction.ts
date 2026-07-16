import { useNavigate } from "react-router-dom";
import { useT } from "@/i18n/useT";
import { useLogout } from "./useAuth";
import { getSession } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { useToast } from "@/app/ToastHost";

/**
 * The one logout sequence shared by the shell and the settings page: best-effort
 * server revoke, then always clear the local session + query cache, toast, and
 * route to /login. De-dupes the logic so both entry points behave identically.
 */
export function useLogoutAction() {
  const { t } = useT();
  const navigate = useNavigate();
  const logout = useLogout();
  const toast = useToast();

  const doLogout = async () => {
    try {
      await logout.mutateAsync();
    } catch {
      // Revoke is best-effort; clear locally regardless.
    }
    getSession().clearSession();
    queryClient.clear();
    toast.push({ tone: "success", title: t("auth:logout.success") });
    void navigate("/login", { replace: true });
  };

  return { doLogout, isPending: logout.isPending };
}
