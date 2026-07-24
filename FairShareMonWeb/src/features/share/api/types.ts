/**
 * Event share-link DTOs — mirror `FairShareMonApi/Models/Share/**` and the
 * public read-only report. Feature-local per the feature-first convention.
 * Datetimes are offset-aware ISO-8601 strings; money is typed `number` — the API
 * returns the server-computed value and the UI renders it, never re-derives it.
 */

import type { MemberBalanceRow } from "@/features/events/api/types";
import type { MemberQrResponse } from "@/features/wallet/api/types";

// Re-export the reused public types so share consumers import from one place.
export type { MemberBalanceRow, MemberQrResponse };

/**
 * The owner-side active-link view (`ShareLinkResponse`), returned by create + get.
 * The bank fields are the destination SNAPSHOT taken at creation (OQ8) — present
 * only when a destination was chosen (`hasQr` true).
 */
export interface ShareLinkResponse {
  token: string;
  /** Offset-aware ISO — the 1-day TTL expiry, displayed via formatDateTime. */
  expiresAt: string;
  createdAt: string;
  /** Whether a destination bank was snapshotted → per-member QR is available. */
  hasQr: boolean;
  bankName?: string | null;
  accountNumber?: string | null;
  accountHolderName?: string | null;
}

/** `CreateShareLinkRequest` — bank is optional (OQ2); `regenerate` mints a new token. */
export interface CreateShareLinkRequest {
  bankAccountUuid?: string;
  regenerate?: boolean;
}

/** One member's share within a public expense row. */
export interface PublicShare {
  memberUuid: string;
  memberName: string;
  amount: number;
  isSettled: boolean;
  note?: string | null;
}

/** A public-safe expense with its per-member shares (no owner PII beyond the report). */
export interface PublicExpense {
  uuid: string;
  name: string;
  payerMemberUuid: string;
  payerName: string;
  /** Offset-aware ISO expense time. */
  expenseTime: string;
  total: number;
  shares: PublicShare[];
}

/**
 * The anonymous public report (`PublicEventShareResponse`). `rows` reuse the
 * event balance-row shape; `hasQr` is the single gate for whether QR is available
 * at all (per-row buttons additionally require `outstanding > 0`).
 */
export interface PublicEventShareResponse {
  eventName: string;
  closedAt?: string | null;
  rows: MemberBalanceRow[];
  expenses: PublicExpense[];
  totalOutstanding: number;
  owingMemberCount: number;
  settledMemberCount: number;
  hasQr: boolean;
}
