/**
 * Expense + share DTOs — mirror `FairShareMonApi/Models/Expenses/**` and
 * `Models/Shares/**`. Feature-local per the feature-first convention. Member,
 * category, and tag responses are imported from their own features (never
 * redefined). `total`/`amount` are typed `number` (the API returns the derived
 * money value; the UI renders it, never computes it). Datetimes are ISO-8601
 * offset-aware strings.
 */
import type { MemberResponse } from "@/features/members/api/types";
import type { CategoryResponse } from "@/features/categories/api/types";
import type { TagResponse } from "@/features/tags/api/types";

/** A single share on an expense (member + amount + note). */
export interface ShareResponse {
  uuid: string;
  /** The member bearing this share (shown verbatim even if soft-deleted). */
  member: MemberResponse;
  /** Whole-VND amount as returned by the API. */
  amount: number;
  note?: string | null;
  createdAt: string;
}

/** Summary row for the list (`ExpenseSummaryResponse`). */
export interface ExpenseSummaryResponse {
  uuid: string;
  name: string;
  expenseTime: string;
  /** Derived total = SUM(shares.amount), authoritative from the API. */
  total: number;
  category: CategoryResponse;
  payer: MemberResponse;
  isSettled: boolean;
  settledAt?: string | null;
  tagNames: string[];
  shareCount: number;
  /** Event linkage (M5) — read-only in M4. */
  eventUuid?: string | null;
  eventName?: string | null;
  eventIsClosed?: boolean | null;
  createdAt: string;
}

/** Full expense detail (`ExpenseResponse`). */
export interface ExpenseResponse {
  uuid: string;
  name: string;
  description?: string | null;
  expenseTime: string;
  /** Derived total = SUM(shares.amount), authoritative from the API. */
  total: number;
  category: CategoryResponse;
  payer: MemberResponse;
  isSettled: boolean;
  settledAt?: string | null;
  shares: ShareResponse[];
  tags: TagResponse[];
  /** Event linkage (M5) — read-only in M4; the closed-event write guard reads this. */
  eventUuid?: string | null;
  eventName?: string | null;
  eventIsClosed?: boolean | null;
  createdAt: string;
}

/** A share row in the atomic create payload (`CreateShareInput`). */
export interface CreateShareInput {
  memberUuid: string;
  amount: number;
  note?: string | null;
}

/** `CreateExpenseRequest` — atomic expense + shares. */
export interface CreateExpenseRequest {
  name: string;
  description?: string | null;
  expenseTime: string;
  /** Omit → backend defaults to the owner-representative member. */
  payerMemberUuid?: string;
  /** Omit → backend defaults to the default category. */
  categoryUuid?: string;
  tagUuids?: string[];
  shares?: CreateShareInput[];
}

/** `UpdateExpenseRequest` — general info only (never touches shares). */
export interface UpdateExpenseRequest {
  name: string;
  description?: string | null;
  expenseTime: string;
  payerMemberUuid?: string;
  categoryUuid?: string;
  /** Full replace of the tag set. */
  tagUuids?: string[];
}

/** `CreateShareRequest` — add a share to an existing expense. */
export interface CreateShareRequest {
  memberUuid: string;
  amount: number;
  note?: string | null;
}

/** `UpdateShareRequest` — edit amount/note or change the member. */
export interface UpdateShareRequest {
  memberUuid: string;
  amount: number;
  note?: string | null;
}

/** `SetSettledRequest`. */
export interface SetSettledRequest {
  isSettled: boolean;
}

/**
 * List filter (`ExpenseFilter`, AND-combined). Only defined keys are sent (the
 * client drops `undefined`/`null`). `tagUuid` is a single tag (the API filters
 * by one tag). The client-side name search is NOT part of this filter — it is
 * applied on the already-fetched list.
 */
export interface ExpenseFilter {
  from?: string;
  to?: string;
  categoryUuid?: string;
  tagUuid?: string;
  settled?: boolean;
  looseOnly?: boolean;
}

/** One row of the immutable change history (`AuditLogResponse`). */
export interface AuditLogResponse {
  uuid: string;
  /** "Expense" | "Share". */
  entityType: string;
  entityUuid: string;
  /** "Create" | "Update" | "Delete". */
  action: string;
  /** Denormalized camelCase snapshot before the change (null on Create). */
  before?: Record<string, unknown> | null;
  /** Denormalized camelCase snapshot after the change (null on Delete). */
  after?: Record<string, unknown> | null;
  createdAt: string;
}
