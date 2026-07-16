import { useEffect } from "react";
import { getSession } from "@/lib/auth/session";
import { refreshOnce } from "@/lib/api/refresh";

// Module-level guard so React 19 StrictMode's double-invoked effect (dev) does
// not fire two boot refreshes.
let bootstrapped = false;

/**
 * On boot: if a refresh token survived in localStorage, exchange it for a fresh
 * access token (rehydrate). Success → authenticated; failure → refreshOnce
 * already cleared the session (unauthenticated). No token → straight to
 * unauthenticated. Session status stays `idle` until this resolves so the
 * guards can show a boot splash.
 */
export function useSessionBootstrap(): void {
  useEffect(() => {
    if (bootstrapped) return;
    bootstrapped = true;

    const { refreshToken } = getSession();
    if (!refreshToken) {
      getSession().markUnauthenticated();
      return;
    }

    void refreshOnce().catch(() => {
      // refreshOnce clears the session + signals redirect on failure.
    });
  }, []);
}
