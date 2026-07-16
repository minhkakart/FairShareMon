import type { AppTFunction } from "@/i18n/useT";
import { ApiError, ErrorCodes, FREE_LIMIT_CODES, isApiError } from "./errors";

export type ErrorIntent =
  | "notFound" /* 1003 + ownership misses → NotFound view (no existence leak) */
  | "premiumRequired" /* 13003 → UpgradePrompt (gold) */
  | "limit" /* 13000/13001/13002 → LimitNotice (calm) */
  | "validation" /* 1001 → map error.fields onto form fields */
  | "unauthorized" /* 1002 → handled by the client's refresh flow */
  | "network" /* no response */
  | "unexpected"; /* everything else */

const NOT_FOUND_CODES: readonly number[] = [
  ErrorCodes.NotFound,
  ErrorCodes.MemberNotFound,
  ErrorCodes.CategoryNotFound,
  ErrorCodes.TagNotFound,
  ErrorCodes.ExpenseNotFound,
  ErrorCodes.ShareNotFound,
  ErrorCodes.EventNotFound,
  ErrorCodes.BankAccountNotFound,
];

/** Classify a thrown error into the UX intent feature screens should render. */
export function classifyError(error: unknown): ErrorIntent {
  if (!isApiError(error)) return "unexpected";
  if (error.isNetwork) return "network";
  if (error.code === ErrorCodes.ValidationFailed) return "validation";
  if (error.code === ErrorCodes.Unauthorized) return "unauthorized";
  if (error.code === ErrorCodes.PremiumFeatureRequired)
    return "premiumRequired";
  if (FREE_LIMIT_CODES.includes(error.code)) return "limit";
  if (NOT_FOUND_CODES.includes(error.code)) return "notFound";
  return "unexpected";
}

/**
 * The message to show a user. The backend already localizes `error.message`, so
 * we render it verbatim; only client-synthetic states (network / non-ApiError)
 * fall back to translated copy.
 */
export function resolveErrorMessage(error: unknown, t: AppTFunction): string {
  if (isApiError(error)) {
    return error.isNetwork ? t("errors:network") : error.message;
  }
  return t("errors:unexpected");
}

/**
 * Merge server `error.fields` (camelCase keys, 1001) onto a form. `setError` is
 * RHF's; unknown/global keys are collected so the caller can show them
 * form-level.
 */
export function applyFieldErrors(
  error: unknown,
  knownFields: readonly string[],
  setError: (field: string, message: string) => void,
): string[] {
  if (!isApiError(error) || !error.fields) return [];
  const formLevel: string[] = [];
  for (const [field, messages] of Object.entries(error.fields)) {
    const message = messages.join(" ");
    const key = field.charAt(0).toLowerCase() + field.slice(1);
    if (knownFields.includes(key)) {
      setError(key, message);
    } else {
      formLevel.push(message);
    }
  }
  return formLevel;
}

export { ApiError };
