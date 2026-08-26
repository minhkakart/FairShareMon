import { useEffect, useState } from "react";
import { queryClient } from "@/lib/query/queryClient";
import { shareApi } from "../api/shareApi";
import { shareKeys } from "./useShare";

export type ShareStreamTerminalReason = "revoked" | "expired";

/**
 * Subscribes to the public share's live-update stream while `enabled` (the
 * report is successfully loaded). `updated` invalidates the report + QR-list
 * query caches (TanStack Query's own `enabled`-gating decides whether either
 * actually refetches — see planning doc Background). `revoked`/`expired` close
 * the connection (the server does not suppress EventSource's auto-reconnect —
 * the client must call `.close()` itself) and are surfaced as a terminal reason
 * for the page to render a distinct "no longer live" state (OQ1).
 */
export function useEventShareStream(
  token: string,
  { enabled }: { enabled: boolean },
): { terminalReason: ShareStreamTerminalReason | null } {
  const [terminalReason, setTerminalReason] = useState<ShareStreamTerminalReason | null>(null);

  useEffect(() => {
    if (!enabled || !token) return;
    setTerminalReason(null); // reset on (re)connect — e.g. a token change while mounted

    const source = new EventSource(shareApi.publicStreamUrl(token));

    source.addEventListener("updated", () => {
      void queryClient.invalidateQueries({ queryKey: shareKeys.public(token) });
      void queryClient.invalidateQueries({ queryKey: shareKeys.publicQrs(token) });
    });
    source.addEventListener("revoked", () => {
      setTerminalReason("revoked");
      source.close();
    });
    source.addEventListener("expired", () => {
      setTerminalReason("expired");
      source.close();
    });
    // 'connected' is a harmless liveness ping — no listener needed (an unhandled
    // named event is simply not dispatched to anything, per EventTarget). The
    // native 'error' event is also intentionally unhandled: EventSource already
    // retries transient drops on its own, and a terminal (non-2xx) failure carries
    // no readable reason — see Assumptions on why that case falls back to the
    // plain report's own staleTime rather than a guessed message here.

    return () => source.close();
  }, [token, enabled]);

  return { terminalReason };
}
