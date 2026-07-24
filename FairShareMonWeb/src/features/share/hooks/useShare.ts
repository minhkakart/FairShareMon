import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { shareApi } from "../api/shareApi";
import type { CreateShareLinkRequest } from "../api/types";

/**
 * Query-key factory for share links. `active` is the owner-side per-event link;
 * `public`/`publicQrs` are the anonymous reads keyed by token.
 */
export const shareKeys = {
  active: (eventUuid: string) => ["share", "active", eventUuid] as const,
  public: (token: string) => ["share", "public", token] as const,
  publicQrs: (token: string) => ["share", "public-qrs", token] as const,
};

/**
 * The event's active share link (owner side). `enabled` = dialog open && Premium
 * (a Free user's dialog shows the upgrade panel and never fires the query).
 * `retry: false` — the terminal codes (13003 / 16001 / ownership 404) are not
 * transient. `data: null` means "not shared yet" (OQ1), not an error.
 */
export function useActiveShareLinkQuery(eventUuid: string, enabled: boolean) {
  return useQuery({
    queryKey: shareKeys.active(eventUuid),
    queryFn: () => shareApi.getActiveLink(eventUuid),
    enabled,
    retry: false,
  });
}

/**
 * The public read-only report. `retry: false` because 16000 (expired / revoked /
 * missing) is terminal — retrying only re-probes the same not-found.
 */
export function usePublicShareQuery(token: string) {
  return useQuery({
    queryKey: shareKeys.public(token),
    queryFn: () => shareApi.getPublicShare(token),
    enabled: token.length > 0,
    retry: false,
  });
}

/**
 * Lazy per-member QR fetch — `enabled` flips true on the first QR-button click.
 * `gcTime: 0` mirrors `useQr.ts` (the data-URL images are large and single-use).
 */
export function usePublicShareMemberQrsQuery(
  token: string,
  { enabled }: { enabled: boolean },
) {
  return useQuery({
    queryKey: shareKeys.publicQrs(token),
    queryFn: () => shareApi.getPublicShareMemberQrs(token),
    enabled: enabled && token.length > 0,
    retry: false,
    gcTime: 0,
  });
}

/** Create / regenerate the share link. Invalidates the active-link cache. */
export function useCreateShareLink() {
  return useMutation({
    mutationFn: ({
      eventUuid,
      body,
    }: {
      eventUuid: string;
      body: CreateShareLinkRequest;
    }) => shareApi.createLink(eventUuid, body),
    onSuccess: (_data, { eventUuid }) => {
      void queryClient.invalidateQueries({
        queryKey: shareKeys.active(eventUuid),
      });
    },
  });
}

/** Revoke the share link. Invalidates the active-link cache. */
export function useRevokeShareLink() {
  return useMutation({
    mutationFn: (eventUuid: string) => shareApi.revokeLink(eventUuid),
    onSuccess: (_data, eventUuid) => {
      void queryClient.invalidateQueries({
        queryKey: shareKeys.active(eventUuid),
      });
    },
  });
}
