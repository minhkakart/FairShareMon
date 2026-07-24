import { api } from "@/lib/api/client";
import type { MemberQrResponse } from "@/features/wallet/api/types";
import type {
  CreateShareLinkRequest,
  PublicEventShareResponse,
  ShareLinkResponse,
} from "./types";

/**
 * Event share-link endpoints over the centralized `api` (envelope unwrapped;
 * errors thrown as typed `ApiError`).
 *
 * The authed owner endpoints inject the Bearer token as usual. The two public
 * endpoints are ANONYMOUS by design — called with `{ anonymous: true,
 * skipAuthRefresh: true }` so they carry no `Authorization` header and never
 * trip the client's `401 → refresh → login` flow for an unauthenticated visitor.
 */
export const shareApi = {
  /** The event's active share link, or `null` when not shared yet (OQ1). */
  getActiveLink: (eventUuid: string) =>
    api.get<ShareLinkResponse | null>(`/v1/events/${eventUuid}/share`),

  /** Create (or, with `regenerate`, replace) the event's share link. */
  createLink: (eventUuid: string, body: CreateShareLinkRequest) =>
    api.post<ShareLinkResponse>(`/v1/events/${eventUuid}/share`, body),

  /** Revoke the event's active share link. */
  revokeLink: (eventUuid: string) =>
    api.delete<{ message: string }>(`/v1/events/${eventUuid}/share`),

  /** Anonymous: the public read-only report for a token. */
  getPublicShare: (token: string) =>
    api.get<PublicEventShareResponse>(`/v1/public/shares/${token}`, {
      anonymous: true,
      skipAuthRefresh: true,
    }),

  /** Anonymous: the per-member VietQR images for a token (may be empty). */
  getPublicShareMemberQrs: (token: string) =>
    api.get<MemberQrResponse[]>(`/v1/public/shares/${token}/qr/members`, {
      anonymous: true,
      skipAuthRefresh: true,
    }),
};
