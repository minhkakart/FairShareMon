/**
 * VietQR bank-directory client — THE ONE SANCTIONED RAW-`fetch` EXCEPTION.
 *
 * Every FairShareMonApi call goes through `src/lib/api/client.ts` (Bearer token,
 * `X-Time-Zone`, `Accept-Language`, and the `ApiResult<T>` envelope). This module
 * deliberately does NOT: VietQR is a third party. Sending it our auth/locale
 * headers would leak session context to an external origin, and it does not speak
 * our envelope — it returns a bare array. So the bank directory is fetched with a
 * plain `fetch` and no app headers. This is the single documented exception to the
 * "never scatter raw fetch" rule (plan D5); do not copy this pattern for anything
 * that talks to our own API.
 *
 * Resilience: the live JSON fetch depends on VietQR CORS and reachability (prod is
 * a static SPA with no proxy). `useVietqrBanks` seeds + falls back to the committed
 * snapshot (`data/vietqrBanks.ts`) so the picker is never empty; this module simply
 * throws on any failure and lets the hook decide.
 */
import { env } from "@/config/env";

/** One raw entry of the VietQR directory (array element, or `{ data: [...] }`). */
interface VietqrRawBank {
  id?: unknown;
  bankCode?: unknown;
  bankName?: unknown;
  bankShortName?: unknown;
  imageId?: unknown;
  status?: unknown;
  caiValue?: unknown;
  unlinkedType?: unknown;
}

/** Normalized internal shape the whole app uses (see plan "Data contract"). */
export interface VietqrBank {
  /** `caiValue`, validated `^\d{6}$` — this is our persisted `bankBin`. */
  bin: string;
  /** `bankCode` (e.g. "TCB") — a search keyword. */
  code: string;
  /** `bankName` (full legal name) — search keyword + secondary line. */
  name: string;
  /** `bankShortName` (e.g. "Techcombank") — persisted into `bankName`, shown primary. */
  shortName: string;
  /** Logo id → `${vietqrBaseUrl}/api/vietqr/images/{imageId}`. */
  imageId: string;
}

const BIN_PATTERN = /^\d{6}$/;

const asString = (value: unknown): string =>
  typeof value === "string" ? value : "";

/** Map + filter a raw list to the normalized shape, dropping invalid BINs. */
function normalize(raw: VietqrRawBank[]): VietqrBank[] {
  return raw
    .map((b) => ({
      bin: asString(b.caiValue).trim(),
      code: asString(b.bankCode).trim(),
      name: asString(b.bankName).trim(),
      shortName: asString(b.bankShortName).trim(),
      imageId: asString(b.imageId).trim(),
    }))
    .filter((b) => BIN_PATTERN.test(b.bin));
}

export const vietqrDirectoryApi = {
  /**
   * Fetch + normalize the live directory. Tolerates both a raw array and a
   * `{ data: [...] }` wrapper. Throws on a non-ok response or a shape it can't
   * read (the hook catches → snapshot fallback).
   */
  async list(signal?: AbortSignal): Promise<VietqrBank[]> {
    const response = await fetch(`${env.vietqrBaseUrl}/api/vietqr/banks`, {
      signal,
      headers: { Accept: "application/json" },
    });
    if (!response.ok) {
      throw new Error(`VietQR directory fetch failed: ${response.status}`);
    }
    const payload: unknown = await response.json();
    const raw = Array.isArray(payload)
      ? payload
      : Array.isArray((payload as { data?: unknown }).data)
        ? ((payload as { data: unknown[] }).data)
        : null;
    if (!raw) {
      throw new Error("VietQR directory response was not an array");
    }
    return normalize(raw as VietqrRawBank[]);
  },
};

/** Build the public logo URL for a VietQR `imageId`. */
export function bankLogoUrl(imageId: string): string {
  return `${env.vietqrBaseUrl}/api/vietqr/images/${imageId}`;
}
