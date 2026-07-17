import { api } from "@/lib/api/client";
import type { BlobResult } from "@/lib/api/client";

/**
 * VietQR image endpoints. The routes live on the expense/event resources, but
 * the concept + the shared `QrDialog` are wallet-owned, so both detail mods
 * import one place. Each returns a PNG `Blob` via the centralized binary path
 * (`api.blob`); error responses still arrive as the JSON envelope and throw a
 * typed `ApiError`. Both endpoints are Premium (403 13003).
 */
export const qrApi = {
  /** Per-expense transfer QR (square PNG). Optional non-default destination. */
  expenseQr: (uuid: string, bankAccountUuid?: string): Promise<BlobResult> =>
    api.blob("GET", `/v1/expenses/${uuid}/qr`, {
      query: { bankAccountUuid },
    }),

  /** Per-event composite settlement QR (portrait PNG). Closed events only. */
  eventQr: (uuid: string, bankAccountUuid?: string): Promise<BlobResult> =>
    api.blob("GET", `/v1/events/${uuid}/qr`, {
      query: { bankAccountUuid },
    }),
};
