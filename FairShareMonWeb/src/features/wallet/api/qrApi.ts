import { api } from "@/lib/api/client";
import type { MemberQrResponse } from "./types";

/**
 * Per-member VietQR endpoints. The routes live on the expense/event resources,
 * but the concept + the shared `QrDialog` are wallet-owned, so both detail mods
 * import one place. Each returns the list of still-owing members with their OWN
 * single QR as a `data:image/png;base64,<…>` data URL (JSON via the centralized
 * `api.get`, which unwraps `ApiResult<T>`; error responses throw a typed
 * `ApiError`). Both endpoints are Premium (403 13003); the event route is
 * closed-only (400 12002).
 */
export const qrApi = {
  /** Still-owing members of an expense, each with their own transfer QR. */
  expenseMemberQrs: (uuid: string, bankAccountUuid?: string) =>
    api.get<MemberQrResponse[]>(`/v1/expenses/${uuid}/qr/members`, {
      query: { bankAccountUuid },
    }),

  /** Still-owing members of a closed event, each with their own settlement QR. */
  eventMemberQrs: (uuid: string, bankAccountUuid?: string) =>
    api.get<MemberQrResponse[]>(`/v1/events/${uuid}/qr/members`, {
      query: { bankAccountUuid },
    }),
};
