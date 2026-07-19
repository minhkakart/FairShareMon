import { api } from "@/lib/api/client";

/**
 * Internal + wire shape for a bank-directory entry — identical to the backend
 * `BankResponse`, so no normalization/mapping layer is needed. `logoUrl` is built
 * server-side (the `imageId` never leaves the backend) and is rendered directly.
 */
export interface Bank {
  /** 6-digit NAPAS BIN — persisted as `bankBin`, used for QR. */
  bin: string;
  /** Short bank code (e.g. "TCB") — a search keyword. */
  code: string;
  /** Full legal name — search keyword + secondary line. */
  name: string;
  /** Brand short name (e.g. "Techcombank") — persisted into `bankName`, shown primary. */
  shortName: string;
  /** Fully-built public logo URL — rendered directly by `BankLogo`. */
  logoUrl: string;
}

/**
 * Bank-directory endpoint (`GET /api/v1/banks`). Authenticated reference data —
 * not Premium-gated, never empty (the backend has a static fallback). Auth,
 * `X-Time-Zone`, `Accept-Language`, envelope unwrap, refresh, and error typing all
 * happen in the centralized client (`src/lib/api/client.ts`).
 */
export const banksApi = {
  list: () => api.get<Bank[]>("/v1/banks"),
};
